using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using NekoRayNT5.App;
using NekoRayNT5.Core;
using NekoRayNT5.Services;

namespace NekoRayNT5.UI
{
    internal delegate DependencyReport DependencyCheck();

    // Shows what the program needs and what is missing, with the next step in plain words.
    // Nothing is downloaded or installed: "Open link" opens an official address in the browser.
    internal sealed class DiagnosticsForm : Form
    {
        private readonly DependencyCheck _check;
        private readonly string _supportText;
        private readonly ListView _list;
        private readonly TextBox _details;
        private readonly Button _open;
        private DependencyReport _report;

        public DiagnosticsForm(DependencyCheck check, string intro, string supportText)
        {
            _check = check;
            _supportText = supportText;
            Text = Lang.T("Diagnostics");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(600, 450);

            int y = 10;
            if (!string.IsNullOrEmpty(intro))
            {
                Label label = new Label();
                label.Location = new Point(12, y);
                label.Size = new Size(576, 32);
                label.Text = intro;
                Controls.Add(label);
                y += 36;
            }
            _list = new ListView();
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.HideSelection = false;
            _list.MultiSelect = false;
            _list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            _list.Columns.Add(Lang.T("Component"), 170);
            _list.Columns.Add(Lang.T("Status"), 90);
            _list.Columns.Add(Lang.T("Details"), 300);
            _list.Location = new Point(12, y);
            _list.Size = new Size(576, 200);
            _list.SelectedIndexChanged += delegate { ShowSelected(); };
            Controls.Add(_list);

            _details = new TextBox();
            _details.Multiline = true;
            _details.ReadOnly = true;
            _details.ScrollBars = ScrollBars.Vertical;
            _details.Location = new Point(12, y + 208);
            _details.Size = new Size(576, 446 - (y + 208) - 40);
            Controls.Add(_details);

            int by = 446 - 32;
            _open = AddButton(Lang.T("Open link"), 12, by, 116, OnOpenClick);
            AddButton(Lang.T("Re-check"), 134, by, 116, delegate { Fill(_check()); });
            AddButton(Lang.T("Copy report"), 256, by, 136, OnCopyClick);
            Button close = AddButton(Lang.T("Close"), 498, by, 90, delegate { Close(); });
            CancelButton = close;
            Fill(check());
        }

        private Button AddButton(string text, int x, int y, int width, EventHandler click)
        {
            Button b = new Button();
            b.Text = text;
            b.Location = new Point(x, y);
            b.Size = new Size(width, 24);
            b.Click += click;
            Controls.Add(b);
            return b;
        }

        private void Fill(DependencyReport report)
        {
            _report = report;
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (DependencyItem item in report.Items)
            {
                ListViewItem row = new ListViewItem(new string[] { item.Name, item.StatusText, item.Summary });
                row.Tag = item;
                if (item.Status == DependencyStatus.Missing)
                {
                    row.ForeColor = Color.Firebrick;
                }
                else if (item.Status == DependencyStatus.Warning)
                {
                    row.ForeColor = Color.DarkGoldenrod;
                }
                _list.Items.Add(row);
            }
            _list.EndUpdate();
            // the first problem is shown at once
            int first = 0;
            for (int i = 0; i < report.Items.Count; i++)
            {
                if (report.Items[i].Status != DependencyStatus.Ok)
                {
                    first = i;
                    break;
                }
            }
            if (_list.Items.Count > 0)
            {
                _list.Items[first].Selected = true;
                _list.EnsureVisible(first);
            }
            ShowSelected();
        }

        private DependencyItem Selected
        {
            get { return _list.SelectedItems.Count == 1 ? (DependencyItem)_list.SelectedItems[0].Tag : null; }
        }

        private void ShowSelected()
        {
            DependencyItem item = Selected;
            _details.Text = item == null ? "" : item.Name + " - " + item.StatusText + "\r\n\r\n" + item.Details
                + (item.Link.Length > 0 ? "\r\n\r\n" + Lang.F("Official page: {0}", item.Link) : "");
            _open.Enabled = item != null && item.Link.Length > 0;
        }

