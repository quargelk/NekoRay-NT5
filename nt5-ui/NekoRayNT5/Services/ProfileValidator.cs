using System;
using NekoRayNT5.Model;
using NekoRayNT5.App;

namespace NekoRayNT5.Services
{
    // Single set of rules for imported links, the editor and the storage file.
    // Only combinations that the configuration builder can express for Xray 1.8.3 are accepted.
    // Messages never contain the values of private fields.
    internal static class ProfileValidator
    {
        public static readonly string[] Flows = new string[] { "", "xtls-rprx-vision", "xtls-rprx-vision-udp443" };
        public static readonly string[] Networks = new string[] { "tcp", "ws", "grpc", "http" };
        public static readonly string[] Securities = new string[] { "none", "tls", "reality" };
        public static readonly string[] VmessSecurities = new string[] { "auto", "aes-128-gcm", "chacha20-poly1305", "none", "zero" };
        public static readonly string[] ShadowsocksMethods = new string[] {
            "aes-128-gcm", "aes-256-gcm", "chacha20-poly1305", "chacha20-ietf-poly1305", "xchacha20-poly1305",
            "xchacha20-ietf-poly1305", "2022-blake3-aes-128-gcm", "2022-blake3-aes-256-gcm",
            "2022-blake3-chacha20-poly1305", "none", "plain" };

        // Returns null when the profile is valid, otherwise a message for the user.
        public static string Validate(ProfileEntry p)
        {
            if (Array.IndexOf(ProfileEntry.Types, p.Type) < 0)
            {
                return Lang.T("Unsupported profile type.");
            }
            if (p.Address.Length == 0)
            {
                return Lang.T("Server address is empty.");
            }
            if (!IsValidAddress(p.Address))
            {
                return Lang.T("Server address contains invalid characters.");
            }
            if (p.Port < 1 || p.Port > 65535)
            {
                return Lang.T("Port must be a number from 1 to 65535.");
            }
            if (HasControlChars(p.Name))
            {
                return Lang.T("Name must not contain control characters.");
            }

            if (p.UsesUuid)
            {
                if (p.Uuid.Length == 0)
                {
                    return Lang.T("UUID is empty.");
                }
                if (!IsUuid(p.Uuid))
                {
                    return Lang.T("UUID must look like xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx (hexadecimal digits).");
                }
            }
            if (p.Type == ProfileEntry.TypeVless && p.Encryption != "none")
            {
                return Lang.T("Unsupported encryption: VLESS supports only \"none\".");
            }
            if (p.Type == ProfileEntry.TypeVmess && Array.IndexOf(VmessSecurities, p.Encryption) < 0)
            {
                return Lang.T("Unsupported VMess security: use auto, aes-128-gcm, chacha20-poly1305, none or zero.");
            }
            if (p.Type == ProfileEntry.TypeShadowsocks && Array.IndexOf(ShadowsocksMethods, p.Encryption) < 0)
            {
                return Lang.T("Unsupported Shadowsocks method.");
            }
            if ((p.Type == ProfileEntry.TypeTrojan || p.Type == ProfileEntry.TypeShadowsocks) && p.Password.Length == 0)
            {
                return Lang.T("Password is empty.");
            }
            if (HasControlChars(p.Password) || HasControlChars(p.Username))
            {
                return Lang.T("User name and password must not contain control characters.");
            }
            if (p.UsesUsername && p.Username.Length == 0 && p.Password.Length > 0)
            {
                return Lang.T("A password is set but the user name is empty.");
            }

            if (!p.UsesStream)
            {
                if (p.Network != "tcp" || p.Security != "none" || p.Flow.Length > 0)
                {
                    return Lang.T("This profile type supports only plain TCP without TLS.");
                }
                return null;
            }
            return ValidateStream(p);
        }

