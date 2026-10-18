using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NekoRayNT5.Model;
using NekoRayNT5.App;

namespace NekoRayNT5.Services
{
    // The message is meant for the user and never contains the link or its private parts.
    internal sealed class LinkFormatException : Exception
    {
        public LinkFormatException(string message)
            : base(message)
        {
        }
    }

    // Share links: vless://, vmess://, trojan://, ss://.
    // Field mapping follows fmt/Link2Bean.cpp and fmt/Bean2Link.cpp of Nekoray 3.17.
    internal static class ShareLinks
    {
        private sealed class UrlParts
        {
            public string UserInfo = "";
            public string Host = "";
            public string Port = "";
            public string Fragment = "";
            public Dictionary<string, string> Query = new Dictionary<string, string>();
        }

        public static ProfileEntry Parse(string link)
        {
            string s = link == null ? "" : link.Trim();
            if (s.Length == 0)
            {
                throw new LinkFormatException(Lang.T("The link is empty."));
            }
            int sep = s.IndexOf("://", StringComparison.Ordinal);
            string scheme = sep > 0 ? s.Substring(0, sep).ToLowerInvariant() : "";
            string rest = sep > 0 ? s.Substring(sep + 3) : "";

            ProfileEntry p;
            switch (scheme)
            {
                case "vless":
                    p = ParseUrlStyle(ProfileEntry.TypeVless, rest);
                    break;
                case "trojan":
                    p = ParseUrlStyle(ProfileEntry.TypeTrojan, rest);
                    break;
                case "vmess":
                    p = ParseVmess(rest);
                    break;
                case "ss":
                    p = ParseShadowsocks(rest);
                    break;
                case "socks":
                case "socks5":
                    p = ParseSocks(rest);
                    break;
                default:
                    throw new LinkFormatException(Lang.T("Unsupported link: it must start with vless://, vmess://, trojan://, ss:// or socks://"));
            }

            if (p.Name.Length == 0)
            {
                p.Name = p.DisplayAddress;
            }
            string error = ProfileValidator.Validate(p);
            if (error != null)
            {
                throw new LinkFormatException(error);
            }
            return p;
        }

