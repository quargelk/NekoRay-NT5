using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using NekoRayNT5.Model;
using NekoRayNT5.App;

namespace NekoRayNT5.Services
{
    // Counts of a subscription update.
    internal sealed class MergeResult
    {
        public int Added;
        public int Updated;
        public int Unchanged;
        public int Removed;
        public int DuplicatesIgnored;
        // Ids that disappeared from the subscription.
        public List<int> RemovedIds = new List<int>();

        public override string ToString()
        {
            return Lang.F("{0} new, {1} changed, {2} unchanged, {3} removed", Added, Updated, Unchanged, Removed)
                   + (DuplicatesIgnored > 0 ? ", " + Lang.F("{0} duplicate(s) in the subscription ignored", DuplicatesIgnored) : "");
        }
    }

    // Groups and profiles in one XML file in the user's data directory.
    // The order of <profile> elements is the display order.
    //
    // The file is never lost silently:
    //  - a damaged file is copied aside and the previous version (.bak) is used when it is readable;
    //  - a file of a newer format is left untouched and saving is refused;
    //  - a file of an older format is copied to "<name>.v<N>-backup" before the first save;
    //  - records with a duplicate id get a new id instead of being dropped;
    //  - every save goes through a temporary file and keeps the previous version as .bak;
    //  - a failed save rolls the in-memory state back.
    internal sealed class ProfileStore
    {
        public const int FormatVersion = 2;

        private const string RootName = "NekoRayNT5";

        private readonly string _path;
        private readonly bool _readOnly;
        private List<ProfileEntry> _profiles = new List<ProfileEntry>();
        private List<ProfileGroup> _groups = new List<ProfileGroup>();
        private int _nextId = 1;
        private int _nextGroupId = 1;
        private int _loadedVersion = FormatVersion;
        private string _readOnlyReason;

        public ProfileStore(string path)
            : this(path, false)
        {
        }

        // readOnly: for reading another file (a backup to import); nothing is ever written or copied.
        public ProfileStore(string path, bool readOnly)
        {
            _path = path;
            _readOnly = readOnly;
            Reset();
        }

        public string FilePath
        {
            get { return _path; }
        }

        public IList<ProfileEntry> Profiles
        {
            get { return _profiles.AsReadOnly(); }
        }

        public IList<ProfileGroup> Groups
        {
            get { return _groups.AsReadOnly(); }
        }

        // Not null when saving is refused in order to protect the existing file.
        public string ReadOnlyReason
        {
            get { return _readOnlyReason; }
        }

        public ProfileEntry Find(int id)
        {
            int index = IndexOf(id);
            return index < 0 ? null : _profiles[index];
        }

        public ProfileGroup FindGroup(int id)
        {
            for (int i = 0; i < _groups.Count; i++)
            {
                if (_groups[i].Id == id)
                {
                    return _groups[i];
                }
            }
            return null;
        }

        public List<ProfileEntry> ProfilesOf(int groupId)
        {
            List<ProfileEntry> result = new List<ProfileEntry>();
            for (int i = 0; i < _profiles.Count; i++)
            {
                if (_profiles[i].GroupId == groupId)
                {
                    result.Add(_profiles[i]);
                }
            }
            return result;
        }

        private void Reset()
        {
            _profiles = new List<ProfileEntry>();
            _groups = new List<ProfileGroup>();
            _groups.Add(new ProfileGroup(ProfileGroup.DefaultId, "Default"));
            _nextId = 1;
            _nextGroupId = 1;
            _loadedVersion = FormatVersion;
        }

        // ---- loading ----

        // Returns null on success, otherwise a warning for the user.
        public string Load()
        {
            Reset();
            _readOnlyReason = null;
            if (!File.Exists(_path))
            {
                return null;
            }

            string problem;
            XmlElement root = Open(_path, out problem);
            if (root == null && problem != null && problem.StartsWith("!"))
            {
                // the file exists but cannot be opened at all: do not touch it
                return Protect(problem.Substring(1));
            }
            if (root == null)
            {
                return RecoverFromBackup(problem);
            }
            return Read(root);
        }

