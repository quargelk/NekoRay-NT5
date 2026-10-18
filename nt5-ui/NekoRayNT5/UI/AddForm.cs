using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using NekoRayNT5.App;
using NekoRayNT5.Model;
using NekoRayNT5.Services;

namespace NekoRayNT5.UI
{
    // The single Add command: share links (one or many), a subscription address, a happ://add/ link
    // or a file. The source is recognised and examined first (an address is downloaded, nothing is
    // saved), the preview shows what was found, and only "Add" changes the profile list.
    internal sealed class AddForm : Form
    {
        private const int MaxFileBytes = 5 * 1024 * 1024;

        private readonly SubscriptionClient _client;
        private readonly IList<ProfileEntry> _existing;
        private readonly TextBox _input;
        private readonly CheckBox _hwid;
        private readonly Label _hwidNote;
        private readonly Button _check;
        private readonly TextBox _preview;
        private readonly CheckBox _subscription;
        private readonly TextBox _name;
        private readonly Label _nameLabel;
        private readonly CheckBox _skipKnown;
        private readonly Button _add;
        private AddPreview _result;
        // incremented on every change of the input, so that a late answer for old input is dropped
        private int _generation;
        private bool _busy;

        public AddForm(SubscriptionClient client, IList<ProfileEntry> existing, string initialText)
        {
            _client = client;
            _existing = existing;
            Text = Lang.T("Add servers or a subscription");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(560, 470);

            Label prompt = new Label();
            prompt.AutoSize = true;
            prompt.Location = new Point(12, 10);
            prompt.Text = Lang.T("Paste share links (vless://, vmess://, trojan://, ss://, socks://), a subscription address\r\n"
                          + "(https://...) or a happ://add/ link, or open a file:");
            Controls.Add(prompt);

            _input = new TextBox();
            _input.Multiline = true;
            _input.ScrollBars = ScrollBars.Vertical;
            _input.WordWrap = true;
            _input.MaxLength = MaxFileBytes;
            _input.Location = new Point(12, 42);
            _input.Size = new Size(428, 80);
            _input.TextChanged += delegate { OnInputChanged(); };
            Controls.Add(_input);

            Button paste = new Button();
            paste.Text = Lang.T("Paste");
            paste.Location = new Point(448, 42);
            paste.Size = new Size(100, 24);
            paste.Click += OnPasteClick;
            Controls.Add(paste);

            Button file = new Button();
            file.Text = Lang.T("Open file...");
            file.Location = new Point(448, 70);
            file.Size = new Size(100, 24);
            file.Click += OnFileClick;
            Controls.Add(file);

            _hwid = new CheckBox();
            _hwid.AutoSize = true;
            _hwid.Location = new Point(12, 130);
            _hwid.Text = Lang.T("Use Happ-compatible HWID");
            _hwid.CheckedChanged += delegate { MarkStale(false); };
            Controls.Add(_hwid);

            _hwidNote = new Label();
            _hwidNote.AutoSize = false;
            _hwidNote.Location = new Point(30, 150);
            _hwidNote.Size = new Size(518, 32);
            _hwidNote.ForeColor = SystemColors.GrayText;
            _hwidNote.Text = Lang.F("Sends the device id of this installation ({0}) as Happ does. Only for "
                             + "providers that require it; change it in Settings - Subscriptions.", Hwid.Mask(client.Hwid));
            Controls.Add(_hwidNote);

            _check = new Button();
            _check.Text = Lang.T("Check");
            _check.Location = new Point(448, 128);
            _check.Size = new Size(100, 24);
            _check.Click += delegate { StartCheck(); };
            Controls.Add(_check);

            GroupBox box = new GroupBox();
            box.Text = Lang.T("What will be added");
            box.Location = new Point(12, 184);
            box.Size = new Size(536, 168);
            Controls.Add(box);
            _preview = new TextBox();
            _preview.Multiline = true;
            _preview.ReadOnly = true;
            _preview.ScrollBars = ScrollBars.Vertical;
            _preview.Location = new Point(10, 18);
            _preview.Size = new Size(516, 140);
            box.Controls.Add(_preview);

            _subscription = new CheckBox();
            _subscription.AutoSize = true;
            _subscription.Location = new Point(12, 360);
            _subscription.Text = Lang.T("Keep as a subscription (a new group, updated from this address)");
            _subscription.CheckedChanged += delegate { UpdateOptions(); };
            Controls.Add(_subscription);

            _nameLabel = new Label();
            _nameLabel.AutoSize = true;
            _nameLabel.Location = new Point(12, 388);
            _nameLabel.Text = Lang.T("Group name:");
            Controls.Add(_nameLabel);
            _name = new TextBox();
            _name.Location = new Point(100, 385);
            _name.Size = new Size(250, 20);
            _name.MaxLength = 100;
            Controls.Add(_name);

            _skipKnown = new CheckBox();
            _skipKnown.AutoSize = true;
            _skipKnown.Location = new Point(12, 412);
            _skipKnown.Text = Lang.T("Skip servers that are already in the list");
            _skipKnown.Checked = true;
            Controls.Add(_skipKnown);

            _add = new Button();
            _add.Text = Lang.T("Add");
            _add.Location = new Point(380, 436);
            _add.Size = new Size(80, 24);
            _add.Enabled = false;
            _add.Click += OnAddClick;
            Controls.Add(_add);
            Button cancel = new Button();
            cancel.Text = Lang.T("Cancel");
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Location = new Point(468, 436);
            cancel.Size = new Size(80, 24);
            Controls.Add(cancel);
            AcceptButton = _check;
            CancelButton = cancel;

            _input.Text = NormalizeLines(initialText);
            OnInputChanged();
            Shown += delegate
            {
                // links need no network: show what they contain at once
                if (AddSource.Classify(_input.Text).Kind == SourceKind.Links)
                {
                    StartCheck();
                }
            };
        }

