using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using NekoRayNT5.Core;
using NekoRayNT5.Model;
using NekoRayNT5.App;

namespace NekoRayNT5.Services
{
    // Programs that can download over HTTPS with a current TLS version; any of them may be missing.
    internal sealed class DownloadTools
    {
        // nt5helper.exe: Go TLS 1.2/1.3 with certificate verification (the Windows store, or cacert.pem).
        public string HelperPath = "";
        // The Mozilla CA bundle given to nt5helper.exe; "" or a missing file: the Windows store.
        public string CaPath = "";
        public string CurlPath = "";
        public string TempDir = "";
        public string UserAgent = "NekoRayNT5";
    }

    // The answer of a download: the text and the response headers that describe a subscription
    // (names in lower case: profile-title, subscription-userinfo, x-hwid-..., content-type, status).
    internal sealed class FetchResult
    {
        public string Text = "";
        public Dictionary<string, string> Meta = new Dictionary<string, string>();
        // How the download went, for the log ("directly", "through the active connection"...).
        public string Via = "";

        public string Header(string name)
        {
            string value;
            return Meta.TryGetValue(name, out value) ? value : "";
        }
    }

    // A failed download. ServerAnswered: the server was reached and refused or sent something unusable,
    // so trying another route would not help.
    internal sealed class FetchException : IOException
    {
        private readonly bool _serverAnswered;
        private readonly Dictionary<string, string> _meta;

        public FetchException(string message, bool serverAnswered, Dictionary<string, string> meta)
            : base(message)
        {
            _serverAnswered = serverAnswered;
            _meta = meta ?? new Dictionary<string, string>();
        }

        public bool ServerAnswered { get { return _serverAnswered; } }
        public Dictionary<string, string> Meta { get { return _meta; } }
    }

    // Downloading and decoding of subscriptions (plain or Base64 lists of share links).
    //
    // .NET Framework 2.0 can only speak SSL 3.0 / TLS 1.0, which most HTTPS servers refuse.
    // Certificate checks are never disabled to get around that. HTTPS goes to nt5helper.exe first,
    // then to curl.exe, and only then to the built-in client. Plain http:// uses the built-in client.
    // Error messages never contain the URL (it usually carries an access token), and extra headers
    // (the HWID) are passed to the external programs in a temporary file, not on the command line.
    // proxyPort > 0 sends the request through the local HTTP inbound of the running core; 0 means
    // directly, without any proxy - also not the system proxy, which may point to this program.
    internal static class Subscription
    {
        public const int MaxBytes = 5 * 1024 * 1024;
        public const int TimeoutMs = 30000;
        private const int MaxRedirects = 5;

        // Response headers kept in FetchResult.Meta (the same list as in nt5helper fetch).
        private static readonly string[] MetaHeaders = new string[] {
            "content-type", "content-disposition", "profile-title", "subscription-userinfo",
            "profile-update-interval", "profile-web-page-url", "support-url", "announce", "providerid" };

        // Null when the address can be used, otherwise the reason.
        public static string CheckUrl(string url)
        {
            Uri uri;
            if (url == null || url.Length == 0)
            {
                return Lang.T("The subscription address is empty.");
            }
            if (url.Length > 4000 || !Uri.TryCreate(url, UriKind.Absolute, out uri)
                || (uri.Scheme != "http" && uri.Scheme != "https") || uri.Host.Length == 0)
            {
                return Lang.T("The subscription address must be a valid http:// or https:// URL.");
            }
            for (int i = 0; i < url.Length; i++)
            {
                if (url[i] <= ' ' || url[i] == '"' || url[i] > '~')
                {
                    return Lang.T("The subscription address must not contain spaces, quotes or non-ASCII characters (percent-encode them).");
                }
            }
            return null;
        }

        public static string Download(string url, int proxyPort, DownloadTools tools)
        {
            return Fetch(url, proxyPort, tools, null).Text;
        }