        private static string ValidateStream(ProfileEntry p)
        {
            if (Array.IndexOf(Networks, p.Network) < 0)
            {
                return Lang.T("Unsupported transport: use tcp, ws, grpc or http.");
            }
            if (Array.IndexOf(Securities, p.Security) < 0)
            {
                return Lang.T("Unsupported security: use none, tls or reality.");
            }
            if (Array.IndexOf(Flows, p.Flow) < 0)
            {
                return Lang.T("Unsupported flow: use an empty value, xtls-rprx-vision or xtls-rprx-vision-udp443.");
            }
            if (p.Flow.Length > 0 && (p.Type != ProfileEntry.TypeVless || p.Network != "tcp" || p.Security == "none"))
            {
                return Lang.T("Flow (XTLS Vision) requires VLESS over TCP with TLS or REALITY.");
            }
            if (p.Network == "http" && p.Security == "none")
            {
                return Lang.T("HTTP/2 transport requires TLS or REALITY.");
            }
            if (p.Security == "reality" && p.Network == "ws")
            {
                return Lang.T("REALITY cannot be used with the WebSocket transport.");
            }
            if (p.Sni.Length > 0 && !IsValidAddress(p.Sni))
            {
                return Lang.T("SNI contains invalid characters.");
            }
            if (HasControlChars(p.Host) || HasControlChars(p.Path) || HasControlChars(p.Alpn) || HasControlChars(p.SpiderX))
            {
                return Lang.T("Host, path, ALPN and SpiderX must not contain control characters.");
            }
            if (p.Fingerprint.Length > 0 && !IsToken(p.Fingerprint))
            {
                return Lang.T("Fingerprint must be a short name such as \"chrome\".");
            }

            if (p.Security == "reality")
            {
                if (p.Fingerprint.Length == 0)
                {
                    return Lang.T("REALITY requires a uTLS fingerprint, for example \"chrome\".");
                }
                if (p.PublicKey.Length == 0)
                {
                    return Lang.T("REALITY public key (pbk) is empty.");
                }
                if (!IsRealityPublicKey(p.PublicKey))
                {
                    return Lang.T("REALITY public key (pbk) must be 32 bytes in URL-safe Base64 (43 characters).");
                }
                if (!IsShortId(p.ShortId))
                {
                    return Lang.T("REALITY short id (sid) must be up to 16 hexadecimal digits, an even number of them.");
                }
                if (p.SpiderX.Length > 0 && p.SpiderX[0] != '/')
                {
                    return Lang.T("REALITY SpiderX must start with \"/\".");
                }
            }
            else if (p.PublicKey.Length > 0 || p.ShortId.Length > 0 || p.SpiderX.Length > 0)
            {
                return Lang.T("Public key, short id and SpiderX apply to REALITY only.");
            }
            return null;
        }

        private static bool IsValidAddress(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')
                          || c == '.' || c == '-' || c == '_' || c == ':' || c > 127;
                if (!ok || char.IsWhiteSpace(c) || char.IsControl(c))
                {
                    return false;
                }
            }
            return true;
        }

        public static bool IsUuid(string s)
        {
            if (s.Length != 36)
            {
                return false;
            }
            for (int i = 0; i < s.Length; i++)
            {
                bool dash = i == 8 || i == 13 || i == 18 || i == 23;
                if (dash ? s[i] != '-' : !PercentEncoding.IsHex(s[i]))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsToken(string s)
        {
            if (s.Length == 0 || s.Length > 32)
            {
                return false;
            }
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-'))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsRealityPublicKey(string s)
        {
            string body = s.TrimEnd('=');
            if (body.Length != 43)
            {
                return false;
            }
            for (int i = 0; i < body.Length; i++)
            {
                char c = body[i];
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-'))
                {
                    return false;
                }
            }
            try
            {
                return Convert.FromBase64String(body.Replace('-', '+').Replace('_', '/') + "=").Length == 32;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static bool IsShortId(string s)
        {
            if (s.Length > 16 || s.Length % 2 != 0)
            {
                return false;
            }
            for (int i = 0; i < s.Length; i++)
            {
                if (!PercentEncoding.IsHex(s[i]))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool HasControlChars(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                if (char.IsControl(s[i]))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
