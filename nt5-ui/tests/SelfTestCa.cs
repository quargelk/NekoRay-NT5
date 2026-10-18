// Checks of version 0.9.1: the CA bundle (cacert.pem) for HTTPS subscriptions and removal of addresses
// from error messages.
//
// HTTPS end to end: a local TLS server (Xray with a self-signed certificate made by "xray tls cert",
// falling back to the scripted plain HTTP server) is downloaded through the real nt5helper.exe with
// the real Subscription code. Only 127.0.0.1/localhost is contacted; the certificate exists for one hour
// in the temporary folder of the test.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using NekoRayNT5.Core;
using NekoRayNT5.Model;
using NekoRayNT5.Platform;
using NekoRayNT5.Services;

internal static partial class SelfTest
{
    private static void TestCaBundle()
    {
        Section("CA bundle (cacert.pem) and address removal");
        string dir = Path.Combine(_work, "ca");
        Directory.CreateDirectory(dir);

        CaBundleInfo missing = CaBundle.Inspect(Path.Combine(dir, "cacert.pem"));
        Check("missing bundle reported as missing", !missing.Exists && missing.Problem != null && missing.ToString() == "missing");

        string shipped = ShippedBundle();
        if (shipped != null)
        {
            CaBundleInfo info = CaBundle.Inspect(shipped);
            Check("shipped cacert.pem is a complete Mozilla bundle (" + info + ")", info.Problem == null
                && info.Certificates >= 100 && info.MozillaDate.Length > 0);
            int age = CaBundle.AgeDays(info, DateTime.UtcNow);
            Check("shipped cacert.pem is current (Mozilla data " + age + " days old, limit 365)", age >= 0 && age <= 365);
            string text = File.ReadAllText(shipped);

            string truncated = Path.Combine(dir, "truncated.pem");
            File.WriteAllText(truncated, text.Substring(0, text.Length / 2));
            CaBundleInfo t = CaBundle.Inspect(truncated);
            Check("truncated bundle is reported as damaged", t.Problem != null && t.Problem.IndexOf("damaged") >= 0);

            string garbage = Path.Combine(dir, "garbage.pem");
            int b = text.IndexOf("-----BEGIN CERTIFICATE-----") + 40;
            File.WriteAllText(garbage, text.Substring(0, b) + "!!!!" + text.Substring(b + 4));
            CaBundleInfo g = CaBundle.Inspect(garbage);
            Check("bundle with a broken certificate is reported as damaged", g.Problem != null && g.Problem.IndexOf("damaged") >= 0);
        }
        else
        {
            Console.WriteLine("      (cacert.pem not found next to xray.exe or nt5helper.exe: shipped bundle checks skipped)");
        }
        string empty = Path.Combine(dir, "empty.pem");
        File.WriteAllText(empty, "## nothing\r\n");
        CaBundleInfo e = CaBundle.Inspect(empty);
        Check("bundle without certificates refused", e.Problem != null && e.Problem.IndexOf("no certificates") >= 0);

        string redacted = Subscription.RedactUrls("fetch: failed for https://sub.example/x?token=" + Token + " and HTTP://a/b?" + Token + ".");
        Check("addresses removed from downloader output", !Contains(Token, redacted) && redacted.IndexOf("<url>") >= 0
            && redacted.StartsWith("fetch: failed for "));

    }

    private static string HelperForTests()
    {
        string env = Environment.GetEnvironmentVariable("NT5HELPER_EXE");
        if (env != null && File.Exists(env))
        {
            return Path.GetFullPath(env);
        }
        string near = _xray.Length > 0 ? Path.Combine(Path.GetDirectoryName(_xray), "nt5helper.exe") : "";
        return near.Length > 0 && File.Exists(near) ? near : null;
    }

    private static string ShippedBundle()
    {
        string helper = HelperForTests();
        string[] candidates = new string[] {
            helper == null ? "" : Path.Combine(Path.GetDirectoryName(helper), CaBundle.FileName),
            _xray.Length > 0 ? Path.Combine(Path.GetDirectoryName(_xray), CaBundle.FileName) : "" };
        foreach (string c in candidates)
        {
            if (c.Length > 0 && File.Exists(c))
            {
                return c;
            }
        }
        return null;
    }

