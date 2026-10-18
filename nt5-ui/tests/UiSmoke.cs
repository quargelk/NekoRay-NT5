// Smoke test of the window code: loads NekoRayNT5.exe, creates the main window hidden, and drives the
// handlers that do not open modal dialogs: import, tabs, search, context menu, sorting, double click
// (connect), switching, stop, dialogs created and closed. Synthetic data in a temporary data directory;
// the system proxy is never switched on. Built and run by run-ui-smoke.cmd.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

internal static class UiSmoke
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const string Uuid = "00000000-0000-4000-8000-000000000001";
    private const string Pbk = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    private static int _failed;
    private static int _passed;

    private static void Check(string name, bool ok)
    {
        if (ok) { _passed++; } else { _failed++; }
        Console.WriteLine((ok ? "PASS  " : "FAIL  ") + name);
    }

    private static object Call(object target, string method, params object[] args)
    {
        Type t = target as Type ?? target.GetType();
        MethodInfo m = null;
        foreach (MethodInfo candidate in t.GetMethods(Any))
        {
            if (candidate.Name == method && candidate.GetParameters().Length == args.Length) { m = candidate; break; }
        }
        if (m == null) { throw new MissingMethodException(t.Name, method); }
        try
        {
            return m.Invoke(target is Type ? null : target, args);
        }
        catch (TargetInvocationException ex)
        {
            throw ex.InnerException;
        }
    }

    private static object Field(object target, string name)
    {
        return target.GetType().GetField(name, Any).GetValue(target);
    }

    private static void Pump(int ms)
    {
        for (int waited = 0; waited < ms; waited += 20)
        {
            Application.DoEvents();
            Thread.Sleep(20);
        }
    }

    private static bool PumpUntil(Func check, int ms)
    {
        for (int waited = 0; waited < ms; waited += 20)
        {
            Application.DoEvents();
            if (check()) { return true; }
            Thread.Sleep(20);
        }
        return check();
    }

    private delegate bool Func();

    // ---- modal dialogs ----
    // A message box opened by the code under test would block the test for ever. A watchdog thread
    // closes every dialog box (#32770) of the UI thread, counts it as a failure and prints its text;
    // a deadline ends a test that hangs anyway.

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool EnumThreadWindows(int threadId, EnumWindowsProc callback, IntPtr lParam);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr lParam);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern int GetCurrentThreadId();

    private static readonly List<string> UnexpectedDialogs = new List<string>();
    private static int _uiThreadId;
    private const int DeadlineSeconds = 240;

    private static string ClassOf(IntPtr hwnd)
    {
        StringBuilder sb = new StringBuilder(64);
        GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string TextOf(IntPtr hwnd)
    {
        StringBuilder sb = new StringBuilder(512);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static void Watchdog()
    {
        DateTime deadline = DateTime.Now.AddSeconds(DeadlineSeconds);
        while (true)
        {
            Thread.Sleep(250);
            EnumThreadWindows(_uiThreadId, delegate(IntPtr hwnd, IntPtr unused)
            {
                if (IsWindowVisible(hwnd) && ClassOf(hwnd) == "#32770")
                {
                    StringBuilder text = new StringBuilder(TextOf(hwnd));
                    EnumChildWindows(hwnd, delegate(IntPtr child, IntPtr unused2)
                    {
                        if (ClassOf(child) == "Static") { text.Append(" | ").Append(TextOf(child)); }
                        return true;
                    }, IntPtr.Zero);
                    lock (UnexpectedDialogs) { UnexpectedDialogs.Add(text.ToString().Replace("\r\n", " ").Replace("\n", " ")); }
                    PostMessage(hwnd, 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);
                }
                return true;
            }, IntPtr.Zero);
            if (DateTime.Now > deadline)
            {
                Console.WriteLine("FAIL  the smoke test did not finish within " + DeadlineSeconds + " seconds");
                Console.WriteLine("ui-smoke passed=" + _passed + " failed=" + (_failed + 1));
                Environment.Exit(1);
            }
        }
    }

    private static void CheckNoDialogs(string when)
    {
        Pump(300);
        string[] seen;
        lock (UnexpectedDialogs) { seen = UnexpectedDialogs.ToArray(); UnexpectedDialogs.Clear(); }
        Check("no unexpected message box " + when + (seen.Length > 0 ? ": " + string.Join(" || ", seen) : ""), seen.Length == 0);
    }

    [STAThread]
    private static int Main(string[] args)
    {
        string dir = Path.GetDirectoryName(Application.ExecutablePath);
        string data = Path.Combine(dir, "data");
        Environment.SetEnvironmentVariable("NEKORAY_NT5_DATA_DIR", data);
        Assembly app = Assembly.LoadFrom(Path.Combine(dir, "NekoRayNT5.exe"));
        // "ru" as the first argument: the whole run in Russian (as Program.Main does after reading the settings)
        _lang = app.GetType("NekoRayNT5.App.Lang");
        string language = args.Length > 0 ? args[0] : "en";
        object langProblem = _lang.GetMethod("Set").Invoke(null, new object[] { language });
        Console.WriteLine("      language: " + language + (langProblem == null ? "" : " (" + langProblem + ")"));
        Application.EnableVisualStyles();
        _uiThreadId = GetCurrentThreadId();
        Thread watchdog = new Thread(Watchdog);
        watchdog.IsBackground = true;
        watchdog.Start();
        _findFreePort = app.GetType("NekoRayNT5.Core.CoreLauncher").GetMethod("FindFreePort", Any);

        // a local chain: the profile is a SOCKS server run by a second xray (no remote servers)
        int upstreamPort = FreePort();
        string upstreamConfig = "{\"inbounds\":[{\"listen\":\"127.0.0.1\",\"port\":" + upstreamPort
            + ",\"protocol\":\"socks\",\"settings\":{\"auth\":\"noauth\"}}],\"outbounds\":[{\"protocol\":\"freedom\"}]}";
        string upstreamFile = Path.Combine(dir, "upstream.json");
        File.WriteAllText(upstreamFile, upstreamConfig);
        Process upstream = new Process();
        upstream.StartInfo.FileName = Path.Combine(dir, "xray.exe");
        upstream.StartInfo.Arguments = "run -c \"" + upstreamFile + "\"";
        upstream.StartInfo.UseShellExecute = false;
        upstream.StartInfo.CreateNoWindow = true;
        upstream.Start();

        Form form = null;
        try
        {
            // settings with free ports, so that nothing on this machine is disturbed
            Directory.CreateDirectory(data);
            File.WriteAllText(Path.Combine(data, "settings.xml"), "<NekoRayNT5Settings formatVersion=\"1\"><socksPort>" + FreePort()
                + "</socksPort><httpPort>" + FreePort() + "</httpPort></NekoRayNT5Settings>");

            // as Program.Main does
            app.GetType("NekoRayNT5.App.DiagLog").GetProperty("Directory").SetValue(null, Path.Combine(data, "logs"), null);
            Type mainType = app.GetType("NekoRayNT5.UI.MainForm");
            form = (Form)Activator.CreateInstance(mainType, Any, null, new object[] { true }, null);
            Pump(1500);
            Check("main window created hidden (tray start), no exception", form != null && !form.Visible);
            TextBox log = (TextBox)Field(form, "textLog");
            Check("start-up log written", log.Text.IndexOf(LF("Profiles loaded: {0}, groups: {1}", 0, 1)) >= 0);
            string coreLine = LF("Core: {0}", "Xray");
            Check("core probed and found loadable", PumpUntil(delegate { return log.Text.IndexOf(coreLine) >= 0; }, 10000));

            string links = "vless://" + Uuid + "@127.0.0.1:443?security=reality&pbk=" + Pbk + "&sni=www.example.com&fp=chrome&sid=01#Zeta\n"
                           + "trojan://synthetic-password-0001@127.0.0.1:443?security=tls#Alpha\n";
            Call(form, "ImportText", links, 0);
            ListView list = (ListView)Field(form, "listProfiles");
            Check("import shows the profiles", list.Items.Count == 2);

            object store = Field(form, "_store");
            Type entryType = app.GetType("NekoRayNT5.Model.ProfileEntry");
            object socks = Call(entryType, "Create", "socks");
            entryType.GetProperty("Name").SetValue(socks, "Local chain", null);
            entryType.GetProperty("Address").SetValue(socks, "127.0.0.1", null);
            entryType.GetProperty("Port").SetValue(socks, upstreamPort, null);
            System.Collections.IList one = (System.Collections.IList)Activator.CreateInstance(
                typeof(List<>).MakeGenericType(entryType));
            one.Add(socks);
            Call(store, "Add", one, 0);
            Call(form, "RefreshProfileList");
            Check("three profiles listed", list.Items.Count == 3);

            Call(form, "SortGroup", 0);
            Check("sort by name", list.Items[0].SubItems[2].Text == "Alpha");
            Call(form, "SortGroup", 2);
            Call(form, "SortGroup", 1);

            TextBox search = (TextBox)Field(form, "textSearch");
            search.Text = "www.example";
            Check("search over SNI finds the profile", list.Items.Count == 1 && list.Items[0].SubItems[2].Text == "Zeta");
            search.Text = "";

            ContextMenuStrip menu = list.ContextMenuStrip;
            Call(form, "OnListMenuOpening", menu, new System.ComponentModel.CancelEventArgs());
            Check("context menu built", menu.Items.Count >= 8);

            // as the user does: open the window from the tray (the first show after "-tray" stays hidden)
            Call(form, "ShowFromTray");
            Call(form, "ShowFromTray");
            Pump(300);
            Check("window shown from the tray", form.Visible);
            // double click on the local chain profile connects
            foreach (ListViewItem item in list.Items) { item.Selected = item.SubItems[2].Text == "Local chain"; }
            Console.WriteLine("      list handle created: " + list.IsHandleCreated + ", selected: " + list.SelectedItems.Count);
            Call(form, "OnProfileDoubleClick", list, EventArgs.Empty);
            object core = Field(form, "_core");
            Console.WriteLine("      state after double click: " + core.GetType().GetProperty("State").GetValue(core, null));
            PropertyInfo state = core.GetType().GetProperty("State");
            Check("double click: Connecting at once", state.GetValue(core, null).ToString() == "Connecting");
            Call(form, "OnProfileDoubleClick", list, EventArgs.Empty);
            Check("double click again while connecting is ignored", log.Text.IndexOf(LF("\"{0}\" is already connecting.", "Local chain")) >= 0
                || log.Text.IndexOf(LF("\"{0}\" is already running.", "Local chain")) >= 0);
            Check("running", PumpUntil(delegate { return state.GetValue(core, null).ToString() == "Running"; }, 20000));
            ToolStripStatusLabel status = (ToolStripStatusLabel)Field(form, "labelRunning");
            ToolStripStatusLabel inbound = (ToolStripStatusLabel)Field(form, "labelInbound");
            Check("status bar shows the running profile and the real mode", status.Text == LF("Running: {0}", "Local chain")
                && inbound.Text.IndexOf(L("Mode: local ports only")) >= 0);
            Check("the running profile is bold", BoldName(list) == "Local chain");

            // Enter on another profile switches (it cannot connect: the core still starts and listens)
            foreach (ListViewItem item in list.Items) { item.Selected = item.SubItems[2].Text == "Alpha"; }
            Call(form, "OnStartClick", list, EventArgs.Empty);
            Check("switch to another profile", PumpUntil(delegate
            {
                return state.GetValue(core, null).ToString() == "Running" && status.Text == LF("Running: {0}", "Alpha");
            }, 20000));

            Call(form, "UpdateUi");
            StringBuilder texts = new StringBuilder();
            CollectTexts(((MenuStrip)Field(form, "menuMain")).Items, texts);
            CollectTexts(((NotifyIcon)Field(form, "_tray")).ContextMenuStrip.Items, texts);
            foreach (Control c in ((Control)Field(form, "panelTop")).Controls) { texts.Append(c.Text).Append('|'); }
            Check("no TUN or TAP item in the menus, tray menu and top panel (TUN mode was removed)",
                texts.ToString().IndexOf("TUN", StringComparison.OrdinalIgnoreCase) < 0
                && texts.ToString().IndexOf("TAP", StringComparison.Ordinal) < 0 && texts.ToString().IndexOf(L("System Proxy")) >= 0);

            Call(core, "Stop");
            Check("Stop", PumpUntil(delegate { return state.GetValue(core, null).ToString() == "NotRunning"; }, 10000)
                && status.Text == L("Not Running"));

            // dialogs: created and closed without showing them modally
            object settings = Field(form, "_settings");
            Form sf = (Form)Activator.CreateInstance(app.GetType("NekoRayNT5.UI.SettingsForm"), Any, null, new object[] { settings }, null);
            sf.Show(); Pump(200); CheckLayout("Settings", sf, true); sf.Close(); sf.Dispose();
            Form gf = (Form)Activator.CreateInstance(app.GetType("NekoRayNT5.UI.GroupEditForm"), Any, null,
                new object[] { "Add subscription", "", "", true }, null);
            gf.Show(); Pump(100); CheckLayout("group (subscription) dialog", gf, true); gf.Close(); gf.Dispose();
            Form gn = (Form)Activator.CreateInstance(app.GetType("NekoRayNT5.UI.GroupNameForm"), Any, null, new object[] { "New group", "" }, null);
            gn.Show(); Pump(100); CheckLayout("group name dialog", gn, true); gn.Close(); gn.Dispose();
            foreach (string type in new string[] { "vless", "vmess", "trojan", "ss", "socks", "http" })
            {
                object sample = Call(entryType, "Create", type);
                Form pf = (Form)Activator.CreateInstance(app.GetType("NekoRayNT5.UI.ProfileEditForm"), Any, null, new object[] { sample, true }, null);
                pf.Show(); Pump(100); CheckLayout("profile editor (" + type + ")", pf, true); pf.Close(); pf.Dispose();
            }
            Form groups = (Form)Activator.CreateInstance(app.GetType("NekoRayNT5.UI.GroupsForm"), Any, null, new object[] { store }, null);
            groups.Show(); Pump(100); CheckLayout("Groups", groups, false); groups.Close(); groups.Dispose();
            Form first = (Form)Activator.CreateInstance(app.GetType("NekoRayNT5.UI.FirstRunForm"), Any, null, new object[] {
                Delegate.CreateDelegate(app.GetType("NekoRayNT5.UI.DependencyCheck"), form, "BuildDependencyReport"),
                Delegate.CreateDelegate(app.GetType("NekoRayNT5.UI.AddRequest"), typeof(UiSmoke).GetMethod("NoAdd", Any)) }, null);
            first.Show(); Pump(100); CheckLayout("first-run assistant", first, false); first.Close(); first.Dispose();
            CheckLayout("main window", form, false);
            Check("main window fits 800x600", form.Width <= 800 && form.Height <= 600);
            bool toolbarFits = true;
            foreach (ToolStripItem item in ((ToolStrip)Field(form, "_toolbar")).Items) { toolbarFits &= item.Placement != ToolStripItemPlacement.Overflow; }
            Check("toolbar buttons fit the 800x600 window (nothing in the overflow)", toolbarFits);
            Check("settings, subscription, profile and group dialogs open and close", true);

            // the single Add: links are examined at once, nothing is saved before Add
            object client = Call(form, "CreateSubscriptionClient");
            int before = list.Items.Count;
            Form add = (Form)Activator.CreateInstance(app.GetType("NekoRayNT5.UI.AddForm"), Any, null,
                new object[] { client, Field(store, "_profiles"), links }, null);
            add.Show(); Pump(400);
            CheckLayout("Add", add, false);
            TextBox preview = (TextBox)Field(add, "_preview");
            Check("Add: links checked at once, duplicates of the list reported, nothing saved yet",
                preview.Text.IndexOf(LF("Servers found: {0}", 2)) >= 0 && preview.Text.IndexOf(LF("Already in your list: {0}", 2)) >= 0
                && list.Items.Count == before && preview.Text.IndexOf(Uuid) < 0);
            ((TextBox)Field(add, "_input")).Text = "happ://crypt4/AAAA";
            Check("Add: encrypted Happ link explained", preview.Text.IndexOf("happ://crypt") >= 0 && preview.Text.Length > 60);
            ((TextBox)Field(add, "_input")).Text = "happ://add/https://sub.example.invalid/s/1";
            Check("Add: happ://add suggests the HWID", ((CheckBox)Field(add, "_hwid")).Checked && ((CheckBox)Field(add, "_hwid")).Visible);
            StringBuilder page = new StringBuilder("Read 1 file\r\n" + (char)0x2022 + "\r\n");
            for (int i = 0; i < 3500; i++) { page.Append("text line ").Append(i).Append(" https://www.example.com/\r\n"); }
            ((TextBox)Field(add, "_input")).Text = page.ToString();
            Call(add, "StartCheck");
            Pump(200);
            Console.WriteLine("      preview of a copied page: " + preview.Text.Replace("\r\n", " | "));
            Check("Add: a copied page gives one short explanation, Add stays disabled", preview.Text.Length < 600
                && preview.Text.IndexOf(LF("Nothing can be added: {0}", "").Trim()) >= 0 && !((Button)Field(add, "_add")).Enabled);
            ((TextBox)Field(add, "_input")).Text = "Read 1 file " + (char)0x2022;
            Check("Add: plain text is explained without Check", preview.Text == L("This text contains no share links and no subscription "
                + "address. Paste links that start with vless://, vmess://, trojan://, ss:// or socks://, a subscription address "
                + "(https://...) or a happ://add/ link, or open a file."));
            add.Close(); add.Dispose();

            Form diag = (Form)Activator.CreateInstance(app.GetType("NekoRayNT5.UI.DiagnosticsForm"), Any, null, new object[] {
                Delegate.CreateDelegate(app.GetType("NekoRayNT5.UI.DependencyCheck"), form, "BuildDependencyReport"), "intro", "text" }, null);
            diag.Show(); Pump(200);
            CheckLayout("Diagnostics", diag, false);
            ListView items = (ListView)Field(diag, "_list");
            bool noTun = true;
            foreach (ListViewItem row in items.Items) { noTun &= row.Text.IndexOf("TUN") < 0 && row.Text.IndexOf("TAP") < 0; }
            Check("Diagnostics lists the components (core, helper, certificates, ports), nothing about TUN", items.Items.Count >= 6
                && items.Items[1].SubItems[1].Text == L("OK") && noTun);
            diag.Close(); diag.Dispose();
            // group tabs: right-click menu (create, rename, delete; the default group is protected)
            TabControl tabs = (TabControl)Field(form, "tabGroups");
            System.Collections.ICollection allProfiles = (System.Collections.ICollection)store.GetType().GetProperty("Profiles").GetValue(store, null);
            int tabsBefore = tabs.TabCount;
            int profilesBefore = allProfiles.Count;
            int gid = (int)Call(form, "CreateGroup", "Synthetic group");
            Check("group menu: New group adds a tab and selects it", gid > 0 && tabs.TabCount == tabsBefore + 1
                && (int)tabs.SelectedTab.Tag == gid);
            object extra = Call(entryType, "Create", "socks");
            entryType.GetProperty("Name").SetValue(extra, "Group member", null);
            entryType.GetProperty("Address").SetValue(extra, "127.0.0.1", null);
            entryType.GetProperty("Port").SetValue(extra, 1081, null);
            one.Clear();
            one.Add(extra);
            Call(store, "Add", one, gid);
            ContextMenuStrip groupMenu = (ContextMenuStrip)Call(form, "BuildGroupMenu", gid);
            ContextMenuStrip defaultMenu = (ContextMenuStrip)Call(form, "BuildGroupMenu", 0);
            Check("group menu: rename and delete for a group, update only for subscriptions", MenuItemState(groupMenu, L("Rename...")) == 1
                && MenuItemState(groupMenu, L("Update subscription")) == 0 && MenuItemState(groupMenu, L("Delete group...")) == 1
                && MenuItemState(groupMenu, L("New group...")) == 1);
            Check("group menu: the default group cannot be deleted", MenuItemState(defaultMenu, L("Rename...")) == 1
                && MenuItemState(defaultMenu, L("Delete group (the default group stays)")) == 0);
            Check("group menu: Rename changes only the name", (bool)Call(form, "RenameGroup", gid, "Renamed group")
                && tabs.SelectedTab.Text == "Renamed group" && allProfiles.Count == profilesBefore + 1);
            Check("group menu: deleting the default group is refused", !(bool)Call(form, "DeleteGroup", 0)
                && Call(store, "FindGroup", 0) != null && allProfiles.Count == profilesBefore + 1);
            Check("group menu: Delete removes that group and only its servers", (bool)Call(form, "DeleteGroup", gid)
                && tabs.TabCount == tabsBefore && allProfiles.Count == profilesBefore && (int)Field(form, "_currentGroup") == 0
                && Call(store, "FindGroup", gid) == null);

            ToolStripButton connect = (ToolStripButton)Field(form, "_toolConnect");
            Call(form, "UpdateUi");
            Check("toolbar shows Connect while not connected", connect.Text == L("Connect"));

            CheckNoDialogs("during the whole run");
            Call(form, "WatchSystemProxy");
            Check("system proxy watch with the proxy off does nothing", log.Text.IndexOf(L("The system proxy settings were changed "
                + "outside this program. They are left as they are; System Proxy is switched off here.")) < 0);

            string diagLog = Path.Combine(Path.Combine(data, "logs"), "nekoray-nt5.log");
            Check("diagnostic log written without credentials", File.Exists(diagLog)
                && File.ReadAllText(diagLog).IndexOf(Uuid) < 0 && File.ReadAllText(diagLog).IndexOf("synthetic-password") < 0
                && File.ReadAllText(diagLog).IndexOf(Pbk) < 0);
            Check("window log has no credentials", log.Text.IndexOf(Uuid) < 0 && log.Text.IndexOf("synthetic-password") < 0);
        }
        catch (Exception ex)
        {
            _failed++;
            Console.WriteLine("FAIL  unexpected exception: " + ex);
        }
        finally
        {
            if (form != null && _failed > 0)
            {
                Console.WriteLine("---- window log ----");
                Console.WriteLine(((TextBox)Field(form, "textLog")).Text);
            }
            if (form != null)
            {
                try { Call(form, "Cleanup"); } catch (Exception ex) { _failed++; Console.WriteLine("FAIL  cleanup: " + ex.Message); }
                form.Dispose();
            }
            try { upstream.Kill(); upstream.WaitForExit(3000); } catch (Exception) { }
        }
        Check("no core left running in the run directory", !File.Exists(Path.Combine(Path.Combine(data, "run"), "core.pid")));
        Console.WriteLine();
        Console.WriteLine("ui-smoke passed=" + _passed + " failed=" + _failed);
        return _failed == 0 ? 0 : 1;
    }

    private static Type _lang;

    private static string L(string english)
    {
        return (string)_lang.GetMethod("T").Invoke(null, new object[] { english });
    }

    private static string LF(string english, params object[] values)
    {
        return (string)_lang.GetMethod("F").Invoke(null, new object[] { english, values });
    }

    private static bool NoAdd()
    {
        return false;
    }

    // Every control inside its parent (and the form inside 800x600), fixed-size labels and buttons large
    // enough for their text, and - in "label: control" dialogs - no label running into the control of its row.
    private static void CheckLayout(string name, Form form, bool rows)
    {
        List<string> problems = new List<string>();
        if (form.Width > 800 || form.Height > 600)
        {
            problems.Add("the window is " + form.Width + "x" + form.Height);
        }
        CollectLayout(form, rows, problems);
        foreach (string p in problems) { Console.WriteLine("      " + name + ": " + p); }
        Check("layout: " + name + " (" + form.Width + "x" + form.Height + ")", problems.Count == 0);
    }

    private static void CollectLayout(Control parent, bool rows, List<string> problems)
    {
        ScrollableControl scroll = parent as ScrollableControl;
        bool scrolls = scroll != null && scroll.AutoScroll;
        foreach (Control c in parent.Controls)
        {
            string id = c.GetType().Name + " \"" + (c.Text.Length > 40 ? c.Text.Substring(0, 40) + "..." : c.Text) + "\"";
            if (!(parent is TabControl) && !(parent is Form && ((Form)parent).IsMdiContainer))
            {
                if (c.Right > parent.ClientSize.Width + 1 && !(parent is ToolStrip))
                {
                    problems.Add(id + " ends at x=" + c.Right + ", beyond the width " + parent.ClientSize.Width);
                }
                if (!scrolls && c.Bottom > parent.ClientSize.Height + 1 && c.Dock == DockStyle.None)
                {
                    problems.Add(id + " ends at y=" + c.Bottom + ", beyond the height " + parent.ClientSize.Height);
                }
            }
            if (c is Label && !((Label)c).AutoSize && c.Text.Length > 0)
            {
                System.Drawing.Size need = TextRenderer.MeasureText(c.Text, c.Font, new System.Drawing.Size(c.Width, int.MaxValue), TextFormatFlags.WordBreak);
                if (need.Height > c.Height + 2)
                {
                    problems.Add(id + " needs " + need.Height + " px of height, has " + c.Height);
                }
            }
            if (c is ButtonBase && !(c is CheckBox) && !(c is RadioButton) && !((ButtonBase)c).AutoSize && c.Text.Length > 0)
            {
                if (TextRenderer.MeasureText(c.Text, c.Font).Width + 8 > c.Width)
                {
                    problems.Add(id + " is too narrow for its text (" + c.Width + " px)");
                }
            }
            if (rows && c is Label && c.Left < 20)
            {
                foreach (Control other in parent.Controls)
                {
                    if (other is Label || other == c || !other.Visible && false) { continue; }
                    bool sameRow = other.Top < c.Bottom && c.Top < other.Bottom;
                    if (sameRow && other.Left > c.Left && c.Right > other.Left)
                    {
                        problems.Add(id + " runs into " + other.GetType().Name + " at x=" + other.Left);
                    }
                }
            }
            CollectLayout(c, rows, problems);
        }
    }

    // 1 enabled, 0 disabled, -1 missing
    private static int MenuItemState(ContextMenuStrip menu, string text)
    {
        foreach (ToolStripItem item in menu.Items)
        {
            if (item.Text == text) { return item.Enabled ? 1 : 0; }
        }
        return -1;
    }

    private static void CollectTexts(ToolStripItemCollection items, StringBuilder sb)
    {
        foreach (ToolStripItem item in items)
        {
            sb.Append(item.Text).Append('|');
            ToolStripMenuItem menu = item as ToolStripMenuItem;
            if (menu != null) { CollectTexts(menu.DropDownItems, sb); }
        }
    }

    private static string BoldName(ListView list)
    {
        foreach (ListViewItem item in list.Items)
        {
            if (item.Font.Bold) { return item.SubItems[2].Text; }
        }
        return "";
    }

    private static MethodInfo _findFreePort;

    // The port picker of the program: free for TCP and UDP, outside the dynamic range, never twice.
    private static int FreePort()
    {
        return (int)_findFreePort.Invoke(null, new object[0]);
    }
}
