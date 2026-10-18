// More automatic checks (version 0.9): storage recovery, subscription merge and download, executable
// compatibility and connect requests.
// Synthetic data only; the real proxy settings are only read, never changed.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using NekoRayNT5.Core;
using NekoRayNT5.Model;
using NekoRayNT5.Platform;
using NekoRayNT5.Services;

internal static partial class SelfTest
{
    private const string Token = "synthetic-token-7f3a";

    // ---------------------------------------------------------------- storage recovery

    private static void TestStoreRecovery()
    {
        Section("Profile storage: recovery, duplicate ids, test results, backup");
        string dir = Path.Combine(_work, "recovery");
        string file = Path.Combine(dir, "profiles.xml");
        ProfileStore s = new ProfileStore(file);
        s.Load();
        s.Add(SampleProfiles(), ProfileGroup.DefaultId);
        s.AddGroup("Second", "");
        Check("two saves leave a previous version (.bak)", File.Exists(file + ".bak"));

        string good = File.ReadAllText(file);
        File.WriteAllText(file, good.Substring(0, good.Length / 3));
        ProfileStore r = new ProfileStore(file);
        string w = r.Load();
        Check("damaged file: the previous version is loaded instead of an empty list",
            w != null && w.IndexOf("previous version") >= 0 && r.Profiles.Count == 14 && r.ReadOnlyReason == null);
        Check("damaged file: kept aside", Directory.GetFiles(dir, "profiles.xml.damaged-*").Length == 1);

        // duplicate ids (hand-edited or written by an old bug): renumbered, nothing dropped
        string dup = good.Replace("<profile id=\"2\"", "<profile id=\"1\"");
        File.WriteAllText(file, dup);
        ProfileStore d = new ProfileStore(file);
        string wd = d.Load();
        Check("duplicate id: both profiles kept, one renumbered, warning", wd != null && wd.IndexOf("new id") >= 0
            && d.Profiles.Count == 14 && d.Find(1) != null && d.Find(15) != null);
        List<ProfileEntry> more = new List<ProfileEntry>();
        more.Add(Reality());
        d.Add(more, 0);
        Check("ids stay unique after renumbering", more[0].Id == 16);

        // test results are stored
        d.SetTestResult(1, 123, "url", "10-14 12:00");
        Check("test result saved", d.SaveTestResults());
        ProfileStore t = new ProfileStore(file);
        t.Load();
        Check("test result and time survive a restart", t.Find(1).Latency == 123 && t.Find(1).TestedAt == "10-14 12:00"
            && t.Find(1).DisplayLatency.IndexOf("10-14 12:00") >= 0);
        t.ClearTestResults(Ids(1));
        ProfileStore t2 = new ProfileStore(file);
        t2.Load();
        Check("cleared test result stays cleared", t2.Find(1).Latency == 0);

        // copy to group
        int copied = t2.CopyToGroup(Ids(1, 3), 1, "");
        Check("copy to group: new ids, originals untouched", copied == 2 && t2.ProfilesOf(1).Count == 2 && t2.Find(1).GroupId == 0
            && t2.ProfilesOf(1)[0].Id > 16);

        // backup export and import
        string backup = Path.Combine(dir, "backup.xml");
        t2.ExportTo(backup);
        string other = Path.Combine(Path.Combine(_work, "recovery2"), "profiles.xml");
        ProfileStore target = new ProfileStore(other);
        target.Load();
        List<ProfileEntry> own = new List<ProfileEntry>();
        own.Add(Reality());
        target.Add(own, 0);
        int groups;
        string warning;
        int imported = target.ImportFrom(backup, out groups, out warning);
        Check("backup import adds everything with new ids, keeps own profiles", imported == t2.Profiles.Count && groups == 1
            && target.Profiles.Count == t2.Profiles.Count + 1 && target.Find(own[0].Id) != null && warning == null);
        ProfileStore reread = new ProfileStore(other);
        Check("imported profiles are saved", reread.Load() == null && reread.Profiles.Count == target.Profiles.Count);
        bool threw = false;
        try
        {
            File.WriteAllText(backup, "<not a backup/>");
            target.ImportFrom(backup, out groups, out warning);
        }
        catch (InvalidDataException) { threw = true; }
        Check("importing a foreign file is refused and changes nothing", threw && target.Profiles.Count == reread.Profiles.Count
            && File.ReadAllText(backup) == "<not a backup/>");
    }

