# Building NekoRay NT5 0.9.7-dev1

One application for Windows 2000 SP4 and Windows XP SP3 (x86): the same `NekoRayNT5.exe`,
`xray.exe` and `nt5helper.exe` on both. Everything below is built on a Windows host (tested on
Windows 11; the build tools are 32-bit and also run on XP).

| Part | Language / tool | Output |
|---|---|---|
| `nt5-ui/NekoRayNT5` | C# 2.0, .NET Framework 2.0, MSBuild 4.0 (or Visual Studio 2010) | `NekoRayNT5.exe` |
| `nt5-ui/helper` | Go 1.20 (patched toolchain below), standard library only | `nt5helper.exe` |
| Xray-core 1.8.3 | Go 1.20 (same toolchain), pinned commit + patches | `xray.exe` |

The two Go programs are not stored in Git (`*.exe` is ignored); they are copied from `nt5-ui/runtime/`
into every build. `nt5-ui/runtime/cacert.pem` (Mozilla CA bundle) is stored.

## 1. Go toolchain (Go 1.20 with Windows XP and Windows 2000 support)

1. Download `go-386.zip` of https://github.com/BieHDC/go-backports, release "go1.20-nt51"
   (tag `2023_02_12_120`): 244 696 306 bytes, SHA-256
   `34b9c70b5949874bf338f0aae4516a5e6db5c4a1aab76e7702f623d5947b9044`. Unpack it, e.g. to `C:\nt5\go`
   (this is GOROOT; it contains `bin\go.exe` for windows/386).
2. Apply the Windows 2000 patch in GOROOT and rebuild the linker (it writes PE version 5.0):

   ```
   cd C:\nt5\go
   patch -p1 < <repo>\nt5-ui\third_party\go1.20-nt51-nt50.patch
   set GOROOT=C:\nt5\go
   C:\nt5\go\bin\go.exe install cmd/link
   ```

   (`patch` is in Git for Windows: `"C:\Program Files\Git\usr\bin\patch.exe"`.) What the patch changes
   and why: `nt5-ui/third_party/README.md`, section "Windows 2000 (NT 5.0) patches".

Common environment for both Go programs:

```
set GOROOT=C:\nt5\go
set GOOS=windows
set GOARCH=386
set GO386=sse2
set CGO_ENABLED=0
```

## 2. nt5helper.exe

```
set NT5_GOROOT=C:\nt5\go
nt5-ui\helper\build.cmd          (add "test" to run its Go tests first; set XRAY_EXE to include the real core)
```

Writes `nt5-ui\runtime\nt5helper.exe`. Expected SHA-256 for this version:
`293cf6f15c767c7dd800483b76f35707bb17de4d810ca74411ca4513d5cc047b`.

## 3. xray.exe (Xray-core 1.8.3)

Sources, patches and licences: `nt5-ui/third_party/README.md`. The script
`nt5-ui/third_party/build-xray.sh` performs steps 1-5 below (Git Bash):

```
export GOROOT=C:/nt5/go
bash nt5-ui/third_party/build-xray.sh <work>
bash nt5-ui/third_party/build-xray.sh <work> <out>/xray-core-1.8.3-nt5-src.tar.gz
```

It writes `<work>\xray.exe`, prints its SHA-256 (exit code 3 if it is not the expected one) and refuses
a toolchain without the Windows 2000 patch. `XRAY_REPO`, `GOPROXY` and `GOMODCACHE` may point to local
copies; the result is the same. With the second argument it also writes the **complete corresponding
source** of the build (about 65 MB: a shallow Git checkout of Xray-core with the patch, the patched
wireguard-go, every other Go module as module-proxy files, the patches, `BUILD.txt`) and checks that
the archive alone rebuilds the same file offline. That archive is published with every binary release
(GPL-3.0; see `nt5-ui/third_party/README.md`, "Licensing").

The same steps by hand:

