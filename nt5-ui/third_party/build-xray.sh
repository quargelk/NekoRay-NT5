#!/usr/bin/env bash
# Builds xray.exe (Xray-core 1.8.3 for Windows 2000/XP) from the pinned sources described in README.md,
# and optionally writes the complete corresponding source of that build as one archive (GPL-3.0
# section 6: the archive to publish with a binary release).
# Usage (Git Bash on Windows): build-xray.sh WORK_DIR [SOURCE_ARCHIVE.tar.gz]
#   GOROOT     the go1.20-nt51 toolchain with go1.20-nt51-nt50.patch applied (see ../../BUILDING.md)
#   XRAY_REPO  Git repository of the Xray-core fork (default https://github.com/MatsuriDayo/Xray-core)
#   GOPROXY, GOMODCACHE as usual (default: the public Go module proxy; modules are checked against go.sum)
# WORK_DIR receives Xray-core/, wireguard-go-nt5/ and xray.exe; existing checkouts there are reused.
# The archive contains Xray-core (shallow Git checkout of the pinned commit plus the go.mod patch),
# wireguard-go-nt5 (patched), goproxy/ (the module zips of go.sum that the build uses), the patches,
# README.md and BUILD.txt; the build is repeated from the archive alone and must give the same file.
# Exit code: 0 ok, 2 bad environment or sources, 3 built but the SHA-256 differs from the expected one.
set -eu
XRAY_COMMIT=f6843e4e1442341e88ac34c09ca2d243cdcc09f6
WG_MODULE=github.com/sagernet/wireguard-go
WG_VERSION=v0.0.0-20221116151939-c99467f53f2c
EXPECTED_SHA256=8fd72ecee3798cfc048c6e84db1b27c9dec1a5e6dede3d955a64d41c525862da
XRAY_REPO=${XRAY_REPO:-https://github.com/MatsuriDayo/Xray-core}

if [ $# -lt 1 ]; then
  echo "usage: $0 WORK_DIR [SOURCE_ARCHIVE.tar.gz]"
  exit 2
fi
HERE=$(cd "$(dirname "$0")" && pwd)
mkdir -p "$1"
WORK=$(cd "$1" && pwd)
ARCHIVE=${2:-}
fail() { echo "error: $*" >&2; exit 2; }
winpath() { if command -v cygpath >/dev/null; then cygpath -m "$1"; else echo "$1"; fi; }

[ -n "${GOROOT:-}" ] || fail "set GOROOT to the patched go1.20-nt51 toolchain"
GO="$GOROOT/bin/go"; [ -f "$GO.exe" ] && GO="$GO.exe"
[ -x "$GO" ] || fail "no go in $GOROOT/bin"
"$GO" version | grep -q 'go1\.20' || fail "GOROOT is not Go 1.20"
[ -f "$GOROOT/src/net/fd_windows_nt50.go" ] || fail "GOROOT lacks go1.20-nt51-nt50.patch (Windows 2000 support)"
export GOOS=windows GOARCH=386 GO386=sse2 CGO_ENABLED=0 GOTOOLCHAIN=local GOWORK=off GOFLAGS=-modcacherw

go_build() { # $1 Xray-core directory, $2 output file
  (cd "$1" && "$GO" build -mod=readonly -trimpath -ldflags "-s -w -buildid=" -o "$(winpath "$2")" ./main)
}
sha() { sha256sum "$1" | cut -d' ' -f1; }

# 1. Xray-core at the pinned commit, with the go.mod replace
if [ ! -d "$WORK/Xray-core/.git" ]; then
  git clone --quiet --no-checkout "$XRAY_REPO" "$WORK/Xray-core"
  git -C "$WORK/Xray-core" -c advice.detachedHead=false checkout --quiet "$XRAY_COMMIT"
fi
[ "$(git -C "$WORK/Xray-core" rev-parse HEAD)" = "$XRAY_COMMIT" ] || fail "$WORK/Xray-core is not at $XRAY_COMMIT"
if ! grep -q 'wireguard-go-nt5' "$WORK/Xray-core/go.mod"; then
  git -C "$WORK/Xray-core" apply "$HERE/xray-core-go-mod.patch"
fi

# 2. wireguard-go from the module proxy, with the Windows XP change
if [ ! -f "$WORK/wireguard-go-nt5/go.mod" ]; then
  dir=$(cd "$WORK" && GOFLAGS="-mod=mod -modcacherw" "$GO" mod download -json "$WG_MODULE@$WG_VERSION" \
        | sed -n 's/^[[:space:]]*"Dir": "\(.*\)",$/\1/p' | sed 's/\\\\/\//g')
  [ -n "$dir" ] && [ -d "$dir" ] || fail "could not download $WG_MODULE@$WG_VERSION"
  cp -R "$dir" "$WORK/wireguard-go-nt5"
  chmod -R u+w "$WORK/wireguard-go-nt5"
  (cd "$WORK/wireguard-go-nt5" && patch --quiet -p1 < "$HERE/wireguard-go-nt5-xp.patch")
fi
grep -q 'S:(ML' "$WORK/wireguard-go-nt5/ipc/uapi_windows.go" && fail "wireguard-go-nt5 is not patched"

# 3. Build
go_build "$WORK/Xray-core" "$WORK/xray.exe"
got=$(sha "$WORK/xray.exe")
echo "built $WORK/xray.exe"
echo "SHA-256 $got"
status=0
if [ "$got" != "$EXPECTED_SHA256" ]; then
  echo "warning: expected $EXPECTED_SHA256 (toolchain or sources differ)" >&2
  status=3
fi
[ -n "$ARCHIVE" ] || exit $status

# 4. Corresponding source archive, then a rebuild from it alone (offline, its own module cache)
TMP=$(mktemp -d)
trap 'chmod -R u+w "$TMP" 2>/dev/null; rm -rf "$TMP"' EXIT
NAME=xray-core-1.8.3-nt5-src
SRC="$TMP/$NAME"
mkdir -p "$SRC/patches"
git -c advice.detachedHead=false clone --quiet --depth 1 "file://$(winpath "$WORK/Xray-core")" "$SRC/Xray-core"
git -C "$SRC/Xray-core" remote set-url origin "$XRAY_REPO"
[ "$(git -C "$SRC/Xray-core" rev-parse HEAD)" = "$XRAY_COMMIT" ] || fail "shallow copy is not at $XRAY_COMMIT"
git -C "$SRC/Xray-core" apply "$HERE/xray-core-go-mod.patch"
cp -R "$WORK/wireguard-go-nt5" "$SRC/wireguard-go-nt5"
cp "$HERE"/*.patch "$HERE/README.md" "$HERE/build-xray.sh" "$SRC/patches/"
(export GOMODCACHE="$(winpath "$TMP/modcache")"; go_build "$SRC/Xray-core" "$TMP/xray-1.exe")
mv "$TMP/modcache/cache/download" "$SRC/goproxy"
cat > "$SRC/BUILD.txt" <<EOF
Corresponding source of xray.exe in NekoRay NT5 (Xray-core 1.8.3 for Windows 2000 and Windows XP,
SHA-256 $EXPECTED_SHA256).

Xray-core/         https://github.com/MatsuriDayo/Xray-core commit $XRAY_COMMIT (shallow Git
                   checkout; the Git data is kept because the build records the commit in the binary),
                   with patches/xray-core-go-mod.patch applied.
wireguard-go-nt5/  $WG_MODULE $WG_VERSION with patches/wireguard-go-nt5-xp.patch applied.
goproxy/           every other Go module the build uses, as Go module proxy files; go.sum checks them.
patches/           the changes, the build script and README.md (sources, licences, toolchain).

Toolchain: Go 1.20 for Windows XP, https://github.com/BieHDC/go-backports release "go1.20-nt51"
(go-386.zip, SHA-256 34b9c70b5949874bf338f0aae4516a5e6db5c4a1aab76e7702f623d5947b9044; its source is
in that zip under go/src), with the Windows 2000 patch go1.20-nt51-nt50.patch (in patches/) applied and
cmd/link rebuilt. See patches/README.md.

Build (Git Bash on Windows, offline):
  export GOROOT=<patched toolchain> GOOS=windows GOARCH=386 GO386=sse2 CGO_ENABLED=0
  export GOPROXY=file:///<this folder>/goproxy GOSUMDB=off GOFLAGS=-modcacherw GOMODCACHE=<empty folder>
  cd Xray-core
  go build -mod=readonly -trimpath -ldflags "-s -w -buildid=" -o xray.exe ./main
EOF
(export GOPROXY="file:///$(winpath "$SRC/goproxy" | sed 's|^/||')" GOSUMDB=off GOMODCACHE="$(winpath "$TMP/modcache2")"
 go_build "$SRC/Xray-core" "$TMP/xray-2.exe")
for f in "$TMP/xray-1.exe" "$TMP/xray-2.exe"; do
  [ "$(sha "$f")" = "$got" ] || fail "build from the archive differs: $(sha "$f")"
done
echo "rebuilt from the archive alone: same SHA-256"
(cd "$TMP" && tar -czf out.tar.gz "$NAME")
mkdir -p "$(dirname "$ARCHIVE")"
mv "$TMP/out.tar.gz" "$ARCHIVE"
echo "source archive $ARCHIVE ($(du -h "$ARCHIVE" | cut -f1))"
exit $status
