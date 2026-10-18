using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using NekoRayNT5.App;

namespace NekoRayNT5.Services
{
    // Proxy settings of one connection; Connection == null is the LAN connection.
    internal sealed class ProxyConnectionSettings
    {
        public const int FlagDirect = 1;
        public const int FlagProxy = 2;
        public const int FlagAutoProxyUrl = 4;
        public const int FlagAutoDetect = 8;

        public string Connection;
        public int Flags = FlagDirect;
        public string Server = "";
        public string Bypass = "";
        public string PacUrl = "";

        public bool SameValues(ProxyConnectionSettings other)
        {
            return Flags == other.Flags && Server == other.Server && Bypass == other.Bypass && PacUrl == other.PacUrl;
        }
    }

    internal enum SystemProxyStatus
    {
        // Not set by this program.
        Off,
        // Set by this program and still in place.
        Applied,
        // Set by this program, but the LAN settings were changed since (by the user or another program).
        ChangedOutside
    }

    internal interface IProxyBackend
    {
        string[] ListConnections();
        ProxyConnectionSettings Query(string connection);
        void Apply(ProxyConnectionSettings settings);
        void NotifyChanged();
    }

    // Turns the system proxy on and puts the user's own settings back afterwards.
    //
    // The original settings of every connection are written to a backup file before anything
    // is changed, and the file is removed only after they have been restored. If the program
    // dies in between, the next start finds the file and restores from it.
    // A connection whose settings were changed by somebody else in the meantime is left alone.
    internal sealed class SystemProxyManager
    {
        private const string RootName = "NekoRayNT5ProxyBackup";

        private readonly IProxyBackend _backend;
        private readonly string _backupPath;

        public SystemProxyManager(IProxyBackend backend, string backupPath)
        {
            _backend = backend;
            _backupPath = backupPath;
        }

        // True while the system proxy is set by this program and the original settings wait in the backup.
        public bool IsEnabled
        {
            get { return File.Exists(_backupPath); }
        }

        // Compares the LAN settings with what this program has set. Throws when they cannot be read.
        public SystemProxyStatus CheckStatus()
        {
            if (!File.Exists(_backupPath))
            {
                return SystemProxyStatus.Off;
            }
            ProxyConnectionSettings applied;
            ReadBackup(out applied);
            return IsOurs(_backend.Query(null), applied) ? SystemProxyStatus.Applied : SystemProxyStatus.ChangedOutside;
        }

        // Points every connection at "server" (host:port). Throws when the original settings cannot be
        // saved or the change cannot be applied; in that case the settings are left as they were.
        public void Enable(string server, string bypass)
        {
            List<ProxyConnectionSettings> original;
            ProxyConnectionSettings previousApplied = null;
            if (File.Exists(_backupPath))
            {
                // Already enabled (or left over from a crash): keep the first snapshot, it is the real original.
                original = ReadBackup(out previousApplied);
            }
            else
            {
                original = new List<ProxyConnectionSettings>();
                original.Add(_backend.Query(null));
                string[] names = _backend.ListConnections();
                for (int i = 0; i < names.Length; i++)
                {
                    original.Add(_backend.Query(names[i]));
                }
            }

            ProxyConnectionSettings applied = new ProxyConnectionSettings();
            applied.Flags = ProxyConnectionSettings.FlagDirect | ProxyConnectionSettings.FlagProxy;
            applied.Server = server;
            applied.Bypass = bypass;

            // The backup must be on disk before the first change.
            WriteBackup(original, applied);

            List<ProxyConnectionSettings> changed = new List<ProxyConnectionSettings>();
            try
            {
                for (int i = 0; i < original.Count; i++)
                {
                    ProxyConnectionSettings current = _backend.Query(original[i].Connection);
                    if (previousApplied != null && !IsOurs(current, previousApplied) && !current.SameValues(original[i]))
                    {
                        // changed by the user since we set it: not ours any more
                        continue;
                    }
                    ProxyConnectionSettings target = new ProxyConnectionSettings();
                    target.Connection = original[i].Connection;
                    target.Flags = applied.Flags;
                    target.Server = applied.Server;
                    target.Bypass = applied.Bypass;
                    _backend.Apply(target);
                    changed.Add(original[i]);
                }
                ProxyConnectionSettings check = _backend.Query(null);
                if ((check.Flags & ProxyConnectionSettings.FlagProxy) == 0 || check.Server != server)
                {
                    throw new IOException(Lang.T("Windows did not accept the proxy settings."));
                }
                _backend.NotifyChanged();
            }
            catch (Exception ex)
            {
                bool restored = true;
                for (int i = 0; i < changed.Count; i++)
                {
                    try
                    {
                        _backend.Apply(changed[i]);
                    }
                    catch (Exception)
                    {
                        restored = false;
                    }
                }
                TryNotify();
                if (restored)
                {
                    SafeFile.TryDelete(_backupPath);
                }
                throw new IOException(Lang.F("The system proxy was not enabled: {0}", ex.Message)
                    + (restored ? "" : " " + Lang.F("The original settings are kept in {0}", _backupPath)), ex);
            }
        }

