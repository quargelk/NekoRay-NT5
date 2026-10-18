using System;
using System.Drawing;
using System.Windows.Forms;
using NekoRayNT5.App;
using NekoRayNT5.Core;
using NekoRayNT5.Model;
using NekoRayNT5.Platform;
using NekoRayNT5.Services;

namespace NekoRayNT5.UI
{
    // All settings in one dialog, grouped as General, Connections, Subscriptions, Routing & DNS
    // and Diagnostics. Settings is replaced only when the dialog is closed with OK and the values are valid.
    internal sealed class SettingsForm : Form
    {
        private AppSettings _settings;

        private NumericUpDown _socksPort;
        private NumericUpDown _httpPort;
        private ComboBox _logLevel;
        private TextBox _testUrl;
        private NumericUpDown _testTimeout;
        private CheckBox _closeToTray;
        private TextBox _proxyBypass;

        private ComboBox _defaultOutbound;
        private ComboBox _domainStrategy;
        private CheckBox _bypassPrivate;
        private TextBox _directDomains;
        private TextBox _proxyDomains;
        private TextBox _blockDomains;
        private TextBox _directIps;
        private TextBox _proxyIps;
        private TextBox _blockIps;

        private TextBox _remoteDns;
        private TextBox _directDns;

        private ComboBox _subscriptionRoute;
        private TextBox _hwidCustom;
        private Label _hwidShown;
        private ComboBox _language;

