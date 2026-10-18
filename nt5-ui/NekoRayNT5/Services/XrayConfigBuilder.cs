using System;
using NekoRayNT5.Model;

namespace NekoRayNT5.Services
{
    // Builds the JSON configuration for Xray 1.8.3 (schema of infra/conf in that version).
    // The result contains the credentials of the profile and must not be logged.
    internal static class XrayConfigBuilder
    {
        public const string TagProxy = "proxy";
        public const string TagDirect = "direct";
        public const string TagBlock = "block";

        private static readonly string[] PrivateRanges = new string[] {
            "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "127.0.0.0/8", "169.254.0.0/16",
            "::1/128", "fc00::/7", "fe80::/10" };

        // Full configuration: SOCKS5 and HTTP inbounds, routing and DNS from the settings.
        public static string Build(ProfileEntry profile, AppSettings settings)
        {
            JsonArray inbounds = new JsonArray();
            inbounds.Add(Inbound("socks-in", "socks", settings.ListenAddress, settings.SocksPort,
                new JsonObject().Add("auth", "noauth").Add("udp", true)));
            inbounds.Add(Inbound("http-in", "http", settings.ListenAddress, settings.HttpPort, new JsonObject()));

            JsonObject root = new JsonObject();
            root.Add("log", new JsonObject().Add("loglevel", settings.LogLevel));
            JsonObject dns = Dns(settings);
            if (dns != null)
            {
                root.Add("dns", dns);
            }
            root.Add("inbounds", inbounds);
            root.Add("outbounds", JsonArray.Of(
                Outbound(profile),
                new JsonObject().Add("tag", TagDirect).Add("protocol", "freedom"),
                new JsonObject().Add("tag", TagBlock).Add("protocol", "blackhole")));
            root.Add("routing", Routing(settings));
            return Json.Serialize(root);
        }

        // Minimal configuration for a latency test: one local HTTP inbound, everything through the profile.
        public static string BuildForTest(ProfileEntry profile, int httpPort)
        {
            JsonObject root = new JsonObject();
            root.Add("log", new JsonObject().Add("loglevel", "warning"));
            root.Add("inbounds", JsonArray.Of(Inbound("http-in", "http", "127.0.0.1", httpPort, new JsonObject())));
            root.Add("outbounds", JsonArray.Of(Outbound(profile)));
            return Json.Serialize(root);
        }

        private static JsonObject Inbound(string tag, string protocol, string listen, int port, JsonObject settings)
        {
            return new JsonObject()
                .Add("tag", tag)
                .Add("listen", listen)
                .Add("port", port)
                .Add("protocol", protocol)
                .Add("settings", settings)
                .Add("sniffing", new JsonObject()
                    .Add("enabled", true)
                    .Add("destOverride", JsonArray.Of("http", "tls"))
                    .Add("routeOnly", true));
        }

        private static JsonObject Outbound(ProfileEntry p)
        {
            JsonObject o = new JsonObject().Add("tag", TagProxy).Add("protocol", p.Type);
            JsonObject settings = new JsonObject();

            if (p.Type == ProfileEntry.TypeVless || p.Type == ProfileEntry.TypeVmess)
            {
                JsonObject user = new JsonObject().Add("id", p.Uuid);
                if (p.Type == ProfileEntry.TypeVless)
                {
                    user.Add("encryption", "none");
                    if (p.Flow.Length > 0)
                    {
                        user.Add("flow", p.Flow);
                    }
                }
                else
                {
                    user.Add("alterId", 0).Add("security", p.Encryption);
                }
                settings.Add("vnext", JsonArray.Of(new JsonObject()
                    .Add("address", p.Address)
                    .Add("port", p.Port)
                    .Add("users", JsonArray.Of(user))));
            }
            else
            {
                JsonObject server = new JsonObject().Add("address", p.Address).Add("port", p.Port);
                if (p.Type == ProfileEntry.TypeTrojan)
                {
                    server.Add("password", p.Password);
                }
                else if (p.Type == ProfileEntry.TypeShadowsocks)
                {
                    server.Add("method", p.Encryption).Add("password", p.Password);
                }
                else if (p.Username.Length > 0)
                {
                    server.Add("users", JsonArray.Of(new JsonObject().Add("user", p.Username).Add("pass", p.Password)));
                }
                settings.Add("servers", JsonArray.Of(server));
            }
            o.Add("settings", settings);

            if (p.UsesStream)
            {
                o.Add("streamSettings", Stream(p));
            }
            return o;
        }

