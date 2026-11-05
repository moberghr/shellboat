#!/usr/bin/env bash
# Throwaway OpenSSH server for the live Shellboat.Ssh tests (skip-safe: the tests skip when it isn't running,
# and a skip is not a pass). Password + key auth for user `shellboat` on 127.0.0.1:2222.
#   ./build/test-sshd.sh          start (idempotent)
#   ./build/test-sshd.sh stop     remove the container
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
name="shellboat-sshd-test"
keydir="$root/build/.test-sshd"

if [[ "${1:-}" == "stop" ]]; then
  docker rm -f "$name" >/dev/null 2>&1 || true
  exit 0
fi

mkdir -p "$keydir"
[[ -f "$keydir/id_ed25519" ]] || ssh-keygen -q -t ed25519 -N "" -f "$keydir/id_ed25519" -C shellboat-test
[[ -f "$keydir/id_ed25519_pass" ]] || ssh-keygen -q -t ed25519 -N "testpass" -f "$keydir/id_ed25519_pass" -C shellboat-test-pass

# OpenSSH 10 penalises a source IP after failed logins (PerSourcePenalties) and then drops every new connection
# for a while — the negative tests (wrong password, rejected host key) would lock the rest of the suite out.
relax_penalties() {
  if docker exec "$name" grep -q '^PerSourcePenalties no' /config/sshd/sshd_config; then
    return
  fi
  docker exec "$name" sh -c 'echo "PerSourcePenalties no" >> /config/sshd/sshd_config'
  docker restart "$name" >/dev/null
  for _ in $(seq 1 30); do
    if docker logs --since 5s "$name" 2>&1 | grep -q "done\|Server listening"; then break; fi
    sleep 1
  done
}

if docker ps --format '{{.Names}}' | grep -qx "$name"; then
  relax_penalties
  echo "$name already running"
  exit 0
fi
docker rm -f "$name" >/dev/null 2>&1 || true

docker run -d --name "$name" -p 127.0.0.1:2222:2222 \
  -e PUID=1000 -e PGID=1000 -e TZ=Etc/UTC \
  -e USER_NAME=shellboat -e USER_PASSWORD=shellboat-test -e PASSWORD_ACCESS=true \
  -e PUBLIC_KEY="$(cat "$keydir/id_ed25519.pub")
$(cat "$keydir/id_ed25519_pass.pub")" \
  lscr.io/linuxserver/openssh-server:latest >/dev/null

for _ in $(seq 1 30); do
  if docker logs "$name" 2>&1 | grep -q "done\|Server listening"; then break; fi
  sleep 1
done
relax_penalties
echo "$name listening on 127.0.0.1:2222 (user shellboat / shellboat-test, keys in build/.test-sshd)"
