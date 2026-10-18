// Checks of version 1.0: the single Add (recognition, preview, duplicates), Happ-compatible HWID,
// subscription headers, download routes (auto / through the connection / direct), local port choice.
// Only 127.0.0.1 is contacted; all data is synthetic.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using NekoRayNT5.Core;
using NekoRayNT5.Model;
using NekoRayNT5.Services;

internal static partial class SelfTest
{
    private static void TestAddSource()
    {
        Section("Add: recognising links, addresses and happ:// links");
        SourceInput s = AddSource.Classify("  https://sub.example.com/api/sub/" + Token + "  ");
        Check("an https address is a URL source", s.Kind == SourceKind.Url && s.Url == "https://sub.example.com/api/sub/" + Token && !s.FromHapp);
        s = AddSource.Classify("happ://add/https://sub.example.com/s/" + Token);
        Check("happ://add/ gives the address and suggests the HWID", s.Kind == SourceKind.Url && s.FromHapp
            && s.Url == "https://sub.example.com/s/" + Token);
        s = AddSource.Classify("happ://add/https%3A%2F%2Fsub.example.com%2Fs%2Fabc");
        Check("happ://add/ with a percent-encoded address", s.Kind == SourceKind.Url && s.Url == "https://sub.example.com/s/abc");
        s = AddSource.Classify("happ://crypt3/AAAAbbbbCCCC");
        Check("encrypted happ://crypt links are refused honestly", s.Kind == SourceKind.Invalid && s.Problem.IndexOf("encrypted") >= 0
            && s.Problem.IndexOf("AAAA") < 0);
        s = AddSource.Classify("happ://routing/xyz");
        Check("other happ:// links are refused", s.Kind == SourceKind.Invalid);
        s = AddSource.Classify("happ://add/ftp://x");
        Check("happ://add/ with a non-http address is refused", s.Kind == SourceKind.Invalid);
        Check("empty input explains what to paste", AddSource.Classify("  \r\n ").Kind == SourceKind.Invalid);
        string links = Vless("security=reality&pbk=" + Pbk + "&sni=www.example.com&fp=chrome", "one") + "\n"
                       + Vless("security=reality&pbk=" + Pbk + "&sni=www.example.com&fp=chrome", "one again") + "\n"
                       + "trojan://" + Secret + "@test.example:443?security=tls#two\nbroken-line\n";
        s = AddSource.Classify(links);
        Check("several links are a link source", s.Kind == SourceKind.Links);

        List<ProfileEntry> existing = new List<ProfileEntry>();
        existing.Add(ShareLinks.Parse("trojan://" + Secret + "@test.example:443?security=tls#already"));
        AddPreview p = AddSource.AnalyzeLinks(s, existing);
        Check("preview: 2 servers, 1 duplicate in the source, 1 already known, 1 line that is not a link", p.CanAdd
            && p.Profiles.Count == 2 && p.DuplicatesInSource == 1 && p.AlreadyKnown == 1 && p.Problems.Count == 0
            && p.Problems.NotLinks == 1);
        string text = p.Describe();
        Check("preview text has counts and no secrets", text.IndexOf("Servers found: 2") >= 0 && text.IndexOf(Secret) < 0
            && text.IndexOf(Uuid) < 0);
        Check("skipping known servers leaves the new one", AddSource.WithoutKnown(p.Profiles, existing).Count == 1);
        p = AddSource.AnalyzeLinks(AddSource.Classify(Convert.ToBase64String(Encoding.UTF8.GetBytes(links))), existing);
        Check("a Base64 list in the clipboard works the same", p.Profiles.Count == 2);
        p = AddSource.AnalyzeLinks(AddSource.Classify("{\"outbounds\":[]}"), existing);
        Check("a JSON configuration is explained, not imported", !p.CanAdd && p.Failure.IndexOf("configuration") >= 0);

        Section("Subscription headers and HWID");
        Dictionary<string, string> meta = new Dictionary<string, string>();
        meta["profile-title"] = "base64:" + Convert.ToBase64String(Encoding.UTF8.GetBytes("My \u0412\u041F\u041D"));
        Check("profile-title in Base64 (UTF-8)", AddSource.TitleOf(meta, "https://x.example/") == "My \u0412\u041F\u041D");
        meta.Clear();
        meta["content-disposition"] = "attachment; filename*=UTF-8''Sub%20One";
        Check("file name of content-disposition", AddSource.TitleOf(meta, "https://x.example/") == "Sub One");
        meta.Clear();
        Check("host name when nothing else", AddSource.TitleOf(meta, "https://x.example/sub/" + Token) == "x.example");
        string info = AddSource.DescribeUserInfo("upload=536870912; download=536870912; total=10737418240; expire=4102444800");
        Check("traffic and expiry: " + info, info == "used 1 GB of 10 GB, expires 2100-01-01" || info.StartsWith("used 1 GB of 10 GB, expires 2099-12-3"));
        Check("broken userinfo gives nothing", AddSource.DescribeUserInfo("garbage;;=") == "");

        string hwid = Hwid.Generate();
        Check("generated HWID: 32 hex digits, different each time", hwid.Length == 32 && Hwid.Check(hwid) == null && hwid != Hwid.Generate());
        Check("HWID check refuses spaces, line breaks and short values", Hwid.Check("abc def ghi") != null && Hwid.Check("abcdefgh\r\nx: y") != null
            && Hwid.Check("short") != null && Hwid.Check("") == null);
        AppSettings settings = new AppSettings();
        bool created;
        string first = Hwid.Effective(settings, out created);
        bool createdAgain;
        Check("the HWID is made once and then kept", created && first == Hwid.Effective(settings, out createdAgain) && !createdAgain);
        settings.HwidCustom = "custom-authorised-1";
        Check("a custom HWID replaces it", Hwid.Effective(settings, out created) == "custom-authorised-1" && !created);
        Check("masked for the screen", Hwid.Mask(first).Length < 12 && Hwid.Mask(first).IndexOf(first) < 0);
        List<KeyValuePair<string, string>> headers = Hwid.Headers(first);
        Check("Happ-compatible header names", headers[0].Key == "x-hwid" && headers[0].Value == first && headers.Count == 4);

        string settingsPath = Path.Combine(_work, "settings-10.xml");
        SettingsStore store = new SettingsStore(settingsPath);
        settings.SubscriptionRoute = AppSettings.RouteDirect;
        settings.FirstRunDone = true;
        store.Save(settings);
        string warning;
        AppSettings loaded = store.Load(out warning);
        Check("route, HWID, custom HWID and first run are saved", loaded.SubscriptionRoute == AppSettings.RouteDirect
            && loaded.HwidGenerated == first && loaded.HwidCustom == "custom-authorised-1" && loaded.FirstRunDone);
        File.WriteAllText(settingsPath, File.ReadAllText(settingsPath).Replace("custom-authorised-1", "bad value with spaces")
            .Replace(">direct<", ">sideways<"));
        loaded = store.Load(out warning);
        Check("invalid saved values fall back safely", loaded.HwidCustom == "" && loaded.SubscriptionRoute == AppSettings.RouteAuto);

        string profilesPath = Path.Combine(_work, "profiles-10.xml");
        ProfileStore ps = new ProfileStore(profilesPath);
        ps.Load();
        ProfileGroup g = ps.AddGroup("Happ sub", "https://sub.example.com/" + Token, true);
        List<ProfileEntry> items = new List<ProfileEntry>();
        items.Add(ShareLinks.Parse("trojan://" + Secret + "@test.example:443?security=tls#t"));
        ps.MergeSubscription(g.Id, items, "2026-10-16 12:00", "used 1 GB of 10 GB");
        ProfileStore again = new ProfileStore(profilesPath);
        again.Load();
        ProfileGroup g2 = again.FindGroup(g.Id);
        Check("subscription keeps its HWID switch and provider note", g2 != null && g2.UseHwid && g2.Info == "used 1 GB of 10 GB");
        again.SetUseHwid(g.Id, false);
        Check("HWID switch can be turned off", !again.FindGroup(g.Id).UseHwid);
    }

