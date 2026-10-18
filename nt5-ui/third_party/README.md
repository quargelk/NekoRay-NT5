# Third-party sources of the distributed binaries

`xray.exe` and `nt5helper.exe` are built for Windows 2000 (NT 5.0) and Windows XP with a patched Go
toolchain; one build of each runs on both (since 0.9.7). This file names
the exact sources of the binaries in `runtime/`, so that they can be rebuilt and so that their
source can be offered together with a binary release (GPL-3.0 requires the complete corresponding
source of `xray.exe`; see "Licensing" below).

NekoRay NT5 is an independent, community-maintained fork and adaptation of NekoRay
(https://github.com/MatsuriDayo/nekoray, GPL-3.0) for Windows XP SP3 and Windows 2000 SP4 (x86). It
is not an official NekoRay release and is not endorsed by the NekoRay maintainers. The project as a
whole is licensed under GPL-3.0 (the repository's `LICENSE`), except where a component or file has a
separately documented license. The third-party components named here remain under their own licenses,
and the Windows 2000 patch of the Go toolchain is distributed under Go's BSD-3-Clause terms (see
"Windows 2000 patch: licence and provenance" below).

## xray.exe — Xray-core 1.8.3

| Item | Value |
|---|---|
| Repository | https://github.com/MatsuriDayo/Xray-core (the Xray-core fork used by Nekoray) |
| Commit | `f6843e4e1442341e88ac34c09ca2d243cdcc09f6` (2023-07-19, "fix matsuridayo/nekoray#710"), version 1.8.3 |
| Change | `xray-core-go-mod.patch`: `replace github.com/sagernet/wireguard-go => ../wireguard-go-nt5` |
| wireguard-go | `github.com/sagernet/wireguard-go v0.0.0-20221116151939-c99467f53f2c` plus `wireguard-go-nt5-xp.patch` |
| Other modules | exactly as in `go.sum` of that commit; the list embedded in the binary is in `../licenses/xray-modules/INDEX.txt` |
| Build settings (embedded) | `go build -trimpath`, `CGO_ENABLED=0`, `GOOS=windows`, `GOARCH=386`, `GO386=sse2`, package `./main` |
| Build command | `go build -mod=readonly -trimpath -ldflags "-s -w -buildid=" -o xray.exe ./main` (offline: `GOPROXY=off`, module cache with the `go.sum` modules) |
| Script | `build-xray.sh WORK_DIR [SOURCE_ARCHIVE.tar.gz]`: fetches the sources above, applies the patches, builds, compares the SHA-256 (`8fd72ece…`) and optionally writes the corresponding source archive (see `../../BUILDING.md`) |

`wireguard-go-nt5-xp.patch` removes the mandatory integrity label (`S:(ML;;NWNRNX;;;HI)`) from the
security descriptor of the UAPI pipe. Integrity labels exist only since Windows Vista; on Windows XP
parsing that descriptor fails and the package panics while it is initialised, so `xray.exe` would not
start at all.

## nt5helper.exe

Source: `../helper` (GPL-3.0). Build: `../helper/build.cmd` (same settings as above, plus `-ldflags "-s -w"`),
with `NT5_GOROOT` set to the toolchain with the NT 5.0 patches (below).
Modules: none besides the Go standard library (`../helper/go.mod`).

## Go toolchain

Go 1.20 "release branch for NT 5.1 (Windows XP)" with backports of the Windows XP support that the
official Go removed in Go 1.11. It was used from an archive `go-386.zip` (244 696 306 bytes,
SHA-256 `34b9c70b5949874bf338f0aae4516a5e6db5c4a1aab76e7702f623d5947b9044`, files dated 2023-02-12).
**Origin (verified 2026-10-17):** this is the release asset `go-386.zip` of
https://github.com/BieHDC/go-backports, release "go1.20-nt51" (tag `2023_02_12_120`, source branch
`release-branch.go1.20-nt51`, a fork of golang/go). The downloaded asset has exactly the same size
and SHA-256 as the archive above. Its patches are marked `BACKPORT(NT_51)` in the sources. The fork
keeps the Go license (BSD-3-Clause, Copyright (c) 2009 The Go Authors,
`../licenses/Go-BSD-3-Clause.txt`). It is a build tool; the Go runtime and standard library linked
into both programs are distributed under that license, together with the NT5-specific patch below.

### Windows 2000 (NT 5.0) patches (0.9.7)

`go1.20-nt51-nt50.patch` (applied to the go1.20-nt51 sources, marked `BACKPORT(NT_50)`; rebuild
`cmd/link` afterwards with `go install cmd/link`) makes the same binaries load and work on NT 5.0.
Every fallback is chosen at run time by whether the function exists, so XP keeps its previous paths:

| Missing on Windows 2000 | Fallback |
|---|---|
| PE subsystem 5.1 (loader refuses) | linker writes subsystem/OS version 5.0 |
| `AddVectoredExceptionHandler` (static import) | looked up at run time; without it an SEH frame on every runtime thread plus a top-level filter turn faults into Go panics |
| `RtlGenRandom` (runtime, `crypto/rand`) | `CryptGenRandom` of the default CSP |
| `RtlGetNtVersionNumbers` | checked before use (long path check only) |
| `ConnectEx` (every TCP dial; error "failed to find ConnectEx") | non-blocking `connect` + `select`, honours deadline and cancel |
| `GetAddrInfoW` (needs XP SP2) | `gethostbyname` / `getservbyname` (IPv4) |
| `GetAdaptersAddresses` | `net.Interfaces` returns an error instead of panicking |

