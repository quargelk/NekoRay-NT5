using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using NekoRayNT5.Services;
using NekoRayNT5.App;

namespace NekoRayNT5.Platform
{
    // System proxy settings through WinINet per-connection options, the documented way
    // that Internet Explorer's own dialog uses. ANSI entry points are used because they
    // exist on every target system (Windows 2000 with IE 5 and later).
    internal sealed class WinInetProxy : IProxyBackend
    {
        private const int INTERNET_OPTION_REFRESH = 37;
        private const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
        private const int INTERNET_OPTION_PER_CONNECTION_OPTION = 75;

        private const int INTERNET_PER_CONN_FLAGS = 1;
        private const int INTERNET_PER_CONN_PROXY_SERVER = 2;
        private const int INTERNET_PER_CONN_PROXY_BYPASS = 3;
        private const int INTERNET_PER_CONN_AUTOCONFIG_URL = 4;

        private const int ERROR_BUFFER_TOO_SMALL = 603;
        private const int MaxRasEntries = 256;

        [StructLayout(LayoutKind.Explicit)]
        private struct OptionValue
        {
            [FieldOffset(0)] public int dwValue;
            [FieldOffset(0)] public IntPtr pszValue;
            // keeps the union 8 bytes long with 4-byte alignment, like FILETIME in the native header
            [FieldOffset(4)] public int ftHigh;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INTERNET_PER_CONN_OPTION
        {
            public int dwOption;
            public OptionValue Value;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INTERNET_PER_CONN_OPTION_LIST
        {
            public int dwSize;
            public IntPtr pszConnection;
            public int dwOptionCount;
            public int dwOptionError;
            public IntPtr pOptions;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct RASENTRYNAME
        {
            public int dwSize;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string szEntryName;
            public int dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 261)] public string szPhonebookPath;
        }