    private static void TestDownloadRoutes()
    {
        Section("Subscription download routes and headers (local HTTP server)");
        string list = Vless("security=reality&pbk=" + Pbk + "&sni=www.example.com&fp=chrome", "route-one") + "\n";
        ScriptedHttpServer server = new ScriptedHttpServer(delegate(string path)
        {
            if (path.StartsWith("/denied")) { return Reply(403, "text/plain", Encoding.ASCII.GetBytes("no"), "x-hwid-max-devices-reached: true\r\n"); }
            if (path.StartsWith("/empty-happ")) { return Reply(200, "text/plain", Encoding.ASCII.GetBytes("bm8gbGlua3M="), "x-hwid-active: true\r\n"); }
            return Reply(200, "text/plain", Encoding.UTF8.GetBytes(list),
                "profile-title: Route test\r\nprofile-update-interval: 12\r\nsubscription-userinfo: upload=0; download=0; total=0\r\n");
        });
        string baseUrl = "http://127.0.0.1:" + server.Port;
        SubscriptionClient client = new SubscriptionClient();
        client.Tools.TempDir = Path.Combine(_work, "routes");
        client.Hwid = "synthetic-hwid-route-01";
        try
        {
            client.Route = AppSettings.RouteDirect;
            client.ProxyPort = 1;   // would fail if it were used
            FetchResult r = client.Fetch(baseUrl + "/sub?" + Token, false);
            Check("direct: no proxy, even while connected", r.Via == "directly" && r.Text.IndexOf("route-one") >= 0);
            Check("no HWID headers unless the subscription asks", server.LastHead.ToLowerInvariant().IndexOf("x-hwid") < 0
                && server.LastHead.IndexOf("NekoRayNT5") >= 0);
            client.Fetch(baseUrl + "/sub", true);
            Check("HWID headers sent by the built-in client", server.LastHead.ToLowerInvariant().IndexOf("x-hwid: synthetic-hwid-route-01") >= 0);
            Check("subscription headers kept", r.Header("profile-title") == "Route test" && r.Header("profile-update-interval") == "12");

            client.Route = AppSettings.RouteProxy;
            client.ProxyPort = 0;
            string e = FetchError(client, baseUrl + "/sub");
            Check("only through the connection, but not connected: clear message", e != null && e.IndexOf("Connect first") >= 0);

            // (auto/proxy with a dead local proxy: see TestHttpsThroughHelper - the built-in client of .NET
            // never sends requests for 127.0.0.1 through a proxy, nt5helper does)

            // a refusal by the server is not retried on another route, and its headers are explained
            client.Route = AppSettings.RouteAuto;
            client.ProxyPort = 0;
            AddPreview p = AddSource.AnalyzeUrl(AddSource.Classify(baseUrl + "/denied?" + Token), client, false, new List<ProfileEntry>());
            Check("403 with a device-limit header: explained, nothing to add", !p.CanAdd && p.Failure.IndexOf("403") >= 0
                && p.HwidNotice.IndexOf("device limit") >= 0 && p.HappHints && p.Failure.IndexOf(Token) < 0);
            p = AddSource.AnalyzeUrl(AddSource.Classify(baseUrl + "/empty-happ"), client, false, new List<ProfileEntry>());
            Check("no links from a Happ-like panel: the HWID is suggested", !p.CanAdd && p.HwidNotice.IndexOf("Use Happ-compatible HWID") >= 0);
            p = AddSource.AnalyzeUrl(AddSource.Classify(baseUrl + "/sub?" + Token), client, false, new List<ProfileEntry>());
            Check("a subscription address: preview with name, kept as a subscription", p.CanAdd && p.Profiles.Count == 1
                && p.Title == "Route test" && p.LooksLikeSubscription && p.Describe().IndexOf(Token) < 0);
        }
        finally
        {
            server.Stop();
        }
    }

