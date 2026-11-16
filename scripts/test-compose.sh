#!/usr/bin/env bash
set -euo pipefail
ulimit -c 0 2>/dev/null || true

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET="${DOTNET:-$HOME/.dotnet/dotnet}"
COMPOSE_PROJECT="${CRYSTAL_COMPOSE_PROJECT:-crystal-compose-test-$$}"
ARTIFACT_DIR="$ROOT/artifacts/compose-test-$$"
ENV_FILE="$ARTIFACT_DIR/compose.env"
SIGNING_DIR="$ARTIFACT_DIR/signing"
SNAPSHOT_DIR="$ARTIFACT_DIR/snapshot"
WEB_PORT="${CRYSTAL_COMPOSE_WEB_PORT:-}"
PG_PORT="${CRYSTAL_COMPOSE_POSTGRES_PORT:-}"
ADMIN_PASSWORD="${CRYSTAL_CONTROL_BOOTSTRAP_PASSWORD:-ComposeTest!Passw0rd2026}"
POSTGRES_PASSWORD="${POSTGRES_PASSWORD:-ComposeDb!Passw0rd2026}"
JWT_SIGNING_KEY="${CRYSTAL_CONTROL_JWT_SIGNING_KEY:-compose-test-jwt-signing-key-0123456789abcdef}"
MINIO_USER="${MINIO_ROOT_USER:-crystal}"
MINIO_PASSWORD="${MINIO_ROOT_PASSWORD:-ComposeMinio!Passw0rd2026}"

if ! command -v docker >/dev/null 2>&1 || ! docker info >/dev/null 2>&1; then
  echo "Docker is required for the Compose smoke test." >&2
  exit 2
fi
if ! command -v openssl >/dev/null 2>&1; then
  echo "openssl is required to generate release signing test keys." >&2
  exit 2
fi
if [ ! -x "$DOTNET" ]; then
  echo "dotnet not found at $DOTNET. Set DOTNET=/path/to/dotnet." >&2
  exit 2
fi
if ! command -v curl >/dev/null 2>&1 || ! command -v python3 >/dev/null 2>&1; then
  echo "curl and python3 are required for the Compose smoke test." >&2
  exit 2
fi

pick_port() {
  python3 - <<'PY'
import socket
s = socket.socket()
s.bind(("127.0.0.1", 0))
print(s.getsockname()[1])
s.close()
PY
}

[ -n "$WEB_PORT" ] || WEB_PORT="$(pick_port)"
[ -n "$PG_PORT" ] || PG_PORT="$(pick_port)"
WEB_HOST="http://127.0.0.1:$WEB_PORT"
WEB_INTERNAL="http://127.0.0.1"

mkdir -p "$ARTIFACT_DIR" "$SIGNING_DIR"
chmod 700 "$SIGNING_DIR"
openssl genpkey -algorithm EC -pkeyopt ec_paramgen_curve:P-256 \
  -out "$SIGNING_DIR/private.pem" >/dev/null 2>&1
chmod 600 "$SIGNING_DIR/private.pem"
openssl pkey -in "$SIGNING_DIR/private.pem" -pubout \
  -out "$SIGNING_DIR/public.pem" >/dev/null 2>&1

cat >"$ENV_FILE" <<EOF
CRYSTAL_COMPOSE_WEB_PORT=$WEB_PORT
CRYSTAL_COMPOSE_POSTGRES_PORT=$PG_PORT
POSTGRES_PASSWORD=$POSTGRES_PASSWORD
MINIO_ROOT_USER=$MINIO_USER
MINIO_ROOT_PASSWORD=$MINIO_PASSWORD
CRYSTAL_CONTROL_JWT_SIGNING_KEY=$JWT_SIGNING_KEY
CRYSTAL_CONTROL_BOOTSTRAP_PASSWORD=$ADMIN_PASSWORD
CRYSTAL_CONTROL_SIGNING_DIR=$SIGNING_DIR
CRYSTAL_CONTROL_RELEASE_SIGNING_KEY_ID=compose-test
CRYSTAL_CONTROL_ASSET_ROOT=$ROOT/tests/fixtures/content
EOF

COMPOSE=(docker compose --project-name "$COMPOSE_PROJECT" --env-file "$ENV_FILE" -f "$ROOT/compose.yaml")
LOGS_DUMPED=0
dump_logs() {
  if [ "$LOGS_DUMPED" -eq 1 ]; then return; fi
  LOGS_DUMPED=1
  "${COMPOSE[@]}" ps >&2 || true
  "${COMPOSE[@]}" logs --no-color --tail=200 >&2 || true
}
cleanup() {
  status=$?
  if [ "$status" -ne 0 ]; then dump_logs; fi
  "${COMPOSE[@]}" down --volumes --remove-orphans >/dev/null 2>&1 || true
  if [ "${KEEP_COMPOSE_ARTIFACTS:-0}" != "1" ]; then
    rm -rf "$ARTIFACT_DIR"
  else
    echo "Compose test artifacts kept at $ARTIFACT_DIR"
  fi
}
trap cleanup EXIT

