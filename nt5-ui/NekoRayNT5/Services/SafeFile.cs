using System;
using System.Globalization;
using System.IO;

namespace NekoRayNT5.Services
{
    // File operations that never leave the user without the previous version of the data.
    internal static class SafeFile
    {
        // Replaces "path" with the finished file "temp"; the previous version stays as path + ".bak".
        public static void Replace(string temp, string path)
        {
            if (!File.Exists(path))
            {
                File.Move(temp, path);
                return;
            }
            string backup = path + ".bak";
            try
            {
                File.Replace(temp, path, backup);
            }
            catch (PlatformNotSupportedException)
            {
                CopyReplace(temp, path, backup);
            }
            catch (IOException)
            {
                // ReplaceFile is not available on every file system
                CopyReplace(temp, path, backup);
            }
        }

        private static void CopyReplace(string temp, string path, string backup)
        {
            File.Copy(path, backup, true);
            File.Copy(temp, path, true);
            File.Delete(temp);
        }

        // Copies a file aside under a new unique name and returns that name.
        public static string CopyAside(string path, string suffix)
        {
            string stamp = path + "." + suffix + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string copy = stamp;
            for (int n = 2; File.Exists(copy); n++)
            {
                copy = stamp + "-" + n;
            }
            File.Copy(path, copy, false);
            return copy;
        }

        public static void EnsureDirectoryFor(string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        public static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