        private void OnOpenClick(object sender, EventArgs e)
        {
            DependencyItem item = Selected;
            if (item == null || item.Link.Length == 0)
            {
                return;
            }
            try
            {
                Process.Start(item.Link);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, Lang.F("The browser could not be opened ({0}).\r\n\r\nOpen this address yourself:\r\n{1}",
                    ex.Message, item.Link), AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void OnCopyClick(object sender, EventArgs e)
        {
            try
            {
                Clipboard.SetText(AppPaths.HidePersonalFolders(_report.ToText() + "\r\n" + _supportText + "\r\n" + _details.Text));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, Lang.F("The clipboard is busy: {0}", ex.Message), AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    // A short first-run assistant: 1. check the program files, 2. add a server or subscription,
    // 3. see that it is ready, 4. connect. Every step can be skipped.
    internal delegate bool AddRequest();

    internal sealed class FirstRunForm : Form
    {
        private readonly DependencyCheck _check;
        private readonly AddRequest _add;
        private readonly Label _step1;
        private readonly Label _step3;
        private readonly Button _connect;
        private bool _added;
        private bool _connectRequested;

        public FirstRunForm(DependencyCheck check, AddRequest add)
        {
            _check = check;
            _add = add;
            Text = Lang.F("Welcome to {0}", AppInfo.Name);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(470, 300);

            Heading(Lang.T("1. Program files"), 12);
            _step1 = Note(34, 36);
            Button details = Small(Lang.T("Details..."), 360, 12);
            details.Click += delegate { using (DiagnosticsForm f = new DiagnosticsForm(_check, null, "")) { f.ShowDialog(this); } UpdateSteps(); };

            Heading(Lang.T("2. Add a server or subscription"), 82);
            Note(104, 30).Text = Lang.T("Paste a share link, a subscription address or a happ://add/ link.");
            Button addButton = Small(Lang.T("Add..."), 360, 82);
            addButton.Click += OnAddClick;

            Heading(Lang.T("3. Check"), 142);
            _step3 = Note(164, 36);

            Heading(Lang.T("4. Connect"), 210);
            Note(232, 30).Text = Lang.T("Connects with the first server and turns on the system proxy for programs that use it.");
            _connect = Small(Lang.T("Connect"), 360, 210);
            _connect.Click += delegate { _connectRequested = true; Close(); };

            Button skip = new Button();
            skip.Text = Lang.T("Close");
            skip.Location = new Point(378, 268);
            skip.Size = new Size(80, 24);
            skip.DialogResult = DialogResult.Cancel;
            Controls.Add(skip);
            CancelButton = skip;
            UpdateSteps();
        }

        public bool ConnectRequested
        {
            get { return _connectRequested; }
        }

        private void Heading(string text, int y)
        {
            Label l = new Label();
            l.AutoSize = true;
            l.Font = new Font(Font, FontStyle.Bold);
            l.Location = new Point(12, y + 4);
            l.Text = text;
            Controls.Add(l);
        }

        private Label Note(int y, int height)
        {
            Label l = new Label();
            l.Location = new Point(24, y);
            l.Size = new Size(330, height);
            Controls.Add(l);
            return l;
        }

        private Button Small(string text, int x, int y)
        {
            Button b = new Button();
            b.Text = text;
            b.Location = new Point(x, y);
            b.Size = new Size(98, 24);
            Controls.Add(b);
            return b;
        }

        private void UpdateSteps()
        {
            DependencyReport report = _check();
            _step1.Text = report.CanConnect
                ? (report.Problems == 0 ? Lang.T("Everything needed is in place.")
                   : Lang.F("Connecting works; {0} optional part(s) missing - see Details.", report.Problems))
                : Lang.F("Connecting is NOT possible yet: {0} See Details.", FirstProblem(report));
            _step3.Text = !report.CanConnect ? Lang.T("Fix step 1 first.")
                : _added ? Lang.T("Ready: the servers are in the list. Select one later with a single click, connect with a double click.")
                : Lang.T("Add a server first.");
            _connect.Enabled = report.CanConnect && _added;
        }

        private static string FirstProblem(DependencyReport report)
        {
            foreach (DependencyItem item in report.Items)
            {
                if (item.Status == DependencyStatus.Missing)
                {
                    return item.Name + " - " + item.Summary + ".";
                }
            }
            return "";
        }

        private void OnAddClick(object sender, EventArgs e)
        {
            if (_add())
            {
                _added = true;
            }
            UpdateSteps();
        }
    }
}
