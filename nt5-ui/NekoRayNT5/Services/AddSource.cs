using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NekoRayNT5.App;
using NekoRayNT5.Model;

namespace NekoRayNT5.Services
{
    internal enum SourceKind
    {
        // nothing usable; Problem says why
        Invalid,
        // share links (one or many, plain, Base64 or percent-encoded)
        Links,
        // an http(s) address: downloaded and examined before anything is saved
        Url
    }

    // What the user gave to Add (pasted text, clipboard or file content), recognised.
    internal sealed class SourceInput
    {
        public SourceKind Kind = SourceKind.Invalid;
        public string Text = "";
        public string Url = "";
        // came as happ://add/...: the provider targets Happ, so the HWID is suggested
        public bool FromHapp;
        public string Problem = "";
    }

    // The result of examining a source, shown before anything is saved.
    internal sealed class AddPreview
    {
        public SourceInput Input;
        public List<ProfileEntry> Profiles = new List<ProfileEntry>();
        public LinkProblems Problems = new LinkProblems();
        // a reason why nothing can be added (download or format failure); "" when there are profiles
        public string Failure = "";
        public string Title = "";
        public string ProviderInfo = "";
        public int DuplicatesInSource;
        public int AlreadyKnown;
        // the source looks like a subscription (several profiles or subscription headers)
        public bool LooksLikeSubscription;
        // the server shows signs of a Happ-compatible panel (HWID headers) or the link was happ://
        public bool HappHints;
        // something the user should know about the HWID (limit reached, may be required...)
        public string HwidNotice = "";
        public bool UsedHwid;
        public string Via = "";
        public Dictionary<string, string> Meta = new Dictionary<string, string>();

        public bool CanAdd
        {
            get { return Profiles.Count > 0; }
        }

        // A short multi-line summary for the preview box (no addresses, no secrets).
        public string Describe()
        {
            StringBuilder sb = new StringBuilder();
            if (Input.Kind == SourceKind.Url)
            {
                sb.Append(Input.FromHapp ? Lang.T("Source: Happ subscription link") : Lang.T("Source: web address")).Append("\r\n");
                if (Via.Length > 0)
                {
                    sb.Append(Lang.F(UsedHwid ? "Downloaded {0}, with the HWID." : "Downloaded {0}.", Via)).Append("\r\n");
                }
            }
            else if (Input.Kind == SourceKind.Links)
            {
                sb.Append(Lang.T("Source: share links")).Append("\r\n");
            }
            if (Failure.Length > 0)
            {
                sb.Append("\r\n").Append(Lang.F("Nothing can be added: {0}", Failure)).Append("\r\n");
            }
            if (Title.Length > 0)
            {
                sb.Append(Lang.F("Name: {0}", Title)).Append("\r\n");
            }
            if (ProviderInfo.Length > 0)
            {
                sb.Append(Lang.F("Provider: {0}", ProviderInfo)).Append("\r\n");
            }
            if (Failure.Length == 0)
            {
                sb.Append(Lang.F("Servers found: {0}", Profiles.Count)).Append("\r\n");
            }
            if (DuplicatesInSource > 0)
            {
                sb.Append(Lang.F("Duplicates in the source (ignored): {0}", DuplicatesInSource)).Append("\r\n");
            }
            if (AlreadyKnown > 0)
            {
                sb.Append(Lang.F("Already in your list: {0}", AlreadyKnown)).Append("\r\n");
            }
            if (Problems.Count > 0)
            {
                sb.Append(Lang.F("Entries that cannot be used: {0}", Problems.Count)).Append("\r\n");
                foreach (string line in Problems.Summarize(5))
                {
                    sb.Append("  ").Append(line).Append("\r\n");
                }
            }
            if (Problems.NotLinks > 0)
            {
                sb.Append(Lang.F("Lines that are not share links (ignored): {0}", Problems.NotLinks)).Append("\r\n");
            }
            if (HwidNotice.Length > 0)
            {
                sb.Append("\r\n").Append(HwidNotice).Append("\r\n");
            }
            return sb.ToString().TrimEnd();
        }
    }

    // Recognition and examination of what is given to Add.
    internal static class AddSource
    {
        public const string HappAddPrefix = "happ://add/";