        public SettingsForm(AppSettings settings)
        {
            _settings = settings.Clone();
            Text = Lang.T("Settings");

            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            TabControl tabs = new TabControl();
            tabs.Location = new Point(8, 8);
            TabPage general = new TabPage(Lang.T("General"));
            TabPage connections = new TabPage(Lang.T("Connections"));
            TabPage subscriptions = new TabPage(Lang.T("Subscriptions"));
            TabPage routing = new TabPage(Lang.T("Routing & DNS"));
            TabPage diagnostics = new TabPage(Lang.T("Diagnostics"));
            tabs.TabPages.Add(general);
            tabs.TabPages.Add(connections);
            tabs.TabPages.Add(subscriptions);
            tabs.TabPages.Add(routing);
            tabs.TabPages.Add(diagnostics);
            Controls.Add(tabs);

            RowLayout g = new RowLayout(general, 170, 330);
            _language = g.Combo(Lang.T("Language"), Lang.Names, false);
            g.Caption(Lang.T("The language changes when the program is started again."));
            _closeToTray = g.Check(Lang.T("Closing the window hides it to the tray"));
            _testUrl = g.Text(Lang.T("Latency test URL (http://)"), 500);
            _testTimeout = g.Number(Lang.T("Test timeout, seconds"), 1, 120);
            g.Caption(Lang.T("Start with Windows and Remember last profile are in the Program menu."));

            RowLayout c = new RowLayout(connections, 170, 330);
            _socksPort = c.Number(Lang.T("SOCKS5 listen port"), 1, 65535);
            _httpPort = c.Number(Lang.T("HTTP listen port"), 1, 65535);
            c.Caption(Lang.T("The HTTP port is the one used as the Windows system proxy."));
            c.Caption(Lang.T("If Windows refuses a port (WSAEACCES, reserved range), choose another, e.g. 10808/10809."));
            _proxyBypass = c.Multiline(Lang.T("System proxy exceptions"), 60);
            c.Caption(Lang.T("Exceptions are separated by semicolons; <local> means names without dots."));

            RowLayout sb = new RowLayout(subscriptions, 170, 330);
            _subscriptionRoute = sb.Combo(Lang.T("Download subscriptions"), new string[] {
                Lang.T("Automatically (through the connection if active)"), Lang.T("Only through the active connection"),
                Lang.T("Directly") }, false);
            sb.Caption(Lang.T("Downloads never use the Windows system proxy, so they cannot loop through this program."));
            sb.Caption(Lang.T("Automatically: when the connection fails before the server answers, once more directly."));
            _hwidShown = sb.Caption(HwidText());
            _hwidCustom = sb.Text(Lang.T("Custom HWID (optional)"), Hwid.MaxLength);
            sb.Caption(Lang.T("Only a HWID that the provider authorised for you; empty uses the HWID of this installation."));
            sb.Caption(Lang.T("The HWID is sent only for subscriptions where \"Use Happ-compatible HWID\" is on."));
            Button newHwid = new Button();
            newHwid.Text = Lang.T("Make a new HWID for this installation");
            newHwid.AutoSize = true;
            newHwid.Location = new Point(12, sb.Bottom + 4);
            newHwid.Click += OnNewHwidClick;
            subscriptions.Controls.Add(newHwid);

            routing.AutoScroll = true;
            RowLayout r = new RowLayout(routing, 110, 390);
            _defaultOutbound = r.Combo(Lang.T("Default outbound"), new string[] { "proxy", "direct" }, false);
            _domainStrategy = r.Combo(Lang.T("Domain strategy"), AppSettings.DomainStrategies, false);
            _bypassPrivate = r.Check(Lang.T("Connect to private (LAN) addresses directly"));
            r.Caption(Lang.T("One rule per line, Xray syntax: domain:example.com, full:..., regexp:..., geosite:cn, 10.0.0.0/8, geoip:cn"));
            r.Caption(Lang.T("geosite: and geoip: rules need geosite.dat / geoip.dat next to xray.exe."));
            _directDomains = r.Multiline(Lang.T("Direct domains"), 44);
            _directIps = r.Multiline(Lang.T("Direct IP"), 44);
            _proxyDomains = r.Multiline(Lang.T("Proxy domains"), 44);
            _proxyIps = r.Multiline(Lang.T("Proxy IP"), 44);
            _blockDomains = r.Multiline(Lang.T("Block domains"), 44);
            _blockIps = r.Multiline(Lang.T("Block IP"), 44);

            r.Caption("DNS");
            _remoteDns = r.Text(Lang.T("Remote DNS"), 300);
            r.Caption(Lang.T("Examples: 8.8.8.8, https://8.8.8.8/dns-query, tcp://1.1.1.1. Empty: no DNS section, names are resolved by the remote server (and by Windows for direct connections)."));
            _directDns = r.Text(Lang.T("Direct DNS"), 300);
            r.Caption(Lang.T("Used for the \"Direct domains\" list when Remote DNS is set. \"localhost\" means the system resolver."));
            r.Caption(Lang.T("Xray uses its DNS only when the domain strategy is not AsIs."));

            RowLayout dg = new RowLayout(diagnostics, 170, 330);
            _logLevel = dg.Combo(Lang.T("Core log level"), AppSettings.LogLevels, false);
            dg.Caption(Lang.T("The core log is shown in the window only; it is never written to disk."));
            dg.Caption(Lang.F("Program log: {0}", DiagLog.FilePath));
            dg.Caption(Lang.T("Preferences - Diagnostics checks the program files, certificates and ports."));

            int width = Math.Max(Math.Max(g.Width, c.Width), r.Width);
            // Routing & DNS scrolls, so that the dialog fits on an 800x600 screen
            int height = Math.Min(Math.Max(Math.Max(c.Bottom, sb.Bottom + 34), r.Bottom) + 8, 430);
            tabs.Size = new Size(width + 8, height + 26);
            Button ok = RowLayout.AddDialogButtons(this, tabs.Right + 8, tabs.Bottom, Lang.T("OK"));
            ok.Click += OnOk;

            AppSettings s = _settings;
            _language.SelectedIndex = Math.Max(0, Array.IndexOf(Lang.Codes, s.Language));
            _socksPort.Value = s.SocksPort;
            _httpPort.Value = s.HttpPort;
            RowLayout.SelectItem(_logLevel, s.LogLevel);
            _testUrl.Text = s.TestUrl;
            _testTimeout.Value = s.TestTimeoutSeconds;
            _closeToTray.Checked = s.CloseToTray;
            _proxyBypass.WordWrap = true;
            _proxyBypass.Text = s.SystemProxyBypass;
            RowLayout.SelectItem(_defaultOutbound, s.DefaultOutbound);
            RowLayout.SelectItem(_domainStrategy, s.DomainStrategy);
            _bypassPrivate.Checked = s.BypassPrivate;
            _directDomains.Text = s.DirectDomains;
            _proxyDomains.Text = s.ProxyDomains;
            _blockDomains.Text = s.BlockDomains;
            _directIps.Text = s.DirectIps;
            _proxyIps.Text = s.ProxyIps;
            _blockIps.Text = s.BlockIps;
            _remoteDns.Text = s.RemoteDns;
            _directDns.Text = s.DirectDns;
            _subscriptionRoute.SelectedIndex = s.SubscriptionRoute == AppSettings.RouteProxy ? 1
                : s.SubscriptionRoute == AppSettings.RouteDirect ? 2 : 0;
            _hwidCustom.Text = s.HwidCustom;
            ShowHwid();
        }