        // Downloads and checks that the answer is text; headers: extra request headers or null.
        public static FetchResult Fetch(string url, int proxyPort, DownloadTools tools, IList<KeyValuePair<string, string>> headers)
        {
            string problem = CheckUrl(url);
            if (problem != null)
            {
                throw new FetchException(problem, true, null);
            }
            if (headers == null)
            {
                headers = new List<KeyValuePair<string, string>>();
            }
            Uri uri = new Uri(url);
            FetchResult result = new FetchResult();
            byte[] data = uri.Scheme == "https"
                ? DownloadHttps(url, proxyPort, tools, headers, result.Meta)
                : DownloadBuiltIn(uri, proxyPort, false, tools, headers, result.Meta);
            try
            {
                result.Text = CheckContent(data);
            }
            catch (IOException ex)
            {
                throw new FetchException(ex.Message, true, result.Meta);
            }
            return result;
        }

        private static byte[] DownloadHttps(string url, int proxyPort, DownloadTools tools,
            IList<KeyValuePair<string, string>> headers, Dictionary<string, string> meta)
        {
            List<string> failures = new List<string>();
            if (tools.HelperPath.Length > 0 && File.Exists(tools.HelperPath))
            {
                string incompatible = ExecutableCheck.Check(tools.HelperPath);
                if (incompatible == null)
                {
                    return DownloadWithHelper(url, proxyPort, tools, headers, meta);
                }
                failures.Add(Lang.F("nt5helper.exe cannot run here: {0}", incompatible));
            }
            if (tools.CurlPath.Length > 0 && File.Exists(tools.CurlPath))
            {
                return DownloadWithCurl(url, proxyPort, tools, headers, meta);
            }
            try
            {
                return DownloadBuiltIn(new Uri(url), proxyPort, true, tools.UserAgent, headers, meta);
            }
            catch (WebException ex)
            {
                if (ex.Response == null && (ex.Status == WebExceptionStatus.SecureChannelFailure
                    || ex.Status == WebExceptionStatus.ReceiveFailure || ex.Status == WebExceptionStatus.ConnectionClosed
                    || ex.Status == WebExceptionStatus.TrustFailure || ex.Status == WebExceptionStatus.SendFailure))
                {
                    failures.Add(Lang.F("the built-in client of .NET Framework 2.0 failed ({0}); it supports only TLS 1.0, "
                                 + "which most servers no longer accept", ex.Status));
                    throw new FetchException(Lang.F("The HTTPS download failed: {0}.\r\n\r\n"
                        + "What you can do:\r\n"
                        + " - put nt5helper.exe (or curl.exe with its CA bundle) next to NekoRayNT5.exe and try again;\r\n"
                        + " - or open the address in a browser, save the page as a text file and add that file.",
                        string.Join("; ", failures.ToArray())),
                        false, meta);
                }
                throw Describe(ex, meta);
            }
        }

        // ---- built-in client ----

