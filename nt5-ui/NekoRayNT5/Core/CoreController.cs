using System;
using System.Threading;
using NekoRayNT5.Model;
using NekoRayNT5.Services;
using NekoRayNT5.App;

namespace NekoRayNT5.Core
{
    internal enum CoreState
    {
        NotRunning,
        Connecting,
        Running,
        Stopping,
        Error
    }

    internal enum StartResult
    {
        // A start (or a switch to this profile) was scheduled.
        Started,
        // This profile is already connecting or running; nothing was done.
        AlreadyActive
    }

    internal delegate void CoreStateHandler(CoreState state, string message);

    // Owns the proxy core: one profile at a time, start / stop / switch without blocking the caller.
    // "Running" means that the core accepted the configuration and listens on the local ports;
    // it does not prove that the remote server is reachable. Events are raised on worker threads, except the Connecting state of Start,
    // which is raised by the caller's thread before Start returns.
    internal sealed class CoreController
    {
        private const int StartTimeoutMs = 15000;
        private const int StopTimeoutMs = 5000;

        private readonly object _operation = new object();
        private readonly object _sync = new object();
        private readonly string _exePath;
        private readonly string _runDir;

        private CoreProcess _process;
        private CoreState _state = CoreState.NotRunning;
        private int _activeProfileId = -1;
        private int _request;
        private bool _stopping;
        private bool _shutDown;

        public event CoreStateHandler StateChanged;
        public event CoreOutputHandler Log;

        public CoreController(string exePath, string runDir)
        {
            _exePath = exePath;
            _runDir = runDir;
        }

        public CoreState State
        {
            get { lock (_sync) { return _state; } }
        }

        // Id of the profile that is being started or is running, otherwise -1.
        public int ActiveProfileId
        {
            get { lock (_sync) { return _activeProfileId; } }
        }

        public bool IsBusy
        {
            get
            {
                CoreState s = State;
                return s == CoreState.Connecting || s == CoreState.Stopping;
            }
        }

        // Process id of the running core, or 0.
        public int CorePid
        {
            get
            {
                lock (_sync)
                {
                    return _process == null ? 0 : _process.Id;
                }
            }
        }

        // Starts the profile; a running or starting profile is stopped first. The same profile that is
        // already connecting or running is left alone unless "restart" is set.
        public StartResult Start(ProfileEntry profile, AppSettings settings, bool restart)
        {
            ProfileEntry p = profile.Clone();
            AppSettings s = settings.Clone();
            int request;
            lock (_sync)
            {
                if (!restart && !_shutDown && _activeProfileId == p.Id
                    && (_state == CoreState.Connecting || _state == CoreState.Running))
                {
                    return StartResult.AlreadyActive;
                }
                request = Interlocked.Increment(ref _request);
            }
            // Visible at once, so that a second click while the worker has not started yet sees "busy".
            SetState(CoreState.Connecting, p.Id, "");
            Run(delegate { StartWorker(p, s, request); });
            return StartResult.Started;
        }

        public void Stop()
        {
            int request = Interlocked.Increment(ref _request);
            Run(delegate
            {
                lock (_operation)
                {
                    bool stopped = StopCore();
                    if (request == _request)
                    {
                        if (stopped)
                        {
                            SetState(CoreState.NotRunning, -1, "");
                        }
                        else
                        {
                            SetState(CoreState.Error, -1, StopTimeoutMessage());
                        }
                    }
                }
            });
        }

        // Synchronous stop for program exit; no events are raised afterwards.
        public void Shutdown(int waitMs)
        {
            Interlocked.Increment(ref _request);
            _shutDown = true;
            bool entered = Monitor.TryEnter(_operation, waitMs);
            try
            {
                CoreProcess p;
                lock (_sync)
                {
                    p = _process;
                    _process = null;
                    _stopping = true;
                }
                bool exited = true;
                if (p != null)
                {
                    p.Stop();
                    exited = p.WaitForExit(waitMs);
                }
                // a core that is still running keeps its pid file: the next start stops it
                if (exited)
                {
                    ChildPidFile.Clear(_runDir, ChildPidFile.Core);
                }
            }
            finally
            {
                if (entered)
                {
                    Monitor.Exit(_operation);
                }
            }
        }

        private static void Run(ThreadStart work)
        {
            Thread t = new Thread(work);
            t.IsBackground = true;
            t.Start();
        }