        // ---- results ----

        public AddPreview Result
        {
            get { return _result; }
        }

        public bool AsSubscription
        {
            get { return _result != null && _result.Input.Kind == SourceKind.Url && _subscription.Checked; }
        }

        public bool UseHwid
        {
            get { return _result != null && _result.UsedHwid; }
        }

        public bool SkipKnown
        {
            get { return _skipKnown.Checked; }
        }

        public string GroupName
        {
            get { return _name.Text.Trim(); }
        }

        // ---- input ----

        // A Windows text box shows a line break only for CR LF; lists from other systems use LF alone.
        private static string NormalizeLines(string text)
        {
            return (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n");
        }

        private void OnPasteClick(object sender, EventArgs e)
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    _input.Text = NormalizeLines(Clipboard.GetText());
                    return;
                }
                ShowPreview(Lang.T("The clipboard contains no text."));
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                ShowPreview(Lang.T("The clipboard is busy. Try again."));
            }
        }

        private void OnFileClick(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = Lang.T("Open a file with share links or a subscription");
                dialog.Filter = Lang.T("Text files (*.txt)|*.txt|All files (*.*)|*.*");
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                try
                {
                    if (new FileInfo(dialog.FileName).Length > MaxFileBytes)
                    {
                        throw new IOException(Lang.T("the file is larger than 5 MB."));
                    }
                    _input.Text = NormalizeLines(Subscription.CheckContent(File.ReadAllBytes(dialog.FileName)));
                    StartCheck();
                }
                catch (Exception ex)
                {
                    ShowPreview(Lang.F("The file cannot be used: {0}", ex.Message));
                }
            }
        }

        private void OnInputChanged()
        {
            SourceInput input = AddSource.Classify(_input.Text);
            bool url = input.Kind == SourceKind.Url;
            // the HWID is offered for addresses only, and suggested for happ:// links
            _hwid.Visible = url;
            _hwidNote.Visible = url;
            if (!url)
            {
                _hwid.Checked = false;
            }
            else if (input.FromHapp)
            {
                _hwid.Checked = true;
            }
            MarkStale(true);
            if (input.Kind == SourceKind.Invalid)
            {
                ShowPreview(_input.Text.Trim().Length == 0 ? "" : input.Problem);
            }
            else
            {
                ShowPreview(url ? Lang.T("Press Check to download the address and see what it contains. Nothing is saved before Add.")
                                : Lang.T("Press Check to see the servers."));
            }
        }

        // Any change makes the last preview stale.
        private void MarkStale(bool inputChanged)
        {
            _generation++;
            _result = null;
            _add.Enabled = false;
            AcceptButton = _check;
            if (!inputChanged && !_busy)
            {
                ShowPreview(Lang.T("Press Check again."));
            }
            UpdateOptions();
        }

        private void ShowPreview(string text)
        {
            _preview.Text = text;
        }

        private void UpdateOptions()
        {
            bool url = _result != null && _result.Input.Kind == SourceKind.Url;
            _subscription.Visible = url;
            _nameLabel.Visible = AsSubscription;
            _name.Visible = AsSubscription;
            _skipKnown.Visible = _result != null && !AsSubscription && _result.AlreadyKnown > 0;
        }

        // ---- check ----

        private void StartCheck()
        {
            if (_busy)
            {
                return;
            }
            SourceInput input = AddSource.Classify(_input.Text);
            if (input.Kind == SourceKind.Invalid)
            {
                ShowPreview(input.Problem);
                return;
            }
            int generation = ++_generation;
            _result = null;
            _add.Enabled = false;
            if (input.Kind == SourceKind.Links)
            {
                ShowResult(generation, AddSource.AnalyzeLinks(input, _existing));
                return;
            }
            bool useHwid = _hwid.Checked;
            _busy = true;
            _check.Enabled = false;
            UseWaitCursor = true;
            ShowPreview(Lang.F(useHwid ? "Downloading {0} with the HWID... (up to {1} seconds)" : "Downloading {0}... (up to {1} seconds)",
                SubscriptionClient.DescribeRoute(_client.Route), Subscription.TimeoutMs / 1000));
            Thread worker = new Thread(delegate()
            {
                AddPreview preview;
                try
                {
                    preview = AddSource.AnalyzeUrl(input, _client, useHwid, _existing);
                }
                catch (Exception ex)
                {
                    // never a crash of the window: the reason goes to the preview
                    preview = new AddPreview();
                    preview.Input = input;
                    preview.Failure = SubscriptionClient.SafeMessage(ex, input.Url);
                    DiagLog.Write("Add: unexpected " + ex.GetType().Name + " while checking an address.");
                }
                try
                {
                    BeginInvoke((MethodInvoker)delegate { ShowResult(generation, preview); });
                }
                catch (InvalidOperationException)
                {
                    // the window was closed meanwhile
                }
            });
            worker.IsBackground = true;
            worker.Start();
        }

        private void ShowResult(int generation, AddPreview preview)
        {
            _busy = false;
            _check.Enabled = true;
            UseWaitCursor = false;
            if (IsDisposed || generation != _generation)
            {
                return;
            }
            _result = preview;
            ShowPreview(preview.Describe());
            if (preview.Input.Kind == SourceKind.Url)
            {
                _subscription.Checked = preview.LooksLikeSubscription;
                if (preview.Title.Length > 0)
                {
                    _name.Text = preview.Title;
                }
            }
            _add.Enabled = preview.CanAdd;
            if (preview.CanAdd)
            {
                AcceptButton = _add;
            }
            UpdateOptions();
        }

        private void OnAddClick(object sender, EventArgs e)
        {
            if (_result == null || !_result.CanAdd)
            {
                return;
            }
            if (AsSubscription && GroupName.Length == 0)
            {
                MessageBox.Show(this, Lang.T("Enter a name for the subscription group."), AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                _name.Focus();
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
