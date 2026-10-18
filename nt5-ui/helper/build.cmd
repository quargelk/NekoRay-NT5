@echo off
rem Builds nt5helper.exe for windows/386 with the Go 1.20 toolchain that has the Windows XP (NT 5.1)
rem backports (https://github.com/BieHDC/go-backports, release go1.20-nt51) plus the Windows 2000 (NT 5.0)
rem patch ..\third_party\go1.20-nt51-nt50.patch (see BUILDING.md), offline, into ..\runtime.
rem Optional: the program works without it (HTTPS subscriptions then only through curl.exe or a file).
rem   NT5_GOROOT      the Go toolchain   (default: nt5-ui\toolchain\go, not kept in Git)
rem   NT5_GOMODCACHE  module cache       (default: %TEMP%\nt5helper-gomodcache; nt5helper needs no modules)
rem Usage: build.cmd [test]   "test" also runs the Go tests (set XRAY_EXE to include the real core).
setlocal
if "%NT5_GOROOT%"=="" set NT5_GOROOT=%~dp0..\toolchain\go
if "%NT5_GOMODCACHE%"=="" set NT5_GOMODCACHE=%TEMP%\nt5helper-gomodcache
if not exist "%NT5_GOROOT%\bin\go.exe" (
    echo Go toolchain not found: %NT5_GOROOT%
    exit /b 2
)
set GOROOT=%NT5_GOROOT%
set GOMODCACHE=%NT5_GOMODCACHE%
set GOPROXY=off
set GOSUMDB=off
set GOFLAGS=-mod=mod
set CGO_ENABLED=0
set GOOS=windows
set GOARCH=386
if "%GOCACHE%"=="" set GOCACHE=%TEMP%\nt5helper-gocache
pushd "%~dp0"
if /i "%~1"=="test" (
    "%GOROOT%\bin\go.exe" test -count=1 .
    if errorlevel 1 ( popd & exit /b 1 )
)
rem -buildvcs=false and an empty build id: the same sources give the same file, inside or outside Git
"%GOROOT%\bin\go.exe" build -trimpath -buildvcs=false -ldflags "-s -w -buildid=" -o "%~dp0..\runtime\nt5helper.exe" .
set RESULT=%ERRORLEVEL%
popd
exit /b %RESULT%