        private void StartWorker(ProfileEntry profile, AppSettings settings, int request)
        {
            lock (_operation)
            {
                if (request != _request || _shutDown)
                {
                    // superseded by a newer start or stop, which reports its own state
                    return;
                }
                bool stopped = StopCore();
                if (request != _request)
                {
                    return;
                }
                if (!stopped)
                {
                    // the previous core still holds the local ports
                    SetState(CoreState.Error, -1, StopTimeoutMessage());
                    return;
                }
                SetState(CoreState.Connecting, profile.Id, "");
                AbortCheck aborted = delegate { return request != _request || _shutDown; };
                CoreProcess process = null;
                try
                {
                    string error = ProfileValidator.Validate(profile);
                    if (error != null)
                    {
                        throw new CoreLaunchException(Lang.F("The profile is not valid: {0}", error));
                    }
                    CheckPort("SOCKS", settings.ListenAddress, settings.SocksPort, true);
                    CheckPort("HTTP", settings.ListenAddress, settings.HttpPort, false);

                    SecretMask mask = new SecretMask(profile);
                    process = CoreLauncher.Launch(_exePath, _runDir,
                        XrayConfigBuilder.Build(profile, settings), mask,
                        new int[] { settings.SocksPort, settings.HttpPort }, StartTimeoutMs,
                        OnOutput, aborted);

                    CoreProcess started = process;
                    process.Exited += delegate(int code) { OnExited(started, code); };
                    lock (_sync)
                    {
                        _process = process;
                        _stopping = false;
                    }
                    ChildPidFile.Write(_runDir, ChildPidFile.Core, process.Id);
                    process = null;

                    if (!started.IsRunning)
                    {
                        // it died between the port check and the handler registration
                        OnExited(started, -1);
                        return;
                    }
                    if (aborted())
                    {
                        throw new CoreLaunchException(CoreLaunchException.Cancelled);
                    }
                    SetState(CoreState.Running, profile.Id, "");
                }
                catch (Exception ex)
                {
                    if (process != null)
                    {
                        process.Stop();
                        process.WaitForExit(StopTimeoutMs);
                    }
                    // Whatever was started is taken down again; the user gets one clear message.
                    StopCore();
                    if (ex is CoreLaunchException && ex.Message == CoreLaunchException.Cancelled)
                    {
                        if (request == _request && !_shutDown)
                        {
                            SetState(CoreState.NotRunning, -1, "");
                        }
                    }
                    else if (ex is CoreLaunchException)
                    {
                        SetState(CoreState.Error, -1, ex.Message);
                    }
                    else
                    {
                        SetState(CoreState.Error, -1, Lang.F("Unexpected error while starting: {0}: {1}", ex.GetType().Name, ex.Message));
                    }
                }
            }
        }

        private static void CheckPort(string name, string address, int port, bool udp)
        {
            string problem = CoreLauncher.CheckPortFree(address, port, udp);
            if (problem != null)
            {
                throw new CoreLaunchException(Lang.F("Local {0} port {1} cannot be used: {2}.\r\nChoose another port in Settings - "
                    + "Connections, or close the program that uses it.", name, port, problem), true);
            }
        }

        // Called with _operation held. False when the core is still running after the timeout: it stays
        // tracked (and its pid file stays for the next start's cleanup), so that no second core is started
        // next to it; the next stop or start tries again.
        private bool StopCore()
        {
            CoreProcess p;
            lock (_sync)
            {
                p = _process;
                if (p == null)
                {
                    return true;
                }
                _stopping = true;
            }
            SetState(CoreState.Stopping, ActiveProfileId, "");
            p.Stop();
            if (!p.WaitForExit(StopTimeoutMs))
            {
                RaiseLog(StopTimeoutMessage());
                return false;
            }
            lock (_sync)
            {
                _process = null;
                _stopping = false;
            }
            ChildPidFile.Clear(_runDir, ChildPidFile.Core);
            return true;
        }

        private static string StopTimeoutMessage()
        {
            return Lang.F("The core process did not exit within {0} seconds.", StopTimeoutMs / 1000);
        }

        private void OnExited(CoreProcess process, int code)
        {
            lock (_sync)
            {
                if (_process != process || _stopping)
                {
                    return;
                }
                _process = null;
            }
            ChildPidFile.Clear(_runDir, ChildPidFile.Core);
            Run(delegate
            {
                lock (_operation)
                {
                    SetState(CoreState.Error, -1, Lang.F("The core exited unexpectedly (exit code {0}). See the log for details.", code));
                }
            });
        }

        private void OnOutput(string line)
        {
            RaiseLog(line);
        }

        private void RaiseLog(string line)
        {
            CoreOutputHandler handler = Log;
            if (handler != null && !_shutDown)
            {
                handler(line);
            }
        }

        private void SetState(CoreState state, int profileId, string message)
        {
            lock (_sync)
            {
                _state = state;
                _activeProfileId = profileId;
            }
            CoreStateHandler handler = StateChanged;
            if (handler != null && !_shutDown)
            {
                handler(state, message);
            }
        }
    }
}
