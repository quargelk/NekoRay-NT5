using System;
using System.Windows.Forms;
using NekoRayNT5.App;
using NekoRayNT5.Model;
using NekoRayNT5.Services;

namespace NekoRayNT5.UI
{
    // Editor of one profile. Only the fields that apply to the profile type and to the chosen
    // transport and security are enabled; the others are stored empty.
    // The caller reads Profile only after OK, and it is always a valid profile.
    internal sealed class ProfileEditForm : Form
    {
        private readonly ProfileEntry _profile;

        private TextBox _name;
        private TextBox _address;
        private NumericUpDown _port;
        private TextBox _uuid;
        private TextBox _username;
        private TextBox _password;
        private ComboBox _encryption;
        private ComboBox _flow;
        private ComboBox _network;
        private TextBox _host;
        private TextBox _path;
        private ComboBox _security;
        private TextBox _sni;
        private TextBox _alpn;
        private ComboBox _fingerprint;
        private CheckBox _allowInsecure;
        private TextBox _publicKey;
        private TextBox _shortId;
        private TextBox _spiderX;

        public ProfileEditForm(ProfileEntry profile, bool isNew)
        {
            _profile = profile.Clone();
            Text = Lang.F(isNew ? "New profile - {0}" : "Edit profile - {0}", _profile.Type.ToUpperInvariant());
            Build();
            LoadValues();
            UpdateEnabled(null, EventArgs.Empty);
        }

        public ProfileEntry Profile
        {
            get { return _profile; }
        }

        private void Build()
        {
            ProfileEntry p = _profile;
            RowLayout rows = new RowLayout(this, 130, 330);
            _name = rows.Text(Lang.T("Name"), 200);
            _address = rows.Text(Lang.T("Address"), 255);
            _port = rows.Number(Lang.T("Port"), 1, 65535);
            if (p.UsesUuid)
            {
                _uuid = rows.Text("UUID", 64);
            }
            if (p.UsesUsername)
            {
                _username = rows.Text(Lang.T("User name"), 255);
            }
            if (p.UsesPassword)
            {
                _password = rows.Text(Lang.T("Password"), 255);
            }
            if (p.Type == ProfileEntry.TypeVmess)
            {
                _encryption = rows.Combo(Lang.T("Security (cipher)"), ProfileValidator.VmessSecurities, false);
            }
            else if (p.Type == ProfileEntry.TypeShadowsocks)
            {
                _encryption = rows.Combo(Lang.T("Method"), ProfileValidator.ShadowsocksMethods, false);
            }
            if (p.Type == ProfileEntry.TypeVless)
            {
                _flow = rows.Combo(Lang.T("Flow"), new string[] { Lang.T("(none)"), ProfileValidator.Flows[1], ProfileValidator.Flows[2] }, false);
            }
            if (p.UsesStream)
            {
                _network = rows.Combo(Lang.T("Transport"), ProfileValidator.Networks, false);
                _host = rows.Text(Lang.T("Host"), 255);
                _path = rows.Text(Lang.T("Path / service name"), 255);
                _security = rows.Combo(Lang.T("Security"), ProfileValidator.Securities, false);
                _sni = rows.Text("SNI", 255);
                _alpn = rows.Text(Lang.T("ALPN (comma separated)"), 64);
                _fingerprint = rows.Combo(Lang.T("uTLS fingerprint"), new string[] { "", "chrome", "firefox", "edge", "safari",
                    "360", "qq", "ios", "android", "random", "randomized" }, true);
                _allowInsecure = rows.Check(Lang.T("Allow insecure (do not verify the certificate)"));
                _publicKey = rows.Text(Lang.T("Reality public key"), 64);
                _shortId = rows.Text(Lang.T("Reality short id"), 16);
                _spiderX = rows.Text("Reality SpiderX", 255);
                _network.SelectedIndexChanged += UpdateEnabled;
                _security.SelectedIndexChanged += UpdateEnabled;
            }
            Button ok = RowLayout.AddDialogButtons(this, rows.Width, rows.Bottom, Lang.T("OK"));
            ok.Click += OnOk;
        }

