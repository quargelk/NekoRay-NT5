// Automatic checks for the non-visual part of NekoRay NT5.
// Built and run by run-tests.cmd with the C# 2.0 compiler; not part of the Visual Studio solution.
// All data here is synthetic. The checks never touch the real system proxy settings,
// the user's data directory or any remote server.
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
    private const string Uuid = "00000000-0000-4000-8000-000000000001";
    private const string Pbk = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string Secret = "synthetic-password-0001";

    private static int _failed;
    private static int _passed;
    private static int _skipped;
    private static string _work;
    private static string _xray;

    private static void Check(string name, bool ok)
    {
        if (ok) { _passed++; } else { _failed++; }
        Console.WriteLine((ok ? "PASS  " : "FAIL  ") + name);
    }

    private static void Section(string name)
    {
        Console.WriteLine();
        Console.WriteLine("== " + name);
    }

    private static int Main(string[] args)
    {
        _xray = args.Length > 0 ? Path.GetFullPath(args[0]) : "";
        _work = Path.Combine(Path.GetTempPath(), "nekoray-nt5-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_work);
        try
        {
            // run-tests.cmd xray.exe TestPorts,TestDownloadRoutes: only these sections (targeted re-runs)
            if (args.Length > 1)
            {
                foreach (string name in string.Join(",", args, 1, args.Length - 1).Split(new char[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    System.Reflection.MethodInfo m = typeof(SelfTest).GetMethod(name.Trim(),
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    if (m == null) { Check("test section " + name + " exists", false); continue; }
                    m.Invoke(null, null);
                }
                return Finish();
            }
            TestJson();
            TestLinks();
            TestStore();
            TestSettings();
            TestSubscriptionDecode();
            TestSecretMask();
            TestSystemProxy();
            TestStoreRecovery();
            TestSubscriptionMerge();
            TestSubscriptionDownload();
            TestAddSource();
            TestAddClassification();
            TestTranslation();
            TestDownloadRoutes();
            TestTempCleanup();
            TestUpgradeFrom091();
            TestPorts();
            TestCaBundle();
            TestExecutableCheck();
            TestPlatformReadOnly();
            TestUpgradeFrom095();
            TestLegacyCleanup();
            if (File.Exists(_xray))
            {
                TestGeneratedConfigs();
                TestCoreLifecycle();
                TestConnectRequests();
                TestBrokenCore();
                TestHttpsThroughHelper();
            }
            else
            {
                _skipped += 2;
                Console.WriteLine();
                Console.WriteLine("SKIPPED  configuration and core checks: xray.exe not found (" + _xray + ")");
            }
        }
        catch (Exception ex)
        {
            _failed++;
            Console.WriteLine("FAIL  unexpected exception: " + ex);
        }
        finally
        {
            try { Directory.Delete(_work, true); } catch (Exception) { }
        }
        return Finish();
    }

    private static int Finish()
    {
        Console.WriteLine();
        Console.WriteLine("passed=" + _passed + " failed=" + _failed + " skipped-sections=" + _skipped);
        return _failed == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------- JSON

    private static void TestJson()
    {
        Section("JSON");
        JsonObject o = new JsonObject().Add("s", "a\"b\\c\n\u0422").Add("n", 443).Add("b", true).Add("z", null)
            .Add("a", JsonArray.Of("x", 1, new JsonObject())).Add("e", new JsonArray());
        JsonObject back = Json.Parse(Json.Serialize(o)) as JsonObject;
        Check("round trip of strings with escapes and non-ASCII", back != null && back.GetString("s") == "a\"b\\c\n\u0422");
        Check("round trip of numbers, booleans, null", back.GetString("n") == "443" && back.GetString("b") == "true" && back.Get("z") == null);
        Check("round trip of arrays", ((JsonArray)back.Get("a")).Count == 3 && ((JsonArray)back.Get("e")).Count == 0);
        bool threw = false;
        try { Json.Parse("{\"a\": }"); } catch (FormatException) { threw = true; }
        Check("malformed JSON is rejected", threw);
        threw = false;
        try { Json.Parse("{} x"); } catch (FormatException) { threw = true; }
        Check("trailing data is rejected", threw);
    }

    // ---------------------------------------------------------------- links

    private static string Error(string link)
    {
        try { ShareLinks.Parse(link); return null; }
        catch (LinkFormatException ex) { return ex.Message; }
    }

    private static void Rejects(string name, string link, string expectedPart)
    {
        string e = Error(link);
        bool ok = e != null && e.IndexOf(expectedPart, StringComparison.OrdinalIgnoreCase) >= 0
                  && e.IndexOf(Uuid) < 0 && e.IndexOf(Pbk) < 0 && e.IndexOf(Secret) < 0;
        Check("reject: " + name + (ok ? "" : "  -> got: " + (e == null ? "(accepted)" : e)), ok);
    }

    private static string Vless(string query, string fragment)
    {
        return "vless://" + Uuid + "@test.example:443?" + query + (fragment == null ? "" : "#" + fragment);
    }

    private static bool Same(ProfileEntry a, ProfileEntry b)
    {
        return a.Type == b.Type && a.Name == b.Name && a.Address == b.Address && a.Port == b.Port && a.Uuid == b.Uuid
            && a.Username == b.Username && a.Password == b.Password && a.Encryption == b.Encryption && a.Flow == b.Flow
            && a.Network == b.Network && a.Host == b.Host && a.Path == b.Path && a.Security == b.Security
            && a.Sni == b.Sni && a.Alpn == b.Alpn && a.Fingerprint == b.Fingerprint && a.AllowInsecure == b.AllowInsecure
            && a.PublicKey == b.PublicKey && a.ShortId == b.ShortId && a.SpiderX == b.SpiderX;
    }

    private static ProfileEntry Reality()
    {
        ProfileEntry p = ProfileEntry.Create(ProfileEntry.TypeVless);
        p.Name = "Synthetic REALITY";
        p.Address = "test.example";
        p.Uuid = Uuid;
        p.Flow = "xtls-rprx-vision";
        p.Sni = "www.example.com";
        p.PublicKey = Pbk;
        p.ShortId = "0123abcd";
        p.SpiderX = "/";
        return p;
    }

    private static List<ProfileEntry> SampleProfiles()
    {
        List<ProfileEntry> list = new List<ProfileEntry>();
        list.Add(Reality());

        ProfileEntry p = ProfileEntry.Create(ProfileEntry.TypeVless);
        p.Name = "vless ws tls"; p.Address = "test.example"; p.Uuid = Uuid; p.Network = "ws"; p.Security = "tls";
        p.Fingerprint = ""; p.Host = "cdn.example.com"; p.Path = "/ws?ed=2048"; p.Sni = "cdn.example.com"; p.Alpn = "http/1.1";
        list.Add(p);

        p = ProfileEntry.Create(ProfileEntry.TypeVless);
        p.Name = "vless grpc reality"; p.Address = "2001:db8::1"; p.Uuid = Uuid; p.Network = "grpc"; p.Path = "svc";
        p.Sni = "www.example.com"; p.PublicKey = Pbk;
        list.Add(p);

        p = ProfileEntry.Create(ProfileEntry.TypeVless);
        p.Name = "vless h2 tls"; p.Address = "test.example"; p.Uuid = Uuid; p.Network = "http"; p.Security = "tls";
        p.Fingerprint = "firefox"; p.Host = "a.example.com,b.example.com"; p.Path = "/h2"; p.AllowInsecure = true;
        list.Add(p);

        p = ProfileEntry.Create(ProfileEntry.TypeVless);
        p.Name = "vless plain"; p.Address = "test.example"; p.Port = 8080; p.Uuid = Uuid; p.Security = "none"; p.Fingerprint = "";
        list.Add(p);

        p = ProfileEntry.Create(ProfileEntry.TypeVless);
        p.Name = "vless tls vision"; p.Address = "test.example"; p.Uuid = Uuid; p.Security = "tls"; p.Fingerprint = "chrome";
        p.Flow = "xtls-rprx-vision"; p.Sni = "test.example";
        list.Add(p);

        p = ProfileEntry.Create(ProfileEntry.TypeVmess);
        p.Name = "vmess tcp"; p.Address = "test.example"; p.Port = 10086; p.Uuid = Uuid;
        list.Add(p);

        p = ProfileEntry.Create(ProfileEntry.TypeVmess);
        p.Name = "vmess ws tls \u0422\u0435\u0441\u0442"; p.Address = "test.example"; p.Uuid = Uuid; p.Encryption = "aes-128-gcm";
        p.Network = "ws"; p.Security = "tls"; p.Host = "cdn.example.com"; p.Path = "/vm"; p.Sni = "cdn.example.com";
        list.Add(p);

        p = ProfileEntry.Create(ProfileEntry.TypeTrojan);
        p.Name = "trojan tls"; p.Address = "test.example"; p.Password = Secret + " /?#&="; p.Sni = "test.example";
        list.Add(p);

        p = ProfileEntry.Create(ProfileEntry.TypeTrojan);
        p.Name = "trojan grpc"; p.Address = "test.example"; p.Password = Secret; p.Network = "grpc"; p.Path = "tj";
        list.Add(p);

        p = ProfileEntry.Create(ProfileEntry.TypeShadowsocks);
        p.Name = "ss aead"; p.Address = "test.example"; p.Password = Secret + ":with:colons";
        list.Add(p);

        p = ProfileEntry.Create(ProfileEntry.TypeShadowsocks);
        p.Name = "ss 2022"; p.Address = "test.example"; p.Encryption = "2022-blake3-aes-128-gcm";
        p.Password = "AAAAAAAAAAAAAAAAAAAAAA==";
        list.Add(p);

        p = ProfileEntry.Create(ProfileEntry.TypeSocks);
        p.Name = "socks auth"; p.Address = "test.example"; p.Username = "synthetic-user"; p.Password = Secret;
        list.Add(p);

        p = ProfileEntry.Create(ProfileEntry.TypeHttp);
        p.Name = "http"; p.Address = "test.example";
        list.Add(p);
        return list;
    }

    private static void TestLinks()
    {
        Section("Share links");
        string good = "encryption=none&security=reality&type=tcp&sni=www.example.com&fp=firefox&pbk=" + Pbk
                      + "&sid=0123abcd&spx=%2Fpath%3Fa%3D1&flow=xtls-rprx-vision";
        ProfileEntry p = ShareLinks.Parse("  " + Vless(good, "%D0%A2%D0%B5%D1%81%D1%82%20%231") + "\r\n");
        Check("vless reality: all fields", p.Type == "vless" && p.Address == "test.example" && p.Port == 443 && p.Uuid == Uuid
            && p.Encryption == "none" && p.Security == "reality" && p.Network == "tcp" && p.Sni == "www.example.com"
            && p.Fingerprint == "firefox" && p.PublicKey == Pbk && p.ShortId == "0123abcd" && p.SpiderX == "/path?a=1"
            && p.Flow == "xtls-rprx-vision");
        Check("name is URL-decoded (UTF-8, space, #)", p.Name == "\u0422\u0435\u0441\u0442 #1");

        ProfileEntry d = ShareLinks.Parse("VLESS://" + Uuid + "@[2001:db8::1]:8443/?security=reality&pbk=" + Pbk);
        Check("defaults: fp=chrome, tcp, encryption none; IPv6; default name",
            d.Fingerprint == "chrome" && d.Network == "tcp" && d.Encryption == "none" && d.Flow == "" && d.ShortId == ""
            && d.Address == "2001:db8::1" && d.Port == 8443 && d.Name == "[2001:db8::1]:8443");

        ProfileEntry ws = ShareLinks.Parse(Vless("type=ws&security=tls&host=cdn.example.com&path=%2Fws&sni=cdn.example.com&alpn=h2%2Chttp%2F1.1&allowInsecure=1", "w"));
        Check("vless ws tls: host, path, alpn, allowInsecure", ws.Network == "ws" && ws.Security == "tls" && ws.Host == "cdn.example.com"
            && ws.Path == "/ws" && ws.Alpn == "h2,http/1.1" && ws.AllowInsecure && ws.Fingerprint == "");
        Check("vless without security is plain", ShareLinks.Parse("vless://" + Uuid + "@test.example:80").Security == "none");
        Check("type=h2 maps to http", ShareLinks.Parse(Vless("type=h2&security=tls&path=%2Fa", null)).Network == "http");
        Check("grpc serviceName", ShareLinks.Parse(Vless("type=grpc&security=tls&serviceName=svc", null)).Path == "svc");

        string vmessJson = "{\"v\":\"2\",\"ps\":\"vm \\u0422\",\"add\":\"test.example\",\"port\":443,\"id\":\"" + Uuid
            + "\",\"aid\":0,\"scy\":\"auto\",\"net\":\"ws\",\"type\":\"none\",\"host\":\"h.example.com\",\"path\":\"/p\",\"tls\":\"tls\",\"sni\":\"s.example.com\"}";
        ProfileEntry vm = ShareLinks.Parse("vmess://" + Convert.ToBase64String(Encoding.UTF8.GetBytes(vmessJson)));
        Check("vmess base64 JSON (numeric port)", vm.Type == "vmess" && vm.Port == 443 && vm.Uuid == Uuid && vm.Network == "ws"
            && vm.Security == "tls" && vm.Host == "h.example.com" && vm.Path == "/p" && vm.Sni == "s.example.com" && vm.Name == "vm \u0422");
        Rejects("vmess alterId", "vmess://" + Convert.ToBase64String(Encoding.UTF8.GetBytes(vmessJson.Replace("\"aid\":0", "\"aid\":64"))), "alterId");
        Rejects("vmess garbage", "vmess://!!!!", "damaged");

        ProfileEntry tj = ShareLinks.Parse("trojan://" + PercentEncoding.Encode(Secret + " x") + "@test.example:443?sni=a.example.com#tj");
        Check("trojan: password decoded, tls by default", tj.Type == "trojan" && tj.Password == Secret + " x" && tj.Security == "tls" && tj.Sni == "a.example.com");

        string cred = Convert.ToBase64String(Encoding.UTF8.GetBytes("aes-256-gcm:" + Secret));
        ProfileEntry ss = ShareLinks.Parse("ss://" + cred.TrimEnd('=') + "@test.example:8388#ss%201");
        Check("ss SIP002", ss.Type == "shadowsocks" && ss.Encryption == "aes-256-gcm" && ss.Password == Secret && ss.Port == 8388 && ss.Name == "ss 1");
        ProfileEntry ssLegacy = ShareLinks.Parse("ss://" + Convert.ToBase64String(Encoding.UTF8.GetBytes("aes-128-gcm:" + Secret + "@test.example:8389")) + "#old");
        Check("ss legacy Base64 form", ssLegacy.Encryption == "aes-128-gcm" && ssLegacy.Password == Secret && ssLegacy.Port == 8389);
        Rejects("ss plugin", "ss://" + cred + "@test.example:8388?plugin=obfs-local", "plugin");
        Rejects("ss unknown method", "ss://" + Convert.ToBase64String(Encoding.UTF8.GetBytes("rc4-md5:" + Secret)) + "@test.example:8388", "method");

        Rejects("empty", "   ", "empty");
        Rejects("unknown scheme", "wireguard://abc", "Unsupported link");
        Rejects("plain text", "hello world", "Unsupported link");
        Rejects("no user id", "vless://test.example:443?security=reality&pbk=" + Pbk, "no user ID");
        Rejects("empty uuid", "vless://@test.example:443?security=reality&pbk=" + Pbk, "UUID is empty");
        Rejects("bad uuid", "vless://not-a-uuid@test.example:443?security=reality&pbk=" + Pbk, "UUID must");
        Rejects("no port", "vless://" + Uuid + "@test.example?security=reality&pbk=" + Pbk, "no port");
        Rejects("port text", "vless://" + Uuid + "@test.example:abc?security=reality&pbk=" + Pbk, "Port must");
        Rejects("port 0", "vless://" + Uuid + "@test.example:0?security=reality&pbk=" + Pbk, "Port must");
        Rejects("port 70000", "vless://" + Uuid + "@test.example:70000?security=reality&pbk=" + Pbk, "Port must");
        Rejects("port huge", "vless://" + Uuid + "@test.example:99999999999999?security=reality&pbk=" + Pbk, "Port must");
        Rejects("empty host", "vless://" + Uuid + "@:443?security=reality&pbk=" + Pbk, "address is empty");
        Rejects("host with space", "vless://" + Uuid + "@te%20st:443?security=reality&pbk=" + Pbk, "invalid characters");
        Rejects("unclosed ipv6", "vless://" + Uuid + "@[2001:db8::1:443?security=reality&pbk=" + Pbk, "bracket");
        Rejects("security unknown", Vless("security=xtls", null), "Unsupported security");
        Rejects("type kcp", Vless("security=reality&type=kcp&pbk=" + Pbk, null), "Unsupported transport");
        Rejects("headerType http", Vless("security=reality&headerType=http&pbk=" + Pbk, null), "header type");
        Rejects("encryption other", Vless("security=reality&encryption=aes&pbk=" + Pbk, null), "encryption");
        Rejects("pbk missing", Vless("security=reality", null), "pbk) is empty");
        Rejects("pbk empty", Vless("security=reality&pbk=", null), "pbk) is empty");
        Rejects("pbk short", Vless("security=reality&pbk=AAAA", null), "32 bytes");
        Rejects("pbk bad chars", Vless("security=reality&pbk=" + Pbk.Substring(1) + "*", null), "32 bytes");
        Rejects("sid odd", Vless("security=reality&sid=abc&pbk=" + Pbk, null), "short id");
        Rejects("sid not hex", Vless("security=reality&sid=zz&pbk=" + Pbk, null), "short id");
        Rejects("sid too long", Vless("security=reality&sid=00112233445566778899&pbk=" + Pbk, null), "short id");
        Rejects("spx without slash", Vless("security=reality&spx=abc&pbk=" + Pbk, null), "SpiderX");
        Rejects("flow unknown", Vless("security=reality&flow=xtls-rprx-direct&pbk=" + Pbk, null), "flow");
        Rejects("flow without tls", Vless("flow=xtls-rprx-vision", null), "Flow");
        Rejects("flow with ws", Vless("security=tls&type=ws&flow=xtls-rprx-vision", null), "Flow");
        Rejects("reality with ws", Vless("security=reality&type=ws&pbk=" + Pbk, null), "WebSocket");
        Rejects("h2 without tls", Vless("type=http", null), "HTTP/2");
        Rejects("fp bad", Vless("security=reality&fp=a%20b&pbk=" + Pbk, null), "Fingerprint");
        Rejects("broken escape in name", Vless("security=reality&pbk=" + Pbk, "bad%2"), "URL encoding");
        Rejects("non-hex escape", Vless("security=reality&pbk=" + Pbk, "bad%zz"), "URL encoding");
        Rejects("invalid utf-8 in name", Vless("security=reality&pbk=" + Pbk, "%FF%FE"), "URL encoding");
        Rejects("broken escape in param", Vless("security=reality&sni=%G1&pbk=" + Pbk, null), "URL encoding");
        Rejects("trojan without password", "trojan://@test.example:443", "Password is empty");

        int exported = 0;
        bool allSame = true;
        foreach (ProfileEntry sample in SampleProfiles())
        {
            Check("sample is valid: " + sample.Name, ProfileValidator.Validate(sample) == null);
            if (sample.Type == ProfileEntry.TypeHttp)
            {
                bool refused = false;
                try { ShareLinks.Export(sample); } catch (NotSupportedException) { refused = true; }
                Check("export refused for " + sample.Type, refused);
                continue;
            }
            ProfileEntry back = ShareLinks.Parse(ShareLinks.Export(sample));
            exported++;
            if (!Same(sample, back))
            {
                allSame = false;
                Console.WriteLine("      round trip differs: " + sample.Name);
            }
        }
        Check("export -> import round trip for " + exported + " profiles (SOCKS included)", allSame && exported == 13);
        ProfileEntry v2rayN = ShareLinks.Parse("socks://" + Convert.ToBase64String(Encoding.UTF8.GetBytes("u1:" + Secret)) + "@test.example:1080#s");
        ProfileEntry plainSocks = ShareLinks.Parse("socks5://u2:pw@test.example:1081");
        ProfileEntry noAuth = ShareLinks.Parse("socks://test.example:1082#open");
        Check("socks:// forms: Base64 user part, plain user part, no authentication", v2rayN.Username == "u1" && v2rayN.Password == Secret
            && plainSocks.Username == "u2" && plainSocks.Password == "pw" && noAuth.Username == "" && noAuth.Port == 1082
            && noAuth.Type == ProfileEntry.TypeSocks);

        LinkProblems errors = new LinkProblems();
        List<ProfileEntry> many = ShareLinks.ParseMany(Vless(good, "a") + "\r\n\r\nnot a link\n" + Vless(good, "b") + "\nvless://x@y", errors);
        Check("several links: valid imported, invalid reported by number, plain text only counted", many.Count == 2
            && errors.Count == 1 && errors.NotLinks == 1 && errors.Details[0].StartsWith("Link 4:"));
    }

    // ---------------------------------------------------------------- storage

    private const string V1File =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<NekoRayNT5 formatVersion=\"1\">\r\n  <profiles nextId=\"5\">\r\n" +
        "    <profile id=\"1\" type=\"vless\"><name>XP-Test</name><address>test.example</address><port>443</port>" +
        "<uuid>" + Uuid + "</uuid><encryption>none</encryption><network>tcp</network><security>reality</security>" +
        "<flow>xtls-rprx-vision</flow><sni>www.example.com</sni><fingerprint>chrome</fingerprint>" +
        "<publicKey>" + Pbk + "</publicKey><shortId>0123abcd</shortId><spiderX></spiderX></profile>\r\n" +
        "    <profile id=\"4\" type=\"vless\"><name>Second</name><address>2001:db8::1</address><port>8443</port>" +
        "<uuid>" + Uuid + "</uuid><publicKey>" + Pbk + "</publicKey></profile>\r\n" +
        "  </profiles>\r\n</NekoRayNT5>\r\n";

    private static string Names(ProfileStore s, int group)
    {
        StringBuilder sb = new StringBuilder();
        foreach (ProfileEntry p in s.ProfilesOf(group)) { sb.Append(p.Name).Append('|'); }
        return sb.ToString();
    }

    private static List<int> Ids(params int[] ids)
    {
        return new List<int>(ids);
    }

    private static void TestStore()
    {
        Section("Profile storage");
        string dir = Path.Combine(_work, "store");
        string file = Path.Combine(Path.Combine(dir, "sub"), "profiles.xml");

        ProfileStore s1 = new ProfileStore(file);
        Check("missing file loads empty with the default group", s1.Load() == null && s1.Profiles.Count == 0 && s1.Groups.Count == 1);
        List<ProfileEntry> samples = SampleProfiles();
        s1.Add(samples, ProfileGroup.DefaultId);
        ProfileGroup g = s1.AddGroup("Group \u0411", "http://sub.example/list");
        Check("file created, ids assigned", File.Exists(file) && samples[0].Id == 1 && samples[13].Id == 14 && g.Id == 1);

        ProfileStore s2 = new ProfileStore(file);
        Check("reload without warning", s2.Load() == null && s2.Profiles.Count == 14 && s2.Groups.Count == 2);
        bool same = true;
        for (int i = 0; i < samples.Count; i++)
        {
            same = same && Same(samples[i], s2.Profiles[i]) && s2.Profiles[i].Id == i + 1;
        }
        Check("round trip of every field and of the order", same);
        Check("group name and URL kept", s2.FindGroup(1).Name == "Group \u0411" && s2.FindGroup(1).Url == "http://sub.example/list");
        Check("format version 2 in file", File.ReadAllText(file).IndexOf("formatVersion=\"2\"") > 0);

        ProfileEntry edit = s2.Find(1).Clone();
        edit.Name = "Renamed <&> \"x\"";
        edit.Port = 2053;
        s2.Update(edit);
        s2.Remove(Ids(2, 3));
        s2.MoveToGroup(Ids(4, 5), 1);
        s2.Move(Ids(5), -1);
        ProfileStore s3 = new ProfileStore(file);
        s3.Load();
        Check("edit and delete persisted", s3.Profiles.Count == 12 && s3.Find(1).Name == "Renamed <&> \"x\"" && s3.Find(1).Port == 2053 && s3.Find(2) == null);
        Check("move to group and reorder persisted", Names(s3, 1) == "vless plain|vless h2 tls|");
        Check("previous version kept as .bak", File.Exists(file + ".bak"));

        s3.Move(Ids(1), 1);
        Check("move down inside a group skips other groups", s3.ProfilesOf(0)[0].Id == 6 && s3.ProfilesOf(0)[1].Id == 1);
        s3.Sort(0, delegate(ProfileEntry a, ProfileEntry b) { return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); });
        Check("sort by name inside a group", s3.ProfilesOf(0)[0].Name == "http" && Names(s3, 1) == "vless plain|vless h2 tls|");

        List<ProfileEntry> one = new List<ProfileEntry>();
        one.Add(Reality());
        s3.Add(one, 0);
        Check("ids of deleted profiles are not reused", one[0].Id == 15);

        List<ProfileEntry> sub = new List<ProfileEntry>();
        sub.Add(Reality());
        ProfileEntry second = Reality();
        second.Port = 8443;
        sub.Add(second);
        s3.MergeSubscription(1, sub, "2026-01-01 00:00");
        Check("subscription update changes only its group", s3.ProfilesOf(1).Count == 4 && s3.ProfilesOf(0).Count == 11 && s3.FindGroup(1).LastUpdate.Length > 0);
        s3.RemoveGroup(1);
        bool refusedDefault = false;
        try { s3.RemoveGroup(0); } catch (InvalidOperationException) { refusedDefault = true; }
        Check("group delete removes its profiles; default group is protected", s3.Groups.Count == 1 && s3.Profiles.Count == 11 && refusedDefault);

        // migration from format 1
        string mdir = Path.Combine(_work, "migrate");
        Directory.CreateDirectory(mdir);
        string mfile = Path.Combine(mdir, "profiles.xml");
        File.WriteAllText(mfile, V1File);
        ProfileStore m = new ProfileStore(mfile);
        string mw = m.Load();
        ProfileEntry m1 = m.Find(1);
        Check("format 1 loads without warning", mw == null && m.Profiles.Count == 2);
        Check("format 1 values and defaults", m1 != null && m1.Name == "XP-Test" && m1.Security == "reality" && m1.Flow == "xtls-rprx-vision"
            && m.Find(4).Fingerprint == "chrome" && m.Find(4).Security == "reality" && m.Find(4).Encryption == "none");
        Check("loading alone does not rewrite the old file", File.ReadAllText(mfile) == V1File);
        m.Add(one, 0);
        Check("first save keeps the format 1 file as a backup", File.Exists(mfile + ".v1-backup") && File.ReadAllText(mfile + ".v1-backup") == V1File);
        ProfileStore m2 = new ProfileStore(mfile);
        Check("migrated file reloads as format 2 with the same data", m2.Load() == null && m2.Profiles.Count == 3 && Same(m1, m2.Find(1)) && one[0].Id == 5);

        // damaged XML
        string intact = File.ReadAllText(file);
        File.WriteAllText(file, intact.Substring(0, intact.Length / 2));
        string damagedBytes = File.ReadAllText(file);
        ProfileStore s4 = new ProfileStore(file);
        string warn = s4.Load();
        string[] copies = Directory.GetFiles(Path.GetDirectoryName(file), "profiles.xml.damaged-*");
        Check("damaged: warning, no crash, the previous version (.bak) is loaded", warn != null && s4.Profiles.Count > 0
            && warn.IndexOf("previous version") >= 0);
        Check("damaged: original copied aside intact", copies.Length == 1 && File.ReadAllText(copies[0]) == damagedBytes);
        Check("damaged: warning has no private data", warn.IndexOf(Uuid) < 0 && warn.IndexOf(Pbk) < 0 && warn.IndexOf(Secret) < 0);
        Check("damaged: saving allowed after safety copy", s4.ReadOnlyReason == null);

        File.WriteAllText(file, "");
        Check("empty file: warning", new ProfileStore(file).Load() != null);
        File.WriteAllText(file, "<other/>");
        Check("foreign xml: warning", new ProfileStore(file).Load() != null);

        File.WriteAllText(file, intact.Replace("<port>2053</port>", "<port>oops</port>"));
        ProfileStore s5 = new ProfileStore(file);
        string w5 = s5.Load();
        Check("bad record: skipped, others kept, warning", w5 != null && s5.Profiles.Count == 10 && s5.Find(1) == null);

        string newer = intact.Replace("formatVersion=\"2\"", "formatVersion=\"3\"");
        File.WriteAllText(file, newer);
        ProfileStore s6 = new ProfileStore(file);
        string w6 = s6.Load();
        bool refused = false;
        try { s6.Add(one, 0); } catch (IOException) { refused = true; }
        Check("newer format: warning and read-only", w6 != null && s6.ReadOnlyReason != null && s6.Profiles.Count == 0);
        Check("newer format: save refused, file untouched, memory rolled back", refused && File.ReadAllText(file) == newer && s6.Profiles.Count == 0);
    }

    private static void TestSettings()
    {
        Section("Settings");
        string file = Path.Combine(Path.Combine(_work, "settings"), "settings.xml");
        SettingsStore store = new SettingsStore(file);
        string warning;
        AppSettings s = store.Load(out warning);
        Check("defaults when the file is missing", warning == null && s.SocksPort == 2080 && s.HttpPort == 2081 && s.BypassPrivate);
        s.SocksPort = 11080; s.HttpPort = 11081; s.AllowLan = true; s.LogLevel = "debug"; s.DefaultOutbound = "direct";
        s.DirectDomains = "domain:example.com\r\ngeosite:cn"; s.BlockIps = "203.0.113.0/24"; s.RemoteDns = "8.8.8.8";
        s.RememberLast = true; s.LastProfileId = 7; s.CloseToTray = false; s.CurrentGroupId = 3;
        store.Save(s);
        AppSettings r = store.Load(out warning);
        Check("round trip", warning == null && r.SocksPort == 11080 && r.HttpPort == 11081 && r.AllowLan && r.LogLevel == "debug"
            && r.DefaultOutbound == "direct" && r.DirectDomains == s.DirectDomains && r.BlockIps == s.BlockIps && r.RemoteDns == "8.8.8.8"
            && r.RememberLast && r.LastProfileId == 7 && !r.CloseToTray && r.CurrentGroupId == 3);
        File.WriteAllText(file, File.ReadAllText(file).Replace("<socksPort>11080</socksPort>", "<socksPort>99999</socksPort>")
            .Replace("<logLevel>debug</logLevel>", "<logLevel>loud</logLevel>"));
        r = store.Load(out warning);
        Check("invalid values fall back to defaults one by one", r.SocksPort == 2080 && r.LogLevel == "warning" && r.HttpPort == 11081);
        File.WriteAllText(file, "<broken");
        r = store.Load(out warning);
        Check("damaged file: defaults, warning, safety copy", warning != null && r.SocksPort == 2080
            && Directory.GetFiles(Path.GetDirectoryName(file), "settings.xml.damaged-*").Length == 1);
    }

    private static void TestSubscriptionDecode()
    {
        Section("Subscription content");
        string list = Vless("security=reality&pbk=" + Pbk, "one") + "\n" + "trojan://" + Secret + "@test.example:443#two\nbroken://x\n";
        LinkProblems errors = new LinkProblems();
        Check("plain list", Subscription.Decode(list, errors).Count == 2 && errors.Count == 1);
        errors.Clear();
        string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(list));
        Check("Base64 list with line breaks", Subscription.Decode(b64.Substring(0, 40) + "\r\n" + b64.Substring(40), errors).Count == 2);
        bool threw = false;
        try { Subscription.Decode("<html>not a subscription</html>", errors); } catch (FormatException) { threw = true; }
        Check("unrelated content is rejected", threw);
        threw = false;
        try { Subscription.Download("ftp://example.com/x", 0, new DownloadTools()); } catch (IOException) { threw = true; }
        Check("non-http address is rejected before any connection", threw);
    }

    private static void TestSecretMask()
    {
        Section("Secret masking");
        ProfileEntry p = Reality();
        p.Password = Secret;
        SecretMask mask = new SecretMask(p);
        string masked = mask.Apply("id=" + Uuid.ToUpperInvariant() + " key=" + Pbk + " pass=" + Secret + " sid=0123abcd ok");
        Check("uuid, key, password and short id are masked", masked == "id=*** key=*** pass=*** sid=*** ok");
    }

    // ---------------------------------------------------------------- system proxy (fake backend only)

    private sealed class FakeBackend : IProxyBackend
    {
        public readonly Dictionary<string, ProxyConnectionSettings> Items = new Dictionary<string, ProxyConnectionSettings>();
        public string FailApplyOn;
        public int Notified;

        public FakeBackend()
        {
            Set("", 9, "", "", "");
            Set("Dial-up \u0411", 3, "old.example:3128", "<local>", "");
            Set("VPN", 5, "", "", "http://pac.example/proxy.pac");
        }

        public void Set(string name, int flags, string server, string bypass, string pac)
        {
            ProxyConnectionSettings s = new ProxyConnectionSettings();
            s.Connection = name.Length == 0 ? null : name;
            s.Flags = flags; s.Server = server; s.Bypass = bypass; s.PacUrl = pac;
            Items[name] = s;
        }

        public string[] ListConnections()
        {
            List<string> names = new List<string>();
            foreach (string key in Items.Keys) { if (key.Length > 0) { names.Add(key); } }
            return names.ToArray();
        }

        public ProxyConnectionSettings Query(string connection)
        {
            ProxyConnectionSettings s = Items[connection == null ? "" : connection];
            ProxyConnectionSettings copy = new ProxyConnectionSettings();
            copy.Connection = s.Connection; copy.Flags = s.Flags; copy.Server = s.Server; copy.Bypass = s.Bypass; copy.PacUrl = s.PacUrl;
            return copy;
        }

        public void Apply(ProxyConnectionSettings s)
        {
            string key = s.Connection == null ? "" : s.Connection;
            if (key == FailApplyOn) { throw new IOException("synthetic failure"); }
            Set(key, s.Flags, s.Server, s.Bypass, s.PacUrl);
        }

        public void NotifyChanged() { Notified++; }

        public string Dump()
        {
            StringBuilder sb = new StringBuilder();
            foreach (string key in new string[] { "", "Dial-up \u0411", "VPN" })
            {
                ProxyConnectionSettings s = Items[key];
                sb.Append(key).Append('=').Append(s.Flags).Append(',').Append(s.Server).Append(',').Append(s.Bypass).Append(',').Append(s.PacUrl).Append(';');
            }
            return sb.ToString();
        }
    }

    private static void TestSystemProxy()
    {
        Section("System proxy (simulated backend; the real settings are only read)");
        string backup = Path.Combine(Path.Combine(_work, "proxy"), "backup.xml");

        FakeBackend b = new FakeBackend();
        string original = b.Dump();
        SystemProxyManager m = new SystemProxyManager(b, backup);
        Check("nothing to restore initially", !m.IsEnabled && m.Disable() == null);
        m.Enable("127.0.0.1:2081", "<local>");
        Check("enable: backup written, every connection points at the local proxy", m.IsEnabled
            && b.Items[""].Flags == 3 && b.Items[""].Server == "127.0.0.1:2081" && b.Items["VPN"].Server == "127.0.0.1:2081" && b.Items["VPN"].PacUrl == "");
        string note = m.Disable();
        Check("disable: exact original values restored, backup removed", b.Dump() == original && !m.IsEnabled && note != null && b.Notified >= 2);

        m.Enable("127.0.0.1:2081", "<local>");
        m.Enable("127.0.0.1:3081", "<local>");
        Check("enabling again (new port) keeps the first snapshot", b.Items[""].Server == "127.0.0.1:3081");
        SystemProxyManager afterCrash = new SystemProxyManager(b, backup);
        Check("after a crash the backup is found", afterCrash.IsEnabled);
        afterCrash.Disable();
        Check("recovery after a crash restores the original values", b.Dump() == original && !afterCrash.IsEnabled);

        m.Enable("127.0.0.1:2081", "<local>");
        b.Set("", 3, "manual.example:8080", "", "");
        m.Disable();
        Check("a connection changed by the user meanwhile is left alone, the others are restored",
            b.Items[""].Server == "manual.example:8080" && b.Items["VPN"].PacUrl == "http://pac.example/proxy.pac" && b.Items["Dial-up \u0411"].Server == "old.example:3128");

        b = new FakeBackend();
        original = b.Dump();
        m = new SystemProxyManager(b, backup);
        b.FailApplyOn = "VPN";
        bool threw = false;
        try { m.Enable("127.0.0.1:2081", "<local>"); } catch (IOException) { threw = true; }
        b.FailApplyOn = null;
        Check("failure while enabling: rolled back, nothing left enabled", threw && b.Dump() == original && !m.IsEnabled);

        m.Enable("127.0.0.1:2081", "<local>");
        b.FailApplyOn = "";
        threw = false;
        try { m.Disable(); } catch (IOException) { threw = true; }
        Check("failure while restoring: reported and the backup is kept", threw && m.IsEnabled);
        b.FailApplyOn = null;
        m.Disable();
        Check("restoring succeeds on the next attempt", b.Dump() == original && !m.IsEnabled);

        m.Enable("127.0.0.1:2081", "<local>");
        b.Items.Remove("VPN");
        b.Items["VPN"] = null;
        b.Items.Remove("VPN");
        bool ok = true;
        try { m.Disable(); } catch (Exception) { ok = false; }
        Check("a dial-up entry deleted meanwhile does not block restoring", ok && !m.IsEnabled && b.Items[""].Flags == 9);

        File.WriteAllText(backup, "<broken");
        threw = false;
        try { m.Disable(); } catch (IOException) { threw = true; }
        Check("damaged backup: clear error, file kept", threw && File.Exists(backup));
        File.Delete(backup);

        bool readOk = true;
        try
        {
            WinInetProxy real = new WinInetProxy();
            real.Query(null);
            real.ListConnections();
        }
        catch (Exception ex)
        {
            readOk = false;
            Console.WriteLine("      " + ex.Message);
        }
        Check("real WinINet settings can be read (read-only call)", readOk);
    }

    // ---------------------------------------------------------------- generated configuration, checked by xray itself

    private static bool XrayAccepts(string json, out string output)
    {
        string file = Path.Combine(_work, "cfg-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(file, json, new UTF8Encoding(false));
        int code = CoreProcess.RunOnce(_xray, "run -test -c \"" + file + "\"", 30000, out output);
        File.Delete(file);
        return code == 0;
    }

    private static void TestGeneratedConfigs()
    {
        Section("Generated configuration (xray run -test)");
        string output;
        CoreProcess.RunOnce(_xray, "version", 10000, out output);
        Console.WriteLine("      core: " + output.Split('\r')[0]);

        AppSettings settings = new AppSettings();
        settings.SocksPort = 21080;
        settings.HttpPort = 21081;
        foreach (ProfileEntry p in SampleProfiles())
        {
            bool ok = XrayAccepts(XrayConfigBuilder.Build(p, settings), out output);
            Check("accepted: " + p.Name + (ok ? "" : "\r\n" + output), ok);
            ok = XrayAccepts(XrayConfigBuilder.BuildForTest(p, 21082), out output);
            Check("accepted (latency test form): " + p.Name + (ok ? "" : "\r\n" + output), ok);
        }

        AppSettings routed = new AppSettings();
        routed.AllowLan = true;
        routed.DomainStrategy = "IPIfNonMatch";
        routed.DefaultOutbound = "direct";
        routed.DirectDomains = "domain:example.com\r\n# comment\r\nfull:direct.example.org";
        routed.ProxyDomains = "keyword-example\r\nregexp:\\.example\\.net$";
        routed.BlockDomains = "domain:ads.example.com";
        routed.DirectIps = "203.0.113.0/24";
        routed.ProxyIps = "198.51.100.7";
        routed.BlockIps = "192.0.2.0/24\r\n2001:db8::/32";
        routed.RemoteDns = "https://8.8.8.8/dns-query";
        routed.DirectDns = "localhost";
        bool accepted = XrayAccepts(XrayConfigBuilder.Build(Reality(), routed), out output);
        Check("accepted: routing rules, DNS, LAN listen, default direct" + (accepted ? "" : "\r\n" + output), accepted);

        string json = XrayConfigBuilder.Build(Reality(), settings);
        JsonObject root = (JsonObject)Json.Parse(json);
        JsonArray inbounds = (JsonArray)root.Get("inbounds");
        JsonObject outbound = (JsonObject)((JsonArray)root.Get("outbounds"))[0];
        JsonObject reality = (JsonObject)((JsonObject)outbound.Get("streamSettings")).Get("realitySettings");
        JsonObject user = (JsonObject)((JsonArray)((JsonObject)((JsonArray)((JsonObject)outbound.Get("settings")).Get("vnext"))[0]).Get("users"))[0];
        Check("separate SOCKS and HTTP inbounds on the configured ports", inbounds.Count == 2
            && ((JsonObject)inbounds[0]).GetString("protocol") == "socks" && ((JsonObject)inbounds[0]).GetString("port") == "21080"
            && ((JsonObject)inbounds[1]).GetString("protocol") == "http" && ((JsonObject)inbounds[1]).GetString("port") == "21081"
            && ((JsonObject)inbounds[0]).GetString("listen") == "127.0.0.1");
        Check("REALITY + Vision parameters are all present", user.GetString("id") == Uuid && user.GetString("flow") == "xtls-rprx-vision"
            && user.GetString("encryption") == "none" && reality.GetString("serverName") == "www.example.com"
            && reality.GetString("fingerprint") == "chrome" && reality.GetString("publicKey") == Pbk && reality.GetString("shortId") == "0123abcd"
            && reality.GetString("spiderX") == "/");

        Check("xray rejects a broken file (the check is real)", !XrayAccepts("{ \"inbounds\": [ { \"protocol\": \"nonsense\" } ] }", out output));
    }

    // ---------------------------------------------------------------- core lifecycle on a local synthetic chain

    private static readonly List<string> Captured = new List<string>();
    private static string _lastMessage = "";

    private static int _stateChanges;

    // Starts a profile and waits until the start attempt has finished in the wanted state.
    private static bool StartAndWait(CoreController c, ProfileEntry p, AppSettings s, CoreState wanted, int ms)
    {
        int seen;
        lock (Captured) { seen = _stateChanges; }
        c.Start(p, s, true);
        for (int waited = 0; waited < ms; waited += 50)
        {
            lock (Captured)
            {
                if (_stateChanges > seen && !c.IsBusy && c.State == wanted) { return true; }
            }
            Thread.Sleep(50);
        }
        return false;
    }

    private static bool WaitState(CoreController c, CoreState wanted, int ms)
    {
        for (int waited = 0; waited < ms; waited += 50)
        {
            if (c.State == wanted) { return true; }
            Thread.Sleep(50);
        }
        return c.State == wanted;
    }

    private static int HttpStatusThrough(int proxyPort, string url)
    {
        try
        {
            return LatencyTester.ProxyRequest(proxyPort, url, 10000);
        }
        catch (Exception)
        {
            return -1;
        }
    }

    // Answers every connection with "204 No Content"; counts the requests.
    private sealed class TinyHttpServer
    {
        private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
        public int Requests;
        public int Port;

        public TinyHttpServer()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Thread t = new Thread(Loop);
            t.IsBackground = true;
            t.Start();
        }

        private void Loop()
        {
            while (true)
            {
                try
                {
                    using (TcpClient client = _listener.AcceptTcpClient())
                    {
                        NetworkStream stream = client.GetStream();
                        byte[] buffer = new byte[4096];
                        stream.ReadTimeout = 3000;
                        int read = stream.Read(buffer, 0, buffer.Length);
                        if (read > 0)
                        {
                            Interlocked.Increment(ref Requests);
                            byte[] reply = Encoding.ASCII.GetBytes("HTTP/1.1 204 No Content\r\nConnection: close\r\nContent-Length: 0\r\n\r\n");
                            stream.Write(reply, 0, reply.Length);
                        }
                    }
                }
                catch (Exception)
                {
                    if (!_listener.Server.IsBound) { return; }
                }
            }
        }
    }

    private static string UpstreamConfig(int port)
    {
        return Json.Serialize(new JsonObject()
            .Add("log", new JsonObject().Add("loglevel", "warning"))
            .Add("inbounds", JsonArray.Of(new JsonObject().Add("listen", "127.0.0.1").Add("port", port).Add("protocol", "socks")
                .Add("settings", new JsonObject().Add("auth", "password")
                    .Add("accounts", JsonArray.Of(new JsonObject().Add("user", "synthetic-user").Add("pass", Secret))))))
            .Add("outbounds", JsonArray.Of(new JsonObject().Add("protocol", "freedom"))));
    }

    private static ProfileEntry LocalSocks(int id, int port)
    {
        ProfileEntry p = ProfileEntry.Create(ProfileEntry.TypeSocks);
        p.Id = id;
        p.Name = "local chain " + id;
        p.Address = "127.0.0.1";
        p.Port = port;
        p.Username = "synthetic-user";
        p.Password = Secret;
        return p;
    }

    private static void TestCoreLifecycle()
    {
        Section("Core start / stop / switch / crash (local synthetic chain, no remote servers)");
        string runDir = Path.Combine(_work, "run");
        TinyHttpServer web = new TinyHttpServer();
        string target = "http://127.0.0.1:" + web.Port + "/";

        int upstreamPort = CoreLauncher.FindFreePort();
        ProfileEntry mock = LocalSocks(0, upstreamPort);
        CoreProcess upstream = CoreLauncher.Launch(_xray, runDir, UpstreamConfig(upstreamPort), new SecretMask(mock),
            new int[] { upstreamPort }, 15000, null, null);
        Check("temporary configuration is deleted right after start-up", Directory.GetFiles(runDir, "xray-*.json").Length == 0);

        AppSettings settings = new AppSettings();
        settings.SocksPort = CoreLauncher.FindFreePort();
        settings.HttpPort = CoreLauncher.FindFreePort();
        settings.BypassPrivate = false;
        settings.LogLevel = "debug";

        CoreController core = new CoreController(_xray, runDir);
        core.Log += delegate(string line) { lock (Captured) { Captured.Add(line); } };
        core.StateChanged += delegate(CoreState state, string message)
        {
            lock (Captured) { Captured.Add("[state] " + state + " " + message); _lastMessage = message; _stateChanges++; }
        };

        try
        {
            Stopwatch watch = Stopwatch.StartNew();
            core.Start(LocalSocks(1, upstreamPort), settings, false);
            Check("Start returns immediately (no blocking of the caller)", watch.ElapsedMilliseconds < 500);
            Check("state reaches Running", WaitState(core, CoreState.Running, 20000) && core.ActiveProfileId == 1);
            Check("pid file exists while running", File.Exists(Path.Combine(runDir, "core.pid")));
            int before = web.Requests;
            Check("HTTP request through the HTTP inbound and the profile succeeds", HttpStatusThrough(settings.HttpPort, target) == 204 && web.Requests == before + 1);
            Check("SOCKS inbound is listening", CoreLauncher.IsPortOpen(settings.SocksPort));

            string note;
            int ms = LatencyTester.UrlTest(_xray, runDir, LocalSocks(1, upstreamPort), target, 10000, out note);
            Check("URL test measures a real request (" + ms + " ms)", ms > 0 && note == "");
            ProfileEntry wrong = LocalSocks(9, upstreamPort);
            wrong.Password = "synthetic-wrong-password";
            ms = LatencyTester.UrlTest(_xray, runDir, wrong, target, 6000, out note);
            Check("URL test fails honestly when the profile cannot connect (" + note + ")", ms == -1 && note.Length > 0);
            ms = LatencyTester.TcpPing(LocalSocks(1, upstreamPort), 3000, out note);
            Check("TCP ping to a listening port", ms > 0);
            ms = LatencyTester.TcpPing(LocalSocks(1, CoreLauncher.FindFreePort()), 3000, out note);
            Check("TCP ping to a closed port fails", ms == -1);

            int firstPid = ReadPid(runDir);
            Check("switching profile ends in Running with the new profile", StartAndWait(core, LocalSocks(2, upstreamPort), settings, CoreState.Running, 20000) && core.ActiveProfileId == 2);
            Check("the previous core process is gone", !ProcessAlive(firstPid));
            Check("traffic flows after the switch", HttpStatusThrough(settings.HttpPort, target) == 204);

            core.Stop();
            Check("Stop ends in Not Running", WaitState(core, CoreState.NotRunning, 10000) && core.ActiveProfileId == -1);
            Check("ports are closed, no pid file, no configuration files", !CoreLauncher.IsPortOpen(settings.SocksPort) && !CoreLauncher.IsPortOpen(settings.HttpPort)
                && !File.Exists(Path.Combine(runDir, "core.pid")) && Directory.GetFiles(runDir, "xray-*.json").Length == 0);

            TcpListener blocker = new TcpListener(IPAddress.Loopback, settings.SocksPort);
            blocker.Start();
            bool error = StartAndWait(core, LocalSocks(1, upstreamPort), settings, CoreState.Error, 10000);
            blocker.Stop();
            Check("port conflict: Error state with an explanation", error && _lastMessage.IndexOf("port " + settings.SocksPort) >= 0 && !CoreLauncher.IsPortOpen(settings.HttpPort));
            blocker = new TcpListener(IPAddress.Loopback, settings.HttpPort);
            blocker.Start();
            error = StartAndWait(core, LocalSocks(1, upstreamPort), settings, CoreState.Error, 10000);
            blocker.Stop();
            Check("HTTP port conflict: Error state with an explanation", error && _lastMessage.IndexOf("HTTP port " + settings.HttpPort) >= 0
                && !CoreLauncher.IsPortOpen(settings.SocksPort));

            AppSettings broken = settings.Clone();
            broken.DirectDomains = "geosite:this-list-does-not-exist";
            Check("configuration refused by xray: Error state, nothing left running", StartAndWait(core, LocalSocks(1, upstreamPort), broken, CoreState.Error, 30000)
                && _lastMessage.IndexOf("rejected") >= 0 && !CoreLauncher.IsPortOpen(settings.SocksPort));

            Check("start works again after errors", StartAndWait(core, LocalSocks(1, upstreamPort), settings, CoreState.Running, 20000));
            int pid = ReadPid(runDir);
            Process.GetProcessById(pid).Kill();
            Check("killed core is noticed: Error state", WaitState(core, CoreState.Error, 10000) && _lastMessage.IndexOf("unexpectedly") >= 0);
            Check("no pid file after the crash", !File.Exists(Path.Combine(runDir, "core.pid")));

            Check("reconnect after a crash", StartAndWait(core, LocalSocks(1, upstreamPort), settings, CoreState.Running, 20000) && HttpStatusThrough(settings.HttpPort, target) == 204);

            // a core left behind by a "crashed" shell is cleaned up by the next start
            pid = ReadPid(runDir);
            File.WriteAllText(Path.Combine(runDir, "xray-stale.json"), "{}");
            CoreLauncher.CleanupStale(_xray, runDir);
            Check("stale core and stale files are removed at start-up", !ProcessAlive(pid) && Directory.GetFiles(runDir, "xray-*.json").Length == 0
                && !File.Exists(Path.Combine(runDir, "core.pid")));
            WaitState(core, CoreState.Error, 10000);

            StartAndWait(core, LocalSocks(1, upstreamPort), settings, CoreState.Running, 20000);
            pid = ReadPid(runDir);
            watch = Stopwatch.StartNew();
            core.Shutdown(5000);
            Check("Shutdown stops the core synchronously", !ProcessAlive(pid) && watch.ElapsedMilliseconds < 6000);

            bool leak = false;
            lock (Captured)
            {
                foreach (string line in Captured)
                {
                    if (line.IndexOf(Secret) >= 0 || line.IndexOf("synthetic-user") >= 0) { leak = true; Console.WriteLine("      LEAK: " + line); }
                }
                Console.WriteLine("      captured log lines: " + Captured.Count);
            }
            Check("no credentials in the log and state messages (debug log level)", !leak && Captured.Count > 10);
        }
        finally
        {
            core.Shutdown(3000);
            upstream.Stop();
            upstream.WaitForExit(3000);
        }
    }

    private static int ReadPid(string runDir)
    {
        return int.Parse(File.ReadAllText(Path.Combine(runDir, "core.pid")).Trim().Split(' ')[0]);
    }

    private static bool ProcessAlive(int pid)
    {
        for (int i = 0; i < 40; i++)
        {
            try
            {
                using (Process p = Process.GetProcessById(pid))
                {
                    if (p.HasExited) { return false; }
                }
            }
            catch (ArgumentException)
            {
                return false;
            }
            Thread.Sleep(50);
        }
        return true;
    }
}