        public static SourceInput Classify(string text)
        {
            SourceInput input = new SourceInput();
            input.Text = text == null ? "" : text.Trim().TrimStart('\uFEFF').Trim();
            if (input.Text.Length == 0)
            {
                input.Problem = Lang.T("Paste a share link (vless://, vmess://, trojan://, ss://, socks://), a subscription "
                                + "address (https://...) or a happ://add/ link, or open a file.");
                return input;
            }
            string[] lines = input.Text.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 1)
            {
                string line = lines[0].Trim();
                string lower = line.ToLowerInvariant();
                if (lower.StartsWith("happ://"))
                {
                    return ClassifyHapp(input, line);
                }
                if (lower.StartsWith("http://") || lower.StartsWith("https://"))
                {
                    string problem = Subscription.CheckUrl(line);
                    if (problem != null)
                    {
                        input.Problem = problem;
                        return input;
                    }
                    input.Kind = SourceKind.Url;
                    input.Url = line;
                    return input;
                }
            }
            if (!MayContainLinks(input.Text))
            {
                input.Problem = Lang.T("This text contains no share links and no subscription address. Paste links that start with "
                                + "vless://, vmess://, trojan://, ss:// or socks://, a subscription address (https://...) or a "
                                + "happ://add/ link, or open a file.");
                return input;
            }
            input.Kind = SourceKind.Links;
            return input;
        }

        // Links, a percent-encoded or Base64 list, or a configuration file (explained later by Decode).
        private static bool MayContainLinks(string text)
        {
            if (text.IndexOf("://", StringComparison.Ordinal) >= 0 || text.IndexOf("%3A%2F%2F", StringComparison.OrdinalIgnoreCase) >= 0
                || text.StartsWith("{") || text.StartsWith("[") || text.StartsWith("proxies:"))
            {
                return true;
            }
            return LooksLikeBase64(text);
        }