    // ---------------------------------------------------------------- subscription merge

    private static ProfileEntry SubEntry(string name, int port)
    {
        ProfileEntry p = Reality();
        p.Name = name;
        p.Port = port;
        return p;
    }

    private static List<ProfileEntry> SubList(params ProfileEntry[] items)
    {
        return new List<ProfileEntry>(items);
    }

    private static void TestSubscriptionMerge()
    {
        Section("Subscription update: merge without loss or duplicates");
        string file = Path.Combine(Path.Combine(_work, "merge"), "profiles.xml");
        ProfileStore s = new ProfileStore(file);
        s.Load();
        ProfileGroup g = s.AddGroup("Sub", "http://127.0.0.1/sub");
        List<ProfileEntry> manual = new List<ProfileEntry>();
        manual.Add(SubEntry("manual in default", 1001));
        s.Add(manual, 0);
        List<ProfileEntry> manualInSub = new List<ProfileEntry>();
        manualInSub.Add(SubEntry("added by hand", 1002));
        s.Add(manualInSub, g.Id);

        MergeResult r1 = s.MergeSubscription(g.Id, SubList(SubEntry("a", 1), SubEntry("b", 2), SubEntry("c", 3), SubEntry("c dup", 3)), "t1");
        Check("first update adds the profiles, ignores an identical duplicate", r1.Added == 3 && r1.DuplicatesIgnored == 1 && s.ProfilesOf(g.Id).Count == 4);
        int idA = FindByName(s, g.Id, "a").Id;
        int idB = FindByName(s, g.Id, "b").Id;
        s.SetTestResult(idB, 50, "", "x");

        MergeResult r2 = s.MergeSubscription(g.Id, SubList(SubEntry("a", 1), SubEntry("b", 2), SubEntry("c", 3)), "t2");
        Check("same content again: nothing added, nothing removed, same ids", r2.Added == 0 && r2.Removed == 0 && r2.Unchanged == 3
            && s.ProfilesOf(g.Id).Count == 4 && FindByName(s, g.Id, "a").Id == idA);
        Check("test result of an unchanged profile is kept", s.Find(idB).Latency == 50);

        ProfileEntry renamed = SubEntry("b renamed", 2);
        ProfileEntry moved = SubEntry("a", 1);
        moved.ShortId = "abcd";
        MergeResult r3 = s.MergeSubscription(g.Id, SubList(moved, renamed, SubEntry("d", 4)), "t3");
        Check("changed profiles keep their ids (by settings, else by unique name), new one added, missing one removed",
            r3.Updated == 2 && r3.Added == 1 && r3.Removed == 1 && s.Find(idA) != null && s.Find(idA).ShortId == "abcd"
            && s.Find(idB) != null && s.Find(idB).Name == "b renamed" && FindByName(s, g.Id, "c") == null);
        Check("a profile added by hand to the subscription group survives", FindByName(s, g.Id, "added by hand") != null);
        Check("other groups are untouched", s.ProfilesOf(0).Count == 1 && s.ProfilesOf(0)[0].Name == "manual in default");
        Check("last update time stored, error cleared", s.FindGroup(g.Id).LastUpdate == "t3" && s.FindGroup(g.Id).LastError == "");

        ProfileStore reload = new ProfileStore(file);
        Check("merge result is saved, subscription marker survives a restart", reload.Load() == null && reload.ProfilesOf(g.Id).Count == 4
            && FindByName(reload, g.Id, "added by hand").FromSubscription == false && FindByName(reload, g.Id, "d").FromSubscription);

        s.SetSubscriptionError(g.Id, "The server answered HTTP 500.");
        ProfileStore reload2 = new ProfileStore(file);
        reload2.Load();
        Check("failed update: error noted, profiles unchanged", reload2.FindGroup(g.Id).LastError.IndexOf("500") >= 0
            && reload2.ProfilesOf(g.Id).Count == 4);

        // a failed save (newer file appeared in between) changes nothing in memory
        string before = File.ReadAllText(file);
        File.SetAttributes(file, FileAttributes.ReadOnly);
        bool threw = false;
        try { s.MergeSubscription(g.Id, SubList(SubEntry("only", 9)), "t4"); }
        catch (Exception) { threw = true; }
        File.SetAttributes(file, FileAttributes.Normal);
        Check("merge that cannot be saved is rolled back", threw && s.ProfilesOf(g.Id).Count == 4 && File.ReadAllText(file) == before);

        // old (0.3.0) files: everything in a subscription group counts as coming from the subscription
        string legacy = before.Replace(" sub=\"true\"", "").Replace(" sub=\"false\"", "");
        File.WriteAllText(file, legacy);
        ProfileStore old = new ProfileStore(file);
        old.Load();
        MergeResult r5 = old.MergeSubscription(g.Id, SubList(SubEntry("d", 4)), "t5");
        Check("0.3.0 file: subscription group content is replaced by the merge", r5.Removed == 3 && old.ProfilesOf(g.Id).Count == 1);
    }

