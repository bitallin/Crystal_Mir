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

pick_port() {
  python3 - <<'PY'
import socket
s = socket.socket()
s.bind(("127.0.0.1", 0))
print(s.getsockname()[1])
s.close()
PY
}

fail() {
  local label="$1"
  local message="$2"
  local log="$3"
  echo "Zone smoke test failed ($label): $message" >&2
  tail -n 100 "$log" >&2 || true
  exit 1
}

start_zone() {
  local label="$1"
  local content_root="$2"
  local log="$3"
  shift 3
  local port
  local status_port
  port="$(pick_port)"
  status_port="$(pick_port)"
  : >"$log"
  CRYSTAL_ZONE_ID="smoke-$label" \
  CRYSTAL_ZONE_IP=127.0.0.1 \
  CRYSTAL_ZONE_PORT="$port" \
  CRYSTAL_ZONE_STATUS_PORT="$status_port" \
    "$DOTNET" "$HOST_DLL" --content-root "$content_root" --startup-timeout 180 "$@" >"$log" 2>&1 &
  ZONE_PID=$!
}

stop_zone() {
  local label="$1"
  local log="$2"
  kill -TERM "$ZONE_PID" 2>/dev/null || true
  for _ in $(seq 1 300); do
    if ! kill -0 "$ZONE_PID" 2>/dev/null; then
      break
    fi
    sleep 0.2
  done

  if kill -0 "$ZONE_PID" 2>/dev/null; then
    kill -KILL "$ZONE_PID" 2>/dev/null || true
    set +e
    wait "$ZONE_PID"
    set -e
    fail "$label" "graceful shutdown timed out" "$log"
  fi

  set +e
  wait "$ZONE_PID"
  local rc=$?
  set -e
  if [ "$rc" -ne 0 ]; then
    fail "$label" "non-zero exit after SIGTERM (exit $rc)" "$log"
  fi
}

run_ready_smoke() {
  local label="$1"
  local content_root="$2"
  local log="$3"
  shift 3
  start_zone "$label" "$content_root" "$log" "$@"
  for _ in $(seq 1 900); do
    if grep -q "Zone ready:" "$log"; then
      break
    fi
    if ! kill -0 "$ZONE_PID" 2>/dev/null; then
      set +e
      wait "$ZONE_PID"
      local rc=$?
      set -e
      fail "$label" "process exited before ready (exit $rc)" "$log"
    fi
    sleep 0.2
  done
  grep -q "Zone ready:" "$log" || fail "$label" "readiness line not found" "$log"
  if grep -Eq "Zone server did not become ready|work loop stopped unexpectedly|FATAL|Unhandled exception" "$log"; then
    fail "$label" "startup error detected" "$log"
  fi
  stop_zone "$label" "$log"
}

run_failure_smoke() {
  local label="$1"
  local content_root="$2"
  local log="$3"
  local pattern="$4"
  shift 4
  start_zone "$label" "$content_root" "$log" "$@"
  for _ in $(seq 1 300); do
    if ! kill -0 "$ZONE_PID" 2>/dev/null; then
      break
    fi
    if grep -q "Zone ready:" "$log"; then
      kill -KILL "$ZONE_PID" 2>/dev/null || true
      set +e
      wait "$ZONE_PID"
      set -e
      fail "$label" "process became ready despite invalid release" "$log"
    fi
    sleep 0.1
  done
  if kill -0 "$ZONE_PID" 2>/dev/null; then
    kill -KILL "$ZONE_PID" 2>/dev/null || true
    set +e
    wait "$ZONE_PID"
    set -e
    fail "$label" "invalid release did not fail startup" "$log"
  fi
  set +e
  wait "$ZONE_PID"
  local rc=$?
  set -e
  if [ "$rc" -eq 0 ]; then
    fail "$label" "invalid release exited successfully" "$log"
  fi
  if grep -q "Zone ready:" "$log"; then
    fail "$label" "invalid release entered ready state" "$log"
  fi
  if ! grep -Eiq "$pattern" "$log"; then
    fail "$label" "expected failure pattern not found: $pattern" "$log"
  fi
}