        [DllImport("wininet.dll", EntryPoint = "InternetQueryOptionA", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool InternetQueryOption(IntPtr hInternet, int option, ref INTERNET_PER_CONN_OPTION_LIST list, ref int size);

        [DllImport("wininet.dll", EntryPoint = "InternetSetOptionA", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool InternetSetOption(IntPtr hInternet, int option, ref INTERNET_PER_CONN_OPTION_LIST list, int size);

        [DllImport("wininet.dll", EntryPoint = "InternetSetOptionA", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool InternetSetOptionNotify(IntPtr hInternet, int option, IntPtr buffer, int size);

        [DllImport("rasapi32.dll", EntryPoint = "RasEnumEntriesA")]
        private static extern int RasEnumEntries(IntPtr reserved, IntPtr phonebook, IntPtr entries, ref int size, ref int count);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalFree(IntPtr handle);

        // Dial-up and VPN connections; each has its own proxy settings. The LAN connection is not in the list.
        public string[] ListConnections()
        {
            int entrySize = Marshal.SizeOf(typeof(RASENTRYNAME));
            int size = entrySize * 8;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                IntPtr buffer = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.WriteInt32(buffer, entrySize);
                    int count = 0;
                    int result;
                    try
                    {
                        result = RasEnumEntries(IntPtr.Zero, IntPtr.Zero, buffer, ref size, ref count);
                    }
                    catch (DllNotFoundException)
                    {
                        return new string[0];
                    }
                    catch (EntryPointNotFoundException)
                    {
                        return new string[0];
                    }
                    if (result == ERROR_BUFFER_TOO_SMALL && attempt == 0)
                    {
                        continue;
                    }
                    if (result != 0)
                    {
                        // Remote Access is not installed or its service is not running: there is nothing to list.
                        return new string[0];
                    }
                    List<string> names = new List<string>();
                    for (int i = 0; i < count && i < MaxRasEntries; i++)
                    {
                        IntPtr item = new IntPtr(buffer.ToInt64() + (long)i * entrySize);
                        RASENTRYNAME entry = (RASENTRYNAME)Marshal.PtrToStructure(item, typeof(RASENTRYNAME));
                        if (entry.szEntryName != null && entry.szEntryName.Length > 0)
                        {
                            names.Add(entry.szEntryName);
                        }
                    }
                    return names.ToArray();
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            return new string[0];
        }

        public ProxyConnectionSettings Query(string connection)
        {
            int[] ids = new int[] { INTERNET_PER_CONN_FLAGS, INTERNET_PER_CONN_PROXY_SERVER,
                INTERNET_PER_CONN_PROXY_BYPASS, INTERNET_PER_CONN_AUTOCONFIG_URL };
            int optionSize = Marshal.SizeOf(typeof(INTERNET_PER_CONN_OPTION));
            IntPtr options = Marshal.AllocHGlobal(optionSize * ids.Length);
            IntPtr name = connection == null ? IntPtr.Zero : Marshal.StringToHGlobalAnsi(connection);
            try
            {
                for (int i = 0; i < ids.Length; i++)
                {
                    INTERNET_PER_CONN_OPTION o = new INTERNET_PER_CONN_OPTION();
                    o.dwOption = ids[i];
                    Marshal.StructureToPtr(o, Offset(options, i * optionSize), false);
                }
                INTERNET_PER_CONN_OPTION_LIST list = new INTERNET_PER_CONN_OPTION_LIST();
                list.dwSize = Marshal.SizeOf(typeof(INTERNET_PER_CONN_OPTION_LIST));
                list.pszConnection = name;
                list.dwOptionCount = ids.Length;
                list.pOptions = options;
                int size = list.dwSize;
                if (!InternetQueryOption(IntPtr.Zero, INTERNET_OPTION_PER_CONNECTION_OPTION, ref list, ref size))
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new Win32Exception(error, Lang.F("Cannot read the system proxy settings (error {0}).", error));
                }

                ProxyConnectionSettings s = new ProxyConnectionSettings();
                s.Connection = connection;
                s.Flags = Read(options, 0, optionSize).Value.dwValue;
                s.Server = TakeString(Read(options, 1, optionSize));
                s.Bypass = TakeString(Read(options, 2, optionSize));
                s.PacUrl = TakeString(Read(options, 3, optionSize));
                return s;
            }
            finally
            {
                Marshal.FreeHGlobal(options);
                if (name != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(name);
                }
            }
        }

        public void Apply(ProxyConnectionSettings s)
        {
            int[] ids = new int[] { INTERNET_PER_CONN_FLAGS, INTERNET_PER_CONN_PROXY_SERVER,
                INTERNET_PER_CONN_PROXY_BYPASS, INTERNET_PER_CONN_AUTOCONFIG_URL };
            string[] strings = new string[] { null, s.Server, s.Bypass, s.PacUrl };
            int optionSize = Marshal.SizeOf(typeof(INTERNET_PER_CONN_OPTION));
            IntPtr options = Marshal.AllocHGlobal(optionSize * ids.Length);
            IntPtr name = s.Connection == null ? IntPtr.Zero : Marshal.StringToHGlobalAnsi(s.Connection);
            List<IntPtr> allocated = new List<IntPtr>();
            try
            {
                for (int i = 0; i < ids.Length; i++)
                {
                    INTERNET_PER_CONN_OPTION o = new INTERNET_PER_CONN_OPTION();
                    o.dwOption = ids[i];
                    if (i == 0)
                    {
                        o.Value.dwValue = s.Flags;
                    }
                    else
                    {
                        IntPtr text = Marshal.StringToHGlobalAnsi(strings[i] == null ? "" : strings[i]);
                        allocated.Add(text);
                        o.Value.pszValue = text;
                    }
                    Marshal.StructureToPtr(o, Offset(options, i * optionSize), false);
                }
                INTERNET_PER_CONN_OPTION_LIST list = new INTERNET_PER_CONN_OPTION_LIST();
                list.dwSize = Marshal.SizeOf(typeof(INTERNET_PER_CONN_OPTION_LIST));
                list.pszConnection = name;
                list.dwOptionCount = ids.Length;
                list.pOptions = options;
                if (!InternetSetOption(IntPtr.Zero, INTERNET_OPTION_PER_CONNECTION_OPTION, ref list, list.dwSize))
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new Win32Exception(error, Lang.F("Cannot change the system proxy settings (error {0}).", error));
                }
            }
            finally
            {
                for (int i = 0; i < allocated.Count; i++)
                {
                    Marshal.FreeHGlobal(allocated[i]);
                }
                Marshal.FreeHGlobal(options);
                if (name != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(name);
                }
            }
        }

        // Tells running programs to re-read the settings.
        public void NotifyChanged()
        {
            InternetSetOptionNotify(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
            InternetSetOptionNotify(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
        }

        private static IntPtr Offset(IntPtr start, int offset)
        {
            return new IntPtr(start.ToInt64() + offset);
        }

        private static INTERNET_PER_CONN_OPTION Read(IntPtr options, int index, int optionSize)
        {
            return (INTERNET_PER_CONN_OPTION)Marshal.PtrToStructure(Offset(options, index * optionSize),
                typeof(INTERNET_PER_CONN_OPTION));
        }

        // WinINet allocates returned strings with GlobalAlloc; the caller frees them.
        private static string TakeString(INTERNET_PER_CONN_OPTION option)
        {
            IntPtr p = option.Value.pszValue;
            if (p == IntPtr.Zero)
            {
                return "";
            }
            string text = Marshal.PtrToStringAnsi(p);
            GlobalFree(p);
            return text == null ? "" : text;
        }
    }
}