Test builds can force these paths on a newer Windows with
`-ldflags "-X runtime.nt50Simulate=1 -X internal/syscall/windows.nt50Simulate=1"`; release builds never set it.

Because of `GO386=sse2`, both programs need a processor with SSE2 (Pentium 4, Athlon 64 or newer).

## Licensing (technical reading, not legal advice)

- `xray.exe` links MPL-2.0 code (Xray-core), GPL-3.0-or-later code (`github.com/sagernet/sing`,
  `sing-shadowsocks`) and MIT/BSD/Apache-2.0 modules. MPL-2.0 allows distribution of such a larger work
  under the GPL (MPL-2.0 section 3.3). The binary must then come with its complete corresponding
  source: the commit above, the patches in this folder, the module sources of `go.sum`, and the build settings.
- Apache-2.0 modules with NOTICE files (grpc, yaml.v2): the notices are in `../licenses/xray-modules`.
- `wireguard-go` (sagernet fork, MIT): its license text is in `../licenses/xray-modules`; the only
  change is `wireguard-go-nt5-xp.patch` here.
- The module list in `../licenses/xray-modules/INDEX.txt` was compared with the build information
  embedded in `runtime/xray.exe` (`go version -m`, 36 `dep` lines plus the wireguard-go `=>` line):
  identical, module paths and versions (checked 2026-10-18).
- **Corresponding source of `xray.exe`** (GPL-3.0 section 6): `build-xray.sh WORK_DIR ARCHIVE` writes
  one archive with everything needed to rebuild that exact file: a shallow Git checkout of the
  Xray-core commit with `xray-core-go-mod.patch` applied, the patched wireguard-go, the source zips of
  all other Go modules the build uses (as Go module proxy files, verified by `go.sum`), the patches and
  the build instructions. The script rebuilds from the archive alone, offline, and checks that the
  result is bit-identical (verified 2026-10-18: `8fd72ece…`, archive about 63 MB). Plan for every binary
  release: attach this archive to the same release (section 6(a)/(d): source offered from the same
  place as the binary), and keep it as long as the binary is offered.
- **Toolchain source.** The Go runtime and standard library compiled into both programs come from the
  toolchain. Its source is the public `go-386.zip` above (it contains `go/src`; BSD-3-Clause) plus
  `go1.20-nt51-nt50.patch`. To avoid depending on a third-party download staying available, a binary
  release should also attach `go-386.zip` (or its `go/src`) next to the Xray source archive.
- `nt5helper.exe`: its corresponding source is `../helper` of this repository plus the toolchain.
- The source of NekoRay NT5 itself (this repository) can be published now; a binary release needs
  the two attachments above.

### Windows 2000 patch: licence and provenance (`go1.20-nt51-nt50.patch`)

**Licence.** The patch is distributed under the BSD-3-Clause terms of Go (`../licenses/Go-BSD-3-Clause.txt`,
the `LICENSE` of the toolchain source), not under the project's GPL-3.0. Distribute it, and the
patched toolchain source, together with that licence text.

**What it is.** The patch is NT5-specific: it is not part of upstream Go (https://github.com/golang/go),
of the go1.20-nt51 backport (BieHDC/go-backports, whose branches contain no NT 5.0 changes) or of the
original NekoRay. It changes nine existing Go files and adds five new ones.

- The nine existing files (`cmd/link/internal/ld/pe.go`, `crypto/rand/rand_windows.go`,
  `net/fd_windows.go`, `net/interface_windows.go`, `net/lookup_windows.go`, `runtime/os_windows.go`,
  `runtime/rt0_windows_386.s`, `runtime/signal_windows.go`, `runtime/sys_windows_386.s`) keep their
  original `Copyright ... The Go Authors` notices and BSD-style headers unchanged. The patch's context
  lines reproduce Go and go1.20-nt51 code, which stays under the Go license.
- The five new files (`internal/syscall/windows/nt50_windows.go`, `net/fd_windows_nt50.go`,
  `net/lookup_windows_nt50.go`, `runtime/signal_windows_nt50_386.go`,
  `runtime/signal_windows_nt50_other.go`) start with
  `// NekoRay NT5 addition for Windows 2000 (NT 5.0); not part of upstream Go (see nt5-ui/third_party/README.md).`
  followed by Go's two-line BSD-style reference to the `LICENSE` file. They carry no copyright line.
  Earlier versions of the patch had `Copyright 2011/2023 The Go Authors` lines here; these were
  removed because they did not reflect the files' origin.

**Provenance.** According to the local Claude Code session record (2026-10-17), the NT5-specific
compatibility code was developed with Claude Code (an AI coding assistant) under the direction of the
NekoRay NT5 maintainer. Parts follow approaches of earlier Go releases that still supported Windows
2000 or used the same Windows functions (SEH exception frames as in Go 1.2, `gethostbyname` /
`getservbyname` as in Go 1.4, `CryptGenRandom` as in Go 1.10). A comparison with those releases found
no copied text. AI assistance does not by itself establish a human author or a copyright owner, and
no separate copyright holder is claimed for these additions.

Changing only comment lines (the same number of lines) does not change the compiled binaries. After
the header change above, the patch was reapplied to the original `go-386.zip`, `cmd/link` was rebuilt,
and `build-xray.sh` (with its source archive) and the helper build gave the same SHA-256 as before:
`xray.exe` `8fd72ece…`, `nt5helper.exe` `293cf6f1…` (2026-10-18).
