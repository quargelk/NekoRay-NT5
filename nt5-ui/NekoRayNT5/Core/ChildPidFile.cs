using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using NekoRayNT5.Services;

namespace NekoRayNT5.Core
{
    // Remembers the child processes started by this program ("run\<name>.pid") so that the next start
    // can stop what a crashed session left running. A process is touched only when its id, its start
    // time and its program file all match: an unrelated xray.exe, or a new process that got the same
    // id, is never killed.
    internal static class ChildPidFile
    {
        public const string Core = "core";
        // the TUN helper of versions 0.9.x (TUN mode was removed); only cleaned up after an update
        public const string LegacyTun = "tun";

        public static void Write(string runDir, string name, int pid)
        {
            try
            {
                long started = 0;
                try
                {
                    using (Process p = Process.GetProcessById(pid))
                    {
                        started = p.StartTime.ToFileTimeUtc();
                    }
                }
                catch (Exception)
                {
                }
                if (!Directory.Exists(runDir))
                {
                    Directory.CreateDirectory(runDir);
                }
                File.WriteAllText(PathOf(runDir, name), pid.ToString(CultureInfo.InvariantCulture) + " "
                    + started.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception)
            {
            }
        }

        public static void Clear(string runDir, string name)
        {
            SafeFile.TryDelete(PathOf(runDir, name));
        }

        // Returns true when a leftover process was found and stopped.
        public static bool KillLeftover(string runDir, string name, string exePath)
        {
            string file = PathOf(runDir, name);
            if (!File.Exists(file))
            {
                return false;
            }
            bool killed = false;
            try
            {
                string[] parts = File.ReadAllText(file).Trim().Split(' ');
                int pid;
                long started = 0;
                if (parts.Length >= 1 && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out pid)
                    && (parts.Length < 2 || long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out started)))
                {
                    killed = KillIfSame(pid, started, exePath);
                }
            }
            catch (Exception)
            {
            }
            SafeFile.TryDelete(file);
            return killed;
        }

        private static bool KillIfSame(int pid, long started, string exePath)
        {
            try
            {
                using (Process p = Process.GetProcessById(pid))
                {
                    if (started != 0 && p.StartTime.ToFileTimeUtc() != started)
                    {
                        return false;
                    }
                    if (!string.Equals(p.MainModule.FileName, exePath, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                    p.Kill();
                    p.WaitForExit(3000);
                    return true;
                }
            }
            catch (Exception)
            {
                // no such process, or it is not ours to touch
                return false;
            }
        }

        private static string PathOf(string runDir, string name)
        {
            return Path.Combine(runDir, name + ".pid");
        }
    }
}