        private static byte[] DownloadBuiltIn(Uri uri, int proxyPort, bool httpsAllowed, string userAgent,
            IList<KeyValuePair<string, string>> headers, Dictionary<string, string> meta)
        {
            Uri current = uri;
            for (int redirect = 0; ; redirect++)
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(current);
                request.Proxy = proxyPort > 0 ? new WebProxy("127.0.0.1", proxyPort) : new WebProxy();
                request.Timeout = TimeoutMs;
                request.ReadWriteTimeout = TimeoutMs;
                request.UserAgent = userAgent;
                request.AllowAutoRedirect = false;
                request.KeepAlive = false;
                request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                for (int i = 0; i < headers.Count; i++)
                {
                    request.Headers[headers[i].Key] = headers[i].Value;
                }
                HttpWebResponse response;
                try
                {
                    response = (HttpWebResponse)request.GetResponse();
                }
                catch (WebException ex)
                {
                    if (ex.Response == null)
                    {
                        throw;
                    }
                    CopyMeta(ex.Response as HttpWebResponse, meta);
                    throw Describe(ex, meta);
                }
                using (response)
                {
                    int status = (int)response.StatusCode;
                    if (status >= 300 && status < 400)
                    {
                        string location = response.Headers["Location"];
                        Uri next;
                        if (location == null || !Uri.TryCreate(current, location, out next))
                        {
                            throw new FetchException(Lang.F("The server sent a redirect without a valid address (HTTP {0}).", status), true, meta);
                        }
                        if (current.Scheme == "https" && next.Scheme != "https")
                        {
                            throw new FetchException(Lang.T("The server redirected from HTTPS to an insecure address; the download was refused."), true, meta);
                        }
                        if (next.Scheme != "http" && next.Scheme != "https")
                        {
                            throw new FetchException(Lang.T("The server redirected to an unsupported address."), true, meta);
                        }
                        if (redirect >= MaxRedirects)
                        {
                            throw new FetchException(Lang.T("Too many redirects."), true, meta);
                        }
                        if (next.Scheme == "https" && !httpsAllowed)
                        {
                            // http -> https: hand over to the HTTPS path (current TLS through the helper)
                            throw new RedirectToHttps(next.AbsoluteUri);
                        }
                        current = next;
                        continue;
                    }
                    CopyMeta(response, meta);
                    if (status != 200)
                    {
                        throw new FetchException(Lang.F("The server answered HTTP {0}.", status), true, meta);
                    }
                    CheckContentType(response.ContentType, meta);
                    using (Stream stream = response.GetResponseStream())
                    {
                        // with decompression the declared length is that of the compressed data
                        return ReadLimited(stream, request.AutomaticDecompression != DecompressionMethods.None
                            && (response.ContentEncoding ?? "").Length > 0 ? -1 : response.ContentLength, meta);
                    }
                }
            }
        }

