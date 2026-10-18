namespace NekoRayNT5.Model
{
    // A tab of the main window. A group with a URL is a subscription.
    internal sealed class ProfileGroup
    {
        public const int DefaultId = 0;

        private int _id;
        private string _name = "";
        private string _url = "";
        private string _lastUpdate = "";
        private string _lastError = "";
        private bool _useHwid;
        private string _info = "";

        public ProfileGroup(int id, string name)
        {
            _id = id;
            _name = name;
        }

        public int Id { get { return _id; } set { _id = value; } }
        public string Name { get { return _name; } set { _name = value; } }
        public string Url { get { return _url; } set { _url = value; } }
        // Local time of the last successful subscription update, for display only.
        public string LastUpdate { get { return _lastUpdate; } set { _lastUpdate = value; } }
        // Reason of the last failed update ("" after a success); never contains the URL.
        public string LastError { get { return _lastError; } set { _lastError = value; } }

        // Send the Happ-compatible HWID headers when downloading this subscription.
        public bool UseHwid { get { return _useHwid; } set { _useHwid = value; } }
        // What the provider reported at the last success (traffic, expiry); for display only.
        public string Info { get { return _info; } set { _info = value; } }

        public bool IsSubscription
        {
            get { return _url.Length > 0; }
        }

        public ProfileGroup Clone()
        {
            return (ProfileGroup)MemberwiseClone();
        }
    }
}