    // 0.9.5 on XP: copied text ("Read 1 file", a bullet (U+2022) and thousands of other lines that also contained
    // an address) became 3516 "unsupported entries". Text that is not a link is now only counted, link
    // problems are capped and grouped, and only a clean source is offered from the clipboard.
    private static void TestAddClassification()
    {
        Section("Add: classification of pasted text (regression of 0.9.5 on XP)");
        string bullet = ((char)0x2022).ToString();
        SourceInput s = AddSource.Classify("Read 1 file\r\n" + bullet);
        Check("plain text without links: explained at once, not a link list", s.Kind == SourceKind.Invalid
            && s.Problem.IndexOf("no share links") >= 0);
        Check("plain text is not offered from the clipboard", !AddSource.IsCleanSource("Read 1 file " + bullet));

        StringBuilder chat = new StringBuilder("Read 1 file\r\n" + bullet + "\r\n");
        for (int i = 0; i < 3500; i++)
        {
            chat.Append("line ").Append(i).Append(" of a copied page\r\n");
        }
        chat.Append("see https://www.example.com/page for details\r\n");
        for (int i = 0; i < 13; i++)
        {
            chat.Append(bullet).Append(" item ").Append(i).Append("\r\n");
        }
        s = AddSource.Classify(chat.ToString());
        AddPreview p = AddSource.AnalyzeLinks(s, new List<ProfileEntry>());
        string text = p.Describe();
        Check("a copied page (3516 lines): no entries per line, one clear reason", !p.CanAdd && p.Problems.Count == 0
            && p.Problems.NotLinks == 3516 && p.Problems.Details.Count == 0 && p.Failure.IndexOf("no share links") >= 0
            && text.Length < 600);
        Check("a copied page with an address is not offered from the clipboard", !AddSource.IsCleanSource(chat.ToString()));

        string good = "security=reality&pbk=" + Pbk + "&sni=www.example.com&fp=chrome";
        string list = bullet + " " + Vless(good, "one") + "\r\n1. trojan://" + Secret + "@test.example:443?security=tls#two\r\n"
                      + "- " + Vless(good, "three").Replace("@test", "@other") + "\r\n# " + Vless(good, "commented") + "\r\n";
        p = AddSource.AnalyzeLinks(AddSource.Classify(list), new List<ProfileEntry>());
        Check("links copied from a formatted list (bullet, 1., -) are read; a # comment is not", p.Profiles.Count == 3
            && p.Problems.Count == 0 && p.Problems.NotLinks == 1);
        Check("a list of links is offered from the clipboard", AddSource.IsCleanSource(Vless(good, "a") + "\r\n" + Vless(good, "b"))
            && AddSource.IsCleanSource("https://sub.example.com/s/" + Token)
            && AddSource.IsCleanSource(Convert.ToBase64String(Encoding.UTF8.GetBytes(Vless(good, "a")))));

        StringBuilder broken = new StringBuilder();
        for (int i = 0; i < 3000; i++)
        {
            broken.Append("vless://x@y\r\n");
        }
        broken.Append("https://sub.example.com/s/" + Token + "\r\n");
        p = AddSource.AnalyzeLinks(AddSource.Classify(broken.ToString()), new List<ProfileEntry>());
        text = p.Describe();
        Check("3000 broken links: all counted, at most " + LinkProblems.MaxDetails + " kept, the reasons grouped",
            p.Problems.Count == 3001 && p.Problems.Details.Count == LinkProblems.MaxDetails
            && text.IndexOf("Entries that cannot be used: 3001") >= 0 && text.IndexOf("Links 1, 2, 3, 4, 5 and 15 more") >= 0
            && text.IndexOf("2981 more") >= 0 && text.Length < 1500 && text.IndexOf(Token) < 0);
        p = AddSource.AnalyzeLinks(AddSource.Classify(Vless(good, "a") + "\r\nhttps://sub.example.com/s/" + Token), new List<ProfileEntry>());
        Check("an address inside a list of links is reported, not downloaded", p.Profiles.Count == 1 && p.Problems.Count == 1
            && p.Problems.Details[0].IndexOf("web address") >= 0 && p.Describe().IndexOf(Token) < 0);
        s = AddSource.Classify("not base64 at all, just words");
        Check("words without links are refused before Check", s.Kind == SourceKind.Invalid);
        s = AddSource.Classify("{\"outbounds\": []}");
        p = AddSource.AnalyzeLinks(s, new List<ProfileEntry>());
        Check("a JSON configuration still gets its own explanation", s.Kind == SourceKind.Links && p.Failure.IndexOf("configuration") >= 0);
    }