1. `git clone https://github.com/MatsuriDayo/Xray-core` and check out
   `f6843e4e1442341e88ac34c09ca2d243cdcc09f6` into `<work>\Xray-core`.
2. Put `github.com/sagernet/wireguard-go v0.0.0-20221116151939-c99467f53f2c` (from the Go module
   proxy or `go mod download`) into `<work>\wireguard-go-nt5` and apply
   `nt5-ui\third_party\wireguard-go-nt5-xp.patch` there.
3. In `<work>\Xray-core` apply `nt5-ui\third_party\xray-core-go-mod.patch` (the `replace` to `../wireguard-go-nt5`).
4. Build (all other modules exactly as in `go.sum`; after the first download it works offline with `GOPROXY=off`):

   ```
   cd <work>\Xray-core
   go build -mod=readonly -trimpath -ldflags "-s -w -buildid=" -o xray.exe ./main
   ```

5. Copy `xray.exe` to `nt5-ui\runtime\`. Expected SHA-256 for this version:
   `8fd72ecee3798cfc048c6e84db1b27c9dec1a5e6dede3d955a64d41c525862da`.

The same command with the unpatched go1.20-nt51 toolchain gives the earlier Windows XP-only core
(`7592b7ac3f819bc017892fc6c617bba6c2f89b33b7394e733fb4037cd9322246`, PE version 5.1).

Check that both Go programs will load on Windows 2000 (`subsys-version=5.0`, no
`AddVectoredExceptionHandler` among the kernel32 imports), in `nt5-ui\tests\peinfo`:
`go run . ..\..\runtime\xray.exe ..\..\runtime\nt5helper.exe`.

## 4. NekoRayNT5.exe and the package

```
nt5-ui\build.cmd Release         (or Debug; needs %WINDIR%\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe)
```

A Release build puts `NekoRayNT5.exe`, the files of `nt5-ui\runtime\`, `README.txt` and `licenses\`
into `nt5-ui\NekoRayNT5\bin\x86\Release\package\` - the folder that is copied to the target system.
The version is set in `nt5-ui\NekoRayNT5\Properties\AssemblyInfo.cs` (0.9.7.0 for 0.9.7-dev1).

## 5. Tests

| Command | What |
|---|---|
| `nt5-ui\tests\run-tests.cmd nt5-ui\runtime\xray.exe` (set `NT5HELPER_EXE` to include HTTPS through nt5helper) | self-test of the program logic, core start/stop/switch, ports, subscriptions (C# 2.0, also runs on XP) |
| `nt5-ui\tests\run-ui-smoke.cmd <xray.exe> Release en` (also `Debug`, `ru`) | window code smoke test; needs a build |
| `nt5-ui\helper\build.cmd test` | nt5helper Go tests (TLS, CA bundle, redirects, limits) |
| `nt5-ui\tests\nt50probe`: `go test .` | includes `tcp-refused`: a refused connection is recognised by its error code (WSAECONNREFUSED, 10061), not by the localised message text |
| `nt5-ui\tests\nt50probe`: `go test .` and `go build -tags nt50 .` | runtime probe for the target systems: goroutines, GC, exceptions, TCP/UDP, DNS, TLS, process start; `nt50probe e2e DIR [XRAY]` runs VLESS+REALITY+Vision through local servers (`tests\e2eserve`) with configs in `DIR\e2e\server.json` / `client.json` (make synthetic keys with `xray x25519` and `xray uuid`) |

End-to-end VLESS + REALITY + Vision through SOCKS5 and the HTTP proxy, with curl, synthetic keys and
local servers only (Git Bash): `bash nt5-ui/tests/e2e-reality.sh <client xray.exe> <server xray.exe> <e2eserve.exe>`
(build `e2eserve.exe` with `go build` in `nt5-ui/tests/e2eserve`).

The NT 5.0 code paths can be exercised on a newer Windows with a test build:
`-ldflags "-X runtime.nt50Simulate=1 -X internal/syscall/windows.nt50Simulate=1"` (never in release builds).