legacy_root="$TMP/legacy"
mkdir -p "$legacy_root"
cp -a --reflink=auto "$CONTENT_ROOT/Configs" "$legacy_root/Configs"
cp -a --reflink=auto "$CONTENT_ROOT/Envir" "$legacy_root/Envir"
cp -a --reflink=auto "$CONTENT_ROOT/Server.MirDB" "$legacy_root/Server.MirDB"
ln -s "$CONTENT_ROOT/Maps" "$legacy_root/Maps"

legacy_log="$TMP/legacy.out"
run_ready_smoke "legacy" "$legacy_root" "$legacy_log"
echo "Legacy zone smoke passed."
grep -E "Zone ready:|Shutdown requested|Zone server stopped" "$legacy_log"

if ! command -v openssl >/dev/null 2>&1; then
  echo "openssl is required for signed release smoke tests." >&2
  exit 2
fi

"$DOTNET" build "$ROOT/tools/Crystal.LegacyImporter/Crystal.LegacyImporter.csproj" -c "$CONFIGURATION" --nologo >/dev/null
"$DOTNET" build "$ROOT/tools/Crystal.ContentTool/Crystal.ContentTool.csproj" -c "$CONFIGURATION" --nologo >/dev/null

release_root="$TMP/release"
"$DOTNET" run --project "$ROOT/tools/Crystal.LegacyImporter/Crystal.LegacyImporter.csproj" \
  -c "$CONFIGURATION" --no-build -- \
  --db "$CONTENT_ROOT/Server.MirDB" \
  --out "$release_root" \
  --content-root "$CONTENT_ROOT" \
  --release "zone-smoke.$(date -u +%Y%m%d%H%M%S)" >"$TMP/import.out" 2>&1 \
  || fail "import" "could not build Jev content snapshot" "$TMP/import.out"

cp -a "$release_root" "$TMP/release-unsigned"

openssl genpkey -algorithm EC -pkeyopt ec_paramgen_curve:P-256 \
  -out "$TMP/release-private.pem" >/dev/null 2>&1
openssl pkey -in "$TMP/release-private.pem" -pubout \
  -out "$TMP/release-public.pem" >/dev/null 2>&1
"$DOTNET" run --project "$ROOT/tools/Crystal.ContentTool/Crystal.ContentTool.csproj" \
  -c "$CONFIGURATION" --no-build -- sign \
  --snapshot "$release_root" \
  --key "$TMP/release-private.pem" \
  --key-id zone-smoke >"$TMP/sign.out" 2>&1 \
  || fail "sign" "could not sign Jev content snapshot" "$TMP/sign.out"

cp -a "$release_root" "$TMP/release-tampered"
printf '\n' >>"$TMP/release-tampered/entities/item.jsonl"

release_log="$TMP/release.out"
run_ready_smoke "release" "$CONTENT_ROOT" "$release_log" \
  --content-release "$release_root" \
  --content-public-key "$TMP/release-public.pem" \
  --content-key-id zone-smoke
grep -q "Zone ready: maps=463," "$release_log" \
  || fail "release" "release mode did not load all 463 maps" "$release_log"
grep -q "items=1628, monsters=555, npcs=375, quests=154" "$release_log" \
  || fail "release" "release mode did not load expected Jev entity counts" "$release_log"
echo "Signed release zone smoke passed."
grep -E "Loading signed content release|Content release materialized|Zone ready:|Shutdown requested|Zone server stopped" "$release_log"

missing_root="$TMP/missing-content"
mkdir -p "$missing_root/Maps"

run_failure_smoke "unsigned" "$CONTENT_ROOT" "$TMP/unsigned.out" "not signed" \
  --content-release "$TMP/release-unsigned" \
  --content-public-key "$TMP/release-public.pem" \
  --content-key-id zone-smoke

run_failure_smoke "tampered" "$CONTENT_ROOT" "$TMP/tampered.out" "hash mismatch" \
  --content-release "$TMP/release-tampered" \
  --content-public-key "$TMP/release-public.pem" \
  --content-key-id zone-smoke

run_failure_smoke "missing-map" "$missing_root" "$TMP/missing-map.out" "was not found in the content root" \
  --content-release "$release_root" \
  --content-public-key "$TMP/release-public.pem" \
  --content-key-id zone-smoke

echo "Zone smoke test passed (legacy, signed release, missing signature, tampered snapshot, missing map)."