    private static void TestTempCleanup()
    {
        Section("Files left by an interrupted download are removed at start-up");
        string dir = Path.Combine(_work, "run-cleanup");
        Directory.CreateDirectory(dir);
        string[] left = new string[] { "subscription-1.req", "subscription-2.meta", "subscription-3.hdr", "subscription-4.tmp",
            "subscription-5.tmp.part" };
        foreach (string name in left)
        {
            File.WriteAllText(Path.Combine(dir, name), "x-hwid: synthetic-hwid-cleanup");
        }
        File.WriteAllText(Path.Combine(dir, "core.pid"), "1");
        int removed = Subscription.CleanupTempFiles(dir);
        Check("headers, answers and partial files removed, other files kept", removed >= left.Length
            && Directory.GetFiles(dir, "subscription-*").Length == 0 && File.Exists(Path.Combine(dir, "core.pid")));
        Check("a missing folder is not an error", Subscription.CleanupTempFiles(Path.Combine(dir, "none")) == 0);

        string profile = Environment.GetEnvironmentVariable("USERPROFILE");
        string appdata = Environment.GetEnvironmentVariable("APPDATA");
        string shared = NekoRayNT5.App.AppPaths.HidePersonalFolders("Data folder: " + appdata + "\\NekoRayNT5\r\nOther: "
            + profile.ToUpperInvariant() + "\\Desktop\\x");
        Check("diagnostic report: user folders replaced by variables (no user name)", shared.IndexOf("%APPDATA%\\NekoRayNT5") >= 0
            && shared.IndexOf("%USERPROFILE%\\Desktop") >= 0 && shared.IndexOf(Environment.UserName, StringComparison.OrdinalIgnoreCase) < 0);
    }

