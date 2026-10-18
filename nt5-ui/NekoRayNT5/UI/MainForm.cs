using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using NekoRayNT5.App;
using NekoRayNT5.Core;
using NekoRayNT5.Model;
using NekoRayNT5.Platform;
using NekoRayNT5.Services;

namespace NekoRayNT5.UI
{
    // Main window. Layout and menu structure follow ui/mainwindow.ui of Nekoray 3.17;
    // only functions that really work are present in the menus.
    //
    // Nothing here may write a UUID, a password, a key, a share link or a subscription
    // address to the log.
    internal partial class MainForm : Form
    {
        private const int MaxLogLines = 300;
        private const int MaxReportedErrors = 5;
        private const int MaxImportFileBytes = 5 * 1024 * 1024;

        private readonly ProfileStore _store = new ProfileStore(AppPaths.ProfilesPath);
        private readonly SettingsStore _settingsStore = new SettingsStore(AppPaths.SettingsPath);
        private readonly SystemProxyManager _systemProxy = new SystemProxyManager(new WinInetProxy(), AppPaths.ProxyBackupPath);
        private readonly CoreController _core = new CoreController(AppPaths.CorePath, AppPaths.RunDir);
        private AppSettings _settings = new AppSettings();

        private readonly NotifyIcon _tray = new NotifyIcon();
        private readonly Icon _iconIdle = IconFactory.Create(Color.Silver);
        private readonly Icon _iconRunning = IconFactory.Create(Color.LimeGreen);
        private readonly Icon _iconProxy = IconFactory.Create(Color.DodgerBlue);
        private readonly Icon _iconError = IconFactory.Create(Color.Red);
        private readonly Font _boldFont;

        private ToolStripMenuItem _menuStart;
        private ToolStripMenuItem _menuStop;
        private ToolStripMenuItem _menuRestart;
        private ToolStripMenuItem _menuEdit;
        private ToolStripMenuItem _menuClone;
        private ToolStripMenuItem _menuMoveUp;
        private ToolStripMenuItem _menuMoveDown;
        private ToolStripMenuItem _menuMoveTo;
        private ToolStripMenuItem _menuCopyTo;
        private ToolStripMenuItem _menuDelete;
        private ToolStripMenuItem _menuShare;
        private ToolStripMenuItem _menuAutoStart;
        private ToolStripMenuItem _menuRemember;
        private ToolStripMenuItem _menuAllowLan;
        private ToolStripMenuItem _menuProxyOn;
        private ToolStripMenuItem _menuProxyOff;
        private ToolStripMenuItem _trayStart;
        private ToolStripMenuItem _trayStop;
        private ToolStripMenuItem _trayProxy;

        private int _currentGroup;
        private string _activeName = "";
        private bool _startHidden;
        private bool _exiting;
        private bool _cleanedUp;
        private bool _rebuildingTabs;
        private bool _balloonShown;
        private bool _proxyWanted;
        private volatile bool _testRunning;
        private volatile bool _cancelWork;
        // Why the core cannot run on this system (checked at start-up), or null.
        private string _coreProblem;
        private readonly Dictionary<int, bool> _updatingGroups = new Dictionary<int, bool>();
        private readonly System.Windows.Forms.Timer _proxyWatch = new System.Windows.Forms.Timer();
        private SystemProxyStatus _lastProxyStatus = SystemProxyStatus.Off;
        private ToolTip _tips;
        private ToolStrip _toolbar;
        private ToolStripButton _toolConnect;
        private ToolStripButton _toolUpdate;

        public MainForm(bool startHidden)
        {
            InitializeComponent();
            _startHidden = startHidden;
            _boldFont = new Font(listProfiles.Font, FontStyle.Bold);
            Icon = _iconIdle;
            Text = AppInfo.Name + " " + AppInfo.Version;

            // Traffic statistics and the connection list need a core API that this shell does not have.
            listProfiles.Columns.Remove(colTraffic);
            // the designer holds the English texts; the interface language replaces them here
            checkSystemProxy.Text = Lang.T("System Proxy");
            colType.Text = Lang.T("Type");
            colAddress.Text = Lang.T("Address");
            colName.Text = Lang.T("Name");
            colTest.Text = Lang.T("Test Result");
            tabLog.Text = Lang.T("Log");
            labelRunning.Text = Lang.T("Not Running");
            tabBottom.TabPages.Remove(tabConnections);

            BuildMenu();
            BuildToolbar();
            BuildTray();
            Shown += delegate { MaybeShowFirstRun(); };

            // One click selects, a double click connects (as Start and Enter do).
            listProfiles.DoubleClick += OnProfileDoubleClick;
            listProfiles.KeyDown += OnProfilesKeyDown;
            listProfiles.SelectedIndexChanged += delegate { UpdateUi(); };
            tabGroups.SelectedIndexChanged += OnGroupTabChanged;
            textSearch.TextChanged += delegate { RefreshProfileList(); };
            checkSystemProxy.Click += OnSystemProxyToggle;
            _tips = new ToolTip();
            _tips.AutoPopDelay = 30000;

            _core.StateChanged += delegate(CoreState state, string message)
            {
                Post(delegate { OnCoreState(state, message); });
            };
            _core.Log += delegate(string line)
            {
                Post(delegate { AppendLog("[core] " + line); });
            };

            _proxyWatch.Interval = 5000;
            _proxyWatch.Tick += delegate { WatchSystemProxy(); };

            // The handle is needed for cross-thread calls even when the window starts hidden in the tray.
            CreateHandle();
            Startup();
            _proxyWatch.Start();
        }

        protected override void SetVisibleCore(bool value)
        {
            if (_startHidden)
            {
                _startHidden = false;
                value = false;
            }
            base.SetVisibleCore(value);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Program.ShowWindowMessage && Program.ShowWindowMessage != 0)
            {
                ShowFromTray();
                return;
            }
            base.WndProc(ref m);
        }

        // ---- start-up and shutdown ----

        private void Startup()
        {
            AppendLog(AppInfo.Name + " " + AppInfo.Version + " (" + AppInfo.OsDescription + ")");

            string warning;
            _settings = _settingsStore.Load(out warning);
            ShowStorageWarning(warning);

            CoreLauncher.CleanupStale(AppPaths.CorePath, AppPaths.RunDir);
            Subscription.CleanupTempFiles(AppPaths.RunDir);
            string legacyNote = LegacyCleanup.Run(AppPaths.RunDir, AppPaths.HelperPath);
            if (legacyNote.Length > 0)
            {
                AppendLog(legacyNote);
            }

            // A backup that is still there means the previous session ended without restoring the system proxy.
            if (_systemProxy.IsEnabled)
            {
                AppendLog(Lang.T("The previous session did not restore the system proxy settings; restoring them now."));
                RestoreSystemProxy();
            }

            try
            {
                warning = _store.Load();
            }
            catch (Exception ex)
            {
                warning = Lang.F("Profiles could not be loaded: {0}", ex.Message);
            }
            ShowStorageWarning(warning);
            AppendLog(Lang.F("Profiles loaded: {0}, groups: {1}", _store.Profiles.Count, _store.Groups.Count));

            _currentGroup = _store.FindGroup(_settings.CurrentGroupId) != null ? _settings.CurrentGroupId : ProfileGroup.DefaultId;
            RebuildGroupTabs();
            _menuAutoStart.Checked = AutoStart.IsEnabled;
            _menuRemember.Checked = _settings.RememberLast;
            _menuAllowLan.Checked = _settings.AllowLan;
            ProbeCore();
            ProbeCaBundle();
            UpdateUi();

            if (_settings.RememberLast)
            {
                ProfileEntry last = _store.Find(_settings.LastProfileId);
                if (last != null && File.Exists(AppPaths.CorePath))
                {
                    _proxyWanted = _settings.LastSystemProxy;
                    StartProfile(last, false);
                }
            }
        }

