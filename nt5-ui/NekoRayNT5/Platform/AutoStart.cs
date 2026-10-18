using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NekoRayNT5.Platform
{
    // "Start with system": a value in the current user's Run key, as Nekoray does (sys/AutoRun.cpp).
    internal static class AutoStart
    {
        public const string TrayArgument = "-tray";

        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "NekoRayNT5";

        private static string Command
        {
            get { return "\"" + Application.ExecutablePath + "\" " + TrayArgument; }
        }

        // True only when the entry points at this copy of the program.
        public static bool IsEnabled
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                    {
                        string value = key == null ? null : key.GetValue(ValueName) as string;
                        return value != null && string.Equals(value, Command, StringComparison.OrdinalIgnoreCase);
                    }
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        public static void Set(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled)
                {
                    key.SetValue(ValueName, Command);
                }
                else
                {
                    key.DeleteValue(ValueName, false);
                }
            }
        }
    }
}