    // Files as 0.9.1 wrote them (without the fields of 0.9.5) open with the old values and safe defaults.
    private static void TestUpgradeFrom091()
    {
        Section("Upgrade: settings and profiles of 0.9.1");
        string dir = Path.Combine(_work, "upgrade");
        Directory.CreateDirectory(dir);
        string settingsFile = Path.Combine(dir, "settings.xml");
        File.WriteAllText(settingsFile, "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<NekoRayNT5Settings formatVersion=\"1\">"
            + "<socksPort>10808</socksPort><httpPort>10809</httpPort><tunMode>false</tunMode><rememberLast>true</rememberLast>"
            + "<lastProfileId>3</lastProfileId><currentGroupId>1</currentGroupId></NekoRayNT5Settings>");
        string warning;
        AppSettings s = new SettingsStore(settingsFile).Load(out warning);
        Check("0.9.1 settings: old values kept, new ones at their defaults", warning == null && s.SocksPort == 10808 && s.HttpPort == 10809
            && s.RememberLast && s.LastProfileId == 3 && s.SubscriptionRoute == AppSettings.RouteAuto && s.HwidGenerated == ""
            && s.HwidCustom == "" && !s.FirstRunDone);

        string profilesFile = Path.Combine(dir, "profiles.xml");
        File.WriteAllText(profilesFile, "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<NekoRayNT5 formatVersion=\"2\">"
            + "<groups nextId=\"2\"><group id=\"0\" name=\"Default\" url=\"\" updated=\"\" />"
            + "<group id=\"1\" name=\"Sub\" url=\"https://sub.example.com/s\" updated=\"2026-10-01 10:00\" /></groups>"
            + "<profiles nextId=\"4\"></profiles></NekoRayNT5>");
        ProfileStore store = new ProfileStore(profilesFile);
        string loadWarning = store.Load();
        ProfileGroup g = store.FindGroup(1);
        Check("0.9.1 subscription: opens without HWID and without a provider note", loadWarning == null && g != null && g.IsSubscription
            && !g.UseHwid && g.Info == "" && g.LastUpdate == "2026-10-01 10:00");
    }

    // Settings of 0.9.2-0.9.5 with TUN mode switched on: TUN mode was removed. They open without a
    // warning, keep every other value and lose the TUN fields on the next save; profiles are untouched.
    private static void TestUpgradeFrom095()
    {
        Section("Upgrade: settings of 0.9.5 with TUN mode on (TUN mode removed)");
        string dir = Path.Combine(_work, "upgrade095");
        Directory.CreateDirectory(dir);
        string settingsFile = Path.Combine(dir, "settings.xml");
        File.WriteAllText(settingsFile, "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<NekoRayNT5Settings formatVersion=\"1\">"
            + "<socksPort>11808</socksPort><httpPort>11809</httpPort><tunMode>true</tunMode>"
            + "<tunAdapter>{11111111-2222-3333-4444-555555555555}</tunAdapter><tunDns>8.8.4.4</tunDns>"
            + "<directIps>203.0.113.0/24</directIps><subscriptionRoute>direct</subscriptionRoute><rememberLast>true</rememberLast>"
            + "<lastProfileId>7</lastProfileId></NekoRayNT5Settings>");
        string warning;
        SettingsStore store = new SettingsStore(settingsFile);
        AppSettings s = store.Load(out warning);
        Check("0.9.5 settings with TUN on: open without a warning, other values kept", warning == null && s.SocksPort == 11808
            && s.HttpPort == 11809 && s.DirectIps == "203.0.113.0/24" && s.SubscriptionRoute == AppSettings.RouteDirect
            && s.RememberLast && s.LastProfileId == 7);
        store.Save(s);
        string saved = File.ReadAllText(settingsFile);
        Check("saving again drops the TUN fields", saved.IndexOf("tun", StringComparison.OrdinalIgnoreCase) < 0
            && saved.IndexOf("11808") >= 0);
        Check("direct IP rules are always used now (they were dropped in TUN mode)",
            XrayConfigBuilder.Build(Reality(), s).IndexOf("203.0.113.0/24") >= 0);
    }