        // Parses the file; null with a problem when it is unusable. A problem starting with "!" means
        // the file could not even be opened (locked, no access).
        private static XmlElement Open(string path, out string problem)
        {
            problem = null;
            XmlDocument doc = new XmlDocument();
            doc.XmlResolver = null;
            try
            {
                doc.Load(path);
            }
            catch (XmlException)
            {
                problem = Lang.T("The profile file is damaged and could not be read.");
                return null;
            }
            catch (IOException ex)
            {
                problem = "!" + Lang.F("The profile file could not be opened: {0}", ex.Message);
                return null;
            }
            catch (UnauthorizedAccessException ex)
            {
                problem = "!" + Lang.F("The profile file could not be opened: {0}", ex.Message);
                return null;
            }
            XmlElement root = doc.DocumentElement;
            int version;
            if (root == null || root.Name != RootName || !TryInt(root.GetAttribute("formatVersion"), out version) || version < 1)
            {
                problem = Lang.T("The profile file has an unknown structure.");
                return null;
            }
            return root;
        }

        // The main file is unusable: keep a copy of it and fall back to the previous version.
        private string RecoverFromBackup(string problem)
        {
            if (_readOnly)
            {
                return problem;
            }
            string copy;
            try
            {
                copy = SafeFile.CopyAside(_path, "damaged");
            }
            catch (Exception ex)
            {
                return Protect(problem + " " + Lang.F("A safety copy could not be made: {0}", ex.Message));
            }
            string backup = _path + ".bak";
            string backupProblem;
            XmlElement root = File.Exists(backup) ? Open(backup, out backupProblem) : null;
            if (root != null)
            {
                string warning = Read(root);
                if (_readOnlyReason != null)
                {
                    return warning;
                }
                return problem + " " + Lang.F("The previous version ({0}) was loaded instead; changes made "
                       + "after it was saved are lost.\r\nThe damaged file was copied to:\r\n{1}", Path.GetFileName(backup), copy)
                       + (warning != null ? "\r\n\r\n" + warning : "");
            }
            return problem + " " + Lang.F("No usable previous version was found; the list starts empty.\r\nThe damaged file was copied to:\r\n{0}", copy);
        }

        private string Read(XmlElement root)
        {
            int version;
            TryInt(root.GetAttribute("formatVersion"), out version);
            if (version > FormatVersion)
            {
                return Protect(Lang.F("The profile file was written by a newer version of the program (format {0}, this version supports {1}).",
                               version, FormatVersion));
            }
            _loadedVersion = version;

            int skipped = 0;
            XmlElement groups = root["groups"];
            if (groups != null)
            {
                foreach (XmlNode node in groups.SelectNodes("group"))
                {
                    XmlElement e = (XmlElement)node;
                    int id;
                    if (!TryInt(e.GetAttribute("id"), out id) || id < 0)
                    {
                        skipped++;
                        continue;
                    }
                    ProfileGroup g = FindGroup(id);
                    if (g == null)
                    {
                        g = new ProfileGroup(id, "");
                        _groups.Add(g);
                    }
                    string name = e.GetAttribute("name").Trim();
                    g.Name = name.Length > 0 ? name : (id == ProfileGroup.DefaultId ? "Default" : "Group " + id);
                    g.Url = e.GetAttribute("url").Trim();
                    g.LastUpdate = e.GetAttribute("updated");
                    g.LastError = e.GetAttribute("error");
                    g.UseHwid = e.GetAttribute("hwid") == "true";
                    g.Info = e.GetAttribute("info");
                    if (id >= _nextGroupId)
                    {
                        _nextGroupId = id + 1;
                    }
                }
                int nextGroupId;
                if (TryInt(groups.GetAttribute("nextId"), out nextGroupId) && nextGroupId > _nextGroupId)
                {
                    _nextGroupId = nextGroupId;
                }
            }

            List<ProfileEntry> renumber = new List<ProfileEntry>();
            XmlElement list = root["profiles"];
            if (list != null)
            {
                foreach (XmlNode node in list.SelectNodes("profile"))
                {
                    ProfileEntry p = ReadProfile((XmlElement)node, version);
                    if (p == null)
                    {
                        skipped++;
                        continue;
                    }
                    ProfileGroup g = FindGroup(p.GroupId);
                    if (g == null)
                    {
                        p.GroupId = ProfileGroup.DefaultId;
                    }
                    else if (!((XmlElement)node).HasAttribute("sub"))
                    {
                        // files of 0.3.0 had no marker; everything in a subscription group came from it
                        p.FromSubscription = g.IsSubscription;
                    }
                    if (IndexOf(p.Id) >= 0)
                    {
                        renumber.Add(p);
                    }
                    else if (p.Id >= _nextId)
                    {
                        _nextId = p.Id + 1;
                    }
                    _profiles.Add(p);
                }
                // Ids of deleted profiles are not reused.
                int nextId;
                if (TryInt(list.GetAttribute("nextId"), out nextId) && nextId > _nextId)
                {
                    _nextId = nextId;
                }
            }
            for (int i = 0; i < renumber.Count; i++)
            {
                renumber[i].Id = _nextId++;
            }

            if (skipped > 0 || renumber.Count > 0)
            {
                string problem = (skipped > 0 ? Lang.F("{0} record(s) in the profile file are invalid and were skipped.", skipped) + " " : "")
                                 + (renumber.Count > 0 ? Lang.F("{0} profile(s) had an id used twice and got a new id.", renumber.Count) + " " : "");
                return KeepCopy(problem.Trim());
            }
            return null;
        }

