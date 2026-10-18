using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using NekoRayNT5.Model;
using NekoRayNT5.Services;
using NekoRayNT5.App;

namespace NekoRayNT5.Core
{
    // A start-up failure with a message that is safe to show and to log.
    internal sealed class CoreLaunchException : Exception
    {
        public const string Cancelled = "Cancelled.";

        private readonly bool _portProblem;

        public CoreLaunchException(string message)
            : this(message, false)
        {
        }

        public CoreLaunchException(string message, bool portProblem)
            : base(message)
        {
            _portProblem = portProblem;
        }

        // True when the core could not listen on one of its local ports (taken or reserved by Windows):
        // a caller that chose the ports itself may pick new ones and try again.
        public bool PortProblem
        {
            get { return _portProblem; }
        }
    }

    internal delegate bool AbortCheck();

    // Removes the credentials of a profile from any text that goes to the log or to a message box.
    internal sealed class SecretMask
    {
        private readonly List<string> _secrets = new List<string>();

        public SecretMask(ProfileEntry p)
        {
            Add(p.Uuid);
            Add(p.Password);
            Add(p.PublicKey);
            Add(p.ShortId);
            Add(p.Username);
        }

        private void Add(string secret)
        {
            if (secret != null && secret.Length >= 4 && !_secrets.Contains(secret))
            {
                _secrets.Add(secret);
            }
        }

