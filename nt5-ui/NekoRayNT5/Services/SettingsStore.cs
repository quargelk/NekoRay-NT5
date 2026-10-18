using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using NekoRayNT5.Model;
using NekoRayNT5.App;

namespace NekoRayNT5.Services
{
    // Program settings in "settings.xml". A file that cannot be read is copied aside
    // and defaults are used; unknown or invalid values fall back to defaults one by one.
    internal sealed class SettingsStore
    {
        private const string RootName = "NekoRayNT5Settings";
        private const int FormatVersion = 1;

        private readonly string _path;

        public SettingsStore(string path)
        {
            _path = path;
        }

        // "warning" receives a message for the user or null.
        public AppSettings Load(out string warning)
        {
            warning = null;
            AppSettings s = new AppSettings();
            if (!File.Exists(_path))
            {
                return s;
            }
            XmlDocument doc = new XmlDocument();
            doc.XmlResolver = null;
            try
            {
                doc.Load(_path);
                if (doc.DocumentElement == null || doc.DocumentElement.Name != RootName)
                {
                    throw new XmlException("Unknown root element.");
                }
            }
            catch (XmlException)
            {
                try
                {
                    warning = Lang.F("The settings file is damaged; default settings are used. The file was copied to:\r\n{0}",
                              SafeFile.CopyAside(_path, "damaged"));
                }
                catch (Exception)
                {
                    warning = Lang.T("The settings file is damaged; default settings are used.");
                }
                return s;
            }
            catch (Exception ex)
            {
                warning = Lang.F("The settings file could not be opened; default settings are used: {0}", ex.Message);
                return s;
            }

            XmlElement r = doc.DocumentElement;
            s.SocksPort = Port(r, "socksPort", s.SocksPort);
            s.HttpPort = Port(r, "httpPort", s.HttpPort);
            s.AllowLan = Bool(r, "allowLan", s.AllowLan);
            s.LogLevel = OneOf(r, "logLevel", AppSettings.LogLevels, s.LogLevel);
            s.DefaultOutbound = OneOf(r, "defaultOutbound", new string[] { "proxy", "direct" }, s.DefaultOutbound);
            s.DomainStrategy = OneOf(r, "domainStrategy", AppSettings.DomainStrategies, s.DomainStrategy);
            s.BypassPrivate = Bool(r, "bypassPrivate", s.BypassPrivate);
            s.DirectDomains = Str(r, "directDomains", s.DirectDomains);
            s.ProxyDomains = Str(r, "proxyDomains", s.ProxyDomains);
            s.BlockDomains = Str(r, "blockDomains", s.BlockDomains);
            s.DirectIps = Str(r, "directIps", s.DirectIps);
            s.ProxyIps = Str(r, "proxyIps", s.ProxyIps);
            s.BlockIps = Str(r, "blockIps", s.BlockIps);
            s.RemoteDns = Str(r, "remoteDns", s.RemoteDns).Trim();
            s.DirectDns = Str(r, "directDns", s.DirectDns).Trim();
            // tunMode, tunAdapter and tunDns of versions 0.9.x are ignored: TUN mode was removed.
            s.TestUrl = Str(r, "testUrl", s.TestUrl).Trim();
            s.TestTimeoutSeconds = Int(r, "testTimeoutSeconds", s.TestTimeoutSeconds, 1, 120);
            s.CloseToTray = Bool(r, "closeToTray", s.CloseToTray);
            s.RememberLast = Bool(r, "rememberLast", s.RememberLast);
            s.LastProfileId = Int(r, "lastProfileId", s.LastProfileId, -1, int.MaxValue);
            s.LastSystemProxy = Bool(r, "lastSystemProxy", s.LastSystemProxy);
            s.SystemProxyBypass = Str(r, "systemProxyBypass", s.SystemProxyBypass).Trim();
            s.CurrentGroupId = Int(r, "currentGroupId", s.CurrentGroupId, 0, int.MaxValue);
            s.FirstRunDone = Bool(r, "firstRunDone", s.FirstRunDone);
            s.Language = OneOf(r, "language", NekoRayNT5.App.Lang.Codes, s.Language);
            string route = Str(r, "subscriptionRoute", s.SubscriptionRoute).Trim();
            s.SubscriptionRoute = route == AppSettings.RouteProxy || route == AppSettings.RouteDirect ? route : AppSettings.RouteAuto;
            s.HwidGenerated = Str(r, "hwid", s.HwidGenerated).Trim();
            if (Hwid.Check(s.HwidGenerated) != null)
            {
                s.HwidGenerated = "";
            }
            s.HwidCustom = Str(r, "hwidCustom", s.HwidCustom).Trim();
            if (Hwid.Check(s.HwidCustom) != null)
            {
                s.HwidCustom = "";
            }
            if (s.HttpPort == s.SocksPort)
            {
                s.HttpPort = new AppSettings().HttpPort;
                s.SocksPort = new AppSettings().SocksPort;
            }
            return s;
        }