        // Restores the original settings. Returns a note for the log, or null when nothing was enabled.
        // Throws when a connection could not be restored; the backup file is kept in that case.
        public string Disable()
        {
            if (!File.Exists(_backupPath))
            {
                return null;
            }
            ProxyConnectionSettings applied;
            List<ProxyConnectionSettings> original = ReadBackup(out applied);

            int restored = 0;
            int skipped = 0;
            int failed = 0;
            string firstError = "";
            for (int i = 0; i < original.Count; i++)
            {
                try
                {
                    ProxyConnectionSettings current = _backend.Query(original[i].Connection);
                    if (!IsOurs(current, applied))
                    {
                        // The user (or another program) has changed this connection: their choice stays.
                        skipped++;
                        continue;
                    }
                    _backend.Apply(original[i]);
                    restored++;
                }
                catch (Exception ex)
                {
                    if (original[i].Connection != null)
                    {
                        // a dial-up entry that no longer exists or cannot be read: nothing to restore there
                        skipped++;
                        continue;
                    }
                    failed++;
                    if (firstError.Length == 0)
                    {
                        firstError = ex.Message;
                    }
                }
            }
            TryNotify();
            if (failed > 0)
            {
                throw new IOException(Lang.F("The system proxy settings of {0} connection(s) could not be restored: {1}", failed, firstError)
                    + " " + Lang.F("The original settings are kept in {0}", _backupPath));
            }
            File.Delete(_backupPath);
            return skipped > 0
                ? Lang.F("System proxy settings restored: {0} connection(s), {1} left as is (changed meanwhile or no longer present).", restored, skipped)
                : Lang.F("System proxy settings restored: {0} connection(s).", restored);
        }

        // Whether a connection still carries the proxy that this program has set.
        private static bool IsOurs(ProxyConnectionSettings current, ProxyConnectionSettings applied)
        {
            return (current.Flags & ProxyConnectionSettings.FlagProxy) != 0
                   && string.Equals(current.Server, applied.Server, StringComparison.OrdinalIgnoreCase);
        }

        private void TryNotify()
        {
            try
            {
                _backend.NotifyChanged();
            }
            catch (Exception)
            {
            }
        }

        // ---- backup file ----

        private void WriteBackup(List<ProxyConnectionSettings> original, ProxyConnectionSettings applied)
        {
            SafeFile.EnsureDirectoryFor(_backupPath);
            string temp = _backupPath + ".tmp";
            using (XmlTextWriter w = new XmlTextWriter(temp, new UTF8Encoding(false)))
            {
                w.Formatting = Formatting.Indented;
                w.WriteStartDocument();
                w.WriteStartElement(RootName);
                w.WriteAttributeString("formatVersion", "1");
                WriteItem(w, "applied", applied);
                for (int i = 0; i < original.Count; i++)
                {
                    WriteItem(w, "original", original[i]);
                }
                w.WriteEndElement();
                w.WriteEndDocument();
            }
            if (File.Exists(_backupPath))
            {
                File.Delete(_backupPath);
            }
            File.Move(temp, _backupPath);
        }

        private static void WriteItem(XmlWriter w, string element, ProxyConnectionSettings s)
        {
            w.WriteStartElement(element);
            if (s.Connection != null)
            {
                w.WriteAttributeString("connection", s.Connection);
            }
            w.WriteAttributeString("flags", s.Flags.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("server", s.Server);
            w.WriteAttributeString("bypass", s.Bypass);
            w.WriteAttributeString("pacUrl", s.PacUrl);
            w.WriteEndElement();
        }

        private List<ProxyConnectionSettings> ReadBackup(out ProxyConnectionSettings applied)
        {
            XmlDocument doc = new XmlDocument();
            doc.XmlResolver = null;
            try
            {
                doc.Load(_backupPath);
                XmlElement root = doc.DocumentElement;
                if (root == null || root.Name != RootName || root["applied"] == null)
                {
                    throw new XmlException("Unknown structure.");
                }
                applied = ReadItem(root["applied"]);
                List<ProxyConnectionSettings> original = new List<ProxyConnectionSettings>();
                foreach (XmlNode node in root.SelectNodes("original"))
                {
                    original.Add(ReadItem((XmlElement)node));
                }
                if (original.Count == 0)
                {
                    throw new XmlException("No saved settings.");
                }
                return original;
            }
            catch (XmlException)
            {
                throw new IOException(Lang.F("The backup of the system proxy settings is damaged: {0}"
                    + "\r\nCheck the proxy settings in Control Panel - Internet Options manually, then delete this file.", _backupPath));
            }
        }

        private static ProxyConnectionSettings ReadItem(XmlElement e)
        {
            ProxyConnectionSettings s = new ProxyConnectionSettings();
            s.Connection = e.HasAttribute("connection") ? e.GetAttribute("connection") : null;
            int flags;
            if (!int.TryParse(e.GetAttribute("flags"), NumberStyles.None, CultureInfo.InvariantCulture, out flags))
            {
                throw new XmlException("Invalid flags.");
            }
            s.Flags = flags;
            s.Server = e.GetAttribute("server");
            s.Bypass = e.GetAttribute("bypass");
            s.PacUrl = e.GetAttribute("pacUrl");
            return s;
        }
    }
}
