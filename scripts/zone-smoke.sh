#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET="${DOTNET:-$HOME/.dotnet/dotnet}"
CONFIGURATION="${CONFIGURATION:-Release}"
HOST_DLL="${ZONE_DLL:-$ROOT/src/Crystal.ZoneServer/bin/$CONFIGURATION/Crystal.ZoneServer.dll}"
CONTENT_ROOT="${CRYSTAL_ZONE_SMOKE_CONTENT:-$ROOT/.external/Crystal.Database/Jev}"

if [ ! -f "$HOST_DLL" ]; then
  "$DOTNET" build "$ROOT/src/Crystal.ZoneServer/Crystal.ZoneServer.csproj" -c "$CONFIGURATION" --nologo
fi

if [ ! -f "$CONTENT_ROOT/Server.MirDB" ]; then
  echo "Smoke content not found: $CONTENT_ROOT/Server.MirDB" >&2
  echo "Set CRYSTAL_ZONE_SMOKE_CONTENT to a prepared content directory." >&2
  exit 1
fi

TMP="$(mktemp -d /tmp/crystal-zone-smoke-XXXXXX)"
cleanup() {
  if [ -n "${TMP:-}" ] && [ -d "$TMP" ]; then
    find "$TMP" -depth -delete 2>/dev/null || true
  fi
}
trap cleanup EXIT

cp -a --reflink=auto "$CONTENT_ROOT/Configs" "$TMP/Configs"
cp -a --reflink=auto "$CONTENT_ROOT/Envir" "$TMP/Envir"
cp -a --reflink=auto "$CONTENT_ROOT/Server.MirDB" "$TMP/Server.MirDB"
ln -s "$CONTENT_ROOT/Maps" "$TMP/Maps"

PORT="$(python3 - <<'PY'
import socket
s = socket.socket()
s.bind(("127.0.0.1", 0))
print(s.getsockname()[1])
s.close()
PY
)"

STATUS_PORT="$(python3 - <<'PY'
import socket
s = socket.socket()
s.bind(("127.0.0.1", 0))
print(s.getsockname()[1])
s.close()
PY
)"

LOG="$TMP/zone.out"
CRYSTAL_ZONE_ID=smoke \
CRYSTAL_ZONE_IP=127.0.0.1 \
CRYSTAL_ZONE_PORT="$PORT" \
CRYSTAL_ZONE_STATUS_PORT="$STATUS_PORT" \
  "$DOTNET" "$HOST_DLL" --content-root "$TMP" --startup-timeout 180 >"$LOG" 2>&1 &

PID=$!

fail() {
  echo "Zone smoke test failed: $1" >&2
  tail -n 80 "$LOG" >&2 || true
  exit 1
}

for _ in $(seq 1 900); do
  if grep -q "Zone ready:" "$LOG"; then
    break
  fi

  if ! kill -0 "$PID" 2>/dev/null; then
    set +e
    wait "$PID"
    RC=$?
    set -e
    fail "process exited before ready (exit $RC)"
  fi

  sleep 0.2
done

grep -q "Zone ready:" "$LOG" || fail "readiness line not found"

if grep -Eq "Zone server did not become ready|work loop stopped unexpectedly|FATAL|Unhandled exception" "$LOG"; then
  fail "startup error detected"
fi

kill -TERM "$PID"

for _ in $(seq 1 300); do
  if ! kill -0 "$PID" 2>/dev/null; then
    break
  fi
  sleep 0.2
done

if kill -0 "$PID" 2>/dev/null; then
  kill -KILL "$PID" 2>/dev/null || true
  set +e
  wait "$PID"
  set -e
  fail "graceful shutdown timed out"
fi

set +e
wait "$PID"
RC=$?
set -e

if [ "$RC" -ne 0 ]; then
  fail "non-zero exit after SIGTERM (exit $RC)"
fi

echo "Zone smoke test passed."
grep -E "Zone ready:|Shutdown requested|Zone server stopped" "$LOG"