        public void Save(AppSettings s)
        {
            SafeFile.EnsureDirectoryFor(_path);
            string temp = _path + ".tmp";
            using (XmlTextWriter w = new XmlTextWriter(temp, new UTF8Encoding(false)))
            {
                w.Formatting = Formatting.Indented;
                w.WriteStartDocument();
                w.WriteStartElement(RootName);
                w.WriteAttributeString("formatVersion", FormatVersion.ToString(CultureInfo.InvariantCulture));
                Put(w, "socksPort", s.SocksPort);
                Put(w, "httpPort", s.HttpPort);
                Put(w, "allowLan", s.AllowLan);
                w.WriteElementString("logLevel", s.LogLevel);
                w.WriteElementString("defaultOutbound", s.DefaultOutbound);
                w.WriteElementString("domainStrategy", s.DomainStrategy);
                Put(w, "bypassPrivate", s.BypassPrivate);
                w.WriteElementString("directDomains", s.DirectDomains);
                w.WriteElementString("proxyDomains", s.ProxyDomains);
                w.WriteElementString("blockDomains", s.BlockDomains);
                w.WriteElementString("directIps", s.DirectIps);
                w.WriteElementString("proxyIps", s.ProxyIps);
                w.WriteElementString("blockIps", s.BlockIps);
                w.WriteElementString("remoteDns", s.RemoteDns);
                w.WriteElementString("directDns", s.DirectDns);
                w.WriteElementString("testUrl", s.TestUrl);
                Put(w, "testTimeoutSeconds", s.TestTimeoutSeconds);
                Put(w, "closeToTray", s.CloseToTray);
                Put(w, "rememberLast", s.RememberLast);
                Put(w, "lastProfileId", s.LastProfileId);
                Put(w, "lastSystemProxy", s.LastSystemProxy);
                w.WriteElementString("systemProxyBypass", s.SystemProxyBypass);
                Put(w, "currentGroupId", s.CurrentGroupId);
                Put(w, "firstRunDone", s.FirstRunDone);
                w.WriteElementString("language", s.Language);
                w.WriteElementString("subscriptionRoute", s.SubscriptionRoute);
                w.WriteElementString("hwid", s.HwidGenerated);
                w.WriteElementString("hwidCustom", s.HwidCustom);
                w.WriteEndElement();
                w.WriteEndDocument();
            }
            SafeFile.Replace(temp, _path);
        }

        private static void Put(XmlWriter w, string name, int value)
        {
            w.WriteElementString(name, value.ToString(CultureInfo.InvariantCulture));
        }

        private static void Put(XmlWriter w, string name, bool value)
        {
            w.WriteElementString(name, value ? "true" : "false");
        }

        private static string Str(XmlElement r, string name, string fallback)
        {
            XmlElement e = r[name];
            return e == null ? fallback : e.InnerText.Replace("\r\n", "\n").Replace("\n", "\r\n");
        }

        private static bool Bool(XmlElement r, string name, bool fallback)
        {
            string v = Str(r, name, "");
            return v == "true" ? true : (v == "false" ? false : fallback);
        }

        private static int Int(XmlElement r, string name, int fallback, int min, int max)
        {
            int v;
            if (int.TryParse(Str(r, name, ""), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out v)
                && v >= min && v <= max)
            {
                return v;
            }
            return fallback;
        }

        private static int Port(XmlElement r, string name, int fallback)
        {
            return Int(r, name, fallback, 1, 65535);
        }

        private static string OneOf(XmlElement r, string name, string[] allowed, string fallback)
        {
            string v = Str(r, name, "");
            return Array.IndexOf(allowed, v) >= 0 ? v : fallback;
        }
    }
}