        private void LoadValues()
        {
            ProfileEntry p = _profile;
            _name.Text = p.Name;
            _address.Text = p.Address;
            _port.Value = Math.Max(_port.Minimum, Math.Min(_port.Maximum, p.Port));
            Set(_uuid, p.Uuid);
            Set(_username, p.Username);
            Set(_password, p.Password);
            if (_encryption != null)
            {
                RowLayout.SelectItem(_encryption, p.Encryption);
            }
            if (_flow != null)
            {
                _flow.SelectedIndex = Math.Max(0, Array.IndexOf(ProfileValidator.Flows, p.Flow));
            }
            if (p.UsesStream)
            {
                RowLayout.SelectItem(_network, p.Network);
                RowLayout.SelectItem(_security, p.Security);
                _host.Text = p.Host;
                _path.Text = p.Path;
                _sni.Text = p.Sni;
                _alpn.Text = p.Alpn;
                _fingerprint.Text = p.Fingerprint;
                _allowInsecure.Checked = p.AllowInsecure;
                _publicKey.Text = p.PublicKey;
                _shortId.Text = p.ShortId;
                _spiderX.Text = p.SpiderX;
            }
        }

        private static void Set(TextBox box, string value)
        {
            if (box != null)
            {
                box.Text = value;
            }
        }

        private void UpdateEnabled(object sender, EventArgs e)
        {
            if (!_profile.UsesStream)
            {
                return;
            }
            string network = (string)_network.SelectedItem;
            string security = (string)_security.SelectedItem;
            bool tls = security != "none";
            bool reality = security == "reality";
            _host.Enabled = network == "ws" || network == "http";
            _path.Enabled = network != "tcp";
            _sni.Enabled = tls;
            _alpn.Enabled = security == "tls";
            _fingerprint.Enabled = tls;
            _allowInsecure.Enabled = security == "tls";
            _publicKey.Enabled = reality;
            _shortId.Enabled = reality;
            _spiderX.Enabled = reality;
            if (_flow != null)
            {
                _flow.Enabled = network == "tcp" && tls;
            }
            if (reality && _fingerprint.Text.Trim().Length == 0)
            {
                _fingerprint.Text = ProfileEntry.DefaultFingerprint;
            }
        }

        // Value of a field, or "" when the field does not apply to the current choice.
        private static string Value(Control control)
        {
            return control != null && control.Enabled ? control.Text.Trim() : "";
        }

        private void OnOk(object sender, EventArgs e)
        {
            ProfileEntry p = _profile.Clone();
            p.Address = _address.Text.Trim();
            p.Port = (int)_port.Value;
            p.Uuid = Value(_uuid);
            p.Username = Value(_username);
            // passwords are kept exactly as typed
            p.Password = _password == null ? "" : _password.Text;
            if (_encryption != null)
            {
                p.Encryption = (string)_encryption.SelectedItem;
            }
            if (p.UsesStream)
            {
                p.Network = (string)_network.SelectedItem;
                p.Security = (string)_security.SelectedItem;
                p.Host = Value(_host);
                p.Path = Value(_path);
                p.Sni = Value(_sni);
                p.Alpn = Value(_alpn);
                p.Fingerprint = Value(_fingerprint);
                p.AllowInsecure = _allowInsecure.Enabled && _allowInsecure.Checked;
                p.PublicKey = Value(_publicKey);
                p.ShortId = Value(_shortId);
                p.SpiderX = Value(_spiderX);
                p.Flow = _flow != null && _flow.Enabled ? ProfileValidator.Flows[Math.Max(0, _flow.SelectedIndex)] : "";
            }
            p.Name = _name.Text.Trim();
            if (p.Name.Length == 0 && p.Address.Length > 0)
            {
                p.Name = p.DisplayAddress;
            }

            string error = ProfileValidator.Validate(p);
            if (error != null)
            {
                MessageBox.Show(this, error, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Copy(p, _profile);
            DialogResult = DialogResult.OK;
        }

        private static void Copy(ProfileEntry from, ProfileEntry to)
        {
            to.Name = from.Name;
            to.Address = from.Address;
            to.Port = from.Port;
            to.Uuid = from.Uuid;
            to.Username = from.Username;
            to.Password = from.Password;
            to.Encryption = from.Encryption;
            to.Flow = from.Flow;
            to.Network = from.Network;
            to.Host = from.Host;
            to.Path = from.Path;
            to.Security = from.Security;
            to.Sni = from.Sni;
            to.Alpn = from.Alpn;
            to.Fingerprint = from.Fingerprint;
            to.AllowInsecure = from.AllowInsecure;
            to.PublicKey = from.PublicKey;
            to.ShortId = from.ShortId;
            to.SpiderX = from.SpiderX;
        }
    }
}
