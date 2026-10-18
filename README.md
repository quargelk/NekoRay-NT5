# NekoRay NT5

A small proxy client for **Windows XP SP3** and **Windows 2000 SP4** (32-bit x86): a Windows Forms
(.NET Framework 2.0) program that manages servers and subscriptions and runs the
[Xray](https://github.com/XTLS/Xray-core) 1.8.3 core, built so that it runs on these systems.

NekoRay NT5 is an independent, community-maintained adaptation of
[NekoRay](https://github.com/MatsuriDayo/nekoray) (GPL-3.0) for Windows NT 5.x. Its design and
defaults follow NekoRay 3.17, but the program is a separate implementation in C# 2.0, and its
Windows 2000 / XP compatibility work (the patched Go toolchain, the Xray build, the helper) is
specific to this project. It is **not** an official NekoRay release and is not endorsed by the
NekoRay maintainers.

> **Status: 0.9.7-dev1 - a development release, not a stable version.** See
> [Tested configurations](#tested-configurations) for what has been verified and where.

![Main window](docs/screenshots/main-window.png)

*Main window with example servers (documentation addresses, no real servers). All screenshots on
this page are captures of the real program taken on a Windows 11 build host with synthetic data;
screenshots from Windows XP and Windows 2000 will be added.*

## Contents

- [What it does](#what-it-does)
- [Features](#features)
- [Requirements](#requirements)
- [Tested configurations](#tested-configurations)
- [Download and install](#download-and-install)
- [First start and everyday use](#first-start-and-everyday-use)
- [System Proxy](#system-proxy)
- [Troubleshooting](#troubleshooting)
- [Known limitations](#known-limitations)
- [Building from source](#building-from-source)
- [License and acknowledgements](#license-and-acknowledgements)

## What it does

**Add a server or subscription -> select a server -> Connect -> optionally tick System Proxy ->
Disconnect.**

- **Connect** starts Xray with the selected server. Xray listens on local ports only: SOCKS5
  `127.0.0.1:2080` and HTTP `127.0.0.1:2081` (changeable in Settings).
- **System Proxy** points the Windows (WinINet) proxy settings at the local HTTP port, so that
  Internet Explorer and other programs that use those settings go through the connection.
- **Disconnect** stops the core and restores the previous proxy settings.

![Connected to a local test server](docs/screenshots/connected-local-test.png)

*Connected state: the real program running Xray 1.8.3 against a local VLESS + REALITY + Vision test
server on 127.0.0.1 (synthetic keys). System Proxy was left off on the build host.*

## Features

- **Protocols:** VLESS (including REALITY and Vision), VMess, Trojan, Shadowsocks, SOCKS5; TCP,
  WebSocket, gRPC and HTTP/2 transports, as supported by Xray 1.8.3.
- **One Add command for everything:** a share link (`vless://`, `vmess://`, `trojan://`, `ss://`,
  `socks://`), a list of links (plain, Base64 or URL-encoded), a file, a subscription address
  (`http://` or `https://`) or a `happ://add/<address>` link. A preview shows the servers found,
  duplicates and unusable lines; nothing is saved before you press **Add**.
- **Subscriptions:** profiles stay stable on update and are kept when an update fails; downloads go
  automatically, only through the active connection, or directly - never through the Windows system
  proxy. Optional Happ-compatible HWID per subscription (a random id made for this installation, or
  your own).
- **HTTPS with current TLS on old Windows:** subscriptions are downloaded by the bundled helper
  `nt5helper.exe` with TLS 1.2/1.3, verified against the bundled Mozilla CA list (`cacert.pem`).
  Certificate checks are never switched off.
- **Groups, latency tests (URL Test), routing and DNS rules, backup and restore, tray icon.**
- **English and Russian interface** (Settings -> General -> Language).
- **Diagnostics:** checks the program files, certificates and ports, explains what is missing and
  copies a report without addresses, ids, keys or user names.

![Add servers or a subscription](docs/screenshots/add-servers.png)

![Subscription settings](docs/screenshots/settings-subscriptions.png)

## Requirements

**To run the program** (end users):

| Requirement | Details |
|---|---|
| Operating system | Windows XP SP3 x86, or Windows 2000 SP4 x86 |
| .NET Framework 2.0 | required on both systems |
| Processor | with SSE2 (Pentium 4, Athlon 64 or newer): `xray.exe` and `nt5helper.exe` are built with `GO386=sse2` |
| Root certificates | recommended on both systems and installed in the tested Windows 2000 configuration. The program's own HTTPS subscription downloads use the bundled `cacert.pem`; the Windows certificate store matters for browsers and for the optional fallback download paths |

No extended kernel (KernelX) and no other system extension is needed for the tested configurations
below. Windows Vista and later are not targets; use the official NekoRay / NekoBox there.

**To build it** (developers only): see [Building from source](#building-from-source).

## Tested configurations

| What | Where | Result |
|---|---|---|
| VLESS links, HTTP subscriptions, URL Test, connect, disconnect, reconnect, browser traffic through the proxy (0.9.7-dev1) | **Windows XP SP3 x86**, no system extensions - manual test by the maintainer | works |
| The same (0.9.7-dev1) | **Windows 2000 SP4 x86, without KernelX**, with .NET Framework 2.0 and the required root certificates installed - manual test by the maintainer | works |
| Release build; self-test (351 checks); UI smoke test (52 checks, English and Russian); nt5helper Go tests; Windows 2000 runtime probe; VLESS + REALITY + Vision end-to-end test | Windows 11 build host (Windows 2000 code paths in simulation) | all pass - **not** a test on the target systems |

The Windows 2000 result applies to the configuration described above. Other Windows 2000
installations (without SP4, with other updates, other hardware) have not been tested and may behave
differently. Details: [docs/nt5-win2000.md](docs/nt5-win2000.md).

## Download and install

1. Download `NekoRayNT5-v0.9.7-dev1-win32.zip` from the
   [Releases](https://github.com/quargelk/NekoRay-NT5/releases) page.
2. Optionally verify it: the release lists its SHA-256, and `SHA256SUMS.txt` inside the archive
   lists every file.
3. Unpack the whole folder anywhere, for example `C:\Programs\NekoRayNT5`. Keep the files together:

   | File | Purpose |
   |---|---|
   | `NekoRayNT5.exe` | the program |
   | `xray.exe` | the core (Xray 1.8.3 built for Windows 2000 / XP) - required to connect |
   | `nt5helper.exe` | HTTPS subscription downloads with current TLS |
   | `cacert.pem` | Mozilla CA bundle used by `nt5helper.exe` |
   | `README.txt` | short instructions |
   | `licenses\` | licenses of all components (`THIRD-PARTY.txt` lists them) |

4. Install .NET Framework 2.0 if it is not present, then run `NekoRayNT5.exe`.

There is no installer. Data is kept in `%APPDATA%\NekoRayNT5` (servers, settings, logs).
Credentials are stored there in plain text; they are never written to the logs.

## First start and everyday use

1. On the first start a short assistant checks the files.
2. **Add...** - paste a link or subscription address, press **Check**, then **Add**.
3. Select a server with one click; a double click (or **Connect**) connects. The status bar shows
   the local SOCKS5 and HTTP ports.
4. Tick **System Proxy** for programs that use the Windows proxy settings, or set other programs to
   SOCKS5 `127.0.0.1:2080` / HTTP `127.0.0.1:2081` yourself.
5. **Disconnect** stops the core and restores the previous proxy settings.

Groups: right-click a group tab to create, rename, update or delete a group.
Language: Settings -> General -> Language (applied at the next start).

![Main window in Russian](docs/screenshots/main-window-ru.png)

## System Proxy

- The previous proxy settings are saved and restored on Disconnect, Exit, a core error, Windows
  log-off and, after a crash, at the next start.
- **System Proxy is not a VPN.** Programs that ignore the Windows proxy settings (many games, console
  tools, browsers with their own proxy settings) are not redirected; set them to the local SOCKS5 or
  HTTP port. DNS of such programs is not protected either.
- There is no TUN / system-wide mode.

## Troubleshooting

- **Diagnostics** (toolbar) checks the program files, the certificates and the local ports and
  explains what is missing. **Copy report** copies a report without addresses, ids, keys or user
  names.
- Logs: `%APPDATA%\NekoRayNT5\logs`.
- *"The core was not found"* - `xray.exe` must be in the same folder as `NekoRayNT5.exe`.
- *A local port is already used* - change the SOCKS5 / HTTP ports in Settings -> Connections.
- *HTTPS subscription fails* - check that `nt5helper.exe` and `cacert.pem` are next to the program
  (Diagnostics shows both) and that the system date is correct.
- *Connected, but a program does not use the connection* - it ignores the Windows proxy settings;
  configure its proxy manually (see [System Proxy](#system-proxy)).

![Diagnostics](docs/screenshots/diagnostics.png)

## Known limitations

- Development release: no stable version yet.
- No Hysteria, TUIC or Naive (Xray 1.8.3 has none); no Clash / sing-box / Xray-JSON subscriptions;
  encrypted `happ://crypt...` links are not supported.
- No traffic statistics or connection list; subscriptions are updated by hand (no timer).
- No TUN / system-wide mode (System Proxy only).
- Windows 2000: only the configuration in [Tested configurations](#tested-configurations) has been
  verified. The Diagnostics window shows "Warning" for Windows 2000 by design and names the tested
  configuration.

## Building from source

Everything is built on a Windows host; full instructions with expected hashes are in
[BUILDING.md](BUILDING.md).

| Part | Tools | Output |
|---|---|---|
| `nt5-ui/NekoRayNT5` (C# 2.0) | .NET Framework 2.0 (reference assemblies) and MSBuild 4.0 from .NET Framework 4.x (`nt5-ui\build.cmd Release`), or Visual Studio 2010 | `NekoRayNT5.exe` and the release folder `nt5-ui\NekoRayNT5\bin\x86\Release\package\` |
| `nt5-ui/helper` (Go, standard library only) | Go 1.20 for Windows XP ([BieHDC/go-backports](https://github.com/BieHDC/go-backports), release `go1.20-nt51`) with the Windows 2000 patch `nt5-ui/third_party/go1.20-nt51-nt50.patch` | `nt5helper.exe` |
| Xray-core 1.8.3 | the same toolchain, Git for Windows (Git Bash, `patch`), `nt5-ui/third_party/build-xray.sh` | `xray.exe`, and optionally its complete corresponding source archive |

`build-xray.sh` fetches the pinned Xray-core commit and modules, applies the patches and checks that
the result is byte-identical to the released `xray.exe`
(SHA-256 `8fd72ecee3798cfc048c6e84db1b27c9dec1a5e6dede3d955a64d41c525862da`).

Tests (see [BUILDING.md](BUILDING.md#5-tests)): `nt5-ui\tests\run-tests.cmd`,
`nt5-ui\tests\run-ui-smoke.cmd`, `nt5-ui\helper\build.cmd test`, `nt5-ui/tests/nt50probe` and
`nt5-ui/tests/e2e-reality.sh`. All tests use local servers and synthetic data.

Repository layout:

```
nt5-ui/NekoRayNT5   the program (C# 2.0, Windows Forms)
nt5-ui/helper       nt5helper (Go)
nt5-ui/third_party  Go toolchain patch, Xray patches, build-xray.sh, sources and licences of the binaries
nt5-ui/tests        self-test, UI smoke test, runtime probe, end-to-end test
nt5-ui/licenses     license texts shipped with the program
nt5-ui/runtime      cacert.pem (xray.exe and nt5helper.exe are built, not stored)
docs/               technical notes, Windows 2000 analysis, test checklist, screenshots
```

## License and acknowledgements

NekoRay NT5 is licensed under the **GNU General Public License v3.0** ([LICENSE](LICENSE)), like the
original NekoRay, except where a component or file has a separately documented license.
Third-party components remain under their own licenses:

- **Xray-core** (MPL-2.0) and its Go modules (MIT, BSD, Apache-2.0, MPL-2.0, GPL-3.0-or-later).
  Because GPL-3.0 modules are linked in, `xray.exe` is distributed under GPL-3.0 terms; its complete
  corresponding source is attached to each release (`xray-core-1.8.3-nt5-src.tar.gz`).
- **Go runtime and standard library** (BSD-3-Clause, The Go Authors), including the Windows XP
  backport by BieHDC.
- **The Windows 2000 patch of the Go toolchain** (`nt5-ui/third_party/go1.20-nt51-nt50.patch`) is
  specific to this project and distributed under Go's BSD-3-Clause terms. It was developed with
  Claude Code (an AI coding assistant) under the direction of the NekoRay NT5 maintainer; no separate
  copyright holder is claimed for these additions. Details:
  [nt5-ui/third_party/README.md](nt5-ui/third_party/README.md).
- **Mozilla CA bundle** (`cacert.pem`, MPL-2.0), as published by the curl project.

The release package carries all license texts in `licenses\`; `licenses\THIRD-PARTY.txt` lists the
components.

Thanks to the authors of NekoRay / NekoBox (MatsuriDayo), Project X (Xray-core), sagernet, gVisor,
the Go authors and BieHDC (Go backport for Windows XP).