        // Splits a text into lines and parses every line that is a share link. "problems" receives the
        // reasons (without the links themselves) and counts the lines that are not links at all.
        public static List<ProfileEntry> ParseMany(string text, LinkProblems problems)
        {
            List<ProfileEntry> result = new List<ProfileEntry>();
            string[] lines = text.Split(new char[] { '\r', '\n' });
            int number = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0)
                {
                    continue;
                }
                number++;
                int start = LinkStart(line);
                if (start < 0)
                {
                    problems.AddNotLink();
                    continue;
                }
                string link = line.Substring(start);
                string lower = link.ToLowerInvariant();
                if (lower.StartsWith("http://") || lower.StartsWith("https://"))
                {
                    problems.AddFailed(number, Lang.T("a web address inside a list of links. Add a subscription address on its own."));
                    continue;
                }
                try
                {
                    result.Add(Parse(link));
                }
                catch (LinkFormatException ex)
                {
                    problems.AddFailed(number, ex.Message);
                }
            }
            return result;
        }

        // Where the link starts in a trimmed line, or -1 when the line is not a link. The scheme
        // ("vless://"...) must open the line; only list marks copied from a formatted list may come
        // before it: numbers, "1.", "2)", "-", "*", "+", ">", bullets and dashes. A line that starts with
        // other text ("See https://...") or with "#" (a comment) is not a link.
        public static int LinkStart(string line)
        {
            int sep = line.IndexOf("://", StringComparison.Ordinal);
            if (sep <= 0)
            {
                return -1;
            }
            int start = sep;
            while (start > 0 && IsSchemeChar(line[start - 1]))
            {
                start--;
            }
            // "1.vless://" or "-vless://": the scheme begins at the first letter
            while (start < sep && !IsAsciiLetter(line[start]))
            {
                start++;
            }
            if (start == sep)
            {
                return -1;
            }
            for (int i = 0; i < start; i++)
            {
                if (!IsListMark(line[i]))
                {
                    return -1;
                }
            }
            return start;
        }

        private static bool IsAsciiLetter(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        }

        private static bool IsSchemeChar(char c)
        {
            return IsAsciiLetter(c) || (c >= '0' && c <= '9') || c == '+' || c == '-';
        }

        private static bool IsListMark(char c)
        {
            // U+2022 bullet, U+00B7 middle dot, U+25CF black circle, U+2013 en dash, U+2014 em dash
            return char.IsWhiteSpace(c) || (c >= '0' && c <= '9') || "-*+.)>".IndexOf(c) >= 0
                   || c == (char)0x2022 || c == (char)0x00B7 || c == (char)0x25CF || c == (char)0x2013 || c == (char)0x2014;
        }

        // ---- vless:// and trojan:// ----

        private static ProfileEntry ParseUrlStyle(string type, string rest)
        {
            UrlParts u = Split(rest);
            ProfileEntry p = new ProfileEntry();
            p.Type = type;
            if (rest.LastIndexOf('@') < 0)
            {
                throw new LinkFormatException(type == ProfileEntry.TypeVless
                    ? Lang.T("The link has no user ID: expected vless://UUID@server:port")
                    : Lang.T("The link has no password: expected trojan://password@server:port"));
            }
            if (type == ProfileEntry.TypeVless)
            {
                p.Uuid = u.UserInfo.Trim();
                p.Encryption = Get(u.Query, "encryption", "none");
                p.Flow = Get(u.Query, "flow", "");
            }
            else
            {
                p.Password = u.UserInfo;
            }
            SetHostPort(p, u);
            ReadStream(p, u.Query, type == ProfileEntry.TypeTrojan ? "tls" : "none");
            p.Name = u.Fragment;
            return p;
        }

        private static void ReadStream(ProfileEntry p, Dictionary<string, string> q, string defaultSecurity)
        {
            string network = Get(q, "type", "tcp");
            p.Network = network == "h2" ? "http" : network;
            if (Array.IndexOf(ProfileValidator.Networks, p.Network) < 0)
            {
                throw new LinkFormatException(Lang.F("Unsupported transport \"{0}\". Supported: tcp, ws, grpc, http.", Shorten(network)));
            }
            string headerType = Get(q, "headerType", "none");
            if (headerType != "none")
            {
                throw new LinkFormatException(Lang.F("Unsupported TCP header type \"{0}\".", Shorten(headerType)));
            }
            p.Security = Get(q, "security", defaultSecurity);
            if (Array.IndexOf(ProfileValidator.Securities, p.Security) < 0)
            {
                throw new LinkFormatException(Lang.F("Unsupported security \"{0}\". Supported: none, tls, reality.", Shorten(p.Security)));
            }
            p.Host = Get(q, "host", "");
            p.Path = p.Network == "grpc" ? Get(q, "serviceName", "") : Get(q, "path", "");
            if (p.Security != "none")
            {
                p.Sni = Get(q, "sni", Get(q, "peer", ""));
                p.Alpn = Get(q, "alpn", "");
                p.Fingerprint = Get(q, "fp", p.Security == "reality" ? ProfileEntry.DefaultFingerprint : "");
                string insecure = Get(q, "allowInsecure", Get(q, "insecure", "0"));
                p.AllowInsecure = insecure == "1" || insecure == "true";
            }
            if (p.Security == "reality")
            {
                p.PublicKey = Get(q, "pbk", "");
                p.ShortId = Get(q, "sid", "");
                p.SpiderX = Get(q, "spx", "");
            }
        }

        // ---- vmess:// ----

        private static ProfileEntry ParseVmess(string rest)
        {
            string body = rest;
            int hash = body.IndexOf('#');
            if (hash >= 0)
            {
                body = body.Substring(0, hash);
            }

            JsonObject json = null;
            try
            {
                json = Json.Parse(PercentEncoding.DecodeBase64Text(body)) as JsonObject;
            }
            catch (FormatException)
            {
            }
            if (json == null)
            {
                if (rest.IndexOf('@') > 0)
                {
                    return ParseVmessUrl(rest);
                }
                throw new LinkFormatException(Lang.T("The VMess link is damaged: Base64 JSON data expected."));
            }

            ProfileEntry p = new ProfileEntry();
            p.Type = ProfileEntry.TypeVmess;
            p.Name = json.GetString("ps").Trim();
            p.Address = json.GetString("add").Trim();
            p.Port = ParsePort(json.GetString("port").Trim());
            p.Uuid = json.GetString("id").Trim();
            string aid = json.GetString("aid").Trim();
            if (aid.Length > 0 && aid != "0")
            {
                throw new LinkFormatException(Lang.T("VMess with alterId other than 0 (legacy MD5 mode) is not supported."));
            }
            p.Encryption = Or(json.GetString("scy"), "auto");

            Dictionary<string, string> q = new Dictionary<string, string>();
            q["type"] = Or(json.GetString("net"), "tcp");
            q["headerType"] = Or(json.GetString("type"), "none");
            q["security"] = Or(json.GetString("tls"), "none");
            q["host"] = json.GetString("host");
            q["path"] = json.GetString("path");
            q["serviceName"] = json.GetString("path");
            q["sni"] = json.GetString("sni");
            q["alpn"] = json.GetString("alpn");
            q["fp"] = json.GetString("fp");
            q["pbk"] = json.GetString("pbk");
            q["sid"] = json.GetString("sid");
            q["spx"] = json.GetString("spx");
            ReadStream(p, q, "none");
            return p;
        }

        // vmess://uuid@host:port?encryption=auto&type=...#name
        private static ProfileEntry ParseVmessUrl(string rest)
        {
            UrlParts u = Split(rest);
            ProfileEntry p = new ProfileEntry();
            p.Type = ProfileEntry.TypeVmess;
            p.Uuid = u.UserInfo.Trim();
            p.Encryption = Get(u.Query, "encryption", "auto");
            SetHostPort(p, u);
            ReadStream(p, u.Query, "none");
            p.Name = u.Fragment;
            return p;
        }

        // ---- ss:// ----

        private static ProfileEntry ParseShadowsocks(string rest)
        {
            string name = "";
            int hash = rest.IndexOf('#');
            if (hash >= 0)
            {
                name = Decode(rest.Substring(hash + 1), Lang.T("profile name")).Trim();
                rest = rest.Substring(0, hash);
            }
            if (rest.IndexOf('@') < 0)
            {
                // legacy form: the whole body is Base64 of method:password@host:port
                try
                {
                    rest = PercentEncoding.DecodeBase64Text(rest);
                }
                catch (FormatException)
                {
                    throw new LinkFormatException(Lang.T("The Shadowsocks link is damaged."));
                }
            }

            UrlParts u = Split(rest);
            if (u.Query.ContainsKey("plugin"))
            {
                throw new LinkFormatException(Lang.T("Shadowsocks plugins are not supported."));
            }
            string credentials = u.UserInfo;
            if (credentials.IndexOf(':') < 0)
            {
                try
                {
                    credentials = PercentEncoding.DecodeBase64Text(credentials);
                }
                catch (FormatException)
                {
                    throw new LinkFormatException(Lang.T("The Shadowsocks link is damaged: method and password expected."));
                }
            }
            int colon = credentials.IndexOf(':');
            if (colon <= 0)
            {
                throw new LinkFormatException(Lang.T("The Shadowsocks link is damaged: method and password expected."));
            }

            ProfileEntry p = new ProfileEntry();
            p.Type = ProfileEntry.TypeShadowsocks;
            p.Encryption = credentials.Substring(0, colon).ToLowerInvariant();
            p.Password = credentials.Substring(colon + 1);
            SetHostPort(p, u);
            p.Name = name;
            return p;
        }

        // ---- export ----

        // Throws NotSupportedException for profile types that have no share link format here.
        public static string Export(ProfileEntry p)
        {
            string host = p.Address.IndexOf(':') >= 0 ? "[" + p.Address + "]" : p.Address;
            string hostPort = host + ":" + p.Port.ToString(CultureInfo.InvariantCulture);
            string name = "#" + PercentEncoding.Encode(p.Name);

            if (p.Type == ProfileEntry.TypeVless)
            {
                StringBuilder q = new StringBuilder();
                Append(q, "encryption", p.Encryption);
                Append(q, "flow", p.Flow);
                AppendStream(q, p);
                return "vless://" + p.Uuid + "@" + hostPort + "?" + q + name;
            }
            if (p.Type == ProfileEntry.TypeTrojan)
            {
                StringBuilder q = new StringBuilder();
                AppendStream(q, p);
                return "trojan://" + PercentEncoding.Encode(p.Password) + "@" + hostPort + "?" + q + name;
            }
            if (p.Type == ProfileEntry.TypeSocks)
            {
                string user = p.Username.Length > 0 ? PercentEncoding.EncodeBase64Url(p.Username + ":" + p.Password) + "@" : "";
                return "socks://" + user + hostPort + name;
            }
            if (p.Type == ProfileEntry.TypeShadowsocks)
            {
                return "ss://" + PercentEncoding.EncodeBase64Url(p.Encryption + ":" + p.Password) + "@" + hostPort + name;
            }
            if (p.Type == ProfileEntry.TypeVmess)
            {
                JsonObject o = new JsonObject();
                o.Add("v", "2");
                o.Add("ps", p.Name);
                o.Add("add", p.Address);
                o.Add("port", p.Port.ToString(CultureInfo.InvariantCulture));
                o.Add("id", p.Uuid);
                o.Add("aid", "0");
                o.Add("scy", p.Encryption);
                o.Add("net", p.Network == "http" ? "h2" : p.Network);
                o.Add("type", "none");
                o.Add("host", p.Host);
                o.Add("path", p.Path);
                o.Add("tls", p.Security == "none" ? "" : p.Security);
                o.Add("sni", p.Sni);
                o.Add("alpn", p.Alpn);
                o.Add("fp", p.Fingerprint);
                if (p.Security == "reality")
                {
                    o.Add("pbk", p.PublicKey);
                    o.Add("sid", p.ShortId);
                    o.Add("spx", p.SpiderX);
                }
                return "vmess://" + Convert.ToBase64String(Encoding.UTF8.GetBytes(Json.Serialize(o)));
            }
            throw new NotSupportedException(Lang.F("Profiles of type {0} have no share link format.", p.Type.ToUpperInvariant()));
        }

        private static void AppendStream(StringBuilder q, ProfileEntry p)
        {
            Append(q, "type", p.Network);
            Append(q, "security", p.Security);
            Append(q, "host", p.Host);
            Append(q, p.Network == "grpc" ? "serviceName" : "path", p.Path);
            if (p.Security != "none")
            {
                Append(q, "sni", p.Sni);
                Append(q, "alpn", p.Alpn);
                Append(q, "fp", p.Fingerprint);
                if (p.AllowInsecure)
                {
                    Append(q, "allowInsecure", "1");
                }
            }
            if (p.Security == "reality")
            {
                Append(q, "pbk", p.PublicKey);
                Append(q, "sid", p.ShortId);
                Append(q, "spx", p.SpiderX);
            }
        }

        private static void Append(StringBuilder q, string key, string value)
        {
            if (value.Length == 0)
            {
                return;
            }
            if (q.Length > 0)
            {
                q.Append('&');
            }
            q.Append(key).Append('=').Append(PercentEncoding.Encode(value));
        }

        // ---- helpers ----

        // ---- socks:// ----

        // socks://BASE64(user:pass)@host:port#name (v2rayN), socks5://user:pass@host:port, or no user part.
        private static ProfileEntry ParseSocks(string rest)
        {
            UrlParts u = Split(rest);
            ProfileEntry p = new ProfileEntry();
            p.Type = ProfileEntry.TypeSocks;
            string credentials = u.UserInfo;
            if (credentials.Length > 0 && credentials.IndexOf(':') < 0)
            {
                try
                {
                    credentials = PercentEncoding.DecodeBase64Text(credentials);
                }
                catch (FormatException)
                {
                    throw new LinkFormatException(Lang.T("The SOCKS link is damaged: user name and password expected."));
                }
            }
            if (credentials.Length > 0)
            {
                int colon = credentials.IndexOf(':');
                p.Username = colon >= 0 ? credentials.Substring(0, colon) : credentials;
                p.Password = colon >= 0 ? credentials.Substring(colon + 1) : "";
            }
            SetHostPort(p, u);
            p.Name = u.Fragment;
            return p;
        }

        // userinfo@host:port/path?query#fragment; every part is URL-decoded.
        private static UrlParts Split(string rest)
        {
            UrlParts u = new UrlParts();
            int hash = rest.IndexOf('#');
            if (hash >= 0)
            {
                u.Fragment = Decode(rest.Substring(hash + 1), Lang.T("profile name")).Trim();
                rest = rest.Substring(0, hash);
            }
            int question = rest.IndexOf('?');
            if (question >= 0)
            {
                ParseQuery(rest.Substring(question + 1), u.Query);
                rest = rest.Substring(0, question);
            }
            int at = rest.LastIndexOf('@');
            if (at >= 0)
            {
                u.UserInfo = Decode(rest.Substring(0, at), Lang.T("user part"));
                rest = rest.Substring(at + 1);
            }
            int slash = rest.IndexOf('/');
            if (slash >= 0)
            {
                rest = rest.Substring(0, slash);
            }

            if (rest.StartsWith("["))
            {
                int close = rest.IndexOf(']');
                if (close < 0)
                {
                    throw new LinkFormatException(Lang.T("Invalid IPv6 server address: closing bracket is missing."));
                }
                u.Host = rest.Substring(1, close - 1);
                string tail = rest.Substring(close + 1);
                if (tail.Length > 0 && tail[0] != ':')
                {
                    throw new LinkFormatException(Lang.T("Invalid server address."));
                }
                u.Port = tail.Length > 0 ? tail.Substring(1) : "";
            }
            else
            {
                int colon = rest.LastIndexOf(':');
                u.Host = colon >= 0 ? rest.Substring(0, colon) : rest;
                u.Port = colon >= 0 ? rest.Substring(colon + 1) : "";
                if (u.Host.IndexOf(':') >= 0)
                {
                    throw new LinkFormatException(Lang.T("Invalid server address: an IPv6 address must be in [brackets]."));
                }
            }
            u.Host = Decode(u.Host, Lang.T("server address")).Trim();
            return u;
        }

        private static void SetHostPort(ProfileEntry p, UrlParts u)
        {
            p.Address = u.Host;
            if (u.Port.Length == 0)
            {
                throw new LinkFormatException(Lang.T("The link has no port: expected server:port"));
            }
            p.Port = ParsePort(u.Port);
        }

        private static int ParsePort(string text)
        {
            int number;
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out number))
            {
                throw new LinkFormatException(Lang.T("Port must be a number from 1 to 65535."));
            }
            return number;
        }

        // The first occurrence of a parameter wins.
        private static void ParseQuery(string query, Dictionary<string, string> result)
        {
            string[] pairs = query.Split('&');
            for (int i = 0; i < pairs.Length; i++)
            {
                string pair = pairs[i];
                if (pair.Length == 0)
                {
                    continue;
                }
                int eq = pair.IndexOf('=');
                string key = Decode(eq >= 0 ? pair.Substring(0, eq) : pair, Lang.T("parameter name"));
                string value = eq >= 0 ? Decode(pair.Substring(eq + 1), Lang.F("parameter \"{0}\"", Shorten(key))).Trim() : "";
                if (!result.ContainsKey(key))
                {
                    result.Add(key, value);
                }
            }
        }

        private static string Get(Dictionary<string, string> args, string key, string fallback)
        {
            string value;
            if (args.TryGetValue(key, out value) && value.Length > 0)
            {
                return value;
            }
            return fallback;
        }

        private static string Or(string value, string fallback)
        {
            value = value.Trim();
            return value.Length > 0 ? value : fallback;
        }

        private static string Decode(string text, string what)
        {
            try
            {
                return PercentEncoding.Decode(text);
            }
            catch (FormatException)
            {
                throw new LinkFormatException(Lang.F("Invalid URL encoding in the {0}.", what));
            }
        }

        private static string Shorten(string value)
        {
            return value.Length <= 24 ? value : value.Substring(0, 24) + "...";
        }
    }
}