cd "$ROOT"
echo "Starting Compose stack on web port $WEB_PORT and PostgreSQL port $PG_PORT..."
if [ "${CRYSTAL_COMPOSE_NO_BUILD:-0}" = "1" ]; then
  "${COMPOSE[@]}" up -d --remove-orphans
else
  "${COMPOSE[@]}" up -d --build --remove-orphans
fi

READY=0
for _ in $(seq 1 180); do
  if "${COMPOSE[@]}" exec -T control-web wget -q -O /dev/null "$WEB_INTERNAL/health/ready" >/dev/null 2>&1; then
    READY=1
    break
  fi
  sleep 2
done
if [ "$READY" -ne 1 ]; then
  echo "Compose stack did not become healthy through the Web proxy." >&2
  dump_logs
  exit 1
fi

if curl -fsS --max-time 5 "$WEB_HOST/" >/dev/null 2>&1; then
  WEB="$WEB_HOST"
  echo "Web is reachable from the host at $WEB."
else
  WEB="$WEB_INTERNAL"
  echo "Host port publishing is unavailable; validating Web and API through the Nginx container."
fi

echo "Applying golden content (the GPL importer is not a Compose service)..."
"$DOTNET" build tools/Crystal.LegacyImporter/Crystal.LegacyImporter.csproj -c Release --nologo >/dev/null
PG_HOST_DSN="Host=127.0.0.1;Port=$PG_PORT;Database=crystal;Username=crystal;Password=$POSTGRES_PASSWORD"
if python3 - "$PG_PORT" <<'PY'
import socket, sys
s = socket.socket()
s.settimeout(1)
try:
    s.connect(("127.0.0.1", int(sys.argv[1])))
except OSError:
    raise SystemExit(1)
finally:
    s.close()
PY
then
  "$DOTNET" run --project tools/Crystal.LegacyImporter/Crystal.LegacyImporter.csproj \
    -c Release --no-build -- \
    --db tests/fixtures/golden/Server.MirDB \
    --out "$SNAPSHOT_DIR" \
    --content-root tests/fixtures/content \
    --release "compose.$(date -u +%Y%m%d%H%M%S)" \
    --dsn "$PG_HOST_DSN" >/dev/null
else
  echo "Host PostgreSQL port is unavailable; running the host-built importer once on the Compose network."
  docker run --rm --network "${COMPOSE_PROJECT}_default" \
    -v "$ROOT:/workspace:ro" \
    -v "$ARTIFACT_DIR:/artifacts" \
    -w /workspace \
    mcr.microsoft.com/dotnet/aspnet:8.0 \
    dotnet tools/Crystal.LegacyImporter/bin/Release/net8.0/Crystal.LegacyImporter.dll \
    --db tests/fixtures/golden/Server.MirDB \
    --out /artifacts/snapshot \
    --content-root tests/fixtures/content \
    --release "compose.$(date -u +%Y%m%d%H%M%S)" \
    --dsn "Host=postgres;Port=5432;Database=crystal;Username=crystal;Password=$POSTGRES_PASSWORD" >/dev/null
fi

if [ "$WEB" = "$WEB_HOST" ]; then
  curl -fsS --max-time 10 "$WEB/" | grep -q "MIR CONTROL"
else
  "${COMPOSE[@]}" exec -T control-web wget -q -O - "$WEB_INTERNAL/" | grep -q "MIR CONTROL"
fi
if "${COMPOSE[@]}" exec -T control-web wget -q -O /dev/null "$WEB_INTERNAL/api/v1/content/item?pageSize=1" >/dev/null 2>&1; then
  echo "Expected unauthenticated content request to fail." >&2
  exit 1
fi

"${COMPOSE[@]}" exec -T control-web wget -q -O - \
  --header='Content-Type: application/json' \
  --post-data="{\"username\":\"admin\",\"password\":\"$ADMIN_PASSWORD\"}" \
  "$WEB_INTERNAL/api/v1/auth/login" >"$ARTIFACT_DIR/login.json"
TOKEN="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["accessToken"])' "$ARTIFACT_DIR/login.json")"
"${COMPOSE[@]}" exec -T control-web wget -q -O - \
  --header="Authorization: Bearer $TOKEN" \
  "$WEB_INTERNAL/api/v1/import-runs?limit=5" >"$ARTIFACT_DIR/runs.json"
"${COMPOSE[@]}" exec -T control-web wget -q -O - \
  --header="Authorization: Bearer $TOKEN" \
  "$WEB_INTERNAL/api/v1/content/item?page=1&pageSize=1" >"$ARTIFACT_DIR/items.json"
python3 - "$ARTIFACT_DIR/runs.json" "$ARTIFACT_DIR/items.json" <<'PY'
import json, sys
runs = json.load(open(sys.argv[1]))
items = json.load(open(sys.argv[2]))
assert runs and runs[0]["status"] == "imported", runs[:1]
assert runs[0]["entityCount"] >= 9797, runs[0]
assert items["total"] == 1628, items["total"]
assert items["items"][0]["contentKey"] == "item.1", items["items"][0]
PY

echo "Compose stack verified: PostgreSQL, Redis, MinIO, migrations, API proxy, Web, login and content queries."
