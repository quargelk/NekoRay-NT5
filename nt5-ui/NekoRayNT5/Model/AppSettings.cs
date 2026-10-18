namespace NekoRayNT5.Model
{
    // Program settings; defaults follow Nekoray 3.17 (main/NekoGui_DataStore.hpp).
    internal sealed class AppSettings
    {
        public static readonly string[] LogLevels = new string[] { "debug", "info", "warning", "error", "none" };
        public static readonly string[] DomainStrategies = new string[] { "AsIs", "IPIfNonMatch", "IPOnDemand" };

        public const string DefaultProxyBypass =
            "localhost;127.*;10.*;172.16.*;172.17.*;172.18.*;172.19.*;172.20.*;172.21.*;172.22.*;172.23.*;" +
            "172.24.*;172.25.*;172.26.*;172.27.*;172.28.*;172.29.*;172.30.*;172.31.*;192.168.*;<local>";

        // Inbounds
        public int SocksPort = 2080;
        public int HttpPort = 2081;
        public bool AllowLan;
        public string LogLevel = "warning";

        // Routing: one rule per line, Xray syntax (domain:example.com, geosite:cn, 10.0.0.0/8, geoip:private ...)
        public string DefaultOutbound = "proxy";
        public string DomainStrategy = "AsIs";
        public bool BypassPrivate = true;
        public string DirectDomains = "";
        public string ProxyDomains = "";
        public string BlockDomains = "";
        public string DirectIps = "";
        public string ProxyIps = "";
        public string BlockIps = "";

        // DNS; an empty remote server leaves name resolution to the system and the remote side.
        public string RemoteDns = "";
        public string DirectDns = "localhost";

        // Subscriptions. Route: "auto" (through the running connection when there is one, else
        // directly), "proxy" (only through the running connection) or "direct" (never through it).
        public const string RouteAuto = "auto";
        public const string RouteProxy = "proxy";
        public const string RouteDirect = "direct";
        public string SubscriptionRoute = RouteAuto;
        // HWID of this installation for Happ-compatible subscriptions (random, made once, see Services/Hwid);
        // a non-empty custom value is used instead (a HWID the provider authorised for this user).
        public string HwidGenerated = "";
        public string HwidCustom = "";

        // Tests
        public string TestUrl = "http://cp.cloudflare.com/";
        public int TestTimeoutSeconds = 10;

        // Behaviour
        public bool CloseToTray = true;
        public bool RememberLast;
        public int LastProfileId = -1;
        public bool LastSystemProxy;
        public string SystemProxyBypass = DefaultProxyBypass;
        public int CurrentGroupId;
        // The first-run assistant was shown (or the user already had profiles).
        public bool FirstRunDone;
        // Interface language ("en" or "ru", see App.Lang); applied at the next start of the program.
        public string Language = "en";

        public string ListenAddress
        {
            get { return AllowLan ? "0.0.0.0" : "127.0.0.1"; }
        }

        public AppSettings Clone()
        {
            return (AppSettings)MemberwiseClone();
        }
    }
}
