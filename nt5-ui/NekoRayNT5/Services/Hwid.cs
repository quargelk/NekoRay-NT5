using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using NekoRayNT5.App;
using NekoRayNT5.Model;

namespace NekoRayNT5.Services
{
    // Device identifier (HWID) for Happ-compatible subscriptions.
    //
    // Some subscription panels (for example Remnawave with a device limit) count the devices of a
    // user by the HWID headers that the Happ client sends, and refuse or limit downloads without them.
    // NekoRay NT5 sends them only for subscriptions where the user switched this on. The HWID is a
    // random value made once for this installation and stored in settings.xml - it is not derived
    // from the hardware and never copied from another device. A custom value can be set instead
    // (for example a HWID the provider authorised for this user). The User-Agent stays NekoRayNT5:
    // the program does not pretend to be Happ.
    // The HWID never goes to the log; Mask shows only its beginning.
    internal static class Hwid
    {
        public const int MinLength = 8;
        public const int MaxLength = 128;

        // Null when the value can be used ("" means "not set"), otherwise the reason.
        public static string Check(string value)
        {
            if (value == null || value.Length == 0)
            {
                return null;
            }
            if (value.Length < MinLength || value.Length > MaxLength)
            {
                return Lang.F("The HWID must be {0} to {1} characters long.", MinLength, MaxLength);
            }
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')
                      || c == '-' || c == '_' || c == '.' || c == ':'))
                {
                    return Lang.T("The HWID may contain only Latin letters, digits and - _ . :");
                }
            }
            return null;
        }

        // A new random HWID: 16 random bytes as 32 lowercase hex digits.
        public static string Generate()
        {
            byte[] bytes = new byte[16];
            new RNGCryptoServiceProvider().GetBytes(bytes);
            StringBuilder sb = new StringBuilder(32);
            for (int i = 0; i < bytes.Length; i++)
            {
                sb.Append(bytes[i].ToString("x2"));
            }
            return sb.ToString();
        }

        // The HWID to send: the custom one, else the generated one (made now if there is none yet;
        // the caller saves the settings when "created" is true).
        public static string Effective(AppSettings settings, out bool created)
        {
            created = false;
            if (settings.HwidCustom.Length > 0)
            {
                return settings.HwidCustom;
            }
            if (settings.HwidGenerated.Length == 0)
            {
                settings.HwidGenerated = Generate();
                created = true;
            }
            return settings.HwidGenerated;
        }

        // Headers of a Happ-compatible request (names as the panels expect them).
        public static List<KeyValuePair<string, string>> Headers(string hwid)
        {
            List<KeyValuePair<string, string>> headers = new List<KeyValuePair<string, string>>();
            headers.Add(new KeyValuePair<string, string>("x-hwid", hwid));
            headers.Add(new KeyValuePair<string, string>("x-device-os", "Windows"));
            Version v = Environment.OSVersion.Version;
            headers.Add(new KeyValuePair<string, string>("x-ver-os", v.Major + "." + v.Minor));
            headers.Add(new KeyValuePair<string, string>("x-device-model", AppInfo.Name));
            return headers;
        }

        // For the screen: the first characters only.
        public static string Mask(string hwid)
        {
            if (hwid == null || hwid.Length == 0)
            {
                return Lang.T("(not created yet)");
            }
            return hwid.Length <= 6 ? "******" : hwid.Substring(0, 6) + "...";
        }
    }
}
