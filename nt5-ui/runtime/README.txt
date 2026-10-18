Files that are copied next to NekoRayNT5.exe on every build (Debug and Release); a Release build also
puts them, with NekoRayNT5.exe, into bin\x86\Release\package\ - the folder to copy to the target system.
Nothing here is required to build; without xray.exe the program starts but cannot connect.

  xray.exe        Xray 1.8.3 for windows/386 that runs on Windows 2000 (NT 5.0) and Windows XP (one build
                  for both); how it is built and from which sources: ..\third_party\README.md.
                  Official Xray downloads do not run on these systems.
  nt5helper.exe   built by ..\helper\build.cmd: HTTPS subscriptions with current TLS.
  cacert.pem      Mozilla CA bundle for nt5helper (the root store of Windows XP is outdated). Kept in Git.
                  Source: https://curl.se/docs/caextract.html (Mozilla data as of 2026-09-25, 121
                  certificates, SHA-256 a41b5d356aea97a529fe27e0f7316d2f9d946d75927476cf9cf1b90637d00505).
                  To update: download https://curl.se/ca/cacert.pem and compare it with
                  https://curl.se/ca/cacert.pem.sha256, then run "..\helper\build.cmd test".
  curl.exe        optional: fallback for HTTPS subscriptions (with its own CA bundle).
  geoip.dat, geosite.dat   optional: for geoip:/geosite: routing rules.

xray.exe, nt5helper.exe, curl.exe and the .dat files are binaries and are not kept in Git.