    private static void TestHttpsThroughHelper()
    {
        Section("HTTPS subscription through nt5helper (local TLS server)");
        string helper = HelperForTests();
        if (helper == null || !File.Exists(_xray))
        {
            _skipped++;
            Console.WriteLine("SKIPPED  nt5helper.exe or xray.exe not found");
            return;
        }
        string dir = Path.Combine(_work, "https");
        Directory.CreateDirectory(dir);
        string list = Vless("security=reality&pbk=" + Pbk + "&sni=www.example.com&fp=chrome", "tls-one") + "\n";
        ScriptedHttpServer plain = new ScriptedHttpServer(delegate(string path)
        {
            return Reply(200, "text/plain", Encoding.UTF8.GetBytes(list), "profile-title: base64:VExTIHRlc3Q=\r\n"
                + "subscription-userinfo: upload=0; download=1073741824; total=10737418240; expire=0\r\nSet-Cookie: s=1\r\n");
        });
        Process tlsServer = null;
        try
        {
            // a self-signed certificate for "localhost", valid for one hour
            string output;
            int code = CoreProcess.RunOnce(_xray, "tls cert --domain=localhost --expire=1h", 20000, out output);
            JsonObject cert = code == 0 ? Json.Parse(output.Substring(output.IndexOf('{'))) as JsonObject : null;
            if (cert == null)
            {
                Check("xray tls cert made a test certificate", false);
                return;
            }
            string certFile = Path.Combine(dir, "test-cert.pem");
            string keyFile = Path.Combine(dir, "test-key.pem");
            File.WriteAllText(certFile, JoinLines((JsonArray)cert.Get("certificate")));
            File.WriteAllText(keyFile, JoinLines((JsonArray)cert.Get("key")));

            int port = CoreLauncher.FindFreePort();
            string config = "{\"log\":{\"loglevel\":\"none\"},\"inbounds\":[{\"listen\":\"127.0.0.1\",\"port\":" + port
                + ",\"protocol\":\"vless\",\"settings\":{\"clients\":[{\"id\":\"" + Uuid + "\"}],\"decryption\":\"none\","
                + "\"fallbacks\":[{\"dest\":" + plain.Port + "}]},\"streamSettings\":{\"network\":\"tcp\",\"security\":\"tls\","
                + "\"tlsSettings\":{\"alpn\":[\"http/1.1\"],\"certificates\":[{\"certificateFile\":" + Json.Serialize(certFile)
                + ",\"keyFile\":" + Json.Serialize(keyFile) + "}]}}}],\"outbounds\":[{\"protocol\":\"freedom\"}]}";
            string configFile = Path.Combine(dir, "tls-server.json");
            File.WriteAllText(configFile, config);
            ProcessStartInfo psi = new ProcessStartInfo(_xray, "run -c \"" + configFile + "\"");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            tlsServer = Process.Start(psi);
            bool up = false;
            for (int i = 0; i < 100 && !up; i++)
            {
                up = TcpTable.IsListening(port);
                if (!up) { Thread.Sleep(100); }
            }
            Check("local TLS server is up", up);
            string url = "https://localhost:" + port + "/sub?" + Token;

            DownloadTools tools = new DownloadTools();
            tools.HelperPath = helper;
            tools.TempDir = Path.Combine(dir, "tmp");

            tools.CaPath = certFile;
            string content = null;
            string error = null;
            try { content = Subscription.Download(url, 0, tools); }
            catch (IOException ex) { error = ex.Message; }
            Check("HTTPS download with the trusted CA succeeds" + (error != null ? " (" + error + ")" : ""),
                content != null && content.IndexOf("tls-one") >= 0);
            Check("no extra headers unless asked (no HWID)", plain.LastHead.ToLowerInvariant().IndexOf("x-hwid") < 0);

            // Happ-compatible HWID through the helper: sent as headers (from a file, not the command line),
            // and the subscription headers come back; nothing else (cookies) is kept
            SubscriptionClient client = new SubscriptionClient();
            client.Tools = tools;
            client.Route = AppSettings.RouteDirect;
            client.Hwid = "synthetic-hwid-0123456789";
            FetchResult fetched = null;
            try { fetched = client.Fetch(url, true); }
            catch (IOException ex) { error = ex.Message; }
            string head = plain.LastHead.ToLowerInvariant();
            Check("HWID headers sent through nt5helper", fetched != null && head.IndexOf("x-hwid: synthetic-hwid-0123456789") >= 0
                && head.IndexOf("x-device-os: windows") >= 0 && head.IndexOf("user-agent: nekoraynt5") >= 0);
            Check("subscription headers returned: title and traffic, no cookie", fetched != null
                && AddSource.TitleOf(fetched.Meta, url) == "TLS test" && fetched.Header("subscription-userinfo").IndexOf("total=10737418240") >= 0
                && !fetched.Meta.ContainsKey("set-cookie"));
            Check("temporary header files removed", Directory.GetFiles(tools.TempDir).Length == 0);

            // routes with a dead local proxy (as when the active connection is broken)
            client.Route = AppSettings.RouteAuto;
            client.ProxyPort = CoreLauncher.FindFreePort();
            int routeBefore = plain.Requests;
            fetched = null;
            try { fetched = client.Fetch(url, false); }
            catch (IOException ex) { error = ex.Message; }
            Check("auto: the connection failed before the server answered, so once more directly",
                fetched != null && fetched.Via.StartsWith("directly (through the active connection it failed") && plain.Requests == routeBefore + 1
                && fetched.Via.IndexOf(Token) < 0);
            client.Route = AppSettings.RouteProxy;
            error = null;
            try { client.Fetch(url, false); }
            catch (IOException ex) { error = ex.Message; }
            Check("only through the connection: refused, no direct attempt", error != null && plain.Requests == routeBefore + 1
                && error.IndexOf(Token) < 0);

            // curl.exe as the fallback downloader (the one built into Windows 10/11, when present): the
            // address goes in a --config file. It trusts only the Windows store, so the self-signed test
            // certificate must be refused - which also proves that curl got the address from the file.
            string systemCurl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "curl.exe");
            if (File.Exists(systemCurl))
            {
                DownloadTools curlOnly = new DownloadTools();
                curlOnly.CurlPath = systemCurl;
                curlOnly.TempDir = Path.Combine(dir, "tmp-curl");
                error = null;
                try { Subscription.Download(url, 0, curlOnly); }
                catch (IOException ex) { error = ex.Message; }
                Console.WriteLine("      curl: " + error);
                Check("curl fallback: certificate checked (self-signed refused), address not in the message, no files left",
                    error != null && error.IndexOf("curl could not download") >= 0 && error.IndexOf(Token) < 0
                    && error.IndexOf("no URL specified", StringComparison.OrdinalIgnoreCase) < 0
                    && Directory.GetFiles(curlOnly.TempDir).Length == 0);
            }
            else
            {
                Console.WriteLine("      (no curl.exe in the system folder: curl fallback not checked)");
            }

            string shipped = ShippedBundle();
            if (shipped != null)
            {
                tools.CaPath = shipped;
                error = DownloadError(url, tools);
                Check("certificate not issued by a CA of cacert.pem: refused, explained, no token", error != null
                    && error.IndexOf("unknown certificate authority") >= 0 && error.IndexOf("cacert.pem") >= 0
                    && !Contains(Token, error));

                string damaged = Path.Combine(dir, "cacert.pem");
                string text = File.ReadAllText(shipped);
                File.WriteAllText(damaged, text.Substring(0, text.Length - 300));
                tools.CaPath = damaged;
                int before = plain.Requests;
                error = DownloadError(url, tools);
                Check("damaged cacert.pem: refused before any connection", error != null && error.IndexOf("damaged") >= 0
                    && plain.Requests == before);
            }

            // the situation of 0.9.0 on XP: no cacert.pem anywhere, only the Windows store (which does not
            // trust this test certificate either)
            string bare = Path.Combine(dir, "bare");
            Directory.CreateDirectory(bare);
            string bareHelper = Path.Combine(bare, "nt5helper.exe");
            File.Copy(helper, bareHelper);
            tools.HelperPath = bareHelper;
            tools.CaPath = Path.Combine(bare, "cacert.pem");
            error = DownloadError(url, tools);
            Check("without cacert.pem: the store refuses, the message says cacert.pem is missing", error != null
                && error.IndexOf("Windows certificate store") >= 0 && error.IndexOf("no cacert.pem") >= 0 && !Contains(Token, error));
        }
        finally
        {
            if (tlsServer != null)
            {
                try { tlsServer.Kill(); tlsServer.WaitForExit(5000); } catch (Exception) { }
                tlsServer.Close();
            }
            plain.Stop();
        }
    }

    private static string JoinLines(JsonArray lines)
    {
        StringBuilder sb = new StringBuilder();
        foreach (object line in lines)
        {
            sb.Append((string)line).Append('\n');
        }
        return sb.ToString();
    }
}