    // Files that a 0.9.x TUN session could leave in the run folder are removed at start-up; an unrelated
    // process is never touched (the pid file names a process that is not the helper).
    private static void TestLegacyCleanup()
    {
        Section("Start-up after 0.9.x: leftovers of the TUN mode");
        string dir = Path.Combine(_work, "run-legacy");
        Directory.CreateDirectory(dir);
        Check("nothing left over: no note", LegacyCleanup.Run(dir, Path.Combine(dir, "nt5helper.exe")) == "");
        File.WriteAllText(Path.Combine(dir, LegacyCleanup.TunStateFileName), "<routes />");
        File.WriteAllText(Path.Combine(dir, ChildPidFile.LegacyTun + ".pid"),
            Process.GetCurrentProcess().Id.ToString(System.Globalization.CultureInfo.InvariantCulture) + " 0");
        string note = LegacyCleanup.Run(dir, Path.Combine(dir, "nt5helper.exe"));
        Check("state and pid files removed, the note explains the routes", note.IndexOf("10.255.0.1") >= 0
            && note.IndexOf("helper process was stopped") < 0
            && !File.Exists(Path.Combine(dir, LegacyCleanup.TunStateFileName)) && !File.Exists(Path.Combine(dir, "tun.pid")));
        Check("this process (named in the pid file, but not the helper) is still running", !Process.GetCurrentProcess().HasExited);
    }

    private static string FetchError(SubscriptionClient client, string url)
    {
        try
        {
            client.Fetch(url, false);
            return null;
        }
        catch (IOException ex)
        {
            return ex.Message;
        }
    }

    private static void TestPorts()
    {
        Section("Local ports: free for TCP and UDP, outside the dynamic range, never twice");
        Dictionary<int, bool> seen = new Dictionary<int, bool>();
        bool ok = true;
        for (int i = 0; i < 40; i++)
        {
            int port = CoreLauncher.FindFreePort();
            if (seen.ContainsKey(port) || port < CoreLauncher.FirstTestPort || port > CoreLauncher.LastTestPort)
            {
                ok = false;
            }
            seen[port] = true;
        }
        Check("40 ports: all different and below the dynamic range", ok);
        int free = CoreLauncher.FindFreePort();
        Check("a returned port can be bound for TCP and UDP", CoreLauncher.CheckPortFree("127.0.0.1", free, true) == null);
        TcpListener busy = new TcpListener(IPAddress.Loopback, free);
        busy.Start();
        try
        {
            string problem = CoreLauncher.CheckPortFree("127.0.0.1", free, true);
            Check("a listening port is reported as used", problem != null && problem.IndexOf("used") >= 0);
        }
        finally
        {
            busy.Stop();
        }
        using (Socket udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
        {
            udp.ExclusiveAddressUse = true;
            udp.Bind(new IPEndPoint(IPAddress.Loopback, free));
            string problem = CoreLauncher.CheckPortFree("127.0.0.1", free, true);
            Check("a port taken for UDP only is found too", problem != null && problem.IndexOf("UDP") >= 0);
        }
        Check("WSAEACCES in the core output is explained (reserved range), not hidden",
            CoreLauncher.DescribeBindFailure("listen tcp 127.0.0.1:50000: bind: An attempt was made to access a socket in a way "
                + "forbidden by its access permissions.").IndexOf("WSAEACCES") >= 0);
        Check("a taken port in the core output is explained", CoreLauncher.DescribeBindFailure("bind: Only one usage of each socket "
            + "address (protocol/network address/port) is normally permitted.").IndexOf("already used") >= 0);
        Check("other start-up output is not taken for a port problem", CoreLauncher.DescribeBindFailure("failed to load config") == null);
    }
}
