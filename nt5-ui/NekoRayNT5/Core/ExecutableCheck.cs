using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using NekoRayNT5.App;

namespace NekoRayNT5.Core
{
    internal delegate bool ExportCheck(string dll, string function);

    // Tells in advance whether Windows can load a program, instead of letting Process.Start fail.
    //
    // Two things are checked from the PE file itself:
    //  - the subsystem version: the loader refuses an image that asks for a newer Windows
    //    ("is not a valid Win32 application"); Windows 2000 is NT 5.0, XP is 5.1;
    //  - the functions imported at load time: one missing function stops the program before it
    //    runs ("The procedure entry point ... could not be located").
    // Functions that a program looks up by itself at run time cannot be seen this way.
    internal static class ExecutableCheck
    {
        private const ushort MachineI386 = 0x14C;
        private const ushort MachineAmd64 = 0x8664;
        private const int MaxImports = 4096;

        // Null when the program can be loaded on this system, otherwise the reason.
        public static string Check(string exePath)
        {
            return Check(exePath, Environment.OSVersion.Version, Is64BitWindows(), SystemExports);
        }

        public static string Check(string exePath, Version os, bool os64, ExportCheck exists)
        {
            string name = Path.GetFileName(exePath);
            try
            {
                using (FileStream f = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    return CheckImage(f, name, os, os64, exists);
                }
            }
            catch (FileNotFoundException)
            {
                return Lang.F("{0} was not found.", name);
            }
            catch (IOException ex)
            {
                return Lang.F("{0} cannot be read: {1}", name, ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Lang.F("{0} cannot be read: {1}", name, ex.Message);
            }
        }

        private static string CheckImage(FileStream f, string name, Version os, bool os64, ExportCheck exists)
        {
            byte[] dos = ReadAt(f, 0, 64);
            if (dos == null || dos[0] != 'M' || dos[1] != 'Z')
            {
                return Lang.F("{0} is not a Windows program.", name);
            }
            int pe = BitConverter.ToInt32(dos, 0x3C);
            byte[] head = ReadAt(f, pe, 24 + 240);
            if (pe <= 0 || head == null || head[0] != 'P' || head[1] != 'E' || head[2] != 0 || head[3] != 0)
            {
                return Lang.F("{0} is not a Windows program.", name);
            }
            ushort machine = BitConverter.ToUInt16(head, 4);
            ushort sections = BitConverter.ToUInt16(head, 6);
            ushort optionalSize = BitConverter.ToUInt16(head, 20);
            int opt = 24;
            ushort magic = BitConverter.ToUInt16(head, opt);
            bool pe32Plus = magic == 0x20B;
            if (machine == MachineAmd64 && !os64)
            {
                return Lang.F("{0} is a 64-bit program; this Windows is 32-bit. Use the x86 (386) build.", name);
            }
            if (machine != MachineI386 && machine != MachineAmd64)
            {
                return Lang.F("{0} is built for another processor (machine type 0x{1}).", name, machine.ToString("X"));
            }

            int subMajor = BitConverter.ToUInt16(head, opt + 48);
            int subMinor = BitConverter.ToUInt16(head, opt + 50);
            if (subMajor > os.Major || (subMajor == os.Major && subMinor > os.Minor))
            {
                return Lang.F("{0} requires Windows NT {1}.{2} or newer ({3}); this system is NT {4}.{5}. Windows refuses to load it.",
                       name, subMajor, subMinor, Describe(subMajor, subMinor), os.Major, os.Minor);
            }

            int dataDir = opt + (pe32Plus ? 112 : 96);
            if (head.Length < dataDir + 16)
            {
                return null;
            }
            uint importRva = BitConverter.ToUInt32(head, dataDir + 8);
            if (importRva == 0)
            {
                return null;
            }

            byte[] table = ReadAt(f, pe + 24 + optionalSize, sections * 40);
            if (table == null)
            {
                return Lang.F("{0} is damaged (section table).", name);
            }
            Section[] map = new Section[sections];
            for (int i = 0; i < sections; i++)
            {
                map[i].VirtualSize = BitConverter.ToUInt32(table, i * 40 + 8);
                map[i].VirtualAddress = BitConverter.ToUInt32(table, i * 40 + 12);
                map[i].RawSize = BitConverter.ToUInt32(table, i * 40 + 16);
                map[i].RawPointer = BitConverter.ToUInt32(table, i * 40 + 20);
            }

            List<string> missing = new List<string>();
            int count = 0;
            for (long d = Offset(map, importRva); d >= 0; d += 20)
            {
                byte[] desc = ReadAt(f, d, 20);
                if (desc == null)
                {
                    return Lang.F("{0} is damaged (import table).", name);
                }
                uint lookup = BitConverter.ToUInt32(desc, 0);
                uint dllNameRva = BitConverter.ToUInt32(desc, 12);
                uint iat = BitConverter.ToUInt32(desc, 16);
                if (dllNameRva == 0)
                {
                    break;
                }
                string dll = ReadString(f, Offset(map, dllNameRva));
                long thunk = Offset(map, lookup != 0 ? lookup : iat);
                int step = pe32Plus ? 8 : 4;
                while (thunk >= 0 && count < MaxImports)
                {
                    byte[] t = ReadAt(f, thunk, step);
                    if (t == null)
                    {
                        break;
                    }
                    ulong value = pe32Plus ? BitConverter.ToUInt64(t, 0) : BitConverter.ToUInt32(t, 0);
                    if (value == 0)
                    {
                        break;
                    }
                    bool byOrdinal = pe32Plus ? (value & 0x8000000000000000UL) != 0 : (value & 0x80000000UL) != 0;
                    if (!byOrdinal)
                    {
                        string function = ReadString(f, Offset(map, (uint)(value & 0x7FFFFFFF)) + 2);
                        if (function != null && dll != null && !exists(dll, function))
                        {
                            missing.Add(dll + "!" + function);
                        }
                    }
                    count++;
                    thunk += step;
                }
            }
            if (missing.Count > 0)
            {
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < missing.Count && i < 5; i++)
                {
                    sb.Append(i == 0 ? "" : ", ").Append(missing[i]);
                }
                if (missing.Count > 5)
                {
                    sb.Append(", ...");
                }
                return Lang.F("{0} needs system functions that this Windows (NT {1}.{2}) does not have: {3}. Windows refuses to start it.",
                       name, os.Major, os.Minor, sb);
            }
            return null;
        }

