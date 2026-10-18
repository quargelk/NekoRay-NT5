namespace NekoRayNT5.Model
{
    // One outbound profile. Uuid, Password, PublicKey and ShortId are private data:
    // they must never be written to the log.
    internal sealed class ProfileEntry
    {
        public const string TypeVless = "vless";
        public const string TypeVmess = "vmess";
        public const string TypeTrojan = "trojan";
        public const string TypeShadowsocks = "shadowsocks";
        public const string TypeSocks = "socks";
        public const string TypeHttp = "http";

        public static readonly string[] Types = new string[] {
            TypeVless, TypeVmess, TypeTrojan, TypeShadowsocks, TypeSocks, TypeHttp };

        public const string DefaultFingerprint = "chrome";

        private int _id = -1;
        private int _groupId;
        private string _type = TypeVless;
        private string _name = "";
        private string _address = "";
        private int _port = 443;
        private string _uuid = "";
        private string _username = "";
        private string _password = "";
        private string _encryption = "";
        private string _flow = "";
        private string _network = "tcp";
        private string _host = "";
        private string _path = "";
        private string _security = "none";
        private string _sni = "";
        private string _alpn = "";
        private string _fingerprint = "";
        private bool _allowInsecure;
        private string _publicKey = "";
        private string _shortId = "";
        private string _spiderX = "";

        // True when the profile came from the subscription of its group (and may be removed by an update);
        // false for profiles added by hand.
        private bool _fromSubscription;

        // Result of the last latency test in ms, 0 = not tested, -1 = failed; the time is local, for display.
        private int _latency;
        private string _latencyNote = "";
        private string _testedAt = "";

        public int Id { get { return _id; } set { _id = value; } }
        public int GroupId { get { return _groupId; } set { _groupId = value; } }
        public string Type { get { return _type; } set { _type = value; } }
        public string Name { get { return _name; } set { _name = value; } }
        public string Address { get { return _address; } set { _address = value; } }
        public int Port { get { return _port; } set { _port = value; } }
        // VLESS / VMess user id.
        public string Uuid { get { return _uuid; } set { _uuid = value; } }
        // SOCKS / HTTP user name.
        public string Username { get { return _username; } set { _username = value; } }
        // Trojan / Shadowsocks / SOCKS / HTTP password.
        public string Password { get { return _password; } set { _password = value; } }
        // VLESS: "none"; VMess: security; Shadowsocks: method.
        public string Encryption { get { return _encryption; } set { _encryption = value; } }
        public string Flow { get { return _flow; } set { _flow = value; } }
        // tcp, ws, grpc, http (HTTP/2).
        public string Network { get { return _network; } set { _network = value; } }
        // ws / http: Host header.
        public string Host { get { return _host; } set { _host = value; } }
        // ws / http: path; grpc: service name.
        public string Path { get { return _path; } set { _path = value; } }
        // none, tls, reality.
        public string Security { get { return _security; } set { _security = value; } }
        public string Sni { get { return _sni; } set { _sni = value; } }
        // Comma separated.
        public string Alpn { get { return _alpn; } set { _alpn = value; } }
        public string Fingerprint { get { return _fingerprint; } set { _fingerprint = value; } }
        public bool AllowInsecure { get { return _allowInsecure; } set { _allowInsecure = value; } }
        public string PublicKey { get { return _publicKey; } set { _publicKey = value; } }
        public string ShortId { get { return _shortId; } set { _shortId = value; } }
        public string SpiderX { get { return _spiderX; } set { _spiderX = value; } }

        public bool FromSubscription { get { return _fromSubscription; } set { _fromSubscription = value; } }

        public int Latency { get { return _latency; } set { _latency = value; } }
        public string LatencyNote { get { return _latencyNote; } set { _latencyNote = value; } }
        public string TestedAt { get { return _testedAt; } set { _testedAt = value; } }

        public bool UsesUuid
        {
            get { return _type == TypeVless || _type == TypeVmess; }
        }

        public bool UsesPassword
        {
            get { return _type == TypeTrojan || _type == TypeShadowsocks || _type == TypeSocks || _type == TypeHttp; }
        }

        public bool UsesUsername
        {
            get { return _type == TypeSocks || _type == TypeHttp; }
        }

        // Transport and TLS settings apply to these types only.
        public bool UsesStream
        {
            get { return _type == TypeVless || _type == TypeVmess || _type == TypeTrojan; }
        }

        public string DisplayType
        {
            get
            {
                string text = _type == TypeShadowsocks ? "SS" : _type.ToUpperInvariant();
                if (UsesStream)
                {
                    if (_network != "tcp")
                    {
                        text += " " + _network;
                    }
                    if (_security != "none")
                    {
                        text += " " + _security;
                    }
                }
                return text;
            }
        }

        public string DisplayAddress
        {
            get
            {
                if (_address.IndexOf(':') >= 0)
                {
                    return "[" + _address + "]:" + _port;
                }
                return _address + ":" + _port;
            }
        }

        public string DisplayLatency
        {
            get
            {
                string when = _testedAt.Length > 0 ? "  [" + _testedAt + "]" : "";
                if (_latency > 0)
                {
                    return _latency + " ms" + (_latencyNote.Length > 0 ? " (" + _latencyNote + ")" : "") + when;
                }
                if (_latency < 0)
                {
                    return (_latencyNote.Length > 0 ? "Failed: " + _latencyNote : "Failed") + when;
                }
                return "";
            }
        }

        public void ClearTest()
        {
            _latency = 0;
            _latencyNote = "";
            _testedAt = "";
        }

        // Default field values of a new profile of the given type.
        public static ProfileEntry Create(string type)
        {
            ProfileEntry p = new ProfileEntry();
            p.Type = type;
            if (type == TypeVless)
            {
                p.Encryption = "none";
                p.Security = "reality";
                p.Fingerprint = DefaultFingerprint;
            }
            else if (type == TypeVmess)
            {
                p.Encryption = "auto";
            }
            else if (type == TypeTrojan)
            {
                p.Security = "tls";
            }
            else if (type == TypeShadowsocks)
            {
                p.Encryption = "aes-256-gcm";
                p.Port = 8388;
            }
            else if (type == TypeSocks)
            {
                p.Port = 1080;
            }
            else if (type == TypeHttp)
            {
                p.Port = 8080;
            }
            return p;
        }

        public ProfileEntry Clone()
        {
            return (ProfileEntry)MemberwiseClone();
        }
    }
}