        private void ShowStorageWarning(string warning)
        {
            if (warning != null)
            {
                AppendLog(Lang.T("Storage problem, see the message box."));
                MessageBox.Show(warning, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // Checks once whether the core can run here; on Windows 2000 it cannot (see ExecutableCheck).
        // nt5helper.exe trusts the CAs of cacert.pem: the root store of Windows XP is outdated.
        private void ProbeCaBundle()
        {
            if (!File.Exists(AppPaths.HelperPath))
            {
                return;
            }
            CaBundleInfo ca = CaBundle.Inspect(AppPaths.CaBundlePath);
            if (!ca.Exists)
            {
                AppendLog(Lang.F("CA bundle: {0} is missing next to the program; HTTPS subscriptions are checked "
                    + "against the Windows certificate store, which is outdated on Windows XP.", CaBundle.FileName));
            }
            else if (ca.Problem != null)
            {
                AppendLog(Lang.F("CA bundle: {0} HTTPS subscriptions will not be downloaded until it is replaced.", ca.Problem));
            }
            else
            {
                int age = CaBundle.AgeDays(ca, DateTime.UtcNow);
                AppendLog(Lang.F(age > 730 ? "CA bundle: {0} - older than two years: replace it with a current one." : "CA bundle: {0}.", ca));
            }
        }

        private void ProbeCore()
        {
            if (!File.Exists(AppPaths.CorePath))
            {
                AppendLog(Lang.F("Core not found: {0}. Put xray.exe next to the program to connect.", AppPaths.CorePath));
                return;
            }
            _coreProblem = ExecutableCheck.Check(AppPaths.CorePath);
            if (_coreProblem != null)
            {
                AppendLog(Lang.F("The core cannot run on this system: {0}", _coreProblem));
                return;
            }
            RunAsync(delegate
            {
                string output;
                int code;
                string problem = null;
                try
                {
                    code = CoreProcess.RunOnce(AppPaths.CorePath, "version", 10000, out output);
                }
                catch (System.ComponentModel.Win32Exception ex)
                {
                    code = -1;
                    output = "";
                    problem = CoreLauncher.DescribeStartError(ex);
                }
                string first = output.Split('\r', '\n')[0];
                Post(delegate
                {
                    if (problem != null)
                    {
                        _coreProblem = problem;
                        AppendLog(Lang.F("The core cannot be started: {0}", problem));
                    }
                    else
                    {
                        AppendLog(code == 0 ? Lang.F("Core: {0}", first) : Lang.F("The core does not start (exit code {0}).", code));
                    }
                    UpdateUi();
                });
            });
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && !_exiting && _settings.CloseToTray)
            {
                e.Cancel = true;
                Hide();
                if (!_balloonShown)
                {
                    _balloonShown = true;
                    _tray.ShowBalloonTip(3000, AppInfo.Name, Lang.T("Still running here. Use Exit in this menu to quit."), ToolTipIcon.Info);
                }
                return;
            }
            base.OnFormClosing(e);
            if (!e.Cancel)
            {
                Cleanup();
            }
        }

        // Restores the system proxy, stops the core and saves the settings. Safe to call more than once;
        // also used when Windows is shutting down and when the program ends because of an error.
        public void Cleanup()
        {
            if (_cleanedUp)
            {
                return;
            }
            _cleanedUp = true;
            _cancelWork = true;
            try
            {
                _settings.CurrentGroupId = _currentGroup;
                _settingsStore.Save(_settings);
            }
            catch (Exception)
            {
            }
            try
            {
                if (_systemProxy.IsEnabled)
                {
                    _systemProxy.Disable();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(Lang.F("The system proxy settings could not be restored:\r\n\r\n{0}", ex.Message), AppInfo.Name,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            _core.Shutdown(5000);
            _proxyWatch.Stop();
            _tray.Visible = false;
            _tray.Dispose();
            _tips.Dispose();
        }

        // Called on a fatal error, possibly on a worker thread: no window calls, no message boxes.
        public void EmergencyCleanup()
        {
            try
            {
                if (_systemProxy.IsEnabled)
                {
                    _systemProxy.Disable();
                    DiagLog.Write("Emergency: system proxy settings restored.");
                }
            }
            catch (Exception ex)
            {
                DiagLog.Write("Emergency: system proxy settings NOT restored: " + ex.Message);
            }
            try
            {
                _core.Shutdown(3000);
            }
            catch (Exception)
            {
            }
        }

        // ---- menus and tray ----

        private void BuildMenu()
        {
            ToolStripMenuItem program = AddMenu(Lang.T("&Program"));
            ToolStripMenuItem addMain = AddItem(program, Lang.T("Add servers or subscription..."), OnAddClick);
            addMain.ShortcutKeys = Keys.Control | Keys.N;
            program.DropDownItems.Add(new ToolStripSeparator());
            _menuAutoStart = AddItem(program, Lang.T("Start with system"), OnAutoStartClick);
            _menuRemember = AddItem(program, Lang.T("Remember last profile"), OnRememberClick);
            _menuAllowLan = AddItem(program, Lang.T("Allow other devices to connect"), OnAllowLanClick);
            ToolStripMenuItem spmode = AddItem(program, Lang.T("System Proxy"), null);
            _menuProxyOn = AddItem(spmode, Lang.T("Enable System Proxy"), delegate { SetSystemProxy(true); });
            _menuProxyOff = AddItem(spmode, Lang.T("Disable"), delegate { SetSystemProxy(false); });
            program.DropDownItems.Add(new ToolStripSeparator());
            _menuRestart = AddItem(program, Lang.T("Restart Proxy"), OnRestartClick);
            AddItem(program, Lang.T("Exit"), delegate { ExitProgram(); });

            ToolStripMenuItem preferences = AddMenu(Lang.T("P&references"));
            AddItem(preferences, Lang.T("Groups"), OnGroupsClick);
            AddItem(preferences, Lang.T("Settings..."), OnSettingsClick);
            AddItem(preferences, Lang.T("Diagnostics..."), delegate { ShowDiagnostics(null); });
            AddItem(preferences, Lang.T("Open Config Folder"), OnOpenConfigFolderClick);
            preferences.DropDownItems.Add(new ToolStripSeparator());
            AddItem(preferences, Lang.T("Export profile backup..."), OnExportBackupClick);
            AddItem(preferences, Lang.T("Import profile backup..."), OnImportBackupClick);
            preferences.DropDownItems.Add(new ToolStripSeparator());
            AddItem(preferences, Lang.T("Copy diagnostic info (no secrets)"), OnCopyDiagnosticsClick);

            ToolStripMenuItem server = AddMenu(Lang.T("&Server"));
            ToolStripMenuItem create = AddItem(server, Lang.T("New profile"), null);
            foreach (string type in ProfileEntry.Types)
            {
                string captured = type;
                AddItem(create, type == ProfileEntry.TypeShadowsocks ? "Shadowsocks" : type.ToUpperInvariant(),
                    delegate { NewProfile(captured); });
            }
            AddItem(server, Lang.T("Add (links, subscription, happ://add, file)..."), OnAddClick);
            server.DropDownItems.Add(new ToolStripSeparator());
            _menuStart = AddItem(server, Lang.T("Start (double click, Enter)"), OnStartClick);
            _menuStop = AddItem(server, Lang.T("Stop"), delegate { _core.Stop(); });
            server.DropDownItems.Add(new ToolStripSeparator());
            _menuEdit = AddItem(server, Lang.T("Edit (F2)"), OnEditClick);
            _menuClone = AddItem(server, Lang.T("Clone"), OnCloneClick);
            _menuMoveUp = AddItem(server, Lang.T("Move Up"), delegate { MoveSelected(-1); });
            _menuMoveDown = AddItem(server, Lang.T("Move Down"), delegate { MoveSelected(1); });
            _menuMoveTo = AddItem(server, Lang.T("Move to group"), null);
            _menuMoveTo.DropDownOpening += delegate { FillGroupMenu(_menuMoveTo, true); };
            _menuMoveTo.DropDownItems.Add(new ToolStripMenuItem("-"));
            _menuCopyTo = AddItem(server, Lang.T("Copy to group"), null);
            _menuCopyTo.DropDownOpening += delegate { FillGroupMenu(_menuCopyTo, false); };
            _menuCopyTo.DropDownItems.Add(new ToolStripMenuItem("-"));
            AddItem(server, Lang.T("Select All"), OnSelectAllClick);
            _menuDelete = AddItem(server, Lang.T("Delete"), OnDeleteClick);
            server.DropDownItems.Add(new ToolStripSeparator());
            _menuShare = AddItem(server, Lang.T("Share"), null);
            AddItem(_menuShare, Lang.T("Copy links of selected"), delegate { ExportLinks(null); });
            AddItem(_menuShare, Lang.T("Export links to file..."), OnExportLinksFileClick);
            AddItem(_menuShare, Lang.T("Export Xray config..."), OnExportConfigClick);
            server.DropDownItems.Add(new ToolStripSeparator());
            ToolStripMenuItem group = AddItem(server, Lang.T("Current Group"), null);
            AddItem(group, Lang.T("Tcp Ping"), delegate { RunTests(false); });
            AddItem(group, Lang.T("Url Test"), delegate { RunTests(true); });
            AddItem(group, Lang.T("Stop tests"), delegate { _cancelWork = true; });
            AddItem(group, Lang.T("Clear Test Result"), OnClearTestsClick);
            group.DropDownItems.Add(new ToolStripSeparator());
            AddItem(group, Lang.T("Sort by name"), delegate { SortGroup(0); });
            AddItem(group, Lang.T("Sort by address"), delegate { SortGroup(1); });
            AddItem(group, Lang.T("Sort by test result"), delegate { SortGroup(2); });
            AddItem(group, Lang.T("Remove Duplicates"), OnRemoveDuplicatesClick);
            ToolStripMenuItem sub = AddItem(server, Lang.T("Subscription"), null);
            AddItem(sub, Lang.T("Add subscription..."), OnAddClick);
            AddItem(sub, Lang.T("Update this subscription"), OnUpdateSubscriptionClick);
            AddItem(sub, Lang.T("Update all subscriptions"), OnUpdateAllSubscriptionsClick);
            ToolStripMenuItem hwid = AddItem(sub, Lang.T("Use Happ-compatible HWID for this subscription"), OnToggleGroupHwidClick);
            sub.DropDownOpening += delegate
            {
                ProfileGroup current = _store.FindGroup(_currentGroup);
                hwid.Enabled = current != null && current.IsSubscription;
                hwid.Checked = current != null && current.UseHwid;
            };
            AddItem(sub, Lang.T("Update this subscription from a file..."), OnImportSubscriptionFileClick);

            // Right click on the list: the most used profile commands.
            ContextMenuStrip context = new ContextMenuStrip();
            context.Opening += OnListMenuOpening;
            listProfiles.ContextMenuStrip = context;

            // Right click on a group tab: create, rename, update, delete that group.
            tabGroups.MouseUp += OnGroupTabsMouseUp;
        }

        // ---- group tabs: right-click menu ----

        private void OnGroupTabsMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right)
            {
                return;
            }
            int groupId = -1;
            for (int i = 0; i < tabGroups.TabCount; i++)
            {
                if (tabGroups.GetTabRect(i).Contains(e.Location))
                {
                    // the menu works on the tab that was clicked: it becomes the current group first
                    tabGroups.SelectedIndex = i;
                    groupId = (int)tabGroups.TabPages[i].Tag;
                    break;
                }
            }
            BuildGroupMenu(groupId).Show(tabGroups, e.Location);
        }

        // groupId: the group of the clicked tab, or -1 (empty part of the tab strip: only "New group").
        private ContextMenuStrip BuildGroupMenu(int groupId)
        {
            ProfileGroup g = groupId >= 0 ? _store.FindGroup(groupId) : null;
            bool updating = g != null && _updatingGroups.ContainsKey(g.Id);
            ContextMenuStrip menu = new ContextMenuStrip();
            AddContext(menu, Lang.T("New group..."), true, delegate { OnNewGroupClick(); });
            menu.Items.Add(new ToolStripSeparator());
            AddContext(menu, Lang.T("Rename..."), g != null, delegate { OnRenameGroupClick(groupId); });
            AddContext(menu, Lang.T("Update subscription"), g != null && g.IsSubscription && !updating,
                delegate { StartSubscriptionUpdate(groupId, true); });
            ToolStripMenuItem hwid = new ToolStripMenuItem(Lang.T("Use Happ-compatible HWID"));
            hwid.Enabled = g != null && g.IsSubscription;
            hwid.Checked = g != null && g.UseHwid;
            hwid.Click += delegate { SetGroupHwid(groupId, !hwid.Checked); };
            menu.Items.Add(hwid);
            AddContext(menu, Lang.T("Manage groups..."), true, OnGroupsClick);
            menu.Items.Add(new ToolStripSeparator());
            AddContext(menu, g != null && g.Id == ProfileGroup.DefaultId ? Lang.T("Delete group (the default group stays)") : Lang.T("Delete group..."),
                g != null && g.Id != ProfileGroup.DefaultId && !updating, delegate { OnDeleteGroupClick(groupId); });
            return menu;
        }

        private void OnNewGroupClick()
        {
            using (GroupNameForm form = new GroupNameForm(Lang.T("New group"), ""))
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    CreateGroup(form.GroupName);
                }
            }
        }

        // Returns the id of the new group (it becomes the current one), or -1 when it was not saved.
        private int CreateGroup(string name)
        {
            ProfileGroup g = null;
            if (!Commit(delegate { g = _store.AddGroup(name, ""); }) || g == null)
            {
                return -1;
            }
            AppendLog(Lang.F("Group \"{0}\" created.", g.Name));
            _currentGroup = g.Id;
            RebuildGroupTabs();
            UpdateUi();
            return g.Id;
        }

        private void OnRenameGroupClick(int groupId)
        {
            ProfileGroup g = _store.FindGroup(groupId);
            if (g == null)
            {
                return;
            }
            using (GroupNameForm form = new GroupNameForm(Lang.T("Rename group"), g.Name))
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    RenameGroup(groupId, form.GroupName);
                }
            }
        }

        // Only the name changes; the subscription address, HWID switch and profiles stay as they are.
        private bool RenameGroup(int groupId, string name)
        {
            ProfileGroup g = _store.FindGroup(groupId);
            if (g == null || name.Length == 0)
            {
                return false;
            }
            string old = g.Name;
            if (!Commit(delegate { _store.UpdateGroup(groupId, name, g.Url); }))
            {
                return false;
            }
            AppendLog(Lang.F("Group \"{0}\" renamed to \"{1}\".", old, name));
            RefreshTabTitles();
            return true;
        }

        private void SetGroupHwid(int groupId, bool on)
        {
            ProfileGroup g = _store.FindGroup(groupId);
            if (g == null || !g.IsSubscription)
            {
                return;
            }
            if (Commit(delegate { _store.SetUseHwid(groupId, on); }))
            {
                AppendLog(Lang.F(on ? "Subscription \"{0}\": Happ-compatible HWID on." : "Subscription \"{0}\": Happ-compatible HWID off.", g.Name));
                UpdateUi();
            }
        }

        private void OnDeleteGroupClick(int groupId)
        {
            ProfileGroup g = _store.FindGroup(groupId);
            if (g == null || g.Id == ProfileGroup.DefaultId)
            {
                return;
            }
            int count = _store.ProfilesOf(g.Id).Count;
            ProfileEntry active = _store.Find(_core.ActiveProfileId);
            bool usesActive = active != null && active.GroupId == g.Id;
            string question = (g.IsSubscription
                    ? (count > 0 ? Lang.F("Delete the subscription group \"{0}\" together with its {1} server(s)?", g.Name, count)
                       : Lang.F("Delete the subscription group \"{0}\"?", g.Name))
                    : (count > 0 ? Lang.F("Delete the group \"{0}\" together with its {1} server(s)?", g.Name, count)
                       : Lang.F("Delete the group \"{0}\"?", g.Name)))
                + (usesActive ? "\r\n\r\n" + Lang.T("The running connection uses a server of this group; it will be stopped.") : "")
                + "\r\n\r\n" + Lang.T("This cannot be undone. Other groups are not changed.");
            if (MessageBox.Show(this, question, AppInfo.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) == DialogResult.Yes)
            {
                DeleteGroup(groupId);
            }
        }

        // After the confirmation. The default group is never deleted; only the profiles of this group go.
        private bool DeleteGroup(int groupId)
        {
            ProfileGroup g = _store.FindGroup(groupId);
            if (g == null || g.Id == ProfileGroup.DefaultId || _updatingGroups.ContainsKey(groupId))
            {
                return false;
            }
            int count = _store.ProfilesOf(g.Id).Count;
            ProfileEntry active = _store.Find(_core.ActiveProfileId);
            if (!Commit(delegate { _store.RemoveGroup(groupId); }))
            {
                return false;
            }
            AppendLog(Lang.F("Group \"{0}\" deleted with {1} server(s).", g.Name, count));
            if (active != null && active.GroupId == groupId)
            {
                _core.Stop();
            }
            if (_currentGroup == groupId)
            {
                _currentGroup = ProfileGroup.DefaultId;
            }
            RebuildGroupTabs();
            UpdateUi();
            return true;
        }

        private void OnListMenuOpening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            ContextMenuStrip menu = (ContextMenuStrip)sender;
            menu.Items.Clear();
            int selected = listProfiles.SelectedItems.Count;
            CoreState state = _core.State;
            AddContext(menu, Lang.T("Start"), selected == 1, OnStartClick);
            AddContext(menu, Lang.T("Stop"), state == CoreState.Running || state == CoreState.Connecting, delegate { _core.Stop(); });
            menu.Items.Add(new ToolStripSeparator());
            AddContext(menu, Lang.T("Edit"), selected == 1, OnEditClick);
            AddContext(menu, Lang.T("Clone"), selected > 0, OnCloneClick);
            AddContext(menu, Lang.T("Delete"), selected > 0, OnDeleteClick);
            menu.Items.Add(new ToolStripSeparator());
            AddContext(menu, Lang.T("Copy links"), selected > 0, delegate { ExportLinks(null); });
            AddContext(menu, Lang.T("Tcp Ping"), selected > 0 || listProfiles.Items.Count > 0, delegate { RunTests(false); });
            AddContext(menu, Lang.T("Url Test"), selected > 0 || listProfiles.Items.Count > 0, delegate { RunTests(true); });
            e.Cancel = false;
        }

        private static void AddContext(ContextMenuStrip menu, string text, bool enabled, EventHandler onClick)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Enabled = enabled;
            item.Click += onClick;
            menu.Items.Add(item);
        }

        private void BuildTray()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem show = new ToolStripMenuItem(Lang.T("Show Window"));
            show.Click += delegate { ShowFromTray(); };
            _trayStart = new ToolStripMenuItem(Lang.T("Start"));
            _trayStart.Click += OnTrayStartClick;
            _trayStop = new ToolStripMenuItem(Lang.T("Stop"));
            _trayStop.Click += delegate { _core.Stop(); };
            _trayProxy = new ToolStripMenuItem(Lang.T("System Proxy"));
            _trayProxy.Click += delegate { SetSystemProxy(!ProxyShownOn); };
            ToolStripMenuItem exit = new ToolStripMenuItem(Lang.T("Exit"));
            exit.Click += delegate { ExitProgram(); };
            menu.Items.AddRange(new ToolStripItem[] { show, new ToolStripSeparator(), _trayStart, _trayStop, _trayProxy,
                new ToolStripSeparator(), exit });

            _tray.Icon = _iconIdle;
            _tray.Text = AppInfo.Name;
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += delegate { ShowFromTray(); };
            _tray.Visible = true;
        }

        private ToolStripMenuItem AddMenu(string text)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            menuMain.Items.Add(item);
            return item;
        }

        private static ToolStripMenuItem AddItem(ToolStripMenuItem parent, string text, EventHandler onClick)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            if (onClick != null)
            {
                item.Click += onClick;
            }
            parent.DropDownItems.Add(item);
            return item;
        }

        private void ShowFromTray()
        {
            Show();
            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }
            Activate();
        }

        private void ExitProgram()
        {
            _exiting = true;
            Close();
        }

        // System proxy as the user should see it: set by us and still in place.
        private bool ProxyShownOn
        {
            get { return _systemProxy.IsEnabled && _lastProxyStatus != SystemProxyStatus.ChangedOutside; }
        }

        private void UpdateUi()
        {
            if (_cleanedUp)
            {
                return;
            }
            CoreState state = _core.State;
            bool running = state == CoreState.Running;
            bool stopping = state == CoreState.Stopping;
            int selected = listProfiles.SelectedItems.Count;
            bool proxyOn = ProxyShownOn;

            _menuStart.Enabled = selected == 1 && !stopping;
            _menuStop.Enabled = running || state == CoreState.Connecting;
            _menuRestart.Enabled = running;
            _menuEdit.Enabled = selected == 1;
            _menuClone.Enabled = selected > 0;
            _menuMoveUp.Enabled = selected > 0 && textSearch.Text.Length == 0;
            _menuMoveDown.Enabled = selected > 0 && textSearch.Text.Length == 0;
            _menuMoveTo.Enabled = selected > 0 && _store.Groups.Count > 1;
            _menuCopyTo.Enabled = selected > 0;
            _menuDelete.Enabled = selected > 0;
            _menuShare.Enabled = selected > 0;
            _menuProxyOn.Enabled = running && !proxyOn;
            _menuProxyOff.Enabled = proxyOn;
            _trayStart.Enabled = !stopping && state != CoreState.Connecting && !running;
            _trayStop.Enabled = _menuStop.Enabled;
            _trayProxy.Enabled = running || proxyOn;
            _trayProxy.Checked = proxyOn;
            bool connecting = state == CoreState.Connecting;
            // while connecting the choice is remembered and applied when the connection is up
            checkSystemProxy.Checked = proxyOn || (connecting && _proxyWanted);
            checkSystemProxy.Enabled = running || proxyOn || connecting;
            bool busy = running || state == CoreState.Connecting;
            _toolConnect.Text = busy ? Lang.T("Disconnect") : Lang.T("Connect");
            _toolConnect.Enabled = busy ? !stopping : (selected == 1 && !stopping);
            _toolConnect.ToolTipText = busy ? Lang.T("Stop the connection (the system proxy is restored)")
                : selected == 1 ? Lang.T("Connect with the selected server (or double-click it)") : Lang.T("Select a server first");
            _toolUpdate.Enabled = _updatingGroups.Count == 0;

            string text;
            switch (state)
            {
                case CoreState.Connecting: text = Lang.F("Connecting: {0}", _activeName); break;
                case CoreState.Running: text = Lang.F("Running: {0}", _activeName); break;
                case CoreState.Stopping: text = Lang.T("Stopping"); break;
                case CoreState.Error: text = Lang.T("Error (see the log)"); break;
                default: text = Lang.T("Not Running"); break;
            }
            labelRunning.Text = text;
            string mode = !running ? "" : "   " + (proxyOn ? Lang.T("Mode: System Proxy") : Lang.T("Mode: local ports only"));
            labelInbound.Text = running
                ? "SOCKS5 " + _settings.ListenAddress + ":" + _settings.SocksPort + "   HTTP " + _settings.ListenAddress + ":"
                  + _settings.HttpPort + mode
                : "";
            if (!_testRunning)
            {
                labelSpeed.Text = GroupStatusText();
            }

            Icon icon = state == CoreState.Error ? _iconError : (running ? (proxyOn ? _iconProxy : _iconRunning) : _iconIdle);
            _tray.Icon = icon;
            Icon = icon;
            string tip = AppInfo.Name + " - " + text + (proxyOn ? " " + Lang.T("(system proxy)") : "");
            _tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;

            int active = _core.ActiveProfileId;
            foreach (ListViewItem item in listProfiles.Items)
            {
                bool isActive = (int)item.Tag == active;
                if (item.Font.Bold != isActive)
                {
                    item.Font = isActive ? _boldFont : listProfiles.Font;
                }
            }
        }

        // Status of the current group when it is a subscription.
        private string GroupStatusText()
        {
            ProfileGroup g = _store.FindGroup(_currentGroup);
            if (g == null || !g.IsSubscription)
            {
                return "";
            }
            if (_updatingGroups.ContainsKey(g.Id))
            {
                return Lang.T("Subscription: updating...");
            }
            if (g.LastError.Length > 0)
            {
                return g.LastUpdate.Length > 0 ? Lang.F("Subscription: last update FAILED (last success {0})", g.LastUpdate)
                    : Lang.T("Subscription: last update FAILED");
            }
            return (g.LastUpdate.Length > 0 ? Lang.F("Subscription updated {0}", g.LastUpdate) : Lang.T("Subscription: never updated"))
                   + (g.Info.Length > 0 ? " - " + g.Info : "") + (g.UseHwid ? " - " + Lang.T("HWID on") : "");
        }

        // ---- core ----

        private void OnStartClick(object sender, EventArgs e)
        {
            List<int> ids = GetSelectedIds();
            ProfileEntry p = ids.Count == 1 ? _store.Find(ids[0]) : null;
            if (p != null)
            {
                StartProfile(p, false);
            }
        }

        private void OnTrayStartClick(object sender, EventArgs e)
        {
            List<int> ids = GetSelectedIds();
            ProfileEntry p = ids.Count == 1 ? _store.Find(ids[0]) : _store.Find(_settings.LastProfileId);
            if (p == null)
            {
                ShowFromTray();
                MessageBox.Show(this, Lang.T("Select a profile in the list first."), AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            StartProfile(p, false);
        }

        private void OnRestartClick(object sender, EventArgs e)
        {
            ProfileEntry p = _store.Find(_core.ActiveProfileId);
            if (p != null)
            {
                StartProfile(p, true);
            }
        }

        // The one way to connect: Start, Enter, double click, tray and "remember last profile" all come here.
        // A second request for the profile that is already connecting or running does nothing;
        // a request for another profile switches safely (the old core is stopped first).
        private void StartProfile(ProfileEntry p, bool restart)
        {
            if (!File.Exists(AppPaths.CorePath))
            {
                ShowMessage(Lang.F("The core was not found:\r\n{0}\r\n\r\nPut xray.exe next to the program.", AppPaths.CorePath),
                    MessageBoxIcon.Warning);
                return;
            }
            if (_coreProblem != null)
            {
                ShowMessage(Lang.F("The core cannot run on this system:\r\n\r\n{0}", _coreProblem), MessageBoxIcon.Warning);
                return;
            }
            string invalid = ProfileValidator.Validate(p);
            if (invalid != null)
            {
                ShowMessage(Lang.F("This profile cannot be used: {0}\r\n\r\nOpen it with Edit to correct it.", invalid), MessageBoxIcon.Warning);
                return;
            }
            if (_core.Start(p, _settings, restart) == StartResult.AlreadyActive)
            {
                AppendLog(Lang.F(_core.State == CoreState.Running ? "\"{0}\" is already running." : "\"{0}\" is already connecting.", p.Name));
                return;
            }
            _activeName = p.Name;
        }

        private void OnProfileDoubleClick(object sender, EventArgs e)
        {
            OnStartClick(sender, e);
        }

        private void ShowMessage(string text, MessageBoxIcon icon)
        {
            if (Visible)
            {
                MessageBox.Show(this, text, AppInfo.Name, MessageBoxButtons.OK, icon);
            }
            else
            {
                MessageBox.Show(text, AppInfo.Name, MessageBoxButtons.OK, icon);
            }
        }

        private void OnCoreState(CoreState state, string message)
        {
            if (_cleanedUp)
            {
                return;
            }
            switch (state)
            {
                case CoreState.Connecting:
                    AppendLog(Lang.F("Starting profile \"{0}\"...", _activeName));
                    break;
                case CoreState.Running:
                    AppendLog(Lang.F("The core is running with profile \"{0}\". SOCKS5 port {1}, HTTP port {2}.", _activeName,
                        _settings.SocksPort, _settings.HttpPort));
                    AppendLog(Lang.T("Note: this confirms the local core only. Use Server - Current Group - Url Test to check the server."));
                    if (_settings.LastProfileId != _core.ActiveProfileId)
                    {
                        _settings.LastProfileId = _core.ActiveProfileId;
                        SaveSettings();
                    }
                    if (_systemProxy.IsEnabled || _proxyWanted)
                    {
                        // re-apply: the HTTP port may have changed
                        _proxyWanted = false;
                        EnableSystemProxy(false);
                    }
                    break;
                case CoreState.Stopping:
                    AppendLog(Lang.T("Stopping the core..."));
                    break;
                case CoreState.NotRunning:
                    AppendLog(Lang.T("The core is stopped."));
                    _proxyWanted = false;
                    RestoreSystemProxy();
                    break;
                case CoreState.Error:
                    AppendLog(Lang.F("ERROR: {0}", message.Replace("\r\n", " | ")));
                    _proxyWanted = false;
                    RestoreSystemProxy();
                    if (Visible)
                    {
                        MessageBox.Show(this, message, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    else
                    {
                        _tray.ShowBalloonTip(5000, AppInfo.Name, Lang.T("The connection stopped with an error. Open the window to see the log."),
                            ToolTipIcon.Error);
                    }
                    break;
            }
            UpdateUi();
        }

        // ---- system proxy ----

        private void OnSystemProxyToggle(object sender, EventArgs e)
        {
            if (_proxyWanted && _core.State == CoreState.Connecting)
            {
                _proxyWanted = false;
                AppendLog(Lang.T("The system proxy will not be switched on."));
                UpdateUi();
                return;
            }
            SetSystemProxy(!ProxyShownOn);
        }

        private void SetSystemProxy(bool enable)
        {
            if (enable)
            {
                if (_core.State == CoreState.Connecting)
                {
                    // switched on as soon as the connection is up (as "Remember last profile" does)
                    _proxyWanted = true;
                    AppendLog(Lang.T("The system proxy will be switched on when the connection is up."));
                    UpdateUi();
                    return;
                }
                if (_core.State != CoreState.Running)
                {
                    MessageBox.Show(this, Lang.T("Start a profile first: the system proxy points at the local HTTP port of the core."),
                        AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                EnableSystemProxy(true);
            }
            else
            {
                RestoreSystemProxy();
            }
            _settings.LastSystemProxy = _systemProxy.IsEnabled;
            _lastProxyStatus = _systemProxy.IsEnabled ? SystemProxyStatus.Applied : SystemProxyStatus.Off;
            SaveSettings();
            UpdateUi();
        }

        private void EnableSystemProxy(bool interactive)
        {
            try
            {
                _systemProxy.Enable("127.0.0.1:" + _settings.HttpPort, _settings.SystemProxyBypass);
                _lastProxyStatus = SystemProxyStatus.Applied;
                AppendLog(Lang.F("System proxy enabled: HTTP 127.0.0.1:{0}. The previous settings are saved and will be restored.", _settings.HttpPort));
            }
            catch (Exception ex)
            {
                AppendLog(Lang.F("System proxy was NOT enabled: {0}", ex.Message));
                if (interactive)
                {
                    MessageBox.Show(this, Lang.F("The system proxy could not be switched on; your proxy settings were not changed.\r\n\r\n{0}"
                        + "\r\n\r\nThe local ports keep working: programs can use HTTP 127.0.0.1:{1} or SOCKS5 127.0.0.1:{2} directly.",
                        ex.Message, _settings.HttpPort, _settings.SocksPort), AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void RestoreSystemProxy()
        {
            if (!_systemProxy.IsEnabled)
            {
                return;
            }
            try
            {
                string note = _systemProxy.Disable();
                if (note != null)
                {
                    AppendLog(note);
                }
            }
            catch (Exception ex)
            {
                AppendLog(Lang.F("System proxy settings were NOT restored: {0}", ex.Message.Replace("\r\n", " ")));
                MessageBox.Show(ex.Message, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ---- program menu ----

        private void OnAutoStartClick(object sender, EventArgs e)
        {
            try
            {
                AutoStart.Set(!AutoStart.IsEnabled);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, Lang.F("The autostart entry could not be changed: {0}", ex.Message), AppInfo.Name,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            _menuAutoStart.Checked = AutoStart.IsEnabled;
        }

        private void OnRememberClick(object sender, EventArgs e)
        {
            _settings.RememberLast = !_settings.RememberLast;
            _settings.LastSystemProxy = _systemProxy.IsEnabled;
            _menuRemember.Checked = _settings.RememberLast;
            SaveSettings();
        }

        private void OnAllowLanClick(object sender, EventArgs e)
        {
            _settings.AllowLan = !_settings.AllowLan;
            _menuAllowLan.Checked = _settings.AllowLan;
            SaveSettings();
            AppendLog(_settings.AllowLan ? Lang.T("The local ports will listen on all interfaces (0.0.0.0).") : Lang.T("The local ports will listen on 127.0.0.1 only."));
            RestartIfRunning();
        }

        private void RestartIfRunning()
        {
            if (_core.State == CoreState.Running)
            {
                OnRestartClick(null, EventArgs.Empty);
            }
        }

        private void SaveSettings()
        {
            try
            {
                _settings.CurrentGroupId = _currentGroup;
                _settingsStore.Save(_settings);
            }
            catch (Exception ex)
            {
                AppendLog(Lang.F("Settings were not saved: {0}", ex.Message));
            }
        }

        private void OnSettingsClick(object sender, EventArgs e)
        {
            using (SettingsForm form = new SettingsForm(_settings))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                AppSettings old = _settings;
                _settings = form.Settings;
                SaveSettings();
                AppendLog(Lang.T("Settings saved."));
                if (old.Language != _settings.Language)
                {
                    ShowMessage(Lang.T("The language changes when the program is started again."), MessageBoxIcon.Information);
                }
                // the connection is restarted only when something it uses has changed
                if (ConnectionSettingsChanged(old, _settings))
                {
                    RestartIfRunning();
                }
            }
        }

        private static bool ConnectionSettingsChanged(AppSettings a, AppSettings b)
        {
            return a.SocksPort != b.SocksPort || a.HttpPort != b.HttpPort || a.AllowLan != b.AllowLan || a.LogLevel != b.LogLevel
                   || a.DefaultOutbound != b.DefaultOutbound || a.DomainStrategy != b.DomainStrategy || a.BypassPrivate != b.BypassPrivate
                   || a.DirectDomains != b.DirectDomains || a.ProxyDomains != b.ProxyDomains || a.BlockDomains != b.BlockDomains
                   || a.DirectIps != b.DirectIps || a.ProxyIps != b.ProxyIps || a.BlockIps != b.BlockIps
                   || a.RemoteDns != b.RemoteDns || a.DirectDns != b.DirectDns || a.SystemProxyBypass != b.SystemProxyBypass;
        }

        private void OnGroupsClick(object sender, EventArgs e)
        {
            using (GroupsForm form = new GroupsForm(_store))
            {
                form.ShowDialog(this);
            }
            if (_store.FindGroup(_currentGroup) == null)
            {
                _currentGroup = ProfileGroup.DefaultId;
            }
            if (_core.ActiveProfileId >= 0 && _store.Find(_core.ActiveProfileId) == null)
            {
                _core.Stop();
            }
            RebuildGroupTabs();
        }

        private void OnOpenConfigFolderClick(object sender, EventArgs e)
        {
            try
            {
                Process.Start(AppPaths.EnsureConfigDir());
            }
            catch (Exception ex)
            {
                AppendLog(Lang.F("Cannot open config folder: {0}", ex.Message));
            }
        }

        // ---- groups and list ----

        private void RebuildGroupTabs()
        {
            _rebuildingTabs = true;
            try
            {
                listProfiles.Parent = null;
                tabGroups.TabPages.Clear();
                foreach (ProfileGroup g in _store.Groups)
                {
                    TabPage page = new TabPage(TabTitle(g));
                    page.Tag = g.Id;
                    page.UseVisualStyleBackColor = true;
                    tabGroups.TabPages.Add(page);
                    if (g.Id == _currentGroup)
                    {
                        tabGroups.SelectedTab = page;
                    }
                }
                listProfiles.Parent = tabGroups.SelectedTab;
            }
            finally
            {
                _rebuildingTabs = false;
            }
            RefreshProfileList();
        }

        private string TabTitle(ProfileGroup g)
        {
            if (!g.IsSubscription)
            {
                // the default group keeps the name it was saved with; only its standard name is translated
                return g.Id == ProfileGroup.DefaultId && g.Name == "Default" ? Lang.T("Default") : g.Name;
            }
            if (_updatingGroups.ContainsKey(g.Id))
            {
                return g.Name + " [sub ...]";
            }
            return g.Name + (g.LastError.Length > 0 ? " [sub !]" : " [sub]");
        }

        private void RefreshTabTitles()
        {
            foreach (TabPage page in tabGroups.TabPages)
            {
                ProfileGroup g = _store.FindGroup((int)page.Tag);
                if (g != null && page.Text != TabTitle(g))
                {
                    page.Text = TabTitle(g);
                }
            }
        }

        private void OnGroupTabChanged(object sender, EventArgs e)
        {
            if (_rebuildingTabs || tabGroups.SelectedTab == null)
            {
                return;
            }
            _currentGroup = (int)tabGroups.SelectedTab.Tag;
            listProfiles.Parent = tabGroups.SelectedTab;
            RefreshProfileList();
        }

        // A profile in this table is stored data; only the bold one is the profile the core runs with.
        private void RefreshProfileList()
        {
            List<int> selected = GetSelectedIds();
            string filter = textSearch.Text.Trim().ToLowerInvariant();
            listProfiles.BeginUpdate();
            try
            {
                listProfiles.Items.Clear();
                foreach (ProfileEntry p in _store.ProfilesOf(_currentGroup))
                {
                    if (filter.Length > 0 && !Matches(p, filter))
                    {
                        continue;
                    }
                    ListViewItem item = new ListViewItem(p.DisplayType);
                    item.SubItems.Add(p.DisplayAddress);
                    item.SubItems.Add(p.Name);
                    item.SubItems.Add(p.DisplayLatency);
                    item.Tag = p.Id;
                    item.Selected = selected.Contains(p.Id);
                    listProfiles.Items.Add(item);
                }
            }
            finally
            {
                listProfiles.EndUpdate();
            }
            UpdateUi();
        }

        // Search over the visible fields and the transport settings; credentials are not searched.
        private static bool Matches(ProfileEntry p, string filter)
        {
            string[] fields = new string[] { p.Name, p.DisplayAddress, p.DisplayType, p.Sni, p.Host, p.Path, p.Network, p.Security };
            for (int i = 0; i < fields.Length; i++)
            {
                if (fields[i].ToLowerInvariant().IndexOf(filter) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private List<int> GetSelectedIds()
        {
            List<int> ids = new List<int>();
            foreach (ListViewItem item in listProfiles.SelectedItems)
            {
                ids.Add((int)item.Tag);
            }
            return ids;
        }

        private List<ProfileEntry> GetSelectedProfiles()
        {
            List<ProfileEntry> result = new List<ProfileEntry>();
            foreach (int id in GetSelectedIds())
            {
                ProfileEntry p = _store.Find(id);
                if (p != null)
                {
                    result.Add(p);
                }
            }
            return result;
        }

        // The selection, or the whole current group when nothing is selected.
        private List<ProfileEntry> GetSelectedOrGroup()
        {
            List<ProfileEntry> result = GetSelectedProfiles();
            return result.Count > 0 ? result : _store.ProfilesOf(_currentGroup);
        }

        private void OnProfilesKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
            {
                OnDeleteClick(sender, e);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Enter)
            {
                OnStartClick(sender, e);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.F2)
            {
                OnEditClick(sender, e);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.A && e.Control)
            {
                OnSelectAllClick(sender, e);
                e.Handled = true;
            }
        }

        private void OnSelectAllClick(object sender, EventArgs e)
        {
            listProfiles.BeginUpdate();
            foreach (ListViewItem item in listProfiles.Items)
            {
                item.Selected = true;
            }
            listProfiles.EndUpdate();
            listProfiles.Focus();
        }

        // ---- profile operations ----

        // Runs a storage change and reports a failure; the store rolls itself back.
        private bool Commit(MethodInvoker change)
        {
            bool ok = true;
            try
            {
                change();
            }
            catch (Exception ex)
            {
                ok = false;
                AppendLog(Lang.T("The change was not saved."));
                MessageBox.Show(this, Lang.T("The change was not saved.") + "\r\n\r\n" + ex.Message, AppInfo.Name,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            RefreshProfileList();
            return ok;
        }

        private void NewProfile(string type)
        {
            using (ProfileEditForm form = new ProfileEditForm(ProfileEntry.Create(type), true))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                List<ProfileEntry> items = new List<ProfileEntry>();
                items.Add(form.Profile);
                if (Commit(delegate { _store.Add(items, _currentGroup); }))
                {
                    AppendLog(Lang.T("Profile added."));
                }
            }
        }

        private void OnEditClick(object sender, EventArgs e)
        {
            List<int> ids = GetSelectedIds();
            ProfileEntry current = ids.Count == 1 ? _store.Find(ids[0]) : null;
            if (current == null)
            {
                return;
            }
            using (ProfileEditForm form = new ProfileEditForm(current, false))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                ProfileEntry edited = form.Profile;
                if (Commit(delegate { _store.Update(edited); }))
                {
                    AppendLog(edited.Id == _core.ActiveProfileId
                        ? Lang.T("Profile updated. It is running with the old values; use Program - Restart Proxy to apply the changes.")
                        : Lang.T("Profile updated."));
                }
            }
        }

        private void OnCloneClick(object sender, EventArgs e)
        {
            List<ProfileEntry> copies = new List<ProfileEntry>();
            foreach (ProfileEntry p in GetSelectedProfiles())
            {
                ProfileEntry copy = p.Clone();
                copy.Name = p.Name + " " + Lang.T("(copy)");
                copy.Latency = 0;
                copy.LatencyNote = "";
                copies.Add(copy);
            }
            if (copies.Count > 0 && Commit(delegate { _store.Add(copies, _currentGroup); }))
            {
                AppendLog(Lang.F("Profiles cloned: {0}", copies.Count));
            }
        }

        private void MoveSelected(int direction)
        {
            List<int> ids = GetSelectedIds();
            if (ids.Count > 0)
            {
                Commit(delegate { _store.Move(ids, direction); });
            }
        }

        private void FillGroupMenu(ToolStripMenuItem menu, bool move)
        {
            menu.DropDownItems.Clear();
            foreach (ProfileGroup g in _store.Groups)
            {
                if (move && g.Id == _currentGroup)
                {
                    continue;
                }
                int target = g.Id;
                AddItem(menu, g.Name + (g.Id == _currentGroup ? " " + Lang.T("(this group)") : ""), delegate
                {
                    List<int> ids = GetSelectedIds();
                    if (ids.Count == 0)
                    {
                        return;
                    }
                    if (move)
                    {
                        if (Commit(delegate { _store.MoveToGroup(ids, target); }))
                        {
                            AppendLog(Lang.F("Profiles moved: {0}", ids.Count));
                        }
                    }
                    else
                    {
                        int copied = 0;
                        if (Commit(delegate { copied = _store.CopyToGroup(ids, target, target == _currentGroup ? " " + Lang.T("(copy)") : ""); }))
                        {
                            AppendLog(Lang.F("Profiles copied: {0}", copied));
                        }
                    }
                });
            }
        }

        private void OnDeleteClick(object sender, EventArgs e)
        {
            List<int> ids = GetSelectedIds();
            if (ids.Count == 0)
            {
                return;
            }
            string question;
            if (ids.Count == 1)
            {
                ProfileEntry p = _store.Find(ids[0]);
                question = Lang.F("Delete profile \"{0}\"?", p == null ? "" : p.Name);
            }
            else
            {
                question = Lang.F("Delete {0} selected profiles?", ids.Count);
            }
            bool active = ids.Contains(_core.ActiveProfileId);
            if (active)
            {
                question += "\r\n\r\n" + Lang.T("The running profile is among them; the connection will be stopped.");
            }
            if (MessageBox.Show(this, question, AppInfo.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                return;
            }
            if (Commit(delegate { _store.Remove(ids); }))
            {
                AppendLog(Lang.F("Profiles deleted: {0}", ids.Count));
                if (active)
                {
                    _core.Stop();
                }
            }
        }

        // 0 = name, 1 = address, 2 = test result (fastest first, failed and untested last)
        private void SortGroup(int by)
        {
            Commit(delegate
            {
                _store.Sort(_currentGroup, delegate(ProfileEntry a, ProfileEntry b)
                {
                    if (by == 0)
                    {
                        return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
                    }
                    if (by == 1)
                    {
                        return string.Compare(a.DisplayAddress, b.DisplayAddress, StringComparison.OrdinalIgnoreCase);
                    }
                    int la = a.Latency > 0 ? a.Latency : (a.Latency < 0 ? int.MaxValue - 1 : int.MaxValue);
                    int lb = b.Latency > 0 ? b.Latency : (b.Latency < 0 ? int.MaxValue - 1 : int.MaxValue);
                    return la.CompareTo(lb);
                });
            });
        }

        private void OnRemoveDuplicatesClick(object sender, EventArgs e)
        {
            Dictionary<string, bool> seen = new Dictionary<string, bool>();
            List<int> duplicates = new List<int>();
            foreach (ProfileEntry p in _store.ProfilesOf(_currentGroup))
            {
                string key = ProfileKey.Of(p);
                if (seen.ContainsKey(key))
                {
                    if (p.Id != _core.ActiveProfileId)
                    {
                        duplicates.Add(p.Id);
                    }
                }
                else
                {
                    seen.Add(key, true);
                }
            }
            if (duplicates.Count == 0)
            {
                MessageBox.Show(this, Lang.T("No duplicates in this group."), AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show(this, Lang.F("Delete {0} duplicate profile(s)? The first of each kind is kept.", duplicates.Count),
                    AppInfo.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes
                && Commit(delegate { _store.Remove(duplicates); }))
            {
                AppendLog(Lang.F("Duplicates deleted: {0}", duplicates.Count));
            }
        }

        // ---- import ----

        // ---- Add: links, subscriptions, happ://add, files ----

        // The download settings of the moment: route, running core, HWID (made and saved on first use).
        private SubscriptionClient CreateSubscriptionClient()
        {
            SubscriptionClient client = new SubscriptionClient();
            client.Tools.HelperPath = AppPaths.HelperPath;
            client.Tools.CurlPath = AppPaths.CurlPath;
            client.Tools.CaPath = AppPaths.CaBundlePath;
            client.Tools.TempDir = AppPaths.RunDir;
            client.Tools.UserAgent = "NekoRayNT5/" + AppInfo.Version;
            client.Route = _settings.SubscriptionRoute;
            client.ProxyPort = _core.State == CoreState.Running ? _settings.HttpPort : 0;
            bool created;
            client.Hwid = Hwid.Effective(_settings, out created);
            if (created)
            {
                SaveSettings();
            }
            return client;
        }

        // Text on the clipboard that looks like something to add (a link, an address, a happ:// link).
        private static string ClipboardSuggestion()
        {
            try
            {
                if (!Clipboard.ContainsText())
                {
                    return "";
                }
                string text = Clipboard.GetText();
                return text != null && text.Length < 1024 * 1024 && AddSource.IsCleanSource(text) ? text.Trim() : "";
            }
            catch (ExternalException)
            {
                return "";
            }
        }

        private void OnAddClick(object sender, EventArgs e)
        {
            ShowAddDialog(ClipboardSuggestion());
        }

        // True when something was added.
        private bool ShowAddDialog(string initialText)
        {
            using (AddForm form = new AddForm(CreateSubscriptionClient(), _store.Profiles, initialText))
            {
                if (form.ShowDialog(this) != DialogResult.OK || form.Result == null)
                {
                    return false;
                }
                return ApplyAdd(form);
            }
        }

        private bool ApplyAdd(AddForm form)
        {
            AddPreview preview = form.Result;
            if (form.AsSubscription)
            {
                ProfileGroup existing = FindSubscription(preview.Input.Url);
                if (existing != null)
                {
                    // the same address twice would give two groups that fight over the same servers
                    if (MessageBox.Show(this, Lang.F("This subscription is already in the list as \"{0}\".\r\n\r\n"
                            + "Update that group with the servers just checked?", existing.Name), AppInfo.Name, MessageBoxButtons.YesNo,
                            MessageBoxIcon.Question) != DialogResult.Yes)
                    {
                        return false;
                    }
                    if (form.UseHwid && !existing.UseHwid && !Commit(delegate { _store.SetUseHwid(existing.Id, true); }))
                    {
                        return false;
                    }
                    _currentGroup = existing.Id;
                    RebuildGroupTabs();
                    ApplySubscription(existing.Id, preview.Profiles, preview.Problems, Lang.F("downloaded {0}", preview.Via), false, preview.ProviderInfo);
                    return true;
                }
                ProfileGroup g = null;
                if (!Commit(delegate { g = _store.AddGroup(UniqueGroupName(form.GroupName), preview.Input.Url, form.UseHwid); }))
                {
                    return false;
                }
                AppendLog(Lang.F(form.UseHwid ? "Subscription \"{0}\" added (with the Happ-compatible HWID)." : "Subscription \"{0}\" added.", g.Name));
                _currentGroup = g.Id;
                RebuildGroupTabs();
                // the checked content is used as it is: no second download
                ApplySubscription(g.Id, preview.Profiles, preview.Problems, Lang.F("downloaded {0}", preview.Via), false, preview.ProviderInfo);
                return true;
            }
            // one-time import: into the current group, or the default group when the current one is a subscription
            ProfileGroup current = _store.FindGroup(_currentGroup);
            int groupId = current != null && !current.IsSubscription ? current.Id : ProfileGroup.DefaultId;
            List<ProfileEntry> items = form.SkipKnown ? AddSource.WithoutKnown(preview.Profiles, _store.Profiles) : preview.Profiles;
            if (items.Count == 0)
            {
                ShowMessage(Lang.T("All servers of this source are already in the list; nothing was added."), MessageBoxIcon.Information);
                return false;
            }
            if (!Commit(delegate { _store.Add(items, groupId); }))
            {
                return false;
            }
            AppendLog(preview.Problems.Count > 0 ? Lang.F("Servers added: {0}; entries not usable: {1}.", items.Count, preview.Problems.Count)
                : Lang.F("Servers added: {0}.", items.Count));
            if (groupId != _currentGroup)
            {
                _currentGroup = groupId;
                RebuildGroupTabs();
            }
            else
            {
                RefreshProfileList();
            }
            UpdateUi();
            return true;
        }

        private ProfileGroup FindSubscription(string url)
        {
            foreach (ProfileGroup g in _store.Groups)
            {
                if (g.IsSubscription && g.Url == url)
                {
                    return g;
                }
            }
            return null;
        }

        private string UniqueGroupName(string name)
        {
            string baseName = name.Length > 0 ? name : Lang.T("Subscription");
            string candidate = baseName;
            for (int n = 2; ; n++)
            {
                bool taken = false;
                foreach (ProfileGroup g in _store.Groups)
                {
                    if (string.Equals(g.Name, candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        taken = true;
                        break;
                    }
                }
                if (!taken)
                {
                    return candidate;
                }
                candidate = baseName + " (" + n + ")";
            }
        }

        // ---- toolbar, first run, diagnostics ----

        // The main actions in one row, text only (no images: plain on Windows XP and 2000).
        private void BuildToolbar()
        {
            _toolbar = new ToolStrip();
            _toolbar.GripStyle = ToolStripGripStyle.Hidden;
            _toolbar.Dock = DockStyle.Top;
            ToolStripButton add = new ToolStripButton(Lang.T("Add..."));
            add.ToolTipText = Lang.T("Add servers or a subscription (links, https:// address, happ://add/ link or file) - Ctrl+N");
            add.Click += OnAddClick;
            _toolConnect = new ToolStripButton(Lang.T("Connect"));
            _toolConnect.Font = new Font(_toolbar.Font, FontStyle.Bold);
            _toolConnect.Click += OnToolConnectClick;
            _toolUpdate = new ToolStripButton(Lang.T("Update subscriptions"));
            _toolUpdate.ToolTipText = Lang.T("Download all subscriptions again (old servers are kept when a download fails)");
            _toolUpdate.Click += OnUpdateAllSubscriptionsClick;
            ToolStripButton settings = new ToolStripButton(Lang.T("Settings..."));
            settings.Click += OnSettingsClick;
            ToolStripButton diagnostics = new ToolStripButton(Lang.T("Diagnostics..."));
            diagnostics.ToolTipText = Lang.T("Check the program files, certificates and ports");
            diagnostics.Click += delegate { ShowDiagnostics(null); };
            _toolbar.Items.AddRange(new ToolStripItem[] { add, new ToolStripSeparator(), _toolConnect, new ToolStripSeparator(),
                _toolUpdate, new ToolStripSeparator(), settings, diagnostics });
            Controls.Add(_toolbar);
            // docked controls are laid out from the last added: keep the order menu, toolbar, top panel
            _toolbar.SendToBack();
            menuMain.SendToBack();
        }

        private void OnToolConnectClick(object sender, EventArgs e)
        {
            CoreState state = _core.State;
            if (state == CoreState.Running || state == CoreState.Connecting)
            {
                _core.Stop();
                return;
            }
            OnStartClick(sender, e);
        }

        // On the first start without servers: a short assistant (check, add, test, connect).
        private void MaybeShowFirstRun()
        {
            if (_settings.FirstRunDone)
            {
                return;
            }
            _settings.FirstRunDone = true;
            SaveSettings();
            if (_store.Profiles.Count > 0)
            {
                return;
            }
            using (FirstRunForm form = new FirstRunForm(BuildDependencyReport, delegate { return ShowAddDialog(ClipboardSuggestion()); }))
            {
                form.ShowDialog(this);
                if (form.ConnectRequested && listProfiles.Items.Count > 0)
                {
                    listProfiles.SelectedItems.Clear();
                    listProfiles.Items[0].Selected = true;
                    OnStartClick(this, EventArgs.Empty);
                }
            }
        }

        private DependencyReport BuildDependencyReport()
        {
            return Dependencies.Check(_coreProblem, _settings);
        }

        // intro: a sentence above the list, or null.
        private void ShowDiagnostics(string intro)
        {
            using (DiagnosticsForm form = new DiagnosticsForm(BuildDependencyReport, intro, BuildDiagnosticText()))
            {
                form.ShowDialog(this);
            }
            UpdateUi();
        }

        // Links one per line, or the same list in Base64. Valid links are imported; for the others
        // the reason is shown without repeating the link itself.
        private void ImportText(string text, int groupId)
        {
            LinkProblems errors = new LinkProblems();
            List<ProfileEntry> imported;
            try
            {
                imported = Subscription.Decode(text, errors);
            }
            catch (FormatException ex)
            {
                MessageBox.Show(this, Lang.T("Nothing was imported.") + "\r\n\r\n" + ex.Message, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (imported.Count > 0 && !Commit(delegate { _store.Add(imported, groupId); }))
            {
                return;
            }
            if (imported.Count > 0)
            {
                AppendLog(Lang.F("Profiles imported: {0}", imported.Count));
            }
            ReportRejected(imported.Count, errors);
        }

        private void ReportRejected(int importedCount, LinkProblems errors)
        {
            if (errors.Count == 0)
            {
                return;
            }
            AppendLog(Lang.F("Links rejected: {0}", errors.Count));
            StringBuilder message = new StringBuilder();
            message.Append(importedCount > 0
                ? Lang.F("Imported: {0}. Not imported: {1}.", importedCount, errors.Count)
                : Lang.T("Nothing was imported."));
            message.Append("\r\n");
            foreach (string line in errors.Summarize(MaxReportedErrors))
            {
                message.Append("\r\n").Append(line);
            }
            MessageBox.Show(this, message.ToString(), AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // ---- export ----

        // Builds share links of the selected profiles; path == null copies them to the clipboard.
        private void ExportLinks(string path)
        {
            StringBuilder links = new StringBuilder();
            int exported = 0;
            int unsupported = 0;
            foreach (ProfileEntry p in GetSelectedProfiles())
            {
                try
                {
                    links.Append(ShareLinks.Export(p)).Append("\r\n");
                    exported++;
                }
                catch (NotSupportedException)
                {
                    unsupported++;
                }
            }
            if (exported > 0)
            {
                try
                {
                    if (path == null)
                    {
                        Clipboard.SetText(links.ToString());
                    }
                    else
                    {
                        File.WriteAllText(path, links.ToString(), new UTF8Encoding(false));
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, Lang.F("The links could not be exported: {0}", ex.Message), AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                AppendLog(Lang.F(path == null ? "Links exported: {0} (clipboard)" : "Links exported: {0} (file)", exported));
            }
            if (unsupported > 0)
            {
                MessageBox.Show(this, Lang.F("{0} profile(s) were skipped: HTTP proxy profiles have no share link format.", unsupported),
                    AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void OnExportLinksFileClick(object sender, EventArgs e)
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = Lang.T("Export links (the file will contain the credentials of the profiles)");
                dialog.Filter = Lang.T("Text files (*.txt)|*.txt");
                dialog.FileName = "profiles.txt";
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    ExportLinks(dialog.FileName);
                }
            }
        }

        private void OnExportConfigClick(object sender, EventArgs e)
        {
            List<ProfileEntry> selected = GetSelectedProfiles();
            if (selected.Count != 1)
            {
                MessageBox.Show(this, Lang.T("Select exactly one profile."), AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = Lang.T("Export Xray configuration (the file will contain the credentials of the profile)");
                dialog.Filter = Lang.T("JSON files (*.json)|*.json");
                dialog.FileName = "config.json";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                try
                {
                    File.WriteAllText(dialog.FileName, XrayConfigBuilder.Build(selected[0], _settings), new UTF8Encoding(false));
                    AppendLog(Lang.T("Xray configuration exported to a file."));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, Lang.F("The configuration could not be exported: {0}", ex.Message), AppInfo.Name,
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        // ---- latency tests ----

        private void RunTests(bool urlTest)
        {
            if (_testRunning)
            {
                MessageBox.Show(this, Lang.T("A test is already running."), AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (urlTest && _coreProblem != null)
            {
                MessageBox.Show(this, Lang.F("The URL test needs the core, which cannot run on this system:\r\n\r\n{0}", _coreProblem),
                    AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (urlTest && !File.Exists(AppPaths.CorePath))
            {
                MessageBox.Show(this, Lang.T("The URL test needs xray.exe next to the program."), AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            List<ProfileEntry> targets = new List<ProfileEntry>();
            foreach (ProfileEntry p in GetSelectedOrGroup())
            {
                targets.Add(p.Clone());
            }
            if (targets.Count == 0)
            {
                return;
            }
            string url = _settings.TestUrl;
            int timeout = _settings.TestTimeoutSeconds * 1000;
            string testRunDir = Path.Combine(AppPaths.RunDir, "test");
            _testRunning = true;
            _cancelWork = false;
            AppendLog(Lang.F(urlTest ? "URL test started for {0} profile(s)." : "TCP ping started for {0} profile(s).", targets.Count));
            RunAsync(delegate
            {
                int done = 0;
                try
                {
                    foreach (ProfileEntry target in targets)
                    {
                        if (_cancelWork)
                        {
                            break;
                        }
                        string note;
                        int ms = urlTest
                            ? LatencyTester.UrlTest(AppPaths.CorePath, testRunDir, target, url, timeout, out note)
                            : LatencyTester.TcpPing(target, timeout, out note);
                        int id = target.Id;
                        string label = urlTest ? note : (ms > 0 ? "tcp" : note);
                        done++;
                        int progress = done;
                        Post(delegate { ApplyLatency(id, ms, label, progress, targets.Count); });
                    }
                }
                finally
                {
                    // also after an unexpected error: otherwise every later test would be refused
                    Post(delegate
                    {
                        _testRunning = false;
                        labelSpeed.Text = "";
                        _store.SaveTestResults();
                        AppendLog(Lang.F("Test finished: {0} of {1} profile(s).", done, targets.Count));
                        UpdateUi();
                    });
                }
            });
        }

        private void ApplyLatency(int id, int ms, string note, int done, int total)
        {
            labelSpeed.Text = Lang.F("Testing {0}/{1}", done, total);
            if (_store.Find(id) == null)
            {
                return;
            }
            _store.SetTestResult(id, ms, note, DateTime.Now.ToString("MM-dd HH:mm"));
            ProfileEntry p = _store.Find(id);
            foreach (ListViewItem item in listProfiles.Items)
            {
                if ((int)item.Tag == id)
                {
                    item.SubItems[3].Text = p.DisplayLatency;
                }
            }
        }

        private void OnClearTestsClick(object sender, EventArgs e)
        {
            List<int> ids = new List<int>();
            foreach (ProfileEntry p in GetSelectedOrGroup())
            {
                ids.Add(p.Id);
            }
            _store.ClearTestResults(ids);
            RefreshProfileList();
        }

        // ---- subscriptions ----

        private void OnToggleGroupHwidClick(object sender, EventArgs e)
        {
            ProfileGroup g = _store.FindGroup(_currentGroup);
            if (g != null && g.IsSubscription)
            {
                SetGroupHwid(g.Id, !g.UseHwid);
            }
        }

        private void OnUpdateSubscriptionClick(object sender, EventArgs e)
        {
            ProfileGroup g = _store.FindGroup(_currentGroup);
            if (g == null || !g.IsSubscription)
            {
                MessageBox.Show(this, Lang.T("This group has no subscription address.\r\n\r\nUse Add with the subscription address, "
                    + "or set the address of this group in Preferences - Groups."),
                    AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            StartSubscriptionUpdate(g.Id, true);
        }

        private void OnUpdateAllSubscriptionsClick(object sender, EventArgs e)
        {
            int count = 0;
            foreach (ProfileGroup g in _store.Groups)
            {
                if (g.IsSubscription)
                {
                    StartSubscriptionUpdate(g.Id, false);
                    count++;
                }
            }
            if (count == 0)
            {
                MessageBox.Show(this, Lang.T("There are no subscriptions yet. Use Add with the subscription address."),
                    AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        // Downloads in the background; a second update of the same subscription is refused while one runs.
        // A failed, empty or unusable answer leaves the profiles of the group as they are.
        private void StartSubscriptionUpdate(int groupId, bool interactive)
        {
            ProfileGroup g = _store.FindGroup(groupId);
            if (g == null || !g.IsSubscription)
            {
                return;
            }
            if (_updatingGroups.ContainsKey(groupId))
            {
                AppendLog(Lang.F("Subscription \"{0}\" is already being updated.", g.Name));
                return;
            }
            string url = g.Url;
            string name = g.Name;
            bool useHwid = g.UseHwid;
            SubscriptionClient client = CreateSubscriptionClient();
            _updatingGroups[groupId] = true;
            RefreshTabTitles();
            UpdateUi();
            AppendLog(Lang.F(useHwid ? "Updating subscription \"{0}\" {1}{2}, with the HWID..." : "Updating subscription \"{0}\" {1}{2}...",
                name, SubscriptionClient.DescribeRoute(client.Route),
                client.ProxyPort > 0 && client.Route != AppSettings.RouteDirect ? " " + Lang.T("(through the active connection)") : ""));
            RunAsync(delegate
            {
                LinkProblems errors = new LinkProblems();
                List<ProfileEntry> profiles = null;
                string failure = null;
                string via = "";
                string info = null;
                try
                {
                    FetchResult result = client.Fetch(url, useHwid);
                    via = result.Via;
                    profiles = Subscription.Decode(result.Text, errors);
                    if (profiles.Count == 0)
                    {
                        failure = errors.Count > 0 ? Lang.F("The subscription contains no supported profiles ({0} entries could not be used).", errors.Count)
                            : Lang.T("The subscription contains no supported profiles.");
                    }
                    info = AddSource.DescribeUserInfo(result.Header("subscription-userinfo"));
                }
                catch (FetchException ex)
                {
                    failure = SubscriptionClient.SafeMessage(ex, url);
                    if (!useHwid && ex.ServerAnswered && (ex.Meta.ContainsKey("status") && (ex.Meta["status"] == "401" || ex.Meta["status"] == "403")))
                    {
                        failure += "\r\n" + Lang.T("If the provider limits devices (Happ-compatible panels), switch on Server - Subscription - "
                                   + "Use Happ-compatible HWID for this subscription.");
                    }
                }
                catch (Exception ex)
                {
                    failure = SubscriptionClient.SafeMessage(ex, url);
                }
                Post(delegate
                {
                    _updatingGroups.Remove(groupId);
                    FinishSubscriptionUpdate(groupId, profiles, errors, failure, interactive, via, info);
                });
            });
        }

        private void FinishSubscriptionUpdate(int groupId, List<ProfileEntry> profiles, LinkProblems errors, string failure,
            bool interactive, string via, string info)
        {
            ProfileGroup g = _store.FindGroup(groupId);
            if (g == null)
            {
                return;
            }
            if (failure != null)
            {
                _store.SetSubscriptionError(groupId, FirstLine(failure));
                AppendLog(Lang.F("Subscription \"{0}\" was NOT updated; its profiles are kept. {1}", g.Name, failure.Replace("\r\n", " ")));
                RefreshTabTitles();
                UpdateUi();
                string text = Lang.F("The subscription \"{0}\" was not updated; the existing profiles are kept.\r\n\r\n{1}", g.Name, failure);
                if (interactive)
                {
                    ShowMessage(text, MessageBoxIcon.Warning);
                }
                else
                {
                    _tray.ShowBalloonTip(5000, AppInfo.Name, Lang.F("Subscription \"{0}\" was not updated. See the log.", g.Name), ToolTipIcon.Warning);
                }
                return;
            }
            ApplySubscription(groupId, profiles, errors, Lang.F("downloaded {0}", via), interactive, info);
        }

        private void ApplySubscription(int groupId, List<ProfileEntry> profiles, LinkProblems errors, string source, bool interactive)
        {
            ApplySubscription(groupId, profiles, errors, source, interactive, null);
        }

        // info: what the provider reported (traffic, expiry); null keeps the previous note.
        private void ApplySubscription(int groupId, List<ProfileEntry> profiles, LinkProblems errors, string source, bool interactive,
            string info)
        {
            ProfileGroup g = _store.FindGroup(groupId);
            string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            MergeResult result = null;
            if (Commit(delegate { result = _store.MergeSubscription(groupId, profiles, stamp, info); }))
            {
                AppendLog(errors.Count > 0
                    ? Lang.F("Subscription \"{0}\" updated ({1}): {2}; {3} entr(ies) not supported.", g.Name, source, result, errors.Count)
                    : Lang.F("Subscription \"{0}\" updated ({1}): {2}.", g.Name, source, result));
                if (result.RemovedIds.Contains(_core.ActiveProfileId))
                {
                    AppendLog(Lang.T("The running profile is no longer in the subscription. It keeps running until you stop it or start another one."));
                }
                if (interactive && errors.Count > 0)
                {
                    ReportRejected(profiles.Count, errors);
                }
            }
            RefreshTabTitles();
            UpdateUi();
        }

        private void OnImportSubscriptionFileClick(object sender, EventArgs e)
        {
            ProfileGroup g = _store.FindGroup(_currentGroup);
            if (g == null || !g.IsSubscription)
            {
                MessageBox.Show(this, Lang.T("Open the tab of the subscription first.\r\n\r\nTo add links from a file use Add - Open file."),
                    AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = Lang.F("Import the content of subscription \"{0}\" from a file", g.Name);
                dialog.Filter = Lang.T("Text files (*.txt)|*.txt|All files (*.*)|*.*");
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                LinkProblems errors = new LinkProblems();
                List<ProfileEntry> profiles;
                try
                {
                    if (new FileInfo(dialog.FileName).Length > Subscription.MaxBytes)
                    {
                        throw new IOException(Lang.T("The file is larger than 5 MB."));
                    }
                    profiles = Subscription.Decode(Subscription.CheckContent(File.ReadAllBytes(dialog.FileName)), errors);
                    if (profiles.Count == 0)
                    {
                        throw new IOException(Lang.T("The file contains no supported profiles."));
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, Lang.T("Nothing was imported; the existing profiles are kept.") + "\r\n\r\n" + ex.Message, AppInfo.Name,
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                ApplySubscription(g.Id, profiles, errors, Lang.T("from a file"), true);
            }
        }

        private static string FirstLine(string text)
        {
            int end = text.IndexOf('\r');
            return end < 0 ? text : text.Substring(0, end);
        }

        // ---- backup and diagnostics ----

        private void OnExportBackupClick(object sender, EventArgs e)
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = Lang.T("Export all profiles (the file will contain the credentials of the profiles)");
                dialog.Filter = Lang.T("Profile backup (*.xml)|*.xml");
                dialog.FileName = "nekoray-nt5-profiles-" + DateTime.Now.ToString("yyyyMMdd") + ".xml";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                try
                {
                    _store.ExportTo(dialog.FileName);
                    AppendLog(Lang.F("Profile backup exported: {0} profile(s).", _store.Profiles.Count));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, Lang.F("The backup could not be written: {0}", ex.Message), AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void OnImportBackupClick(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = Lang.T("Import a profile backup (profiles are added; nothing is replaced)");
                dialog.Filter = Lang.T("Profile backup (*.xml)|*.xml|All files (*.*)|*.*");
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                int profiles = 0;
                int groups = 0;
                string warning = null;
                if (Commit(delegate { profiles = _store.ImportFrom(dialog.FileName, out groups, out warning); }))
                {
                    AppendLog(Lang.F("Profile backup imported: {0} profile(s), {1} new group(s).", profiles, groups));
                    RebuildGroupTabs();
                    if (warning != null)
                    {
                        MessageBox.Show(this, Lang.T("Imported with a warning:") + "\r\n\r\n" + warning, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            }
        }

        private void OnCopyDiagnosticsClick(object sender, EventArgs e)
        {
            try
            {
                Clipboard.SetText(BuildDiagnosticText());
                AppendLog(Lang.T("Diagnostic info copied to the clipboard (no addresses, ids or keys)."));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, Lang.F("The clipboard is busy: {0}", ex.Message), AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // A summary for support: no addresses, ids, keys, subscription URLs or the HWID.
        private string BuildDiagnosticText()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(AppInfo.Name).Append(" ").Append(AppInfo.Version).Append("\r\n");
            sb.Append(AppInfo.OsDescription).Append("\r\n");
            sb.Append("Program folder: ").Append(AppPaths.BaseDir).Append("\r\n");
            sb.Append("Data folder: ").Append(AppPaths.ConfigDir).Append("\r\n");
            AppendTool(sb, "xray.exe", AppPaths.CorePath);
            AppendTool(sb, "nt5helper.exe", AppPaths.HelperPath);
            AppendTool(sb, "curl.exe", AppPaths.CurlPath);
            sb.Append("CA bundle: ").Append(CaBundle.Inspect(AppPaths.CaBundlePath).ToString()).Append("\r\n");
            sb.Append("Core state: ").Append(_core.State).Append("\r\n");
            sb.Append("System proxy: ").Append(_lastProxyStatus).Append("\r\n");
            sb.Append("Ports: SOCKS ").Append(_settings.SocksPort).Append(", HTTP ").Append(_settings.HttpPort)
                .Append(", allow LAN ").Append(_settings.AllowLan).Append("\r\n");
            sb.Append("Profiles: ").Append(_store.Profiles.Count).Append(", groups: ").Append(_store.Groups.Count).Append("\r\n");
            int subscriptions = 0;
            int withHwid = 0;
            foreach (ProfileGroup g in _store.Groups)
            {
                if (g.IsSubscription)
                {
                    subscriptions++;
                    if (g.UseHwid)
                    {
                        withHwid++;
                    }
                }
            }
            sb.Append("Subscriptions: ").Append(subscriptions).Append(" (HWID on: ").Append(withHwid).Append("), route ")
                .Append(_settings.SubscriptionRoute).Append(", custom HWID ").Append(_settings.HwidCustom.Length > 0 ? "set" : "not set")
                .Append("\r\n");
            sb.Append("Diagnostic log: ").Append(DiagLog.FilePath).Append("\r\n");
            return AppPaths.HidePersonalFolders(sb.ToString());
        }

        private static void AppendTool(StringBuilder sb, string name, string path)
        {
            sb.Append(name).Append(": ");
            if (!File.Exists(path))
            {
                sb.Append("not present\r\n");
                return;
            }
            string problem = ExecutableCheck.Check(path);
            sb.Append(problem == null ? "present, loadable" : "present, NOT loadable: " + problem).Append("\r\n");
        }

        // ---- system proxy watch ----

        // The user (or another program) may change the proxy settings while we run. We do not fight that:
        // our claim is dropped and the user's settings stay.
        private void WatchSystemProxy()
        {
            if (_cleanedUp)
            {
                return;
            }
            SystemProxyStatus status;
            try
            {
                status = _systemProxy.CheckStatus();
            }
            catch (Exception)
            {
                return;
            }
            if (status == _lastProxyStatus)
            {
                return;
            }
            _lastProxyStatus = status;
            if (status == SystemProxyStatus.ChangedOutside)
            {
                AppendLog(Lang.T("The system proxy settings were changed outside this program. They are left as they are; "
                    + "System Proxy is switched off here."));
                RestoreSystemProxy();
                _settings.LastSystemProxy = false;
                SaveSettings();
                _lastProxyStatus = SystemProxyStatus.Off;
            }
            UpdateUi();
        }

        // ---- threads and log ----

        // Runs work on a background thread. An exception there must not end the program
        // (.NET 2.0 would close it): it is logged and shown instead.
        private void RunAsync(ThreadStart work)
        {
            Thread thread = new Thread(delegate()
            {
                try
                {
                    work();
                }
                catch (Exception ex)
                {
                    DiagLog.WriteCrash("Background task failed (the program continues)", ex);
                    Post(delegate
                    {
                        AppendLog(Lang.F("ERROR in a background task: {0}: {1}", ex.GetType().Name, ex.Message));
                        UpdateUi();
                    });
                }
            });
            thread.IsBackground = true;
            thread.Start();
        }

        // Runs the action on the UI thread; silently dropped when the window is going away.
        private void Post(MethodInvoker action)
        {
            if (_cleanedUp || IsDisposed || !IsHandleCreated)
            {
                return;
            }
            try
            {
                BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void AppendLog(string line)
        {
            if (!line.StartsWith("[core] "))
            {
                // the core's own output names the visited sites and is not written to disk
                DiagLog.Write(line);
            }
            string[] lines = textLog.Lines;
            int keep = Math.Min(lines.Length, MaxLogLines - 1);
            string[] result = new string[keep + 1];
            Array.Copy(lines, lines.Length - keep, result, 0, keep);
            result[keep] = DateTime.Now.ToString("HH:mm:ss") + " " + line;
            textLog.Lines = result;
            textLog.SelectionStart = textLog.TextLength;
            textLog.ScrollToCaret();
        }
    }
}
