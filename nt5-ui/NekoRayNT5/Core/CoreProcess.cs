using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace NekoRayNT5.Core
{
    internal delegate void CoreOutputHandler(string line);
    internal delegate void CoreExitedHandler(int exitCode);

    // Runs xray.exe as a separate child process and reports its output and exit.
    // Events are raised on thread-pool threads; UI code must marshal them itself.
    internal sealed class CoreProcess : IDisposable
    {
        private readonly object _sync = new object();
        private readonly string _exePath;
        private readonly ManualResetEvent _exited = new ManualResetEvent(true);
        private Process _process;
        private int _id;

        public event CoreOutputHandler Output;
        public event CoreExitedHandler Exited;

        public CoreProcess(string exePath)
        {
            _exePath = exePath;
        }

        public bool Exists
        {
            get { return File.Exists(_exePath); }
        }

        public bool IsRunning
        {
            get
            {
                lock (_sync)
                {
                    return _process != null;
                }
            }
        }

        // Process id of the last started process.
        public int Id
        {
            get { return _id; }
        }

        public void Start(string arguments)
        {
            lock (_sync)
            {
                if (_process != null)
                {
                    throw new InvalidOperationException("Core is already running.");
                }

                Process p = new Process();
                p.StartInfo.FileName = _exePath;
                p.StartInfo.Arguments = arguments;
                p.StartInfo.WorkingDirectory = Path.GetDirectoryName(_exePath);
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;
                p.EnableRaisingEvents = true;
                p.OutputDataReceived += OnDataReceived;
                p.ErrorDataReceived += OnDataReceived;
                p.Exited += OnExited;

                _exited.Reset();
                try
                {
                    p.Start();
                }
                catch
                {
                    _exited.Set();
                    p.Dispose();
                    throw;
                }
                _id = p.Id;
                JobObject.TryAssign(p);
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                _process = p;
            }
        }

        // Asks the process to terminate; use WaitForExit to wait for it.
        public void Stop()
        {
            Process p;
            lock (_sync)
            {
                p = _process;
            }
            if (p == null)
            {
                return;
            }
            try
            {
                if (!p.HasExited)
                {
                    p.Kill();
                }
            }
            catch (InvalidOperationException)
            {
                // already exited
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // exiting right now
            }
        }

        // True when the process is not running any more.
        public bool WaitForExit(int milliseconds)
        {
            return _exited.WaitOne(milliseconds, false);
        }

        public void Dispose()
        {
            Stop();
        }

        private void OnDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null)
            {
                return;
            }
            CoreOutputHandler handler = Output;
            if (handler != null)
            {
                handler(e.Data);
            }
        }

        private void OnExited(object sender, EventArgs e)
        {
            Process p = (Process)sender;
            int code = -1;
            try
            {
                // Flushes the asynchronous output readers before the exit is reported.
                p.WaitForExit();
                code = p.ExitCode;
            }
            catch (InvalidOperationException)
            {
            }
            lock (_sync)
            {
                if (_process == p)
                {
                    _process = null;
                }
            }
            p.Dispose();
            _exited.Set();

            CoreExitedHandler handler = Exited;
            if (handler != null)
            {
                handler(code);
            }
        }

        // Runs the program to completion and returns its exit code, or -1 when it had to be killed
        // after the timeout. "output" receives stdout and stderr.
        public static int RunOnce(string exePath, string arguments, int timeoutMs, out string output)
        {
            StringBuilder sb = new StringBuilder();
            using (Process p = new Process())
            {
                p.StartInfo.FileName = exePath;
                p.StartInfo.Arguments = arguments;
                p.StartInfo.WorkingDirectory = Path.GetDirectoryName(exePath);
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;
                DataReceivedEventHandler collect = delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data != null)
                    {
                        lock (sb)
                        {
                            sb.Append(e.Data).Append("\r\n");
                        }
                    }
                };
                p.OutputDataReceived += collect;
                p.ErrorDataReceived += collect;
                p.Start();
                JobObject.TryAssign(p);
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();

                int code = -1;
                if (p.WaitForExit(timeoutMs))
                {
                    p.WaitForExit();
                    code = p.ExitCode;
                }
                else
                {
                    try
                    {
                        p.Kill();
                        p.WaitForExit(2000);
                    }
                    catch (InvalidOperationException)
                    {
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                    }
                }
                lock (sb)
                {
                    output = sb.ToString();
                }
                return code;
            }
        }
    }
}
