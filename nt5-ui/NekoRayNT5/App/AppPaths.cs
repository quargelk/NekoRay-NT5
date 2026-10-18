using System;
using System.IO;
using System.Windows.Forms;

namespace NekoRayNT5.App
{
    internal static class AppPaths
    {
        public const string CoreFileName = "xray.exe";

        // Overrides the data directory, for portable use and for tests.
        public const string DataDirVariable = "NEKORAY_NT5_DATA_DIR";

        public static string BaseDir
        {
            get { return Application.StartupPath; }
        }

        // Per-user data: "%APPDATA%\NekoRayNT5".
        public static string ConfigDir
        {
            get
            {
                string custom = Environment.GetEnvironmentVariable(DataDirVariable);
                if (custom != null && custom.Trim().Length > 0)
                {
                    return Path.GetFullPath(custom.Trim());
                }
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NekoRayNT5");
            }
        }

        public static string ProfilesPath
        {
            get { return Path.Combine(ConfigDir, "profiles.xml"); }
        }

        public static string SettingsPath
        {
            get { return Path.Combine(ConfigDir, "settings.xml"); }
        }

        // Original system proxy settings while the system proxy is switched on by this program.
        public static string ProxyBackupPath
        {
            get { return Path.Combine(ConfigDir, "system-proxy-backup.xml"); }
        }

        // Short-lived files: generated core configuration, process id of the running core.
        public static string RunDir
        {
            get { return Path.Combine(ConfigDir, "run"); }
        }

        public static string CorePath
        {
            get { return Path.Combine(BaseDir, CoreFileName); }
        }

        // Optional helper: HTTPS downloads with current TLS ("nt5helper fetch").
        public static string HelperPath
        {
            get { return Path.Combine(BaseDir, "nt5helper.exe"); }
        }

        // Mozilla CA bundle for nt5helper.exe (shipped with the program; see runtime\README.txt).
        public static string CaBundlePath
        {
            get { return Path.Combine(BaseDir, "cacert.pem"); }
        }

        // Optional fallback for HTTPS subscriptions.
        public static string CurlPath
        {
            get { return Path.Combine(BaseDir, "curl.exe"); }
        }

        public static string LogDir
        {
            get { return Path.Combine(ConfigDir, "logs"); }
        }

        // For text that the user may share (diagnostic report): the folders of the user profile are
        // replaced by their variables, so that the Windows user name does not leave the computer.
        public static string HidePersonalFolders(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }
            // the longest first: %LOCALAPPDATA% and %APPDATA% lie inside %USERPROFILE%
            string[] names = new string[] { "LOCALAPPDATA", "APPDATA", "TEMP", "USERPROFILE" };
            string result = text;
            foreach (string name in names)
            {
                string value = Environment.GetEnvironmentVariable(name);
                if (!string.IsNullOrEmpty(value) && value.Length > 3)
                {
                    result = ReplaceIgnoreCase(result, value.TrimEnd('\\'), "%" + name + "%");
                }
            }
            return result;
        }

        private static string ReplaceIgnoreCase(string text, string find, string replacement)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            int pos = 0;
            while (true)
            {
                int at = text.IndexOf(find, pos, StringComparison.OrdinalIgnoreCase);
                if (at < 0)
                {
                    sb.Append(text, pos, text.Length - pos);
                    return sb.ToString();
                }
                sb.Append(text, pos, at - pos).Append(replacement);
                pos = at + find.Length;
            }
        }

        public static string EnsureConfigDir()
        {
            string dir = ConfigDir;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            return dir;
        }
    }
}