        private static JsonObject Stream(ProfileEntry p)
        {
            JsonObject s = new JsonObject().Add("network", p.Network).Add("security", p.Security);

            if (p.Security == "tls")
            {
                JsonObject tls = new JsonObject();
                if (p.Sni.Length > 0)
                {
                    tls.Add("serverName", p.Sni);
                }
                tls.Add("allowInsecure", p.AllowInsecure);
                JsonArray alpn = SplitList(p.Alpn, ',');
                if (alpn.Count > 0)
                {
                    tls.Add("alpn", alpn);
                }
                if (p.Fingerprint.Length > 0)
                {
                    tls.Add("fingerprint", p.Fingerprint);
                }
                s.Add("tlsSettings", tls);
            }
            else if (p.Security == "reality")
            {
                JsonObject reality = new JsonObject()
                    .Add("serverName", p.Sni)
                    .Add("fingerprint", p.Fingerprint)
                    .Add("publicKey", p.PublicKey)
                    .Add("shortId", p.ShortId);
                if (p.SpiderX.Length > 0)
                {
                    reality.Add("spiderX", p.SpiderX);
                }
                s.Add("realitySettings", reality);
            }

            if (p.Network == "ws")
            {
                JsonObject ws = new JsonObject().Add("path", p.Path.Length > 0 ? p.Path : "/");
                if (p.Host.Length > 0)
                {
                    ws.Add("headers", new JsonObject().Add("Host", p.Host));
                }
                s.Add("wsSettings", ws);
            }
            else if (p.Network == "grpc")
            {
                s.Add("grpcSettings", new JsonObject().Add("serviceName", p.Path));
            }
            else if (p.Network == "http")
            {
                JsonObject http = new JsonObject().Add("path", p.Path.Length > 0 ? p.Path : "/");
                JsonArray hosts = SplitList(p.Host, ',');
                if (hosts.Count > 0)
                {
                    http.Add("host", hosts);
                }
                s.Add("httpSettings", http);
            }
            return s;
        }

        private static JsonObject Dns(AppSettings settings)
        {
            if (settings.RemoteDns.Length == 0)
            {
                return null;
            }
            JsonArray servers = new JsonArray();
            JsonArray directDomains = SplitLines(settings.DirectDomains);
            if (settings.DirectDns.Length > 0 && directDomains.Count > 0)
            {
                servers.Add(new JsonObject().Add("address", settings.DirectDns).Add("domains", directDomains));
            }
            servers.Add(settings.RemoteDns);
            return new JsonObject().Add("servers", servers);
        }

        // Order: block, proxy, direct, private addresses, then the default outbound.
        private static JsonObject Routing(AppSettings settings)
        {
            JsonArray rules = new JsonArray();
            AddRule(rules, "domain", SplitLines(settings.BlockDomains), TagBlock);
            AddRule(rules, "ip", SplitLines(settings.BlockIps), TagBlock);
            AddRule(rules, "domain", SplitLines(settings.ProxyDomains), TagProxy);
            AddRule(rules, "ip", SplitLines(settings.ProxyIps), TagProxy);
            AddRule(rules, "domain", SplitLines(settings.DirectDomains), TagDirect);
            AddRule(rules, "ip", SplitLines(settings.DirectIps), TagDirect);
            if (settings.BypassPrivate)
            {
                AddRule(rules, "ip", JsonArray.Of((object[])PrivateRanges), TagDirect);
            }
            rules.Add(new JsonObject()
                .Add("type", "field")
                .Add("network", "tcp,udp")
                .Add("outboundTag", settings.DefaultOutbound == "direct" ? TagDirect : TagProxy));

            return new JsonObject().Add("domainStrategy", settings.DomainStrategy).Add("rules", rules);
        }

        private static void AddRule(JsonArray rules, string key, JsonArray values, string outbound)
        {
            if (values.Count > 0)
            {
                rules.Add(new JsonObject().Add("type", "field").Add(key, values).Add("outboundTag", outbound));
            }
        }

        private static JsonArray SplitLines(string text)
        {
            JsonArray result = new JsonArray();
            string[] lines = text.Split(new char[] { '\r', '\n' });
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length > 0 && !line.StartsWith("#"))
                {
                    result.Add(line);
                }
            }
            return result;
        }

        private static JsonArray SplitList(string text, char separator)
        {
            JsonArray result = new JsonArray();
            string[] parts = text.Split(separator);
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Trim().Length > 0)
                {
                    result.Add(parts[i].Trim());
                }
            }
            return result;
        }
    }
}
