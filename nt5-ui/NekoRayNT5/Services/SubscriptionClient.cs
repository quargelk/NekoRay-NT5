using System;
using System.Collections.Generic;
using System.IO;
using NekoRayNT5.App;
using NekoRayNT5.Model;

namespace NekoRayNT5.Services
{
    // Downloads a subscription along the route chosen in the settings:
    //   direct - never through the running connection (nor through the system proxy, which may point
    //            to this program: downloads use no proxy at all, so they cannot loop into it);
    //   proxy  - only through the HTTP inbound of the running core (fails when nothing is connected);
    //   auto   - through the running core when there is one; if that fails before the server answers
    //            (the connection itself may be the broken part), once more directly.
    // Prepared on the UI thread, used on a worker thread; holds no references to the forms.
    internal sealed class SubscriptionClient
    {
        public DownloadTools Tools = new DownloadTools();
        public string Route = AppSettings.RouteAuto;
        // HTTP inbound port of the running core, 0 when nothing is connected.
        public int ProxyPort;
        // HWID to send when a subscription asks for it ("" when unknown).
        public string Hwid = "";

        public FetchResult Fetch(string url, bool useHwid)
        {
            List<KeyValuePair<string, string>> headers = useHwid && Hwid.Length > 0
                ? Services.Hwid.Headers(Hwid) : new List<KeyValuePair<string, string>>();
            if (Route == AppSettings.RouteDirect || (Route == AppSettings.RouteAuto && ProxyPort == 0))
            {
                FetchResult direct = Subscription.Fetch(url, 0, Tools, headers);
                direct.Via = Lang.T("directly");
                return direct;
            }
            if (ProxyPort == 0)
            {
                throw new FetchException(Lang.T("Subscriptions are set to download only through the active connection "
                    + "(Settings - Subscriptions), but nothing is connected. Connect first, or choose Auto or Direct."), false, null);
            }
            try
            {
                FetchResult proxied = Subscription.Fetch(url, ProxyPort, Tools, headers);
                proxied.Via = Lang.T("through the active connection");
                return proxied;
            }
            catch (FetchException first)
            {
                if (Route == AppSettings.RouteProxy || first.ServerAnswered)
                {
                    throw;
                }
                try
                {
                    FetchResult direct = Subscription.Fetch(url, 0, Tools, headers);
                    direct.Via = Lang.F("directly (through the active connection it failed: {0})", first.Message);
                    return direct;
                }
                catch (FetchException second)
                {
                    throw new FetchException(Lang.F("Through the active connection: {0}\r\nDirectly: {1}", first.Message, second.Message),
                        second.ServerAnswered, second.Meta);
                }
            }
        }

        public static string DescribeRoute(string route)
        {
            return route == AppSettings.RouteProxy ? Lang.T("only through the active connection")
                : route == AppSettings.RouteDirect ? Lang.T("directly") : Lang.T("automatically");
        }

        // Error text without the address or tokens.
        public static string SafeMessage(Exception ex, string url)
        {
            string message = ex.Message;
            if (!string.IsNullOrEmpty(url))
            {
                message = message.Replace(url, "<url>");
            }
            return Subscription.RedactUrls(message);
        }
    }
}