    private static ProfileEntry FindByName(ProfileStore s, int group, string name)
    {
        foreach (ProfileEntry p in s.ProfilesOf(group))
        {
            if (p.Name == name) { return p; }
        }
        return null;
    }

    // ---------------------------------------------------------------- subscription download (local server)

    private delegate byte[] Responder(string path);

    // Answers each request with the raw bytes returned for its path.
    private sealed class ScriptedHttpServer
    {
        private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
        private readonly Responder _responder;
        public int Port;
        public int Requests;
        // the request line and headers of the last request (to check what the client sent)
        public volatile string LastHead = "";

        public ScriptedHttpServer(Responder responder)
        {
            _responder = responder;
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Thread t = new Thread(Loop);
            t.IsBackground = true;
            t.Start();
        }

        public void Stop()
        {
            _listener.Stop();
        }

        private void Loop()
        {
            while (true)
            {
                TcpClient client;
                try { client = _listener.AcceptTcpClient(); }
                catch (Exception) { return; }
                try
                {
                    NetworkStream stream = client.GetStream();
                    stream.ReadTimeout = 3000;
                    byte[] buffer = new byte[8192];
                    int read = stream.Read(buffer, 0, buffer.Length);
                    string head = Encoding.ASCII.GetString(buffer, 0, read);
                    LastHead = head;
                    string path = head.Split(' ')[1];
                    Interlocked.Increment(ref Requests);
                    byte[] reply = _responder(path);
                    stream.Write(reply, 0, reply.Length);
                }
                catch (Exception)
                {
                }
                finally
                {
                    client.Close();
                }
            }
        }
    }

    private static byte[] Reply(int status, string type, byte[] body, string extraHeaders)
    {
        string head = "HTTP/1.1 " + status + " X\r\nContent-Type: " + type + "\r\nContent-Length: " + body.Length
                      + "\r\nConnection: close\r\n" + extraHeaders + "\r\n";
        byte[] h = Encoding.ASCII.GetBytes(head);
        byte[] all = new byte[h.Length + body.Length];
        Buffer.BlockCopy(h, 0, all, 0, h.Length);
        Buffer.BlockCopy(body, 0, all, h.Length, body.Length);
        return all;
    }

    private static string DownloadError(string url, DownloadTools tools)
    {
        try
        {
            Subscription.Download(url, 0, tools);
            return null;
        }
        catch (IOException ex)
        {
            return ex.Message;
        }
    }

