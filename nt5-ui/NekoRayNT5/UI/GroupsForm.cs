using System;
using System.Drawing;
using System.Windows.Forms;
using NekoRayNT5.App;
using NekoRayNT5.Model;
using NekoRayNT5.Services;

namespace NekoRayNT5.UI
{
    // Name and subscription address of one group.
    internal sealed class GroupEditForm : Form
    {
        private readonly TextBox _name;
        private readonly TextBox _url;
        private readonly bool _urlRequired;

        public GroupEditForm(string title, string name, string url)
            : this(title, name, url, false)
        {
        }

        public GroupEditForm(string title, string name, string url, bool urlRequired)
        {
            Text = title;
            _urlRequired = urlRequired;
            RowLayout rows = new RowLayout(this, 120, 340);
            _name = rows.Text(Lang.T("Name"), 100);
            _url = rows.Text(Lang.T("Subscription URL"), 4000);
            rows.Caption(urlRequired
                ? Lang.T("An http:// or https:// address that returns share links (plain or Base64). It is downloaded right away.")
                : Lang.T("Leave the URL empty for an ordinary group. Only http:// and https:// addresses are accepted."));
            _name.Text = name;
            _url.Text = url;
            Button ok = RowLayout.AddDialogButtons(this, rows.Width, rows.Bottom, Lang.T("OK"));
            ok.Click += OnOk;
        }

        public string GroupName
        {
            get { return _name.Text.Trim(); }
        }

        public string GroupUrl
        {
            get { return _url.Text.Trim(); }
        }

        private void OnOk(object sender, EventArgs e)
        {
            string error = null;
            if (GroupName.Length == 0)
            {
                error = Lang.T("The name is empty.");
            }
            else if (GroupUrl.Length > 0 || _urlRequired)
            {
                error = Subscription.CheckUrl(GroupUrl);
            }
            if (error != null)
            {
                MessageBox.Show(this, error, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
        }
    }

    // Only the name of a group: "New group" and "Rename" of the group tabs' right-click menu.
    internal sealed class GroupNameForm : Form
    {
        private readonly TextBox _name;

        public GroupNameForm(string title, string name)
        {
            Text = title;
            RowLayout rows = new RowLayout(this, 60, 260);
            _name = rows.Text(Lang.T("Name"), 100);
            _name.Text = name;
            Button ok = RowLayout.AddDialogButtons(this, rows.Width, rows.Bottom, Lang.T("OK"));
            ok.Click += OnOk;
        }

        public string GroupName
        {
            get { return _name.Text.Trim(); }
        }

        private void OnOk(object sender, EventArgs e)
        {
            if (GroupName.Length == 0)
            {
                MessageBox.Show(this, Lang.T("The name is empty."), AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
        }
    }

    // List of groups: add, edit, delete. Changes are saved immediately through the store.
    internal sealed class GroupsForm : Form
    {
        private readonly ProfileStore _store;
        private readonly ListView _list = new ListView();

        public GroupsForm(ProfileStore store)
        {
            _store = store;
            Text = Lang.T("Groups");
            ClientSize = new Size(460, 260);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;

            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.HideSelection = false;
            _list.MultiSelect = false;
            _list.Location = new Point(12, 12);
            _list.Size = new Size(340, 236);
            _list.Columns.Add(Lang.T("Name"), 170);
            _list.Columns.Add(Lang.T("Profiles"), 60);
            _list.Columns.Add(Lang.T("Kind"), 90);
            _list.DoubleClick += OnEdit;
            Controls.Add(_list);

            AddButton(Lang.T("New..."), 12, OnNew);
            AddButton(Lang.T("Edit..."), 41, OnEdit);
            AddButton(Lang.T("Delete"), 70, OnDelete);
            Button close = AddButton(Lang.T("Close"), 225, null);
            close.DialogResult = DialogResult.OK;
            CancelButton = close;
            Fill();
        }

        private Button AddButton(string text, int y, EventHandler onClick)
        {
            Button button = new Button();
            button.Text = text;
            button.Size = new Size(90, 23);
            button.Location = new Point(360, y);
            if (onClick != null)
            {
                button.Click += onClick;
            }
            Controls.Add(button);
            return button;
        }

        private void Fill()
        {
            _list.Items.Clear();
            foreach (ProfileGroup g in _store.Groups)
            {
                ListViewItem item = new ListViewItem(g.Name);
                item.SubItems.Add(_store.ProfilesOf(g.Id).Count.ToString());
                item.SubItems.Add(g.IsSubscription ? Lang.T("Subscription") : Lang.T("Group"));
                item.Tag = g.Id;
                _list.Items.Add(item);
            }
        }

        private ProfileGroup Selected
        {
            get { return _list.SelectedItems.Count == 1 ? _store.FindGroup((int)_list.SelectedItems[0].Tag) : null; }
        }

        private void Try(MethodInvoker change)
        {
            try
            {
                change();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, Lang.T("The change was not saved.") + "\r\n\r\n" + ex.Message, AppInfo.Name,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            Fill();
        }

        private void OnNew(object sender, EventArgs e)
        {
            using (GroupEditForm form = new GroupEditForm(Lang.T("New group"), "", ""))
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    Try(delegate { _store.AddGroup(form.GroupName, form.GroupUrl); });
                }
            }
        }

        private void OnEdit(object sender, EventArgs e)
        {
            ProfileGroup g = Selected;
            if (g == null)
            {
                return;
            }
            using (GroupEditForm form = new GroupEditForm(Lang.T("Edit group"), g.Name, g.Url))
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    Try(delegate { _store.UpdateGroup(g.Id, form.GroupName, form.GroupUrl); });
                }
            }
        }

        private void OnDelete(object sender, EventArgs e)
        {
            ProfileGroup g = Selected;
            if (g == null)
            {
                return;
            }
            if (g.Id == ProfileGroup.DefaultId)
            {
                MessageBox.Show(this, Lang.T("The default group cannot be deleted."), AppInfo.Name,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            int count = _store.ProfilesOf(g.Id).Count;
            string question = count > 0 ? Lang.F("Delete group \"{0}\" together with its {1} profile(s)?", g.Name, count)
                : Lang.F("Delete group \"{0}\"?", g.Name);
            if (MessageBox.Show(this, question, AppInfo.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2) == DialogResult.Yes)
            {
                Try(delegate { _store.RemoveGroup(g.Id); });
            }
        }
    }
}
