#!/usr/bin/env bash
# End-to-end VLESS + REALITY + Vision test on a Windows host (Git Bash), with synthetic keys made for
# this run and local servers only:
#   curl -> client xray (SOCKS5 / HTTP inbound) -> VLESS+REALITY+Vision -> server xray -> freedom -> local HTTP
# The REALITY dest is a local TLS 1.3 server (tests/e2eserve). Stops only the processes it started.
# Usage: e2e-reality.sh CLIENT_XRAY SERVER_XRAY E2ESERVE_EXE [WORK_DIR]
#   E2ESERVE_EXE: built from tests/e2eserve (go build -o e2eserve.exe .)
# Exit code: number of failed checks. Uses local ports 18080, 18100-18103, 18443, 18444.
set -u
if [ $# -lt 3 ]; then
  echo "usage: $0 CLIENT_XRAY SERVER_XRAY E2ESERVE_EXE [WORK_DIR]"
  exit 100
fi
CLIENT=$1
SERVER=$2
E2ESERVE=$3
RUN=${4:-$(mktemp -d)}
mkdir -p "$RUN"
fails=0
ok() { echo "PASS $*"; }
bad() { echo "FAIL $*"; fails=$((fails+1)); }

KEYS=$("$CLIENT" x25519)
PRIV=$(echo "$KEYS" | sed -n 's/^Private key: //p' | tr -d '\r')
PUB=$(echo "$KEYS" | sed -n 's/^Public key: //p' | tr -d '\r')
ID=$("$CLIENT" uuid | tr -d '\r')
SID=$(od -An -N4 -tx1 /dev/urandom | tr -d ' \n')
WRONGPUB=$("$CLIENT" x25519 | sed -n 's/^Public key: //p' | tr -d '\r')

server_cfg() {
cat <<EOF
{ "log": {"loglevel": "warning"},
  "inbounds": [{ "listen": "127.0.0.1", "port": 18444, "protocol": "vless",
    "settings": {"clients": [{"id": "$ID", "flow": "xtls-rprx-vision"}], "decryption": "none"},
    "streamSettings": {"network": "tcp", "security": "reality",
      "realitySettings": {"dest": "127.0.0.1:18443", "serverNames": ["dest.test"], "privateKey": "$PRIV", "shortIds": ["$SID"]}}}],
  "outbounds": [{"protocol": "freedom"}] }
EOF
}
client_cfg() { # $1 public key, $2 socks port, $3 http port
cat <<EOF
{ "log": {"loglevel": "warning"},
  "inbounds": [{"listen": "127.0.0.1", "port": $2, "protocol": "socks", "settings": {"udp": true}},
               {"listen": "127.0.0.1", "port": $3, "protocol": "http"}],
  "outbounds": [{"protocol": "vless",
    "settings": {"vnext": [{"address": "127.0.0.1", "port": 18444, "users": [{"id": "$ID", "flow": "xtls-rprx-vision", "encryption": "none"}]}]},
    "streamSettings": {"network": "tcp", "security": "reality",
      "realitySettings": {"serverName": "dest.test", "fingerprint": "chrome", "publicKey": "$1", "shortId": "$SID"}}}] }
EOF
}
server_cfg > "$RUN/server.json"
client_cfg "$PUB" 18100 18101 > "$RUN/client.json"
client_cfg "$WRONGPUB" 18102 18103 > "$RUN/client-wrongkey.json"

pids=()
"$E2ESERVE" 127.0.0.1:18443 127.0.0.1:18080 > "$RUN/e2eserve.log" 2>&1 & pids+=($!)
"$SERVER" run -c "$RUN/server.json" > "$RUN/server.log" 2>&1 & pids+=($!)
"$CLIENT" run -c "$RUN/client.json" > "$RUN/client.log" 2>&1 & pids+=($!)
"$CLIENT" run -c "$RUN/client-wrongkey.json" > "$RUN/client-wrongkey.log" 2>&1 & pids+=($!)
sleep 3

want=nt50-e2e-ok
for target in http://127.0.0.1:18080/ http://localhost:18080/; do
  got=$(curl -s --max-time 15 --socks5-hostname 127.0.0.1:18100 "$target" | tr -d '\r\n')
  [ "$got" = "$want" ] && ok "socks5 $target" || bad "socks5 $target: got '$got'"
  got=$(curl -s --max-time 15 -x http://127.0.0.1:18101 "$target" | tr -d '\r\n')
  [ "$got" = "$want" ] && ok "http-proxy $target" || bad "http-proxy $target: got '$got'"
done
n=$(curl -s --max-time 30 --socks5-hostname 127.0.0.1:18100 "http://127.0.0.1:18080/" -o /dev/null -w '%{http_code}')
[ "$n" = "200" ] && ok "status 200" || bad "status '$n'"
curls=()
for i in $(seq 1 50); do
  curl -s --max-time 20 --socks5-hostname 127.0.0.1:18100 http://127.0.0.1:18080/ -o "$RUN/par.$i" & curls+=($!)
done
for c in "${curls[@]}"; do wait "$c"; done
par=0
for i in $(seq 1 50); do [ "$(tr -d '\r\n' < "$RUN/par.$i")" = "$want" ] && par=$((par+1)); done
[ $par = 50 ] && ok "50 parallel requests" || bad "parallel: $par/50"
# TLS inside the tunnel (to the local TLS server; its self-signed certificate is not under test here)
got=$(curl -s --max-time 15 -k --socks5-hostname 127.0.0.1:18100 https://127.0.0.1:18443/ | tr -d '\r\n')
[ "$got" = "$want" ] && ok "tls inside the tunnel" || bad "tls inside the tunnel: got '$got'"
got=$(curl -s --max-time 10 --socks5-hostname 127.0.0.1:18102 http://127.0.0.1:18080/ | tr -d '\r\n')
[ "$got" != "$want" ] && ok "wrong public key refused" || bad "wrong public key got through"

for p in "${pids[@]}"; do kill "$p" 2>/dev/null; done
wait 2>/dev/null
grep -il "panic\|fatal\|exception 0x" "$RUN"/*.log && bad "crash text in a log"
echo "RESULT e2e: $fails failed (logs: $RUN)"
exit $fails
