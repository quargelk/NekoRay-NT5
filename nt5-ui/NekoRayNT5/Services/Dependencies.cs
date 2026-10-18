using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NekoRayNT5.App;
using NekoRayNT5.Core;
using NekoRayNT5.Model;
using NekoRayNT5.Platform;

namespace NekoRayNT5.Services
{
    internal enum DependencyStatus
    {
        Ok,
        // works, but something is worth knowing (optional part missing, old CA bundle...)
        Warning,
        // a function that needs it does not work
        Missing
    }

    internal sealed class DependencyItem
    {
        public string Name = "";
        public DependencyStatus Status = DependencyStatus.Ok;
        // one line for the list
        public string Summary = "";
        // what is wrong and what to do, in plain words
        public string Details = "";
        // an official page with the component, or ""
        public string Link = "";

        public string StatusText
        {
            get { return Status == DependencyStatus.Ok ? Lang.T("OK") : Status == DependencyStatus.Warning ? Lang.T("Note") : Lang.T("Missing"); }
        }
    }

    internal sealed class DependencyReport
    {
        public List<DependencyItem> Items = new List<DependencyItem>();
        public bool CanConnect = true;

        public int Problems
        {
            get
            {
                int n = 0;
                foreach (DependencyItem item in Items)
                {
                    if (item.Status == DependencyStatus.Missing)
                    {
                        n++;
                    }
                }
                return n;
            }
        }

        public string ToText()
        {
            StringBuilder sb = new StringBuilder();
            foreach (DependencyItem item in Items)
            {
                sb.Append(item.StatusText).Append("  ").Append(item.Name).Append(": ").Append(item.Summary).Append("\r\n");
            }
            return sb.ToString();
        }
    }

    // What the program needs, checked in one place: for the diagnostics window, the first-run
    // assistant and the explanations before Connect. Nothing is downloaded or installed here.
    internal static class Dependencies
    {
        // Official sources (opened in the browser only when the user asks).
        public const string CaBundleSource = "https://curl.se/docs/caextract.html";

