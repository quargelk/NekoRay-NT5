using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using NekoRayNT5.App;
using NekoRayNT5.Platform;
using NekoRayNT5.UI;

namespace NekoRayNT5
{
    internal static class Program
    {
        private static readonly IntPtr HwndBroadcast = new IntPtr(0xFFFF);

        // Broadcast by a second copy of the program to bring the first one to the front.
        public static int ShowWindowMessage;

        private static MainForm _mainForm;
        private static int _fatal;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int RegisterWindowMessage(string name);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [STAThread]
        private static void Main(string[] args)
        {
            // Installed first: on .NET 2.0 an exception on any thread ends the process, and the
            // user must at least learn why and get the system proxy back.
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            try
            {
                DiagLog.Directory = Path.Combine(AppPaths.ConfigDir, "logs");
            }
            catch (Exception)
            {
                DiagLog.Directory = null;
            }

            ShowWindowMessage = RegisterWindowMessage("NekoRayNT5.ShowWindow");

            bool createdNew;
            using (Mutex mutex = new Mutex(true, AppInfo.MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    if (ShowWindowMessage != 0)
                    {
                        PostMessage(HwndBroadcast, ShowWindowMessage, IntPtr.Zero, IntPtr.Zero);
                    }
                    return;
                }

                DiagLog.Write("---- start " + AppInfo.Name + " " + AppInfo.Version + " (" + AppInfo.OsDescription + ")");
                bool startHidden = Array.IndexOf(args, AutoStart.TrayArgument) >= 0;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += OnThreadException;
                ApplyLanguage();
                try
                {
                    _mainForm = new MainForm(startHidden);
                    Application.Run(_mainForm);
                }
                catch (Exception ex)
                {
                    ReportFatal("start-up or message loop", ex);
                }
                finally
                {
                    // Whatever happened, do not leave the system proxy pointing at a dead port
                    // or the core running without the shell.
                    if (_mainForm != null)
                    {
                        _mainForm.Cleanup();
                    }
                    DiagLog.Write("---- exit");
                }
                GC.KeepAlive(mutex);
            }
        }

        // An error in a window event handler: the program keeps running.
        // The language is chosen before the first window exists; settings that cannot be read mean English
        // (the main window reports the settings problem itself).
        private static void ApplyLanguage()
        {
            string language = Lang.English;
            try
            {
                string warning;
                language = new NekoRayNT5.Services.SettingsStore(AppPaths.SettingsPath).Load(out warning).Language;
            }
            catch (Exception)
            {
            }
            string problem = Lang.Set(language);
            if (problem != null)
            {
                DiagLog.Write("Language: " + problem);
            }
        }

        private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            string file = DiagLog.WriteCrash("Unhandled exception in the user interface (the program continues)", e.Exception);
            MessageBox.Show(Lang.T("An unexpected error occurred. The program continues to run.") + "\r\n\r\n"
                + e.Exception.GetType().Name + ": " + e.Exception.Message
                + (file != null ? "\r\n\r\n" + Lang.F("Details were written to:\r\n{0}", file) : ""),
                AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        // An error on a worker thread: .NET ends the process after this handler.
        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            ReportFatal("Unhandled exception on a worker thread; the program has to close", e.ExceptionObject as Exception);
        }

        private static void ReportFatal(string context, Exception ex)
        {
            if (Interlocked.Exchange(ref _fatal, 1) != 0)
            {
                return;
            }
            string file = DiagLog.WriteCrash(context, ex);
            if (_mainForm != null)
            {
                // No window calls here: this may run on any thread.
                _mainForm.EmergencyCleanup();
            }
            try
            {
                MessageBox.Show(Lang.T("NekoRay NT5 stopped because of an unexpected error.") + "\r\n\r\n"
                    + (ex == null ? "" : ex.GetType().Name + ": " + ex.Message + "\r\n\r\n")
                    + Lang.T("The system proxy settings were restored and the core was stopped.")
                    + (file != null ? "\r\n\r\n" + Lang.F("Details were written to:\r\n{0}", file) : ""),
                    AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception)
            {
            }
        }
    }
}
