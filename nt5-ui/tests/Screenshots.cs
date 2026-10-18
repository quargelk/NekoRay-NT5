// Makes review screenshots of every window from the real program with synthetic data only:
// documentation addresses (RFC 5737 / example.com), an all-zero UUID, no real servers, no test
// results. The system proxy is never switched on and nothing connects. Run by make-screenshots.cmd.
// They show the host Windows, so they are for checking texts and layout (both languages, 800x600),
// not for the README, which uses screenshots taken on Windows XP (docs/nt5-screenshots.md).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

internal static class Screenshots
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const string Uuid = "00000000-0000-4000-8000-000000000000";
    private const string Pbk = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    private static object Field(object target, string name)
    {
        return target.GetType().GetField(name, Any).GetValue(target);
    }

    private static object Call(object target, string method, params object[] args)
    {
        foreach (MethodInfo m in target.GetType().GetMethods(Any))
        {
            if (m.Name == method && m.GetParameters().Length == args.Length)
            {
                return m.Invoke(target, args);
            }
        }
        throw new MissingMethodException(method);
    }

    private static void Pump(int ms)
    {
        for (int waited = 0; waited < ms; waited += 20)
        {
            Application.DoEvents();
            Thread.Sleep(20);
        }
    }

    private static void Save(Form form, string file)
    {
        Pump(400);
        using (Bitmap bmp = new Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
            bmp.Save(file, ImageFormat.Png);
        }
        Console.WriteLine("saved " + Path.GetFileName(file));
    }

    private static bool NoAdd()
    {
        return false;
    }

    [STAThread]
    private static int Main(string[] args)
    {
        string dir = Path.GetDirectoryName(Application.ExecutablePath);
        string outDir = args.Length > 0 ? args[0] : dir;
        string language = args.Length > 1 ? args[1] : "en";
        string suffix = "-" + language + ".png";
        string data = Path.Combine(dir, "data");
        Directory.CreateDirectory(data);
        Environment.SetEnvironmentVariable("NEKORAY_NT5_DATA_DIR", data);
        // no first-run assistant; ports far from anything in use
        File.WriteAllText(Path.Combine(data, "settings.xml"), "<NekoRayNT5Settings formatVersion=\"1\"><socksPort>2080</socksPort>"
            + "<httpPort>2081</httpPort><firstRunDone>true</firstRunDone></NekoRayNT5Settings>");
        Assembly app = Assembly.LoadFrom(Path.Combine(dir, "NekoRayNT5.exe"));
        app.GetType("NekoRayNT5.App.Lang").GetMethod("Set").Invoke(null, new object[] { language });
        Application.EnableVisualStyles();
        app.GetType("NekoRayNT5.App.DiagLog").GetProperty("Directory").SetValue(null, Path.Combine(data, "logs"), null);

        Form main = (Form)Activator.CreateInstance(app.GetType("NekoRayNT5.UI.MainForm"), Any, null, new object[] { false }, null);
        string links =
            "vless://" + Uuid + "@203.0.113.10:443?security=reality&pbk=" + Pbk + "&sni=www.example.com&fp=chrome&sid=01&flow=xtls-rprx-vision#Example VLESS Reality\n"
            + "vless://" + Uuid + "@203.0.113.11:443?security=tls&type=ws&host=cdn.example.com&path=%2Fws&sni=cdn.example.com#Example VLESS WebSocket\n"
            + "trojan://example-password@198.51.100.20:443?security=tls&sni=trojan.example.net#Example Trojan\n"
            + "ss://" + Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes("chacha20-ietf-poly1305:example-password"))
            + "@198.51.100.30:8388#Example Shadowsocks\n"
            + "socks://198.51.100.40:1080#Example SOCKS5\n";
        main.StartPosition = FormStartPosition.Manual;
        main.Location = new Point(40, 40);
        main.Size = new Size(800, 600);
        main.Show();
        Pump(1500);
        Call(main, "ImportText", links, 0);
        ListView list = (ListView)Field(main, "listProfiles");
        list.Items[0].Selected = true;
        Call(main, "UpdateUi");
        Save(main, Path.Combine(outDir, "main-window" + suffix));

        object client = Call(main, "CreateSubscriptionClient");
        object store = Field(main, "_store");
        Form add = (Form)Activator.CreateInstance(app.GetType("NekoRayNT5.UI.AddForm"), Any, null,
            new object[] { client, Field(store, "_profiles"), "vless://" + Uuid + "@203.0.113.12:443?security=reality&pbk=" + Pbk
                + "&sni=www.example.org&fp=chrome&sid=02#Example new server\n" + links.Split('\n')[2] + "\nnot-a-link\n" }, null);
        add.StartPosition = FormStartPosition.Manual;
        add.Location = new Point(60, 60);
        add.Show();
        Save(add, Path.Combine(outDir, "add-preview" + suffix));
        add.Close();

        Form diag = (Form)Activator.CreateInstance(app.GetType("NekoRayNT5.UI.DiagnosticsForm"), Any, null, new object[] {
            Delegate.CreateDelegate(app.GetType("NekoRayNT5.UI.DependencyCheck"), main, "BuildDependencyReport"), null, "" }, null);
        diag.StartPosition = FormStartPosition.Manual;
        diag.Location = new Point(60, 60);
        diag.Show();
        Save(diag, Path.Combine(outDir, "diagnostics" + suffix));
        diag.Close();

        Form settings = (Form)Activator.CreateInstance(app.GetType("NekoRayNT5.UI.SettingsForm"), Any, null,
            new object[] { Field(main, "_settings") }, null);
        settings.StartPosition = FormStartPosition.Manual;
        settings.Location = new Point(60, 60);
        settings.Show();
        string[] pages = new string[] { "general", "connections", "subscriptions", "routing", "diagnostics" };
        foreach (Control c in settings.Controls)
        {
            TabControl tabs = c as TabControl;
            if (tabs != null)
            {
                for (int i = 0; i < tabs.TabCount && i < pages.Length; i++)
                {
                    tabs.SelectedIndex = i;
                    Save(settings, Path.Combine(outDir, "settings-" + pages[i] + suffix));
                }
            }
        }
        settings.Close();

        object profile = ((System.Collections.IList)Field(store, "_profiles"))[0];
        Form edit = (Form)Activator.CreateInstance(app.GetType("NekoRayNT5.UI.ProfileEditForm"), Any, null, new object[] { profile, false }, null);
        edit.StartPosition = FormStartPosition.Manual;
        edit.Location = new Point(60, 20);
        edit.Show();
        Save(edit, Path.Combine(outDir, "profile-edit" + suffix));
        edit.Close();

        Form groups = (Form)Activator.CreateInstance(app.GetType("NekoRayNT5.UI.GroupsForm"), Any, null, new object[] { store }, null);
        groups.StartPosition = FormStartPosition.Manual;
        groups.Location = new Point(60, 60);
        groups.Show();
        Save(groups, Path.Combine(outDir, "groups" + suffix));
        groups.Close();

        Form first = (Form)Activator.CreateInstance(app.GetType("NekoRayNT5.UI.FirstRunForm"), Any, null, new object[] {
            Delegate.CreateDelegate(app.GetType("NekoRayNT5.UI.DependencyCheck"), main, "BuildDependencyReport"),
            Delegate.CreateDelegate(app.GetType("NekoRayNT5.UI.AddRequest"), typeof(Screenshots).GetMethod("NoAdd", Any)) }, null);
        first.StartPosition = FormStartPosition.Manual;
        first.Location = new Point(60, 60);
        first.Show();
        Save(first, Path.Combine(outDir, "first-run" + suffix));
        first.Close();

        Call(main, "Cleanup");
        main.Dispose();
        return 0;
    }
}