        public static DependencyReport Check(string coreProblem, AppSettings settings)
        {
            DependencyReport report = new DependencyReport();

            DependencyItem os = Add(report, "Windows");
            Version v = Environment.OSVersion.Version;
            os.Summary = AppInfo.OsDescription;
            if (v.Major == 5 && v.Minor == 0)
            {
                os.Status = DependencyStatus.Warning;
                // The core and the helper are built for NT 5.0 (0.9.7); whether they load here is shown by their own rows.
                os.Details = Lang.T("Windows 2000: xray.exe and nt5helper.exe are built for Windows 2000 (NT 5.0); the Core and Helper "
                             + "rows show whether they load here. Tested: Windows 2000 SP4 x86 without KernelX, with .NET Framework 2.0 "
                             + "and the required root certificates installed. Other configurations may behave differently. "
                             + "Windows XP SP3 is the main target system.");
            }
            else
            {
                os.Details = Lang.T("Supported. Windows XP SP3 is the main target system.");
            }

            DependencyItem core = Add(report, Lang.T("Core (xray.exe)"));
            if (!File.Exists(AppPaths.CorePath))
            {
                core.Status = DependencyStatus.Missing;
                core.Summary = Lang.T("not found");
                core.Details = Lang.F("xray.exe is not next to NekoRayNT5.exe ({0}). Without it the program cannot "
                               + "connect. Copy xray.exe from the NekoRay NT5 package again: it is a build of Xray 1.8.3 for "
                               + "Windows XP; the official Xray downloads do not run on XP.", AppPaths.BaseDir);
                report.CanConnect = false;
            }
            else if (coreProblem != null)
            {
                core.Status = DependencyStatus.Missing;
                core.Summary = Lang.T("cannot run here");
                core.Details = coreProblem;
                report.CanConnect = false;
            }
            else
            {
                core.Summary = Lang.T("present, loadable");
                core.Details = Lang.T("Xray core next to the program. It is started only by this program and stopped with it.");
            }

            DependencyItem helper = Add(report, Lang.T("Helper (nt5helper.exe)"));
            string helperProblem = File.Exists(AppPaths.HelperPath) ? ExecutableCheck.Check(AppPaths.HelperPath) : Lang.T("not found");
            if (helperProblem != null)
            {
                helper.Status = DependencyStatus.Missing;
                helper.Summary = File.Exists(AppPaths.HelperPath) ? Lang.T("cannot run here") : Lang.T("not found");
                helper.Details = Lang.F("nt5helper.exe downloads HTTPS subscriptions with current TLS. {0} Without it HTTPS "
                                 + "subscriptions need curl.exe. Share links and http:// subscriptions work.",
                                 File.Exists(AppPaths.HelperPath) ? helperProblem
                                    : Lang.T("Copy it from the NekoRay NT5 package next to NekoRayNT5.exe."));
            }
            else
            {
                helper.Summary = Lang.T("present, loadable");
                helper.Details = Lang.T("Used for HTTPS subscriptions (TLS 1.2/1.3, certificates checked).");
            }

            DependencyItem ca = Add(report, Lang.T("Certificates (cacert.pem)"));
            ca.Link = CaBundleSource;
            CaBundleInfo bundle = CaBundle.Inspect(AppPaths.CaBundlePath);
            if (!bundle.Exists)
            {
                ca.Status = DependencyStatus.Missing;
                ca.Summary = Lang.T("not found");
                ca.Details = Lang.T("Without cacert.pem, HTTPS subscriptions are checked against the Windows certificate store, which "
                             + "is outdated on Windows XP (\"unknown certificate authority\"). Copy cacert.pem from the package, "
                             + "or download the current Mozilla CA bundle (cacert.pem) from the curl project page and put it "
                             + "next to NekoRayNT5.exe.");
            }
            else if (bundle.Problem != null)
            {
                ca.Status = DependencyStatus.Missing;
                ca.Summary = Lang.T("damaged");
                ca.Details = bundle.Problem + " " + Lang.T("Replace it with a fresh copy from the package or from the curl project page.");
            }
            else
            {
                int age = CaBundle.AgeDays(bundle, DateTime.UtcNow);
                ca.Status = age > 730 ? DependencyStatus.Warning : DependencyStatus.Ok;
                ca.Summary = bundle.ToString();
                ca.Details = Lang.T("Trusted root certificates for HTTPS subscriptions (Mozilla CA bundle). Certificates are always checked.")
                             + (age > 730 ? " " + Lang.T("The bundle is older than two years: replace it with a current one.") : "");
            }

            DependencyItem curl = Add(report, Lang.T("curl.exe (optional)"));
            curl.Summary = File.Exists(AppPaths.CurlPath) ? Lang.T("present") : Lang.T("not present (not needed)");
            curl.Details = Lang.T("Only a fallback for HTTPS subscriptions when nt5helper.exe cannot run.");

            DependencyItem ports = Add(report, Lang.T("Local ports"));
            string socks = CoreLauncher.CheckPortFree(settings.ListenAddress, settings.SocksPort, true);
            string http = CoreLauncher.CheckPortFree(settings.ListenAddress, settings.HttpPort, false);
            if (socks == null && http == null)
            {
                ports.Summary = Lang.F("SOCKS {0}, HTTP {1}: free", settings.SocksPort, settings.HttpPort);
                ports.Details = Lang.F("The core listens on these ports ({0}).", settings.ListenAddress);
            }
            else
            {
                // while connected the core itself holds them
                ports.Status = DependencyStatus.Warning;
                ports.Summary = Lang.F("SOCKS {0}, HTTP {1}: in use or refused", settings.SocksPort, settings.HttpPort);
                ports.Details = (socks != null ? Lang.F("SOCKS port {0}: {1}.", settings.SocksPort, socks) + " " : "")
                                + (http != null ? Lang.F("HTTP port {0}: {1}.", settings.HttpPort, http) + " " : "")
                                + Lang.T("This is normal while connected (the core uses them). Otherwise choose other ports in "
                                + "Settings - Connections.");
            }

            return report;
        }

        private static DependencyItem Add(DependencyReport report, string name)
        {
            DependencyItem item = new DependencyItem();
            item.Name = name;
            report.Items.Add(item);
            return item;
        }
    }
}
