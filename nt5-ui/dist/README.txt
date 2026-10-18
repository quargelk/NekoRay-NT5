NekoRay NT5 - proxy client for Windows XP and Windows 2000
==========================================================

Requirements: Windows XP SP3 (x86) or Windows 2000 SP4 (x86) with .NET Framework 2.0, and a processor
with SSE2 (Pentium 4, Athlon 64 or newer). The same files are used on both systems.
Windows 2000 SP4 x86 was tested without KernelX, with .NET Framework 2.0 and current root
certificates installed; other Windows 2000 configurations may behave differently.
Version 0.9.7-dev1 - a development release. Project page: https://github.com/quargelk/NekoRay-NT5
NekoRay NT5 is an independent adaptation of NekoRay (https://github.com/MatsuriDayo/nekoray), not
an official NekoRay release.

Files (keep them together in one folder):
  NekoRayNT5.exe   the program
  xray.exe         the core (Xray 1.8.3 built for Windows 2000/XP) - required to connect
  nt5helper.exe    HTTPS subscriptions with current TLS
  cacert.pem       trusted root certificates for HTTPS subscriptions (the Windows XP store is outdated)
  licenses\        licenses and the list of components (THIRD-PARTY.txt)

Start
  1. Run NekoRayNT5.exe. On the first start an assistant checks the files and helps to add a server.
  2. Add... - paste a share link (vless://, vmess://, trojan://, ss://, socks://), several links, a
     subscription address (https://...) or a happ://add/ link, or open a file. Check shows what was
     found; nothing is saved before Add.
     "Use Happ-compatible HWID" is offered for subscription addresses: switch it on only when the
     provider requires a device id. The id is random for this installation (Settings - Subscriptions).
  3. Select a server with one click, connect with a double click or Connect. Tick "System Proxy" so
     that programs which use the Windows proxy settings (Internet Explorer, many others) go through it.
  4. Disconnect restores the previous proxy settings; so does Exit, and the next start after a crash.

System Proxy is not a VPN: programs that ignore the Windows proxy settings are not redirected. Set
them to the local ports yourself - SOCKS5 127.0.0.1:2080, HTTP 127.0.0.1:2081 (shown in the status
bar; Settings - Connections). There is no TUN / system-wide mode.

Language: Settings - General - Language (English or Russian); it changes at the next start.
Groups: right-click a group tab to create, rename, update or delete a group.

Problems: Diagnostics (toolbar) checks the files, certificates and ports, and copies a report
without addresses, ids, keys or your user name. Logs: %APPDATA%\NekoRayNT5\logs.