        private static void CopyMeta(HttpWebResponse response, Dictionary<string, string> meta)
        {
            if (response == null)
            {
                return;
            }
            meta["status"] = ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture);
            foreach (string name in response.Headers.AllKeys)
            {
                string lower = name.ToLowerInvariant();
                if (Array.IndexOf(MetaHeaders, lower) >= 0 || lower.StartsWith("x-hwid"))
                {
                    meta[lower] = CleanHeader(response.Headers[name]);
                }
            }
        }

        private static string CleanHeader(string value)
        {
            StringBuilder sb = new StringBuilder(value ?? "");
            for (int i = 0; i < sb.Length; i++)
            {
                if (sb[i] < ' ' || sb[i] == '\u007f')
                {
                    sb[i] = ' ';
                }
            }
            return sb.ToString().Trim();
        }

        // Signals that an http:// subscription moved to https://.
        private sealed class RedirectToHttps : IOException
        {
            public readonly string Url;

            public RedirectToHttps(string url)
                : base("Redirected to HTTPS.")
            {
                Url = url;
            }
        }

        // Entry point used by Fetch for plain http:// addresses; follows a move to https:// once.
        private static byte[] DownloadBuiltIn(Uri uri, int proxyPort, bool httpsAllowed, DownloadTools tools,
            IList<KeyValuePair<string, string>> headers, Dictionary<string, string> meta)
        {
            try
            {
                return DownloadBuiltIn(uri, proxyPort, httpsAllowed, tools.UserAgent, headers, meta);
            }
            catch (RedirectToHttps r)
            {
                meta.Clear();
                return DownloadHttps(r.Url, proxyPort, tools, headers, meta);
            }
            catch (WebException ex)
            {
                throw Describe(ex, meta);
            }
        }

        private static FetchException Describe(WebException ex, Dictionary<string, string> meta)
        {
            HttpWebResponse http = ex.Response as HttpWebResponse;
            if (http != null)
            {
                return new FetchException(Lang.F("The server answered HTTP {0}.", (int)http.StatusCode + HttpHint((int)http.StatusCode)), true, meta);
            }
            if (ex.Status == WebExceptionStatus.Timeout)
            {
                return new FetchException(Lang.F("The server did not answer within {0} seconds.", TimeoutMs / 1000), false, meta);
            }
            if (ex.Status == WebExceptionStatus.NameResolutionFailure)
            {
                return new FetchException(Lang.T("The server name could not be resolved (check the address and the network)."), false, meta);
            }
            if (ex.Status == WebExceptionStatus.ConnectFailure)
            {
                return new FetchException(Lang.T("Could not connect to the server."), false, meta);
            }
            return new FetchException(Lang.F("The download failed ({0}).", ex.Status), false, meta);
        }

        public static string HttpHint(int code)
        {
            return code == 401 || code == 403 ? " " + Lang.T("(access denied: the address or its token may be wrong or expired, "
                                                + "or the provider requires a device id - see \"Use Happ-compatible HWID\")")
                : code == 404 ? " " + Lang.T("(not found: check the address)")
                : code == 429 ? " " + Lang.T("(too many requests: try again later)")
                : code >= 500 ? " " + Lang.T("(server error: try again later)") : "";
        }

        private static void CheckContentType(string contentType, Dictionary<string, string> meta)
        {
            string type = contentType == null ? "" : contentType.ToLowerInvariant();
            int semicolon = type.IndexOf(';');
            if (semicolon >= 0)
            {
                type = type.Substring(0, semicolon);
            }
            type = type.Trim();
            if (type.StartsWith("image/") || type.StartsWith("audio/") || type.StartsWith("video/")
                || type == "application/zip" || type == "application/x-gzip" || type == "application/gzip"
                || type == "application/pdf" || type == "application/x-msdownload")
            {
                throw new FetchException(Lang.F("The server sent a file of type \"{0}\", not a subscription.", type), true, meta);
            }
        }

        private static byte[] ReadLimited(Stream stream, long declared, Dictionary<string, string> meta)
        {
            if (declared > MaxBytes)
            {
                throw new FetchException(Lang.F("The subscription is larger than {0} MB.", MaxBytes / 1024 / 1024), true, meta);
            }
            MemoryStream data = new MemoryStream();
            byte[] buffer = new byte[8192];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                data.Write(buffer, 0, read);
                if (data.Length > MaxBytes)
                {
                    throw new FetchException(Lang.F("The subscription is larger than {0} MB.", MaxBytes / 1024 / 1024), true, meta);
                }
            }
            return data.ToArray();
        }

        // ---- external downloaders ----

        // Exit codes of nt5helper fetch (helper/fetch.go).
        private const int HelperUsage = 2;
        private const int HelperStatus = 4;
        private const int HelperTooLarge = 5;

        private static byte[] DownloadWithHelper(string url, int proxyPort, DownloadTools tools,
            IList<KeyValuePair<string, string>> headers, Dictionary<string, string> meta)
        {
            // A damaged bundle is reported here; it is never skipped in favour of the outdated store.
            string ca = "";
            if (tools.CaPath.Length > 0 && File.Exists(tools.CaPath))
            {
                CaBundleInfo info = CaBundle.Inspect(tools.CaPath);
                if (info.Problem != null)
                {
                    throw new FetchException(Lang.F("HTTPS subscriptions cannot be checked: {0}", info.Problem), false, meta);
                }
                ca = " -ca \"" + tools.CaPath + "\"";
            }
            string dir = TempDirOf(tools);
            string headerFile = WriteHeaderFile(dir, headers);
            // the address (with its token) goes in a file: command lines can be read by other programs
            string urlFile = WritePrivateFile(dir, ".url", url + "\r\n");
            string metaFile = Path.Combine(dir, "subscription-" + Guid.NewGuid().ToString("N") + ".meta");
            try
            {
                string args = "fetch -max " + MaxBytes + " -timeout " + (TimeoutMs / 1000)
                              + (proxyPort > 0 ? " -proxy http://127.0.0.1:" + proxyPort : "") + ca
                              + " -agent \"" + tools.UserAgent + "\""
                              + (headerFile != null ? " -headers \"" + headerFile + "\"" : "")
                              + " -meta \"" + metaFile + "\""
                              + " -url-file \"" + urlFile + "\" -out ";
                int code = -1;
                try
                {
                    return RunDownloader(tools.HelperPath, args, url, tools, "nt5helper", out code);
                }
                catch (FetchException ex)
                {
                    ReadMetaFile(metaFile, meta);
                    int status;
                    string message = code == HelperStatus && meta.ContainsKey("status") && int.TryParse(meta["status"], out status)
                        && status != 200 ? Lang.F("The server answered HTTP {0}.", status + HttpHint(status)) : ex.Message;
                    if (code == HelperUsage)
                    {
                        message += " " + Lang.T("nt5helper.exe does not accept the arguments of this version: copy nt5helper.exe from the "
                                   + "same package as NekoRayNT5.exe.");
                    }
                    throw new FetchException(message, ex.ServerAnswered || code == HelperStatus || code == HelperTooLarge, meta);
                }
                finally
                {
                    ReadMetaFile(metaFile, meta);
                }
            }
            finally
            {
                SafeFile.TryDelete(metaFile);
                SafeFile.TryDelete(urlFile);
                if (headerFile != null)
                {
                    SafeFile.TryDelete(headerFile);
                }
            }
        }

        private static byte[] DownloadWithCurl(string url, int proxyPort, DownloadTools tools,
            IList<KeyValuePair<string, string>> headers, Dictionary<string, string> meta)
        {
            string dir = TempDirOf(tools);
            string headerFile = WriteHeaderFile(dir, headers);
            string dumpFile = Path.Combine(dir, "subscription-" + Guid.NewGuid().ToString("N") + ".hdr");
            // the address in a curl config file (not on the command line); CheckUrl refused quotes and spaces
            string configFile = WritePrivateFile(dir, ".url", "url = \"" + url.Replace("\\", "\\\\") + "\"\r\n");
            try
            {
                // --proto limits redirects to https as well; certificate checks stay on (no -k).
                string args = "--fail --silent --show-error --location --max-redirs " + MaxRedirects
                              + " --proto =https --proto-redir =https --max-time " + (TimeoutMs / 1000)
                              + " --max-filesize " + MaxBytes + " --compressed"
                              + " --user-agent \"" + tools.UserAgent + "\""
                              + (headerFile != null ? " --header \"@" + headerFile + "\"" : "")
                              + " --dump-header \"" + dumpFile + "\""
                              + (proxyPort > 0 ? " --proxy http://127.0.0.1:" + proxyPort : " --noproxy *")
                              + " --config \"" + configFile + "\" --output ";
                int code = -1;
                try
                {
                    return RunDownloader(tools.CurlPath, args, url, tools, "curl", out code);
                }
                catch (FetchException ex)
                {
                    ReadCurlHeaders(dumpFile, meta);
                    // 22: HTTP error with --fail, 63: larger than --max-filesize
                    throw new FetchException(ex.Message, ex.ServerAnswered || code == 22 || code == 63, meta);
                }
                finally
                {
                    ReadCurlHeaders(dumpFile, meta);
                }
            }
            finally
            {
                SafeFile.TryDelete(dumpFile);
                SafeFile.TryDelete(configFile);
                if (headerFile != null)
                {
                    SafeFile.TryDelete(headerFile);
                }
            }
        }

        private static string TempDirOf(DownloadTools tools)
        {
            string dir = tools.TempDir.Length > 0 ? tools.TempDir : Path.GetTempPath();
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            return dir;
        }

        // Null when there are no extra headers.
        private static string WriteHeaderFile(string dir, IList<KeyValuePair<string, string>> headers)
        {
            if (headers.Count == 0)
            {
                return null;
            }
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < headers.Count; i++)
            {
                sb.Append(headers[i].Key).Append(": ").Append(CleanHeader(headers[i].Value)).Append("\r\n");
            }
            return WritePrivateFile(dir, ".req", sb.ToString());
        }

        // A short-lived file for a downloader (address, headers); deleted after the download and, if
        // the program ends in between, at the next start (CleanupTempFiles).
        private static string WritePrivateFile(string dir, string extension, string content)
        {
            string file = Path.Combine(dir, "subscription-" + Guid.NewGuid().ToString("N") + extension);
            File.WriteAllText(file, content, new UTF8Encoding(false));
            return file;
        }

        // Removes what a download left behind when the program ended in the middle of it (request
        // headers with the HWID, answers, partial files). Called at start-up, before any download.
        public static int CleanupTempFiles(string dir)
        {
            int removed = 0;
            if (!Directory.Exists(dir))
            {
                return 0;
            }
            foreach (string pattern in new string[] { "subscription-*.req", "subscription-*.url", "subscription-*.meta", "subscription-*.hdr",
                "subscription-*.tmp", "subscription-*.tmp.part" })
            {
                foreach (string file in Directory.GetFiles(dir, pattern))
                {
                    SafeFile.TryDelete(file);
                    if (!File.Exists(file))
                    {
                        removed++;
                    }
                }
            }
            return removed;
        }

        // "name: value" lines written by nt5helper -meta.
        private static void ReadMetaFile(string file, Dictionary<string, string> meta)
        {
            if (!File.Exists(file))
            {
                return;
            }
            try
            {
                ParseHeaderLines(File.ReadAllText(file), meta);
            }
            catch (IOException)
            {
                // only a description
            }
        }

        // curl --dump-header: one block per response (redirects); only the last one counts.
        private static void ReadCurlHeaders(string file, Dictionary<string, string> meta)
        {
            if (!File.Exists(file))
            {
                return;
            }
            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch (IOException)
            {
                return;
            }
            int last = text.LastIndexOf("HTTP/", StringComparison.Ordinal);
            if (last < 0)
            {
                return;
            }
            text = text.Substring(last);
            int lineEnd = text.IndexOf('\n');
            string statusLine = lineEnd < 0 ? text : text.Substring(0, lineEnd);
            string[] parts = statusLine.Split(' ');
            if (parts.Length >= 2)
            {
                meta["status"] = parts[1].Trim();
            }
            ParseHeaderLines(lineEnd < 0 ? "" : text.Substring(lineEnd + 1), meta);
        }

        public static void ParseHeaderLines(string text, Dictionary<string, string> meta)
        {
            string[] lines = text.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon <= 0)
                {
                    continue;
                }
                string name = lines[i].Substring(0, colon).Trim().ToLowerInvariant();
                if (name == "status" || Array.IndexOf(MetaHeaders, name) >= 0 || name.StartsWith("x-hwid"))
                {
                    meta[name] = CleanHeader(lines[i].Substring(colon + 1));
                }
            }
        }

        private static byte[] RunDownloader(string exe, string argsBeforeFile, string url, DownloadTools tools, string name, out int code)
        {
            string file = Path.Combine(TempDirOf(tools), "subscription-" + Guid.NewGuid().ToString("N") + ".tmp");
            code = -1;
            try
            {
                string output;
                try
                {
                    code = CoreProcess.RunOnce(exe, argsBeforeFile + "\"" + file + "\"", TimeoutMs + 10000, out output);
                }
                catch (System.ComponentModel.Win32Exception ex)
                {
                    throw new FetchException(Lang.F("{0} could not be started: {1}", name, CoreLauncher.DescribeStartError(ex)), false, null);
                }
                if (code != 0 || !File.Exists(file))
                {
                    string text = RedactUrls(output.Replace(url, "<url>")).Trim();
                    string reason = code < 0 ? Lang.T("no answer in time") : Lang.F("exit code {0}", code);
                    throw new FetchException(text.Length > 0
                        ? Lang.F("{0} could not download the subscription ({1}): {2}", name, reason, Shorten(text, 400))
                        : Lang.F("{0} could not download the subscription ({1}).", name, reason), false, null);
                }
                FileInfo info = new FileInfo(file);
                if (info.Length > MaxBytes)
                {
                    throw new FetchException(Lang.F("The subscription is larger than {0} MB.", MaxBytes / 1024 / 1024), true, null);
                }
                return File.ReadAllBytes(file);
            }
            finally
            {
                SafeFile.TryDelete(file);
            }
        }

        // Replaces every http:// or https:// address (a redirect target may carry a token too) by "<url>".
        public static string RedactUrls(string text)
        {
            StringBuilder sb = new StringBuilder(text.Length);
            int pos = 0;
            while (pos < text.Length)
            {
                int http = text.IndexOf("http://", pos, StringComparison.OrdinalIgnoreCase);
                int https = text.IndexOf("https://", pos, StringComparison.OrdinalIgnoreCase);
                int start = http < 0 ? https : (https < 0 ? http : Math.Min(http, https));
                if (start < 0)
                {
                    sb.Append(text, pos, text.Length - pos);
                    break;
                }
                sb.Append(text, pos, start - pos).Append("<url>");
                int end = start;
                while (end < text.Length && text[end] > ' ' && text[end] != '"' && text[end] != '\'')
                {
                    end++;
                }
                pos = end;
            }
            return sb.ToString();
        }

        private static string Shorten(string text, int max)
        {
            return text.Length <= max ? text : text.Substring(0, max) + "...";
        }

        // ---- content ----

        // Turns downloaded bytes into text; rejects what is clearly not a subscription.
        public static string CheckContent(byte[] data)
        {
            if (data.Length == 0)
            {
                throw new IOException(Lang.T("The server sent an empty answer."));
            }
            for (int i = 0; i < data.Length && i < 4096; i++)
            {
                if (data[i] == 0)
                {
                    throw new IOException(Lang.T("The server sent binary data, not a subscription."));
                }
            }
            string text;
            try
            {
                text = new UTF8Encoding(false, true).GetString(data);
            }
            catch (ArgumentException)
            {
                throw new IOException(Lang.T("The answer is not UTF-8 text, so it is not a subscription."));
            }
            string head = text.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
            string lower = head.Length > 200 ? head.Substring(0, 200).ToLowerInvariant() : head.ToLowerInvariant();
            if (lower.StartsWith("<!doctype html") || lower.StartsWith("<html") || lower.StartsWith("<head") || lower.StartsWith("<?xml"))
            {
                throw new IOException(Lang.T("The server sent a web page instead of a subscription. The address may be wrong, "
                    + "expired or require a login."));
            }
            if (head.Trim().Length == 0)
            {
                throw new IOException(Lang.T("The server sent an empty answer."));
            }
            return text;
        }

        // Accepts a list of links, or the same list in Base64 (standard or URL-safe, with or without
        // padding and line breaks), or a percent-encoded list. Throws FormatException when it is none.
        public static List<ProfileEntry> Decode(string content, LinkProblems problems)
        {
            string text = content.Trim().TrimStart('\uFEFF').Trim();
            if (text.Length == 0)
            {
                throw new FormatException(Lang.T("The data is empty."));
            }
            if (text.IndexOf("://", StringComparison.Ordinal) < 0)
            {
                if (text.IndexOf("%3A%2F%2F", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    text = PercentEncoding.Decode(text);
                }
                else
                {
                    if (text.StartsWith("{") || text.StartsWith("[") || text.StartsWith("proxies:")
                        || text.IndexOf("\nproxies:", StringComparison.Ordinal) >= 0)
                    {
                        throw new FormatException(Lang.T("The data is a configuration file (Xray/sing-box JSON or Clash YAML), "
                            + "not a list of share links; NekoRay NT5 can import only share links. Ask the provider "
                            + "for a v2rayN-compatible (Base64) subscription address."));
                    }
                    try
                    {
                        text = PercentEncoding.DecodeBase64Text(text);
                    }
                    catch (FormatException)
                    {
                        throw new FormatException(Lang.T("The data is neither a list of links nor Base64 text."));
                    }
                    if (text.IndexOf("://", StringComparison.Ordinal) < 0)
                    {
                        throw new FormatException(Lang.T("The data contains no share links."));
                    }
                }
            }
            return ShareLinks.ParseMany(text, problems);
        }
    }
}