        private static bool LooksLikeBase64(string text)
        {
            int letters = 0;
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c))
                {
                    continue;
                }
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')
                          || c == '+' || c == '/' || c == '=' || c == '-' || c == '_';
                if (!ok)
                {
                    return false;
                }
                letters++;
            }
            return letters >= 8;
        }

        // For the text offered from the clipboard when Add opens: only a clean source - one address,
        // one happ:// link, a Base64 list, or lines that are all share links. Other text that merely
        // contains a link (a copied page, a chat) is not offered; it can still be pasted by hand.
        public static bool IsCleanSource(string text)
        {
            SourceInput input = Classify(text);
            if (input.Kind == SourceKind.Url)
            {
                return true;
            }
            if (input.Kind != SourceKind.Links)
            {
                return false;
            }
            if (input.Text.IndexOf("://", StringComparison.Ordinal) < 0)
            {
                return LooksLikeBase64(input.Text);
            }
            int links = 0;
            foreach (string line in input.Text.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = line.Trim();
                if (t.Length == 0)
                {
                    continue;
                }
                if (ShareLinks.LinkStart(t) < 0)
                {
                    return false;
                }
                links++;
            }
            return links > 0;
        }

        private static SourceInput ClassifyHapp(SourceInput input, string line)
        {
            string lower = line.ToLowerInvariant();
            if (lower.StartsWith("happ://crypt"))
            {
                input.Problem = Lang.T("This is an encrypted Happ link (happ://crypt...). Only the Happ application can decrypt it; "
                                + "NekoRay NT5 has no compatible decoder. Ask the provider for the plain subscription address "
                                + "(https://...) and add that.");
                return input;
            }
            if (!lower.StartsWith(HappAddPrefix))
            {
                input.Problem = Lang.T("This Happ link type is not supported. Supported: happ://add/<subscription address>.");
                return input;
            }
            string url = line.Substring(HappAddPrefix.Length).Trim();
            if (url.StartsWith("http%3A", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https%3A", StringComparison.OrdinalIgnoreCase))
            {
                url = PercentEncoding.Decode(url);
            }
            string problem = Subscription.CheckUrl(url);
            if (problem != null)
            {
                input.Problem = Lang.F("The happ://add/ link does not contain a valid subscription address. {0}", problem);
                return input;
            }
            input.Kind = SourceKind.Url;
            input.Url = url;
            input.FromHapp = true;
            return input;
        }

        // Examines share links given as text. existing: the profiles already in the list.
        public static AddPreview AnalyzeLinks(SourceInput input, IList<ProfileEntry> existing)
        {
            AddPreview preview = new AddPreview();
            preview.Input = input;
            try
            {
                preview.Profiles = Subscription.Decode(input.Text, preview.Problems);
            }
            catch (FormatException ex)
            {
                preview.Failure = ex.Message;
            }
            if (preview.Failure.Length == 0 && preview.Profiles.Count == 0)
            {
                preview.Failure = preview.Problems.Count == 0 && preview.Problems.NotLinks > 0
                    ? Lang.F("the text contains no share links ({0} line(s) of other text). Paste only the "
                      + "links, one per line.", preview.Problems.NotLinks)
                    : Lang.T("no supported share links were found.");
            }
            Deduplicate(preview, existing);
            return preview;
        }

        // Downloads an address and examines the answer. Nothing is saved.
        public static AddPreview AnalyzeUrl(SourceInput input, SubscriptionClient client, bool useHwid, IList<ProfileEntry> existing)
        {
            AddPreview preview = new AddPreview();
            preview.Input = input;
            preview.UsedHwid = useHwid;
            preview.HappHints = input.FromHapp;
            FetchResult result = null;
            try
            {
                result = client.Fetch(input.Url, useHwid);
                preview.Meta = result.Meta;
                preview.Via = result.Via;
            }
            catch (FetchException ex)
            {
                preview.Meta = ex.Meta;
                preview.Failure = SubscriptionClient.SafeMessage(ex, input.Url);
            }
            catch (System.IO.IOException ex)
            {
                preview.Failure = SubscriptionClient.SafeMessage(ex, input.Url);
            }
            if (result != null)
            {
                try
                {
                    preview.Profiles = Subscription.Decode(result.Text, preview.Problems);
                    if (preview.Profiles.Count == 0)
                    {
                        preview.Failure = Lang.T("the answer contains no supported share links.");
                    }
                }
                catch (FormatException ex)
                {
                    preview.Failure = ex.Message;
                }
            }
            preview.Title = TitleOf(preview.Meta, input.Url);
            preview.ProviderInfo = DescribeUserInfo(preview.Meta.ContainsKey("subscription-userinfo") ? preview.Meta["subscription-userinfo"] : "");
            foreach (string name in preview.Meta.Keys)
            {
                if (name.StartsWith("x-hwid"))
                {
                    preview.HappHints = true;
                }
            }
            preview.HwidNotice = HwidNotice(preview.Meta, useHwid, preview.Profiles.Count, preview.HappHints);
            preview.LooksLikeSubscription = preview.Profiles.Count > 1 || preview.Meta.ContainsKey("subscription-userinfo")
                || preview.Meta.ContainsKey("profile-title") || preview.Meta.ContainsKey("profile-update-interval") || input.FromHapp;
            Deduplicate(preview, existing);
            return preview;
        }

        private static void Deduplicate(AddPreview preview, IList<ProfileEntry> existing)
        {
            Dictionary<string, bool> known = new Dictionary<string, bool>();
            for (int i = 0; i < existing.Count; i++)
            {
                known[ProfileKey.Of(existing[i])] = true;
            }
            Dictionary<string, bool> seen = new Dictionary<string, bool>();
            List<ProfileEntry> unique = new List<ProfileEntry>();
            for (int i = 0; i < preview.Profiles.Count; i++)
            {
                string key = ProfileKey.Of(preview.Profiles[i]);
                if (seen.ContainsKey(key))
                {
                    preview.DuplicatesInSource++;
                    continue;
                }
                seen.Add(key, true);
                unique.Add(preview.Profiles[i]);
                if (known.ContainsKey(key))
                {
                    preview.AlreadyKnown++;
                }
            }
            preview.Profiles = unique;
        }

        // Removes the profiles that are already in the list (for a one-time import of links).
        public static List<ProfileEntry> WithoutKnown(IList<ProfileEntry> profiles, IList<ProfileEntry> existing)
        {
            Dictionary<string, bool> known = new Dictionary<string, bool>();
            for (int i = 0; i < existing.Count; i++)
            {
                known[ProfileKey.Of(existing[i])] = true;
            }
            List<ProfileEntry> result = new List<ProfileEntry>();
            for (int i = 0; i < profiles.Count; i++)
            {
                if (!known.ContainsKey(ProfileKey.Of(profiles[i])))
                {
                    result.Add(profiles[i]);
                }
            }
            return result;
        }

        private static string HwidNotice(Dictionary<string, string> meta, bool usedHwid, int count, bool hints)
        {
            if (IsTrue(meta, "x-hwid-max-devices-reached"))
            {
                return Lang.T("The provider reports that the device limit of this subscription is reached. Remove an old device "
                       + "in the provider's panel or bot, then try again.");
            }
            if (IsTrue(meta, "x-hwid-not-supported"))
            {
                return Lang.T("The provider reports that it does not accept device ids (HWID) here.");
            }
            if (!usedHwid && (count == 0 || hints))
            {
                return hints
                    ? Lang.T("The provider looks Happ-compatible. If it limits devices, tick \"Use Happ-compatible HWID\" and check again.")
                    : Lang.T("If the provider requires a device id (for example a Happ subscription), tick \"Use Happ-compatible HWID\" "
                      + "and check again.");
            }
            return "";
        }

        private static bool IsTrue(Dictionary<string, string> meta, string name)
        {
            string value;
            return meta.TryGetValue(name, out value) && value.Trim().ToLowerInvariant() == "true";
        }

        // ---- subscription headers ----

        // profile-title (plain or "base64:..."), else the file name of content-disposition, else the host.
        public static string TitleOf(Dictionary<string, string> meta, string url)
        {
            string value;
            if (meta.TryGetValue("profile-title", out value))
            {
                string title = DecodeTitle(value);
                if (title.Length > 0)
                {
                    return title;
                }
            }
            if (meta.TryGetValue("content-disposition", out value))
            {
                string name = FileNameOf(value);
                if (name.Length > 0)
                {
                    return name;
                }
            }
            Uri uri;
            return Uri.TryCreate(url, UriKind.Absolute, out uri) ? uri.Host : "";
        }

        public static string DecodeTitle(string value)
        {
            string v = (value ?? "").Trim();
            if (v.StartsWith("base64:", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    v = PercentEncoding.DecodeBase64Text(v.Substring(7).Trim());
                }
                catch (FormatException)
                {
                    return "";
                }
            }
            return CleanName(v);
        }

        private static string FileNameOf(string disposition)
        {
            string lower = disposition.ToLowerInvariant();
            int star = lower.IndexOf("filename*=");
            if (star >= 0)
            {
                string v = disposition.Substring(star + 10).Split(';')[0].Trim().Trim('"');
                int quote = v.IndexOf("''");
                if (quote >= 0)
                {
                    v = v.Substring(quote + 2);
                }
                return CleanName(PercentEncoding.Decode(v));
            }
            int plain = lower.IndexOf("filename=");
            if (plain >= 0)
            {
                return CleanName(disposition.Substring(plain + 9).Split(';')[0].Trim().Trim('"'));
            }
            return "";
        }

        private static string CleanName(string name)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in name ?? "")
            {
                if (c >= ' ' && c != '\u007f')
                {
                    sb.Append(c);
                }
            }
            string result = sb.ToString().Trim();
            return result.Length > 60 ? result.Substring(0, 60) : result;
        }

        // "upload=1; download=2; total=3; expire=1700000000" -> "used 3 B of 3 B, expires 2023-11-14".
        public static string DescribeUserInfo(string value)
        {
            if (value == null || value.Trim().Length == 0)
            {
                return "";
            }
            long upload = 0, download = 0, total = 0, expire = 0;
            foreach (string part in value.Split(';'))
            {
                string[] kv = part.Split('=');
                if (kv.Length != 2)
                {
                    continue;
                }
                long n;
                if (!long.TryParse(kv[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) || n < 0)
                {
                    continue;
                }
                switch (kv[0].Trim().ToLowerInvariant())
                {
                    case "upload": upload = n; break;
                    case "download": download = n; break;
                    case "total": total = n; break;
                    case "expire": expire = n; break;
                }
            }
            List<string> parts = new List<string>();
            if (upload + download > 0 || total > 0)
            {
                parts.Add(total > 0 ? Lang.F("used {0} of {1}", Size(upload + download), Size(total))
                    : Lang.F("used {0} (no limit)", Size(upload + download)));
            }
            if (expire > 0 && expire < 32503680000L)
            {
                DateTime at = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(expire).ToLocalTime();
                parts.Add(Lang.F(at < DateTime.Now ? "EXPIRED {0}" : "expires {0}", at.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
            }
            return string.Join(", ", parts.ToArray());
        }

        private static string Size(long bytes)
        {
            double gb = bytes / 1073741824.0;
            if (gb >= 1)
            {
                return gb.ToString("0.##", CultureInfo.InvariantCulture) + " GB";
            }
            return (bytes / 1048576.0).ToString("0.#", CultureInfo.InvariantCulture) + " MB";
        }
    }
}