        // Returns null for a record that cannot be used.
        private static ProfileEntry ReadProfile(XmlElement e, int version)
        {
            ProfileEntry p = new ProfileEntry();
            int id;
            int port;
            if (!TryInt(e.GetAttribute("id"), out id) || id < 1 || !TryInt(Text(e, "port", ""), out port))
            {
                return null;
            }
            // Format 1 knew only VLESS + TCP + REALITY and these were its implied values.
            bool legacy = version == 1;
            int groupId;
            p.Id = id;
            p.Port = port;
            p.GroupId = TryInt(e.GetAttribute("group"), out groupId) ? groupId : ProfileGroup.DefaultId;
            p.Type = legacy && !e.HasAttribute("type") ? ProfileEntry.TypeVless : e.GetAttribute("type");
            p.FromSubscription = e.GetAttribute("sub") == "true";
            p.Name = Text(e, "name", "");
            p.Address = Text(e, "address", "");
            p.Uuid = Text(e, "uuid", "");
            p.Username = Text(e, "username", "");
            p.Password = Text(e, "password", "");
            p.Encryption = Text(e, "encryption", legacy ? "none" : "");
            p.Flow = Text(e, "flow", "");
            p.Network = Text(e, "network", "tcp");
            p.Host = Text(e, "host", "");
            p.Path = Text(e, "path", "");
            p.Security = Text(e, "security", legacy ? "reality" : "none");
            p.Sni = Text(e, "sni", "");
            p.Alpn = Text(e, "alpn", "");
            p.Fingerprint = Text(e, "fingerprint", legacy ? ProfileEntry.DefaultFingerprint : "");
            p.AllowInsecure = Text(e, "allowInsecure", "") == "true";
            p.PublicKey = Text(e, "publicKey", "");
            p.ShortId = Text(e, "shortId", "");
            p.SpiderX = Text(e, "spiderX", "");
            XmlElement test = e["test"];
            int ms;
            if (test != null && int.TryParse(test.GetAttribute("ms"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out ms))
            {
                p.Latency = ms;
                p.LatencyNote = test.GetAttribute("note");
                p.TestedAt = test.GetAttribute("at");
            }
            return ProfileValidator.Validate(p) == null ? p : null;
        }

        private static string Text(XmlElement parent, string name, string fallback)
        {
            XmlElement child = parent[name];
            return child == null ? fallback : child.InnerText;
        }

        private static bool TryInt(string text, out int value)
        {
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
        }

        // Copies a partly unreadable file aside so that the next save cannot destroy the skipped records.
        private string KeepCopy(string problem)
        {
            if (_readOnly)
            {
                return problem;
            }
            string copy;
            try
            {
                copy = SafeFile.CopyAside(_path, "damaged");
            }
            catch (IOException ex)
            {
                return Protect(problem + " " + Lang.F("A safety copy could not be made: {0}", ex.Message));
            }
            catch (UnauthorizedAccessException ex)
            {
                return Protect(problem + " " + Lang.F("A safety copy could not be made: {0}", ex.Message));
            }
            return problem + " " + Lang.F("The original file was copied to:\r\n{0}", copy);
        }

        private string Protect(string problem)
        {
            Reset();
            _readOnlyReason = problem;
            return problem + "\r\n" + Lang.T("The file is left untouched; changes made now will not be saved.");
        }

        // ---- changes ----

        private delegate void Change();

        // Applies a change and saves; restores the previous state when saving fails.
        // Profiles and groups are replaced, never modified in place, so the copies below are enough.
        private void Commit(Change change)
        {
            List<ProfileEntry> profiles = new List<ProfileEntry>(_profiles);
            List<ProfileGroup> groups = new List<ProfileGroup>(_groups);
            int nextId = _nextId;
            int nextGroupId = _nextGroupId;
            try
            {
                change();
                Save();
            }
            catch
            {
                _profiles = profiles;
                _groups = groups;
                _nextId = nextId;
                _nextGroupId = nextGroupId;
                throw;
            }
        }

        public void Add(IList<ProfileEntry> items, int groupId)
        {
            if (FindGroup(groupId) == null)
            {
                throw new InvalidOperationException(Lang.T("The group no longer exists."));
            }
            Commit(delegate
            {
                // The given objects are stored (and get their ids); the caller must not change them afterwards.
                for (int i = 0; i < items.Count; i++)
                {
                    items[i].Id = _nextId++;
                    items[i].GroupId = groupId;
                    items[i].FromSubscription = false;
                    _profiles.Add(items[i]);
                }
            });
        }

        public void Update(ProfileEntry item)
        {
            Commit(delegate
            {
                int index = IndexOf(item.Id);
                if (index < 0)
                {
                    throw new InvalidOperationException(Lang.T("The profile no longer exists."));
                }
                ProfileEntry p = item.Clone();
                p.GroupId = _profiles[index].GroupId;
                p.FromSubscription = _profiles[index].FromSubscription;
                _profiles[index] = p;
            });
        }

        public void Remove(IList<int> ids)
        {
            Commit(delegate
            {
                for (int i = 0; i < ids.Count; i++)
                {
                    int index = IndexOf(ids[i]);
                    if (index >= 0)
                    {
                        _profiles.RemoveAt(index);
                    }
                }
            });
        }

        // Moves the profiles one step up (-1) or down (+1) among the profiles of their group.
        public void Move(IList<int> ids, int direction)
        {
            Commit(delegate
            {
                List<int> order = new List<int>(ids);
                order.Sort(delegate(int a, int b) { return IndexOf(a).CompareTo(IndexOf(b)); });
                if (direction > 0)
                {
                    order.Reverse();
                }
                for (int i = 0; i < order.Count; i++)
                {
                    int index = IndexOf(order[i]);
                    if (index < 0)
                    {
                        continue;
                    }
                    int other = index + direction;
                    while (other >= 0 && other < _profiles.Count && _profiles[other].GroupId != _profiles[index].GroupId)
                    {
                        other += direction;
                    }
                    if (other < 0 || other >= _profiles.Count || order.Contains(_profiles[other].Id))
                    {
                        continue;
                    }
                    ProfileEntry tmp = _profiles[index];
                    _profiles[index] = _profiles[other];
                    _profiles[other] = tmp;
                }
            });
        }

        // Moved profiles keep their ids and become ordinary (manual) profiles of the target group.
        public void MoveToGroup(IList<int> ids, int groupId)
        {
            if (FindGroup(groupId) == null)
            {
                throw new InvalidOperationException(Lang.T("The group no longer exists."));
            }
            Commit(delegate
            {
                for (int i = 0; i < ids.Count; i++)
                {
                    int index = IndexOf(ids[i]);
                    if (index < 0 || _profiles[index].GroupId == groupId)
                    {
                        continue;
                    }
                    ProfileEntry moved = _profiles[index].Clone();
                    moved.GroupId = groupId;
                    moved.FromSubscription = false;
                    _profiles.RemoveAt(index);
                    _profiles.Add(moved);
                }
            });
        }

        // Adds copies with new ids to the group; returns the number of copies.
        public int CopyToGroup(IList<int> ids, int groupId, string nameSuffix)
        {
            List<ProfileEntry> copies = new List<ProfileEntry>();
            for (int i = 0; i < ids.Count; i++)
            {
                ProfileEntry p = Find(ids[i]);
                if (p != null)
                {
                    ProfileEntry copy = p.Clone();
                    copy.Name = p.Name + nameSuffix;
                    copy.ClearTest();
                    copies.Add(copy);
                }
            }
            if (copies.Count > 0)
            {
                Add(copies, groupId);
            }
            return copies.Count;
        }

        // Reorders the profiles of one group; the positions used by the group stay the same.
        public void Sort(int groupId, Comparison<ProfileEntry> comparison)
        {
            Commit(delegate
            {
                List<ProfileEntry> sorted = ProfilesOf(groupId);
                List<ProfileEntry> original = new List<ProfileEntry>(sorted);
                // List.Sort is not stable, so the original position breaks ties.
                sorted.Sort(delegate(ProfileEntry a, ProfileEntry b)
                {
                    int c = comparison(a, b);
                    return c != 0 ? c : original.IndexOf(a).CompareTo(original.IndexOf(b));
                });
                int next = 0;
                for (int i = 0; i < _profiles.Count; i++)
                {
                    if (_profiles[i].GroupId == groupId)
                    {
                        _profiles[i] = sorted[next++];
                    }
                }
            });
        }

        // Stores the result of a latency test. Not worth an error message when it cannot be saved.
        public void SetTestResult(int id, int ms, string note, string at)
        {
            int index = IndexOf(id);
            if (index < 0)
            {
                return;
            }
            ProfileEntry p = _profiles[index].Clone();
            p.Latency = ms;
            p.LatencyNote = note;
            p.TestedAt = at;
            _profiles[index] = p;
        }

        // Saves results stored with SetTestResult; returns false when the file could not be written.
        public bool SaveTestResults()
        {
            try
            {
                Commit(delegate { });
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public void ClearTestResults(IList<int> ids)
        {
            for (int i = 0; i < ids.Count; i++)
            {
                int index = IndexOf(ids[i]);
                if (index >= 0)
                {
                    ProfileEntry p = _profiles[index].Clone();
                    p.ClearTest();
                    _profiles[index] = p;
                }
            }
            SaveTestResults();
        }

        // Applies the content of a subscription to its group:
        //  - a profile that is still in the subscription keeps its id, position and test result
        //    (matched by connection settings, or else by a unique name of the same type) and gets the new values;
        //  - new profiles are added at the end of the group;
        //  - profiles that came from the subscription and are no longer in it are removed;
        //  - profiles added to the group by hand are never touched.
        // Throws when the result cannot be saved; nothing changes then.
        public MergeResult MergeSubscription(int groupId, IList<ProfileEntry> items, string updated)
        {
            return MergeSubscription(groupId, items, updated, null);
        }

        // info: what the provider reported (traffic, expiry), null keeps the old note.
        public MergeResult MergeSubscription(int groupId, IList<ProfileEntry> items, string updated, string info)
        {
            if (FindGroup(groupId) == null)
            {
                throw new InvalidOperationException(Lang.T("The group no longer exists."));
            }
            MergeResult result = new MergeResult();
            Commit(delegate
            {
                // incoming, without duplicates
                List<ProfileEntry> incoming = new List<ProfileEntry>();
                Dictionary<string, bool> seenKeys = new Dictionary<string, bool>();
                for (int i = 0; i < items.Count; i++)
                {
                    string key = ProfileKey.Of(items[i]);
                    if (seenKeys.ContainsKey(key))
                    {
                        result.DuplicatesIgnored++;
                        continue;
                    }
                    seenKeys.Add(key, true);
                    incoming.Add(items[i]);
                }

                List<ProfileEntry> existing = new List<ProfileEntry>();
                for (int i = 0; i < _profiles.Count; i++)
                {
                    if (_profiles[i].GroupId == groupId && _profiles[i].FromSubscription)
                    {
                        existing.Add(_profiles[i]);
                    }
                }

                ProfileEntry[] match = new ProfileEntry[incoming.Count];
                Dictionary<int, bool> used = new Dictionary<int, bool>();
                // 1. same connection settings
                Dictionary<string, ProfileEntry> byKey = new Dictionary<string, ProfileEntry>();
                for (int i = 0; i < existing.Count; i++)
                {
                    string key = ProfileKey.Of(existing[i]);
                    if (!byKey.ContainsKey(key))
                    {
                        byKey.Add(key, existing[i]);
                    }
                }
                for (int i = 0; i < incoming.Count; i++)
                {
                    ProfileEntry old;
                    if (byKey.TryGetValue(ProfileKey.Of(incoming[i]), out old) && !used.ContainsKey(old.Id))
                    {
                        match[i] = old;
                        used.Add(old.Id, true);
                    }
                }
                // 2. same type and a name that is unique on both sides (the server changed a key or a port)
                Dictionary<string, int> oldNames = CountNames(existing, used);
                Dictionary<string, int> newNames = new Dictionary<string, int>();
                for (int i = 0; i < incoming.Count; i++)
                {
                    if (match[i] == null)
                    {
                        Count(newNames, incoming[i].Type + "\u0001" + incoming[i].Name);
                    }
                }
                for (int i = 0; i < incoming.Count; i++)
                {
                    if (match[i] != null || incoming[i].Name.Length == 0)
                    {
                        continue;
                    }
                    string name = incoming[i].Type + "\u0001" + incoming[i].Name;
                    if (newNames[name] != 1 || !oldNames.ContainsKey(name) || oldNames[name] != 1)
                    {
                        continue;
                    }
                    for (int j = 0; j < existing.Count; j++)
                    {
                        if (!used.ContainsKey(existing[j].Id) && existing[j].Type + "\u0001" + existing[j].Name == name)
                        {
                            match[i] = existing[j];
                            used.Add(existing[j].Id, true);
                            break;
                        }
                    }
                }

                // apply
                for (int i = 0; i < incoming.Count; i++)
                {
                    ProfileEntry p = incoming[i].Clone();
                    p.GroupId = groupId;
                    p.FromSubscription = true;
                    if (match[i] != null)
                    {
                        ProfileEntry old = match[i];
                        p.Id = old.Id;
                        bool sameKey = ProfileKey.Of(old) == ProfileKey.Of(p);
                        if (sameKey)
                        {
                            p.Latency = old.Latency;
                            p.LatencyNote = old.LatencyNote;
                            p.TestedAt = old.TestedAt;
                        }
                        if (sameKey && SameDetails(old, p))
                        {
                            result.Unchanged++;
                        }
                        else
                        {
                            result.Updated++;
                        }
                        _profiles[IndexOf(old.Id)] = p;
                    }
                    else
                    {
                        p.Id = _nextId++;
                        p.ClearTest();
                        _profiles.Add(p);
                        result.Added++;
                    }
                }
                for (int i = 0; i < existing.Count; i++)
                {
                    if (!used.ContainsKey(existing[i].Id))
                    {
                        result.RemovedIds.Add(existing[i].Id);
                        _profiles.RemoveAt(IndexOf(existing[i].Id));
                        result.Removed++;
                    }
                }

                ProfileGroup g = FindGroup(groupId).Clone();
                g.LastUpdate = updated;
                g.LastError = "";
                if (info != null)
                {
                    g.Info = info;
                }
                _groups[_groups.IndexOf(FindGroup(groupId))] = g;
            });
            return result;
        }

        private static bool SameDetails(ProfileEntry a, ProfileEntry b)
        {
            return a.Name == b.Name && a.Alpn == b.Alpn && a.Fingerprint == b.Fingerprint
                   && a.AllowInsecure == b.AllowInsecure && a.SpiderX == b.SpiderX;
        }

        private static Dictionary<string, int> CountNames(List<ProfileEntry> list, Dictionary<int, bool> skip)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>();
            for (int i = 0; i < list.Count; i++)
            {
                if (!skip.ContainsKey(list[i].Id))
                {
                    Count(counts, list[i].Type + "\u0001" + list[i].Name);
                }
            }
            return counts;
        }

        private static void Count(Dictionary<string, int> counts, string key)
        {
            int n;
            counts.TryGetValue(key, out n);
            counts[key] = n + 1;
        }

        // Remembers why the last update of a subscription failed; the profiles are not touched.
        public void SetSubscriptionError(int groupId, string error)
        {
            ProfileGroup old = FindGroup(groupId);
            if (old == null)
            {
                return;
            }
            try
            {
                Commit(delegate
                {
                    ProfileGroup g = old.Clone();
                    g.LastError = error;
                    _groups[_groups.IndexOf(old)] = g;
                });
            }
            catch (Exception)
            {
                // only a status note
            }
        }

        public ProfileGroup AddGroup(string name, string url)
        {
            return AddGroup(name, url, false);
        }

        public ProfileGroup AddGroup(string name, string url, bool useHwid)
        {
            ProfileGroup g = null;
            Commit(delegate
            {
                g = new ProfileGroup(_nextGroupId++, name);
                g.Url = url;
                g.UseHwid = useHwid;
                _groups.Add(g);
            });
            return g;
        }

        // Switches the Happ-compatible HWID headers of a subscription on or off.
        public void SetUseHwid(int groupId, bool useHwid)
        {
            Commit(delegate
            {
                ProfileGroup old = FindGroup(groupId);
                if (old == null)
                {
                    throw new InvalidOperationException(Lang.T("The group no longer exists."));
                }
                ProfileGroup g = old.Clone();
                g.UseHwid = useHwid;
                _groups[_groups.IndexOf(old)] = g;
            });
        }

        public void UpdateGroup(int groupId, string name, string url)
        {
            Commit(delegate
            {
                ProfileGroup old = FindGroup(groupId);
                if (old == null)
                {
                    throw new InvalidOperationException(Lang.T("The group no longer exists."));
                }
                ProfileGroup g = old.Clone();
                g.Name = name;
                if (g.Url != url)
                {
                    g.LastError = "";
                }
                g.Url = url;
                _groups[_groups.IndexOf(old)] = g;
            });
        }

        // Removes a group together with its profiles. The default group cannot be removed.
        public void RemoveGroup(int groupId)
        {
            if (groupId == ProfileGroup.DefaultId)
            {
                throw new InvalidOperationException(Lang.T("The default group cannot be deleted."));
            }
            Commit(delegate
            {
                _profiles.RemoveAll(delegate(ProfileEntry p) { return p.GroupId == groupId; });
                _groups.RemoveAll(delegate(ProfileGroup g) { return g.Id == groupId; });
            });
        }

        // ---- backup ----

        // Writes all groups and profiles to another file (with the credentials, on the user's request).
        public void ExportTo(string path)
        {
            string temp = path + ".tmp";
            WriteFile(temp);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            File.Move(temp, path);
        }

        // Adds the groups and profiles of a backup file: groups other than the default one are added as
        // new groups, all profiles get new ids. Returns the number of imported profiles. Throws
        // InvalidDataException when the file is not a readable backup; nothing changes then.
        public int ImportFrom(string path, out int groupsAdded, out string warning)
        {
            ProfileStore source = new ProfileStore(path, true);
            warning = source.Load();
            if (source.ReadOnlyReason != null || (source.Profiles.Count == 0 && source.Groups.Count <= 1))
            {
                throw new InvalidDataException(warning != null ? warning : Lang.T("The file contains no profiles."));
            }
            int added = 0;
            int newGroups = 0;
            Commit(delegate
            {
                Dictionary<int, int> groupMap = new Dictionary<int, int>();
                groupMap.Add(ProfileGroup.DefaultId, ProfileGroup.DefaultId);
                foreach (ProfileGroup g in source.Groups)
                {
                    if (g.Id == ProfileGroup.DefaultId)
                    {
                        continue;
                    }
                    ProfileGroup copy = new ProfileGroup(_nextGroupId++, UniqueGroupName(g.Name));
                    copy.Url = g.Url;
                    copy.LastUpdate = g.LastUpdate;
                    copy.UseHwid = g.UseHwid;
                    _groups.Add(copy);
                    groupMap.Add(g.Id, copy.Id);
                    newGroups++;
                }
                foreach (ProfileEntry p in source.Profiles)
                {
                    ProfileEntry copy = p.Clone();
                    copy.Id = _nextId++;
                    copy.GroupId = groupMap[p.GroupId];
                    _profiles.Add(copy);
                    added++;
                }
            });
            groupsAdded = newGroups;
            return added;
        }

        private string UniqueGroupName(string name)
        {
            string candidate = name;
            for (int n = 2; GroupNameUsed(candidate); n++)
            {
                candidate = name + " (" + n + ")";
            }
            return candidate;
        }

        private bool GroupNameUsed(string name)
        {
            for (int i = 0; i < _groups.Count; i++)
            {
                if (string.Equals(_groups[i].Name, name, StringComparison.CurrentCultureIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private int IndexOf(int id)
        {
            for (int i = 0; i < _profiles.Count; i++)
            {
                if (_profiles[i].Id == id)
                {
                    return i;
                }
            }
            return -1;
        }

        // ---- saving ----

        private void Save()
        {
            if (_readOnly)
            {
                throw new InvalidOperationException(Lang.T("This profile file is opened for reading only."));
            }
            if (_readOnlyReason != null)
            {
                throw new IOException(_readOnlyReason + " " + Lang.T("Changes are not saved to protect the existing file."));
            }
            SafeFile.EnsureDirectoryFor(_path);

            // Keep the file of the old format before it is rewritten in the new one.
            if (_loadedVersion < FormatVersion && File.Exists(_path))
            {
                string old = _path + ".v" + _loadedVersion + "-backup";
                if (!File.Exists(old))
                {
                    File.Copy(_path, old, false);
                }
            }

            string temp = _path + ".tmp";
            WriteFile(temp);
            SafeFile.Replace(temp, _path);
            _loadedVersion = FormatVersion;
        }

        private void WriteFile(string file)
        {
            using (XmlTextWriter w = new XmlTextWriter(file, new UTF8Encoding(false)))
            {
                w.Formatting = Formatting.Indented;
                w.WriteStartDocument();
                w.WriteStartElement(RootName);
                w.WriteAttributeString("formatVersion", FormatVersion.ToString(CultureInfo.InvariantCulture));

                w.WriteStartElement("groups");
                w.WriteAttributeString("nextId", _nextGroupId.ToString(CultureInfo.InvariantCulture));
                for (int i = 0; i < _groups.Count; i++)
                {
                    w.WriteStartElement("group");
                    w.WriteAttributeString("id", _groups[i].Id.ToString(CultureInfo.InvariantCulture));
                    w.WriteAttributeString("name", _groups[i].Name);
                    w.WriteAttributeString("url", _groups[i].Url);
                    w.WriteAttributeString("updated", _groups[i].LastUpdate);
                    if (_groups[i].LastError.Length > 0)
                    {
                        w.WriteAttributeString("error", _groups[i].LastError);
                    }
                    if (_groups[i].UseHwid)
                    {
                        w.WriteAttributeString("hwid", "true");
                    }
                    if (_groups[i].Info.Length > 0)
                    {
                        w.WriteAttributeString("info", _groups[i].Info);
                    }
                    w.WriteEndElement();
                }
                w.WriteEndElement();

                w.WriteStartElement("profiles");
                w.WriteAttributeString("nextId", _nextId.ToString(CultureInfo.InvariantCulture));
                for (int i = 0; i < _profiles.Count; i++)
                {
                    WriteProfile(w, _profiles[i]);
                }
                w.WriteEndElement();

                w.WriteEndElement();
                w.WriteEndDocument();
                w.Flush();
            }
        }

        private static void WriteProfile(XmlWriter w, ProfileEntry p)
        {
            w.WriteStartElement("profile");
            w.WriteAttributeString("id", p.Id.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("type", p.Type);
            w.WriteAttributeString("group", p.GroupId.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("sub", p.FromSubscription ? "true" : "false");
            w.WriteElementString("name", p.Name);
            w.WriteElementString("address", p.Address);
            w.WriteElementString("port", p.Port.ToString(CultureInfo.InvariantCulture));
            WriteOptional(w, "uuid", p.Uuid);
            WriteOptional(w, "username", p.Username);
            WriteOptional(w, "password", p.Password);
            WriteOptional(w, "encryption", p.Encryption);
            WriteOptional(w, "flow", p.Flow);
            w.WriteElementString("network", p.Network);
            WriteOptional(w, "host", p.Host);
            WriteOptional(w, "path", p.Path);
            w.WriteElementString("security", p.Security);
            WriteOptional(w, "sni", p.Sni);
            WriteOptional(w, "alpn", p.Alpn);
            WriteOptional(w, "fingerprint", p.Fingerprint);
            WriteOptional(w, "allowInsecure", p.AllowInsecure ? "true" : "");
            WriteOptional(w, "publicKey", p.PublicKey);
            WriteOptional(w, "shortId", p.ShortId);
            WriteOptional(w, "spiderX", p.SpiderX);
            if (p.Latency != 0)
            {
                w.WriteStartElement("test");
                w.WriteAttributeString("ms", p.Latency.ToString(CultureInfo.InvariantCulture));
                w.WriteAttributeString("note", p.LatencyNote);
                w.WriteAttributeString("at", p.TestedAt);
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }

        private static void WriteOptional(XmlWriter w, string name, string value)
        {
            if (value.Length > 0)
            {
                w.WriteElementString(name, value);
            }
        }
    }
}
