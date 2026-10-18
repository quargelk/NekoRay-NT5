# NekoRay NT5

A small proxy client for **Windows XP** and **Windows 2000**: a Windows Forms
(.NET Framework 2.0) program that manages profiles and subscriptions and runs the
[Xray](https://github.com/XTLS/Xray-core) core. Its design follows
[Nekoray](https://github.com/MatsuriDayo/nekoray) 3.17.

> **Status: 0.9.7-dev1, a development version - not a release.** What has been verified, and where,
> is listed under [Verified so far](#verified-so-far).
> Overview, screenshots and downloads: [../README.md](../README.md).

## What it does - and what it does not

The whole workflow:

**Add a profile or subscription -> select a profile -> Connect -> optionally tick System Proxy ->
Disconnect.**

- Connect starts Xray with the selected profile. Xray listens on local ports only: SOCKS5
  `127.0.0.1:2080` and HTTP `127.0.0.1:2081` (changeable in Settings).
- **System Proxy** points the Windows (WinINet) proxy settings at the local HTTP port. Programs
  that use those settings - Internet Explorer and many others - then go through the connection.
  The previous settings are saved and restored on Disconnect, Exit, a core error, Windows log-off
  and, after a crash, at the next start.
- **System Proxy is not a VPN.** Programs that ignore the Windows proxy settings (many games,
  console tools, other browsers with their own settings) are not redirected; set them to the
  SOCKS5 or HTTP port yourself. DNS of such programs is not protected either.
- There is **no TUN / system-wide mode** (an experimental one was removed in 0.9.6).

## Features

- Profiles: VLESS (incl. REALITY and Vision), VMess, Trojan, Shadowsocks, SOCKS5; TCP, WebSocket,
  gRPC and HTTP/2 transports as supported by Xray 1.8.3.
- One **Add** command for everything: a share link, a list of links (plain, Base64 or URL-encoded,
  also copied from bulleted or numbered lists), a file, a subscription address (`https://...`) or a
  `happ://add/<address>` link. The source is checked first; a preview shows the servers found,
  duplicates and unusable lines; nothing is saved before you press Add. Ordinary text (a copied web
  page or chat) is recognised as such instead of producing thousands of errors. Encrypted
  `happ://crypt...` links are **not** supported.
- Subscriptions: stable profiles on update, kept unchanged when an update fails; downloaded
  automatically, only through the active connection, or directly - never through the Windows
  system proxy. Optional **Happ-compatible HWID** per subscription (a random id made once for this
  installation, or your own authorised id).
- HTTPS subscriptions with TLS 1.2/1.3 on XP through the bundled helper `nt5helper.exe`, verified
  against the bundled Mozilla CA list. Certificate checks are never switched off.
- Groups with a right-click menu on the tabs (new, rename, update subscription, delete with a
  confirmation; the default group cannot be deleted), routing and DNS rules, latency tests,
  profile backup and restore, tray icon.
- **English and Russian** interface (Settings -> General -> Language; English by default; applied
  at the next start).
- **Diagnostics**: checks the program files, certificates and ports, explains what is missing and
  copies a report without addresses, ids, keys or user names.

## Operating systems

| System | Status |
|---|---|
| Windows XP SP3, 32-bit | Main target, no system extensions needed. With 0.9.7-dev1 the user reports working: VLESS share links, HTTP subscriptions, URL Test, connect, disconnect, reconnect, browser traffic through the proxy. |
| Windows 2000 SP4, 32-bit | Second target, the same files as on XP: `xray.exe` and `nt5helper.exe` are built for NT 5.0 (`third_party/README.md`). With 0.9.7-dev1 the user reports the same results as on XP on Windows 2000 SP4 with .NET Framework 2.0 and current root certificates installed, **without** KernelX. Other Windows 2000 configurations: not tested. Details and limitations: `../docs/nt5-win2000.md`. |
| Windows Vista and later | Not a target; the program runs, but use the official Nekoray/NekoBox there. |

Requirements: **.NET Framework 2.0** and a processor with **SSE2** (Pentium 4, Athlon 64 or newer):
`xray.exe` and `nt5helper.exe` are built with `GO386=sse2`.

## Install and use

1. Copy the whole release folder (`NekoRayNT5.exe`, `xray.exe`, `nt5helper.exe`, `cacert.pem`,
   `README.txt`, `licenses\`) to the computer and run `NekoRayNT5.exe`. A short assistant checks the
   files on the first start.
2. **Add...** - paste a link or subscription address, press **Check**, then **Add**.
3. Select a server with one click; a double click (or **Connect**) connects.
4. Tick **System Proxy** for programs that use the Windows proxy settings; set other programs to
   SOCKS5 `127.0.0.1:2080` or HTTP `127.0.0.1:2081`.
5. **Disconnect** stops the core and restores the previous proxy settings.

Data is kept in `%APPDATA%\NekoRayNT5` (profiles, settings, logs). Credentials are stored there in
plain text; they are never written to the logs.

## Building

Visual Studio 2010 SP1 (Visual C# 2010 Express is enough), .NET Framework 2.0, no NuGet:

1. Put `xray.exe` and `nt5helper.exe` into `nt5-ui\runtime\` (they are not kept in Git; see
   `runtime\README.txt`). `cacert.pem` is already there.
2. Open `nt5-ui\NekoRayNT5.sln`, choose `Debug | x86`, press **F5**.
3. `Release | x86` assembles the release folder in `NekoRayNT5\bin\x86\Release\package\`
   (program, runtime files, `README.txt`, `licenses\`; no symbols, no test files).

Project settings: `ToolsVersion 4.0`, target framework v2.0, `LangVersion ISO-2` (C# 2.0), x86,
warnings as errors. `build.cmd [Release|Debug]` builds the same solution with MSBuild 4.0.

`nt5helper.exe` (Go, standard library only) is built by `helper\build.cmd` with the Go 1.20
toolchain that has the Windows XP backports
([BieHDC/go-backports](https://github.com/BieHDC/go-backports), release `go1.20-nt51`) with the
Windows 2000 patch from `third_party` applied (put it into `nt5-ui\toolchain\go` or set `NT5_GOROOT`).
How `xray.exe` is built, and from which sources, is in [third_party/README.md](third_party/README.md)
(`third_party/build-xray.sh`); the full build is described in [../BUILDING.md](../BUILDING.md).

Translations: `NekoRayNT5\Lang\ru.txt` (the English text is the key; see `App\Lang.cs`). The tests
fail when a text of the program has no translation or a translation breaks a `{0}` placeholder.

## Tests

```
tests\run-tests.cmd runtime\xray.exe                  logic, storage, subscriptions, core, translations (C# 2.0 compiler)
tests\run-tests.cmd runtime\xray.exe TestPorts        only the named sections
tests\run-ui-smoke.cmd runtime\xray.exe Debug en      the window code and the layout of every window (en or ru)
tests\make-screenshots.cmd [folder] [en|ru]           pictures of every window for a visual check (not for this README)
helper\build.cmd test                                 Go tests of nt5helper (XRAY_EXE=... adds the real core)
```

All tests use local servers and synthetic profiles; they never change the real proxy settings and
need no real subscription. The real system proxy is tested by hand on a test system - see
`../docs/nt5-test-checklist.md`.

## Verified so far

| What | Where | Result |
|---|---|---|
| Debug and Release builds (MSBuild 4.0, .NET 2.0 reference assemblies) | Windows 11 (host) | no errors, no warnings |
| Automatic tests: self-tests, UI smoke in English and Russian, Go tests | Windows 11 (host) | all pass - this is **not** a test on XP |
| VLESS + REALITY + Vision through the system proxy | Windows XP SP3 (version 0.9.0, by the user) | works |
| Servers and subscriptions | Windows XP (version 0.9.5, by the user) | works |
| Version 0.9.6-rc2 (portfix test package): port fix, switching, restart, languages | Windows XP SP3 (by the user) | works (`../docs/nt5-test-checklist.md`) |
| 0.9.7-dev1: builds, self-test 351/351, UI smoke 52/52 (Debug/Release, en/ru), nt5helper Go tests, NT 5.0 probe, VLESS+REALITY+Vision e2e | Windows 11 (host; NT 5.0 paths simulated) | all pass - **not** a test on the target systems |
| 0.9.7-dev1: VLESS links, HTTP subscriptions, URL Test, connect, disconnect, reconnect, browser traffic | Windows XP SP3 (by the user) | works |
| 0.9.7-dev1: the same | Windows 2000 SP4, .NET 2.0, root certificates, without KernelX (by the user) | works |
| Visual Studio 2010 SP1 | - | **not verified** (the project is in VS2010 format) |

## Known limitations

- No Hysteria, TUIC or Naive (Xray 1.8.3 has none); no Clash / sing-box / Xray-JSON subscriptions.
- No traffic statistics or connection list (no core API in this program).
- Subscriptions are updated by hand (no timer).

## Licenses and credits

NekoRay NT5 and `nt5helper` are GPL-3.0, like Nekoray. The release folder carries the licenses of
everything it contains in `licenses\` (`THIRD-PARTY.txt` lists them): Xray-core (MPL-2.0) and its Go
modules, the Go runtime (BSD-3-Clause) and the Mozilla CA bundle (MPL-2.0). Because `xray.exe` links
GPL-3.0 modules, it is distributed under GPL-3.0 terms with its corresponding source - see
[third_party/README.md](third_party/README.md). A public binary release needs that source published
first.

Thanks to the authors of Nekoray / NekoBox (MatsuriDayo), Project X (Xray-core), gVisor,
sagernet, and the Go backport for Windows XP (BieHDC).
