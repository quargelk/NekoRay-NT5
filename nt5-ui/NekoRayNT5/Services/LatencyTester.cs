using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using NekoRayNT5.Core;
using NekoRayNT5.Model;
using NekoRayNT5.App;

namespace NekoRayNT5.Services
{
    // Latency measurements. Every number comes from a real connection; a failure returns -1 and a reason.
    internal static class LatencyTester
    {
        // Time of a TCP connection to the server itself. It does not prove that the proxy works.
        public static int TcpPing(ProfileEntry profile, int timeoutMs, out string note)
        {
            note = "";
            IPAddress[] addresses;
            try
            {
                addresses = Dns.GetHostAddresses(profile.Address);
            }
            catch (Exception)
            {
                note = Lang.T("cannot resolve the address");
                return -1;
            }
            if (addresses.Length == 0)
            {
                note = Lang.T("cannot resolve the address");
                return -1;
            }

            Socket socket = new Socket(addresses[0].AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                Stopwatch watch = Stopwatch.StartNew();
                IAsyncResult result = socket.BeginConnect(new IPEndPoint(addresses[0], profile.Port), null, null);
                if (!result.AsyncWaitHandle.WaitOne(timeoutMs, false))
                {
                    note = Lang.T("timeout");
                    return -1;
                }
                socket.EndConnect(result);
                return Math.Max(1, (int)watch.ElapsedMilliseconds);
            }
            catch (SocketException ex)
            {
                note = ex.SocketErrorCode.ToString();
                return -1;
            }
            finally
            {
                socket.Close();
            }
        }

        // Starts a temporary core with this profile only and times one HTTP request through it.
        public static int UrlTest(string exePath, string runDir, ProfileEntry profile, string url, int timeoutMs, out string note)
        {
            note = "";
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                note = Lang.T("the test URL must start with http://");
                return -1;
            }
            CoreProcess process = null;
            try
            {
                int port = 0;
                // The temporary port may still be refused by Windows when the core opens it (taken in
                // between, or a reserved range): a new port is tried, at most three times in all.
                for (int attempt = 1; process == null; attempt++)
                {
                    port = CoreLauncher.FindFreePort();
                    try
                    {
                        process = CoreLauncher.Launch(exePath, runDir, XrayConfigBuilder.BuildForTest(profile, port),
                            new SecretMask(profile), new int[] { port }, 15000, null, null);
                    }
                    catch (CoreLaunchException ex)
                    {
                        if (!ex.PortProblem || attempt >= 3)
                        {
                            throw;
                        }
                    }
                }

                Stopwatch watch = Stopwatch.StartNew();
                int status = ProxyRequest(port, url, timeoutMs);
                if (status == 502 || status == 503 || status == 504)
                {
                    // Xray answers 503 itself when the outbound could not connect.
                    note = Lang.F("no connection through the profile (HTTP {0})", status);
                    return -1;
                }
                return Math.Max(1, (int)watch.ElapsedMilliseconds);
            }
            catch (SocketException ex)
            {
                note = ex.SocketErrorCode == SocketError.TimedOut ? Lang.T("timeout") : ex.SocketErrorCode.ToString();
                return -1;
            }
            catch (System.IO.IOException ex)
            {
                note = process == null ? ex.Message : Lang.T("timeout or connection closed");
                return -1;
            }
            catch (CoreLaunchException ex)
            {
                note = FirstLine(ex.Message);
                return -1;
            }
            catch (Exception ex)
            {
                note = ex.Message;
                return -1;
            }
            finally
            {
                if (process != null)
                {
                    process.Stop();
                    process.WaitForExit(3000);
                }
            }
        }

        // Sends one plain HTTP request through a local HTTP proxy and returns the status code.
        // A raw socket is used on purpose: HttpWebRequest silently bypasses proxies for some addresses.
        public static int ProxyRequest(int proxyPort, string url, int timeoutMs)
        {
            Uri uri = new Uri(url);
            using (TcpClient client = new TcpClient())
            {
                client.SendTimeout = timeoutMs;
                client.ReceiveTimeout = timeoutMs;
                client.Connect(IPAddress.Loopback, proxyPort);
                NetworkStream stream = client.GetStream();
                byte[] request = System.Text.Encoding.ASCII.GetBytes("GET " + uri.AbsoluteUri + " HTTP/1.1\r\nHost: " + uri.Authority
                    + "\r\nUser-Agent: NekoRayNT5\r\nProxy-Connection: close\r\nConnection: close\r\n\r\n");
                stream.Write(request, 0, request.Length);

                byte[] buffer = new byte[64];
                int total = 0;
                while (total < 12)
                {
                    int read = stream.Read(buffer, total, buffer.Length - total);
                    if (read <= 0)
                    {
                        throw new System.IO.IOException(Lang.T("Connection closed."));
                    }
                    total += read;
                }
                // "HTTP/1.x NNN"
                string head = System.Text.Encoding.ASCII.GetString(buffer, 0, total);
                int status;
                if (!head.StartsWith("HTTP/") || !int.TryParse(head.Substring(9, 3), out status))
                {
                    throw new System.IO.IOException(Lang.T("Not an HTTP response."));
                }
                return status;
            }
        }

        private static string FirstLine(string text)
        {
            int end = text.IndexOf('\r');
            return end < 0 ? text : text.Substring(0, end);
        }
    }
}