    private static void TestSubscriptionDownload()
    {
        Section("Subscription download (local HTTP server, synthetic data)");
        string list = Vless("security=reality&pbk=" + Pbk + "&sni=www.example.com&fp=chrome", "one") + "\r\n\r\n  "
                      + "trojan://" + Secret + "@test.example:443?security=tls#two  \r\nnot-a-link\n";
        string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(list));
        string big = new string('A', Subscription.MaxBytes + 10);
        ScriptedHttpServer server = new ScriptedHttpServer(delegate(string path)
        {
            if (path.StartsWith("/plain")) { return Reply(200, "text/plain", Encoding.UTF8.GetBytes(list), ""); }
            if (path.StartsWith("/b64")) { return Reply(200, "text/plain; charset=utf-8", Encoding.ASCII.GetBytes(b64.Substring(0, 30) + "\n" + b64.Substring(30)), ""); }
            if (path.StartsWith("/moved")) { return Reply(302, "text/plain", new byte[0], "Location: /plain?" + Token + "\r\n"); }
            if (path.StartsWith("/loop")) { return Reply(302, "text/plain", new byte[0], "Location: /loop\r\n"); }
            if (path.StartsWith("/missing")) { return Reply(404, "text/plain", Encoding.ASCII.GetBytes("no"), ""); }
            if (path.StartsWith("/html")) { return Reply(200, "text/html", Encoding.ASCII.GetBytes("<!DOCTYPE html><html>login</html>"), ""); }
            if (path.StartsWith("/empty")) { return Reply(200, "text/plain", new byte[0], ""); }
            if (path.StartsWith("/binary")) { return Reply(200, "application/octet-stream", new byte[] { 1, 0, 2, 0 }, ""); }
            if (path.StartsWith("/image")) { return Reply(200, "image/png", new byte[] { 137, 80, 78, 71 }, ""); }
            if (path.StartsWith("/big")) { return Reply(200, "text/plain", Encoding.ASCII.GetBytes(big), ""); }
            return Reply(500, "text/plain", new byte[0], "");
        });
        string baseUrl = "http://127.0.0.1:" + server.Port;
        DownloadTools tools = new DownloadTools();
        tools.TempDir = Path.Combine(_work, "subtmp");
        try
        {
            LinkProblems errors = new LinkProblems();
            List<ProfileEntry> plain = Subscription.Decode(Subscription.Download(baseUrl + "/plain?" + Token, 0, tools), errors);
            Check("plain list with CRLF, blank lines and spaces: valid links imported, bad one reported", plain.Count == 2
                && errors.Count + errors.NotLinks == 1 && (errors.Count == 0 || errors.Details[0].IndexOf("not-a-link") < 0));
            errors.Clear();
            Check("Base64 list with a line break", Subscription.Decode(Subscription.Download(baseUrl + "/b64", 0, tools), errors).Count == 2);
            Check("redirect followed", Subscription.Download(baseUrl + "/moved", 0, tools).IndexOf("vless://") >= 0);

            string e;
            e = DownloadError(baseUrl + "/loop?" + Token, tools);
            Check("redirect loop stopped", e != null && e.IndexOf("redirect") >= 0);
            e = DownloadError(baseUrl + "/missing?" + Token, tools);
            Check("HTTP 404 explained", e != null && e.IndexOf("404") >= 0);
            e = DownloadError(baseUrl + "/html", tools);
            Check("login page (HTML) is not taken for a subscription", e != null && e.IndexOf("web page") >= 0);
            e = DownloadError(baseUrl + "/empty", tools);
            Check("empty answer refused", e != null && e.IndexOf("empty") >= 0);
            e = DownloadError(baseUrl + "/binary", tools);
            Check("binary answer refused", e != null && e.IndexOf("binary") >= 0);
            e = DownloadError(baseUrl + "/image", tools);
            Check("unexpected MIME type refused", e != null && e.IndexOf("image/png") >= 0);
            e = DownloadError(baseUrl + "/big", tools);
            Check("answer over 5 MB refused", e != null && e.IndexOf("5 MB") >= 0);
            e = DownloadError("http://127.0.0.1:" + CoreLauncher.FindFreePort() + "/sub?" + Token, tools);
            Check("connection refused explained", e != null && e.Length > 0);
            Check("error messages never contain the address or its token", !Contains(Token, e));

            Check("address check: spaces, quotes and non-ASCII refused", Subscription.CheckUrl("http://a b/") != null
                && Subscription.CheckUrl("http://x/\"") != null && Subscription.CheckUrl("http://x/\u0444") != null
                && Subscription.CheckUrl("https://example.com/sub?token=1") == null && Subscription.CheckUrl("file:///c:/x") != null);

            // https without any downloader: clear advice, no insecure fallback
            e = DownloadError("https://127.0.0.1:" + server.Port + "/plain?" + Token, tools);
            Check("HTTPS with only .NET 2.0: refused with advice (no certificate bypass)", e != null && !Contains(Token, e)
                && (e.IndexOf("nt5helper") >= 0 || e.IndexOf("failed") >= 0));

            string helper = Path.Combine(Path.GetDirectoryName(_xray), "nt5helper.exe");
            if (!File.Exists(helper))
            {
                helper = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(Path.Combine(_work, "x"))), "nt5helper.exe");
            }
            string helperEnv = Environment.GetEnvironmentVariable("NT5HELPER_EXE");
            if (helperEnv != null && File.Exists(helperEnv))
            {
                tools.HelperPath = helperEnv;
                e = DownloadError("https://127.0.0.1:" + server.Port + "/plain?" + Token, tools);
                Check("HTTPS through nt5helper against a non-TLS server fails cleanly, without the token", e != null
                    && e.IndexOf("nt5helper") >= 0 && !Contains(Token, e));
                tools.HelperPath = "";
            }
            else
            {
                Console.WriteLine("      (NT5HELPER_EXE not set: the nt5helper HTTPS path is covered by its own Go tests)");
            }

            // content checks shared with "Import subscription from file"
            bool threw = false;
            try { Subscription.Decode(Subscription.CheckContent(Encoding.UTF8.GetBytes("<html><body>x</body></html>")), errors); }
            catch (IOException) { threw = true; }
            Check("file import: a saved web page is refused", threw);
            errors.Clear();
            string encoded = list.Replace("://", "%3A%2F%2F");
            Check("percent-encoded list is decoded", Subscription.Decode(encoded, errors).Count == 2);
        }
        finally
        {
            server.Stop();
        }
    }

    private static bool Contains(string needle, string text)
    {
        return text != null && text.IndexOf(needle) >= 0;
    }

    // ---------------------------------------------------------------- can Windows load the program?

    // Every Go program imports CreateThread at load time (the XP build and the NT 5.0 build of xray.exe).
    private static bool NoCreateThread(string dll, string function)
    {
        return function != "CreateThread" && ExecutableCheck.SystemExports(dll, function);
    }

    // A copy of the program that asks the loader for subsystem version major.minor.
    private static string WithSubsystemVersion(string exe, ushort major, ushort minor)
    {
        byte[] image = File.ReadAllBytes(exe);
        int opt = BitConverter.ToInt32(image, 0x3C) + 24;
        BitConverter.GetBytes(major).CopyTo(image, opt + 48);
        BitConverter.GetBytes(minor).CopyTo(image, opt + 50);
        string copy = Path.Combine(_work, "xray-subsystem-" + major + "." + minor + ".exe");
        File.WriteAllBytes(copy, image);
        return copy;
    }

    private static void TestExecutableCheck()
    {
        Section("Executable check (why 0.3.0 crashed on Windows 2000)");
        string text = Path.Combine(_work, "not-a-program.exe");
        File.WriteAllText(text, "this is text");
        Check("a text file is not a Windows program", ExecutableCheck.Check(text) != null);
        bool threw193 = false;
        try
        {
            string output;
            CoreProcess.RunOnce(text, "version", 5000, out output);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // NT 5.x reports 193 (bad image), newer Windows 216 for the same file
            threw193 = (ex.NativeErrorCode == 193 || ex.NativeErrorCode == 216)
                && CoreLauncher.DescribeStartError(ex).IndexOf(ex.NativeErrorCode.ToString()) >= 0;
        }
        Check("starting it throws Win32Exception 193/216 (this exception crashed 0.3.0 on Windows 2000)", threw193);

        if (File.Exists(_xray))
        {
            Check("xray.exe loads on this system", ExecutableCheck.Check(_xray) == null);
            // Independent of which core build is tested (the XP build asks for NT 5.1, the NT 5.0 build for 5.0):
            // the version is set in a copy.
            string xp = WithSubsystemVersion(_xray, 5, 1);
            string w2k = ExecutableCheck.Check(xp, new Version(5, 0), false, ExecutableCheck.SystemExports);
            Check("a core that asks for NT 5.1 is refused on NT 5.0 (" + w2k + ")", w2k != null && w2k.IndexOf("5.1") >= 0
                && w2k.IndexOf("Windows XP") >= 0);
            Check("the same core is accepted on NT 5.1", ExecutableCheck.Check(xp, new Version(5, 1), false, ExecutableCheck.SystemExports) == null);
            string nt50 = WithSubsystemVersion(_xray, 5, 0);
            Check("a core that asks for NT 5.0 is accepted on NT 5.0",
                ExecutableCheck.Check(nt50, new Version(5, 0), false, ExecutableCheck.SystemExports) == null);
            string missing = ExecutableCheck.Check(_xray, new Version(5, 1), false, NoCreateThread);
            Check("a missing kernel32 function is named", missing != null && missing.IndexOf("kernel32.dll!CreateThread") >= 0);
        }
        string self = Process.GetCurrentProcess().MainModule.FileName;
        Check("this test program itself loads", ExecutableCheck.Check(self) == null);
    }

    // ---------------------------------------------------------------- platform calls that only read

    // The core's HTTP port after a disconnect: the browsers' old connections are left in FIN_WAIT_2 / TIME_WAIT,
    // and the port must count as free (Windows XP refused an exclusive bind there, see CoreLauncher.TryBind).
    // A real listener, on 127.0.0.1 or on 0.0.0.0, is still reported.
    private static void TestPortCheckAfterDisconnect()
    {
        Socket listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);
        int port = ((IPEndPoint)listener.LocalEndPoint).Port;
        Check("port check reports a listener on 127.0.0.1", CoreLauncher.CheckPortFree("127.0.0.1", port) != null);
        TcpClient client = new TcpClient();
        client.Connect(IPAddress.Loopback, port);
        Socket accepted = listener.Accept();
        // the core goes away first, as on Disconnect
        listener.Close();
        accepted.Close();
        Thread.Sleep(300);
        Check("port with only a leftover connection (FIN_WAIT_2) counts as free", CoreLauncher.CheckPortFree("127.0.0.1", port) == null);
        client.Close();
        Thread.Sleep(300);
        Check("port with only a leftover connection (TIME_WAIT) counts as free", CoreLauncher.CheckPortFree("127.0.0.1", port) == null);

        TcpListener any = new TcpListener(IPAddress.Any, 0);
        any.Start();
        int anyPort = ((IPEndPoint)any.LocalEndpoint).Port;
        string problem = CoreLauncher.CheckPortFree("127.0.0.1", anyPort);
        any.Stop();
        Check("port check reports a listener on 0.0.0.0", problem != null);

        // bound but not listening: not in the TCP table, only the test bind finds it
        Socket bound = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        bound.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        int boundPort = ((IPEndPoint)bound.LocalEndPoint).Port;
        problem = CoreLauncher.CheckPortFree("127.0.0.1", boundPort);
        bound.Close();
        Check("port check reports a socket bound to 127.0.0.1 without listening", problem != null);
    }

    private static void TestPlatformReadOnly()
    {
        Section("Platform (read-only calls on the real system)");
        TcpListener l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        bool listening = TcpTable.IsListening(port);
        l.Stop();
        Check("TCP table sees a listening port, and not a free one", listening && !TcpTable.IsListening(CoreLauncher.FindFreePort()));
        Check("port check reports a listener on the port", CoreLauncher.CheckPortFree("127.0.0.1", port) == null);
        TestPortCheckAfterDisconnect();

        // DiagLog writes, and survives a missing directory
        string logDir = Path.Combine(_work, "logs");
        NekoRayNT5.App.DiagLog.Directory = logDir;
        NekoRayNT5.App.DiagLog.Write("synthetic line");
        string crash = NekoRayNT5.App.DiagLog.WriteCrash("synthetic", new InvalidOperationException("synthetic failure"));
        Check("diagnostic and crash logs are written", File.ReadAllText(Path.Combine(logDir, "nekoray-nt5.log")).IndexOf("synthetic line") >= 0
            && crash != null && File.ReadAllText(crash).IndexOf("synthetic failure") >= 0);
        NekoRayNT5.App.DiagLog.Directory = null;
    }

    // ---------------------------------------------------------------- connect requests (double click, switching)

    private static int CountOurCores()
    {
        int n = 0;
        foreach (Process p in Process.GetProcesses())
        {
            try
            {
                if (string.Equals(p.MainModule.FileName, _xray, StringComparison.OrdinalIgnoreCase)) { n++; }
            }
            catch (Exception) { }
            finally { p.Dispose(); }
        }
        return n;
    }

    private static void TestConnectRequests()
    {
        Section("Connect requests: double click, repeated clicks, fast switching, Stop while connecting");
        string runDir = Path.Combine(_work, "run2");
        int upstreamPort = CoreLauncher.FindFreePort();
        CoreProcess upstream = CoreLauncher.Launch(_xray, runDir, UpstreamConfig(upstreamPort), new SecretMask(LocalSocks(0, upstreamPort)),
            new int[] { upstreamPort }, 15000, null, null);
        int baseline = CountOurCores();
        AppSettings settings = new AppSettings();
        settings.SocksPort = CoreLauncher.FindFreePort();
        settings.HttpPort = CoreLauncher.FindFreePort();
        CoreController core = new CoreController(_xray, runDir);
        // the last error of the core, printed when a check fails (it explains intermittent failures)
        string lastError = "";
        core.StateChanged += delegate(CoreState s, string message)
        {
            if (s == CoreState.Error) { lastError = " [ports " + settings.SocksPort + "/" + settings.HttpPort + "/" + upstreamPort + ": " + (message ?? "").Replace("\r\n", " | ").Replace("\n", " | ") + "]"; }
        };
        try
        {
            ProfileEntry a = LocalSocks(1, upstreamPort);
            StartResult first = core.Start(a, settings, false);
            Check("the first request is accepted and shows Connecting at once", first == StartResult.Started && core.State == CoreState.Connecting
                && core.IsBusy);
            StartResult second = core.Start(a, settings, false);
            StartResult third = core.Start(a, settings, false);
            Check("repeated requests for the same profile are ignored", second == StartResult.AlreadyActive && third == StartResult.AlreadyActive);
            bool connected = WaitState(core, CoreState.Running, 20000);
            Check("connected" + (connected ? "" : lastError), connected);
            Check("exactly one core process", CountOurCores() == baseline + 1);
            Check("a request for the running profile is ignored too", core.Start(a, settings, false) == StartResult.AlreadyActive);

            // fast switching: B, C, B while the first switch is still starting
            core.Start(LocalSocks(2, upstreamPort), settings, false);
            core.Start(LocalSocks(3, upstreamPort), settings, false);
            core.Start(LocalSocks(2, upstreamPort), settings, false);
            Thread.Sleep(300);
            Check("fast switching ends with the last profile", WaitState(core, CoreState.Running, 20000) && core.ActiveProfileId == 2);
            Thread.Sleep(500);
            Check("and with one core process only", CountOurCores() == baseline + 1);
            Check("traffic flows", CoreLauncher.IsPortOpen(settings.HttpPort));

            // Stop while connecting
            core.Start(LocalSocks(4, upstreamPort), settings, false);
            core.Stop();
            Check("Stop during Connecting ends in Not Running", WaitState(core, CoreState.NotRunning, 20000));
            Thread.Sleep(1000);
            Check("no core process is left after Stop during Connecting", CountOurCores() == baseline && core.State == CoreState.NotRunning);

            Check("restart = true starts the same profile again", core.Start(a, settings, true) == StartResult.Started
                && WaitState(core, CoreState.Running, 20000));
            int pid1 = ReadPid(runDir);
            Check("restart replaces the process", core.Start(a, settings, true) == StartResult.Started && WaitState(core, CoreState.Running, 20000)
                && ReadPid(runDir) != pid1 && !ProcessAlive(pid1));

            // the pid file does not let an unrelated process be killed
            File.WriteAllText(Path.Combine(runDir, "core.pid"), upstream.Id + " 1");
            CoreLauncher.CleanupStale(_xray, runDir);
            Check("a pid file with another start time does not kill that process", ProcessAlive(upstream.Id));
        }
        finally
        {
            core.Shutdown(5000);
            upstream.Stop();
            upstream.WaitForExit(3000);
        }
    }

    private static bool WaitCores(int count)
    {
        for (int i = 0; i < 60; i++)
        {
            if (CountOurCores() == count) { return true; }
            Thread.Sleep(100);
        }
        return false;
    }

    // ---------------------------------------------------------------- a core that Windows cannot start

    private static void TestBrokenCore()
    {
        Section("A core that cannot start (as xray.exe on Windows 2000)");
        string fake = Path.Combine(_work, "fake-core.exe");
        File.WriteAllText(fake, "not a program");
        CoreController core = new CoreController(fake, Path.Combine(_work, "run4"));
        string message = "";
        core.StateChanged += delegate(CoreState state, string m) { if (state == CoreState.Error) { message = m; } };
        AppSettings settings = new AppSettings();
        settings.SocksPort = CoreLauncher.FindFreePort();
        settings.HttpPort = CoreLauncher.FindFreePort();
        core.Start(LocalSocks(1, 1), settings, false);
        Check("Error state with an explanation instead of a crash", WaitState(core, CoreState.Error, 10000)
            && message.IndexOf("cannot run on this system") >= 0);
        core.Shutdown(1000);
    }
}
