using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace NekoRayNT5.App
{
    // Diagnostic log of the shell in "<data dir>\logs". Only messages of the shell itself go here:
    // they never contain UUIDs, passwords, keys, share links or subscription addresses. The output
    // of the core is not written to disk, because it names the sites the user visits.
    // Never throws: a log that cannot be written is simply skipped.
    internal static class DiagLog
    {
        public const string FileName = "nekoray-nt5.log";
        public const string CrashFileName = "crash.log";

        private const long MaxBytes = 512 * 1024;

        private static readonly object Sync = new object();
        private static string _dir;

        // Null switches file logging off (until set again).
        public static string Directory
        {
            get { lock (Sync) { return _dir; } }
            set { lock (Sync) { _dir = value; } }
        }

        public static string FilePath
        {
            get
            {
                string dir = Directory;
                return dir == null ? null : Path.Combine(dir, FileName);
            }
        }

        public static void Write(string line)
        {
            lock (Sync)
            {
                if (_dir == null)
                {
                    return;
                }
                try
                {
                    if (!System.IO.Directory.Exists(_dir))
                    {
                        System.IO.Directory.CreateDirectory(_dir);
                    }
                    string path = Path.Combine(_dir, FileName);
                    FileInfo info = new FileInfo(path);
                    if (info.Exists && info.Length > MaxBytes)
                    {
                        string old = path + ".1";
                        if (File.Exists(old))
                        {
                            File.Delete(old);
                        }
                        File.Move(path, old);
                    }
                    File.AppendAllText(path, Stamp() + " " + line + "\r\n", new UTF8Encoding(false));
                }
                catch (Exception)
                {
                }
            }
        }

        // Writes the details of a fatal error. Returns the file name or null.
        public static string WriteCrash(string context, Exception ex)
        {
            Write("FATAL " + context + ": " + (ex == null ? "(no exception)" : ex.GetType().FullName + ": " + ex.Message));
            lock (Sync)
            {
                if (_dir == null)
                {
                    return null;
                }
                try
                {
                    if (!System.IO.Directory.Exists(_dir))
                    {
                        System.IO.Directory.CreateDirectory(_dir);
                    }
                    string path = Path.Combine(_dir, CrashFileName);
                    StringBuilder sb = new StringBuilder();
                    sb.Append("==== ").Append(Stamp()).Append(" ").Append(AppInfo.Name).Append(" ").Append(AppInfo.Version)
                        .Append(" (").Append(AppInfo.OsDescription).Append(")\r\n");
                    sb.Append(context).Append("\r\n");
                    sb.Append(ex == null ? "(no exception)" : ex.ToString()).Append("\r\n\r\n");
                    File.AppendAllText(path, sb.ToString(), new UTF8Encoding(false));
                    return path;
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        private static string Stamp()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }
    }
}