        public AppSettings Settings
        {
            get { return _settings; }
        }

        private void ShowHwid()
        {
            _hwidShown.Text = HwidText();
        }

        private string HwidText()
        {
            return Lang.F("HWID of this installation: {0} (random, made once, not taken from the hardware)", Hwid.Mask(_settings.HwidGenerated));
        }

        private void OnNewHwidClick(object sender, EventArgs e)
        {
            if (MessageBox.Show(this, Lang.T("Make a new HWID for this installation?\r\n\r\nProviders that limit devices will see this "
                    + "computer as a new device; the old one may have to be removed in the provider's panel or bot. "
                    + "The new HWID is kept when you press OK."), AppInfo.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                return;
            }
            _settings.HwidGenerated = Hwid.Generate();
            ShowHwid();
        }

        private void OnOk(object sender, EventArgs e)
        {
            string error = null;
            string testUrl = _testUrl.Text.Trim();
            Uri uri;
            if (_socksPort.Value == _httpPort.Value)
            {
                error = Lang.T("The SOCKS5 and HTTP ports must be different.");
            }
            else if (Hwid.Check(_hwidCustom.Text.Trim()) != null)
            {
                error = Lang.F("Custom HWID: {0}", Hwid.Check(_hwidCustom.Text.Trim()));
            }
            else if (!testUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                     || !Uri.TryCreate(testUrl, UriKind.Absolute, out uri))
            {
                error = Lang.T("The latency test URL must be a valid address that starts with http://\r\n"
                        + "(HTTPS test addresses cannot be used by this program).");
            }
            if (error != null)
            {
                MessageBox.Show(this, error, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            AppSettings s = _settings.Clone();
            s.Language = Lang.Codes[Math.Max(0, _language.SelectedIndex)];
            s.SocksPort = (int)_socksPort.Value;
            s.HttpPort = (int)_httpPort.Value;
            s.LogLevel = (string)_logLevel.SelectedItem;
            s.TestUrl = testUrl;
            s.TestTimeoutSeconds = (int)_testTimeout.Value;
            s.CloseToTray = _closeToTray.Checked;
            s.SystemProxyBypass = _proxyBypass.Text.Replace("\r", "").Replace("\n", "").Trim();
            s.DefaultOutbound = (string)_defaultOutbound.SelectedItem;
            s.DomainStrategy = (string)_domainStrategy.SelectedItem;
            s.BypassPrivate = _bypassPrivate.Checked;
            s.DirectDomains = _directDomains.Text.Trim();
            s.ProxyDomains = _proxyDomains.Text.Trim();
            s.BlockDomains = _blockDomains.Text.Trim();
            s.DirectIps = _directIps.Text.Trim();
            s.ProxyIps = _proxyIps.Text.Trim();
            s.BlockIps = _blockIps.Text.Trim();
            s.RemoteDns = _remoteDns.Text.Trim();
            s.DirectDns = _directDns.Text.Trim();
            s.SubscriptionRoute = _subscriptionRoute.SelectedIndex == 1 ? AppSettings.RouteProxy
                : _subscriptionRoute.SelectedIndex == 2 ? AppSettings.RouteDirect : AppSettings.RouteAuto;
            s.HwidCustom = _hwidCustom.Text.Trim();
            _settings = s;
            DialogResult = DialogResult.OK;
        }
    }
}