        private struct Section
        {
            public uint VirtualAddress;
            public uint VirtualSize;
            public uint RawPointer;
            public uint RawSize;
        }

        private static long Offset(Section[] map, uint rva)
        {
            for (int i = 0; i < map.Length; i++)
            {
                uint size = Math.Max(map[i].VirtualSize, map[i].RawSize);
                if (rva >= map[i].VirtualAddress && rva < map[i].VirtualAddress + size)
                {
                    return (long)rva - map[i].VirtualAddress + map[i].RawPointer;
                }
            }
            return -1;
        }

        private static byte[] ReadAt(FileStream f, long offset, int count)
        {
            if (offset < 0 || count < 0 || offset + count > f.Length)
            {
                return null;
            }
            byte[] buffer = new byte[count];
            f.Seek(offset, SeekOrigin.Begin);
            int done = 0;
            while (done < count)
            {
                int read = f.Read(buffer, done, count - done);
                if (read <= 0)
                {
                    return null;
                }
                done += read;
            }
            return buffer;
        }

        private static string ReadString(FileStream f, long offset)
        {
            if (offset < 0)
            {
                return null;
            }
            byte[] chunk = ReadAt(f, offset, (int)Math.Min(256, f.Length - offset));
            if (chunk == null)
            {
                return null;
            }
            int end = Array.IndexOf(chunk, (byte)0);
            return Encoding.ASCII.GetString(chunk, 0, end < 0 ? chunk.Length : end);
        }

        private static string Describe(int major, int minor)
        {
            if (major == 5 && minor == 0) { return "Windows 2000"; }
            if (major == 5 && minor == 1) { return "Windows XP"; }
            if (major == 5 && minor == 2) { return "Windows XP x64 / Server 2003"; }
            if (major == 6 && minor == 0) { return "Windows Vista"; }
            if (major == 6 && minor == 1) { return "Windows 7"; }
            if (major == 6) { return Lang.T("Windows 8 or newer"); }
            return Lang.T("a newer Windows");
        }

        // ---- the real system ----

        // A function exists when the system DLL can be found and exports it. DLLs that are not part of
        // Windows (not in the system directory) are not loaded here; they are reported as missing.
        public static bool SystemExports(string dll, string function)
        {
            IntPtr module = GetModuleHandle(dll);
            bool loaded = false;
            if (module == IntPtr.Zero)
            {
                string path = Path.Combine(Environment.SystemDirectory, dll);
                if (!File.Exists(path))
                {
                    return false;
                }
                module = LoadLibrary(path);
                loaded = module != IntPtr.Zero;
                if (!loaded)
                {
                    return false;
                }
            }
            try
            {
                return GetProcAddress(module, function) != IntPtr.Zero;
            }
            finally
            {
                if (loaded)
                {
                    FreeLibrary(module);
                }
            }
        }

        private static bool Is64BitWindows()
        {
            if (IntPtr.Size == 8)
            {
                return true;
            }
            try
            {
                IntPtr k32 = GetModuleHandle("kernel32.dll");
                if (k32 == IntPtr.Zero || GetProcAddress(k32, "IsWow64Process") == IntPtr.Zero)
                {
                    return false;
                }
                bool wow;
                return IsWow64Process(System.Diagnostics.Process.GetCurrentProcess().Handle, out wow) && wow;
            }
            catch (Exception)
            {
                return false;
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string name);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string path);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FreeLibrary(IntPtr module);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWow64Process(IntPtr process, [MarshalAs(UnmanagedType.Bool)] out bool wow64);
    }
}