        public string Apply(string text)
        {
            for (int i = 0; i < _secrets.Count; i++)
            {
                if (text.IndexOf(_secrets[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    text = ReplaceIgnoreCase(text, _secrets[i]);
                }
            }
            return text;
        }

        private static string ReplaceIgnoreCase(string text, string secret)
        {
            StringBuilder sb = new StringBuilder();
            int pos = 0;
            while (true)
            {
                int found = text.IndexOf(secret, pos, StringComparison.OrdinalIgnoreCase);
                if (found < 0)
                {
                    sb.Append(text, pos, text.Length - pos);
                    return sb.ToString();
                }
                sb.Append(text, pos, found - pos).Append("***");
                pos = found + secret.Length;
            }
        }
    }

    // Starts xray.exe with a generated configuration:
    // temporary file -> "run -test" -> "run" -> wait for the local ports -> delete the file.
    internal static class CoreLauncher
    {
        private const int TestTimeoutMs = 30000;
        private const int PollMs = 100;

        private static int _counter;

        // Returns the running process. Throws CoreLaunchException; nothing is left behind in that case.
        public static CoreProcess Launch(string exePath, string runDir, string configJson, SecretMask mask,
            int[] ports, int timeoutMs, CoreOutputHandler output, AbortCheck aborted)
        {
            if (!File.Exists(exePath))
            {
                throw new CoreLaunchException(Lang.F("Core not found: {0}\r\nPut xray.exe next to the program.", exePath));
            }
            string incompatible = ExecutableCheck.Check(exePath);
            if (incompatible != null)
            {
                throw new CoreLaunchException(Lang.F("The core cannot run on this system: {0}", incompatible));
            }

            string configPath = WriteConfig(runDir, configJson);
            CoreProcess process = null;
            try
            {
                string testOutput;
                int code;
                try
                {
                    code = CoreProcess.RunOnce(exePath, "run -test -c \"" + configPath + "\"", TestTimeoutMs, out testOutput);
                }
                catch (System.ComponentModel.Win32Exception ex)
                {
                    throw new CoreLaunchException(Lang.F("Cannot start the core: {0}", DescribeStartError(ex)));
                }
                if (code != 0)
                {
                    throw new CoreLaunchException((code < 0 ? Lang.T("Xray rejected the configuration (the check did not finish in time):")
                        : Lang.T("Xray rejected the configuration:")) + "\r\n"
                        + mask.Apply(Tail(testOutput.Replace(configPath, "<config>"), 6)));
                }
                if (aborted != null && aborted())
                {
                    throw new CoreLaunchException(CoreLaunchException.Cancelled);
                }

                StringBuilder startLog = new StringBuilder();
                process = new CoreProcess(exePath);
                process.Output += delegate(string line)
                {
                    string safe = mask.Apply(line.Replace(configPath, "<config>"));
                    lock (startLog)
                    {
                        if (startLog.Length < 8000)
                        {
                            startLog.Append(safe).Append("\r\n");
                        }
                    }
                    if (output != null)
                    {
                        output(safe);
                    }
                };
                try
                {
                    process.Start("run -c \"" + configPath + "\"");
                }
                catch (System.ComponentModel.Win32Exception ex)
                {
                    throw new CoreLaunchException(Lang.F("Cannot start the core: {0}", DescribeStartError(ex)));
                }
                catch (Exception ex)
                {
                    throw new CoreLaunchException(Lang.F("Cannot start the core: {0}", ex.Message));
                }

                int waited = 0;
                while (true)
                {
                    if (!process.IsRunning)
                    {
                        string text;
                        lock (startLog)
                        {
                            text = startLog.ToString();
                        }
                        string bind = DescribeBindFailure(text);
                        throw new CoreLaunchException(Lang.T("The core exited during start-up:") + "\r\n" + Tail(text, 6)
                            + (bind != null ? "\r\n\r\n" + bind : ""), bind != null);
                    }
                    if (aborted != null && aborted())
                    {
                        throw new CoreLaunchException(CoreLaunchException.Cancelled);
                    }
                    if (AllPortsOpen(ports))
                    {
                        break;
                    }
                    if (waited >= timeoutMs)
                    {
                        throw new CoreLaunchException(Lang.F("The core did not open its local ports within {0} seconds.", timeoutMs / 1000));
                    }
                    Thread.Sleep(PollMs);
                    waited += PollMs;
                }
                CoreProcess started = process;
                process = null;
                return started;
            }
            finally
            {
                if (process != null)
                {
                    process.Stop();
                    process.WaitForExit(5000);
                }
                SafeFile.TryDelete(configPath);
            }
        }

        private static string WriteConfig(string runDir, string json)
        {
            try
            {
                if (!Directory.Exists(runDir))
                {
                    Directory.CreateDirectory(runDir);
                }
                string path = Path.Combine(runDir, "xray-" + Process.GetCurrentProcess().Id + "-"
                    + Interlocked.Increment(ref _counter) + ".json");
                File.WriteAllText(path, json, new UTF8Encoding(false));
                return path;
            }
            catch (IOException ex)
            {
                throw new CoreLaunchException(Lang.F("Cannot write the temporary configuration: {0}", ex.Message));
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new CoreLaunchException(Lang.F("Cannot write the temporary configuration: {0}", ex.Message));
            }
        }

        // Deletes configuration files left by a crashed session and stops a core it left running.
        public static void CleanupStale(string exePath, string runDir)
        {
            try
            {
                if (!Directory.Exists(runDir))
                {
                    return;
                }
                ChildPidFile.KillLeftover(runDir, ChildPidFile.Core, exePath);
                foreach (string file in Directory.GetFiles(runDir, "xray-*.json"))
                {
                    SafeFile.TryDelete(file);
                }
                string testDir = Path.Combine(runDir, "test");
                if (Directory.Exists(testDir))
                {
                    foreach (string file in Directory.GetFiles(testDir, "xray-*.json"))
                    {
                        SafeFile.TryDelete(file);
                    }
                }
            }
            catch (Exception)
            {
                // cleanup is best effort
            }
        }

        // Text for the user when Windows refuses to start a program.
        public static string DescribeStartError(System.ComponentModel.Win32Exception ex)
        {
            switch (ex.NativeErrorCode)
            {
                case 2:
                case 3:
                    return Lang.T("the program file was not found.");
                case 5:
                    return Lang.T("access is denied (antivirus or file permissions).");
                case 193:
                    return Lang.T("Windows says it is not a valid program for this system (error 193). "
                           + "The file may be damaged or built for a newer Windows.");
                case 216:
                    return Lang.T("the program is built for another processor or Windows version (error 216).");
                default:
                    return Lang.F("{0} (error {1}).", ex.Message, ex.NativeErrorCode);
            }
        }

        // ---- ports ----

        // Null when the port can be listened on, otherwise the reason. udp: the SOCKS inbound also
        // listens for UDP, and Windows may reserve a port for one protocol only.
        public static string CheckPortFree(string listenAddress, int port)
        {
            return CheckPortFree(listenAddress, port, false);
        }

        public static string CheckPortFree(string listenAddress, int port, bool udp)
        {
            // A program listening on 0.0.0.0 without exclusive use would still let us bind 127.0.0.1;
            // the system's TCP table shows it.
            if (NekoRayNT5.Platform.TcpTable.IsListening(port))
            {
                return Lang.T("it is already used by another program");
            }
            IPAddress address = IPAddress.Parse(listenAddress);
            string problem = TryBind(address, port, false);
            if (problem == null && udp)
            {
                problem = TryBind(address, port, true);
            }
            return problem;
        }

        // Null when the port could be bound (it is released again at once), otherwise the reason.
        // A plain bind, as the core (Go) does: on Windows XP an exclusive bind also fails while old
        // connections of the port are still in TIME_WAIT (after a disconnect, from the browsers that used
        // the system proxy), although the core could listen on it. Real listeners are still refused here,
        // and the TCP table check above finds those on 0.0.0.0.
        private static string TryBind(IPAddress address, int port, bool udp)
        {
            Socket socket = udp
                ? new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
                : new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                socket.ExclusiveAddressUse = false;
                socket.Bind(new IPEndPoint(address, port));
                if (!udp)
                {
                    socket.Listen(1);
                }
                return null;
            }
            catch (SocketException ex)
            {
                return DescribeBindError(ex.SocketErrorCode, udp);
            }
            finally
            {
                socket.Close();
            }
        }

        private static string DescribeBindError(SocketError code, bool udp)
        {
            switch (code)
            {
                case SocketError.AddressAlreadyInUse:
                    return Lang.T("it is already used by another program") + (udp ? " (UDP)" : "");
                case SocketError.AccessDenied:
                    // WSAEACCES: an excluded port range (Hyper-V, WinNAT, Docker...) or a security program.
                    return Lang.F(udp ? "Windows does not allow listening on it for UDP (WSAEACCES: the port lies in a range reserved by "
                           + "Windows - see \"netsh int ipv4 show excludedportrange protocol={0}\" - or a security program blocks it)"
                           : "Windows does not allow listening on it (WSAEACCES: the port lies in a range reserved by "
                           + "Windows - see \"netsh int ipv4 show excludedportrange protocol={0}\" - or a security program blocks it)",
                           udp ? "udp" : "tcp");
                case SocketError.AddressNotAvailable:
                    return Lang.T("the listen address is not available on this computer");
                default:
                    return Lang.F("Windows refused it ({0})", code);
            }
        }

        // Explains a failed listen in the start-up output of Xray (Go passes on the Windows error text).
        // Null when the output shows no such failure.
        public static string DescribeBindFailure(string coreOutput)
        {
            string text = coreOutput ?? "";
            if (text.IndexOf("forbidden by its access permissions", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return Lang.T("Windows refused one of the local ports (WSAEACCES): it lies in a port range reserved by Windows "
                       + "(Hyper-V, WinNAT, Docker...) or a security program blocks it. Choose other ports in "
                       + "Settings - Connections (for example 10808 and 10809).");
            }
            if (text.IndexOf("Only one usage of each socket address", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("address already in use", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return Lang.T("One of the local ports is already used by another program. Close that program or choose "
                       + "other ports in Settings - Connections.");
            }
            return null;
        }

        // Ports handed out recently by FindFreePort, so that two calls never return the same one.
        private static readonly List<int> RecentPorts = new List<int>();
        private static readonly Random PortRandom = new Random();
        public const int FirstTestPort = 20000;
        public const int LastTestPort = 44999;

        // A loopback port that is free for TCP and UDP (the SOCKS inbound of the core listens on both).
        // The port is taken at random below the dynamic range (49152-65535 since Vista, 1025-5000 on XP):
        // Windows gives dynamic ports to outgoing connections of any program at any moment, so a dynamic
        // port that was free a moment ago may be taken when the core starts. Newer Windows also reserve
        // whole ranges (Hyper-V, NAT) where bind fails with WSAEACCES; such ports are skipped, and the
        // caller still sees CoreLaunchException.PortProblem if Windows refuses the port later.
        // Throws IOException when no port could be found.
        public static int FindFreePort()
        {
            for (int attempt = 0; attempt < 60; attempt++)
            {
                int port;
                lock (RecentPorts)
                {
                    port = PortRandom.Next(FirstTestPort, LastTestPort + 1);
                    if (RecentPorts.Contains(port))
                    {
                        continue;
                    }
                    RecentPorts.Add(port);
                    if (RecentPorts.Count > 256)
                    {
                        RecentPorts.RemoveAt(0);
                    }
                }
                if (CheckPortFree("127.0.0.1", port, true) == null)
                {
                    return port;
                }
            }
            throw new IOException(Lang.F("No free local port was found for TCP and UDP between {0} and {1} (Windows refused 60 random ports).",
                                  FirstTestPort, LastTestPort));
        }

        private static bool AllPortsOpen(int[] ports)
        {
            for (int i = 0; i < ports.Length; i++)
            {
                if (!IsPortOpen(ports[i]))
                {
                    return false;
                }
            }
            return true;
        }

        public static bool IsPortOpen(int port)
        {
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    client.Connect(IPAddress.Loopback, port);
                    return true;
                }
            }
            catch (SocketException)
            {
                return false;
            }
        }

        private static string Tail(string text, int lines)
        {
            string[] all = text.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            StringBuilder sb = new StringBuilder();
            for (int i = Math.Max(0, all.Length - lines); i < all.Length; i++)
            {
                sb.Append(all[i]).Append("\r\n");
            }
            return sb.Length == 0 ? Lang.T("(no output)") : sb.ToString().TrimEnd();
        }
    }
}
