using System;
using System.Reflection;

namespace NekoRayNT5.App
{
    internal static class AppInfo
    {
        public const string Name = "NekoRay NT5";
        public const string MutexName = "NekoRayNT5.SingleInstance";

        public static string Version
        {
            get
            {
                Version v = Assembly.GetExecutingAssembly().GetName().Version;
                return v.Major + "." + v.Minor + "." + v.Build;
            }
        }

        public static string OsDescription
        {
            get
            {
                OperatingSystem os = Environment.OSVersion;
                string sp = os.ServicePack;
                string text = "Windows NT " + os.Version.Major + "." + os.Version.Minor + " build " + os.Version.Build;
                if (sp != null && sp.Length > 0)
                {
                    text += " " + sp;
                }
                return text + ", CLR " + Environment.Version;
            }
        }
    }
}
