#!/usr/bin/env bash
set -euo pipefail
ulimit -c 0 2>/dev/null || true

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET="${DOTNET:-$HOME/.dotnet/dotnet}"
CONFIGURATION="${CONFIGURATION:-Release}"
DSN="${1:-${CRYSTAL_DATABASE_URL:-${CRYSTAL_DB_CONNECTION:-}}}"
CONFIG_SOURCE="${CRYSTAL_ZONE_AGENT_CONFIGS:-$ROOT/.external/Crystal.Database/Jev/Configs}"
ENVIR_SOURCE="${CRYSTAL_ZONE_AGENT_ENVIR:-$ROOT/.external/Crystal.Database/Jev/Envir}"
MAP_SOURCE="${CRYSTAL_ZONE_AGENT_MAPS:-$ROOT/.external/Crystal.Database/Jev/Maps}"
GOLDEN_DB="$ROOT/tests/fixtures/golden/Server.MirDB"
CONTENT_FIXTURE="$ROOT/tests/fixtures/content"

if [ -z "$DSN" ]; then
  echo "Set CRYSTAL_DATABASE_URL/CRYSTAL_DB_CONNECTION or pass a PostgreSQL connection string." >&2
  exit 2
fi

if [ ! -f "$GOLDEN_DB" ] || [ ! -d "$CONTENT_FIXTURE" ]; then
  echo "Zone Agent E2E fixtures not found: $GOLDEN_DB / $CONTENT_FIXTURE; skipping." >&2
  exit 0
fi

if [ ! -d "$CONFIG_SOURCE" ]; then
  echo "Zone Agent E2E configs not found: $CONFIG_SOURCE; skipping." >&2
  exit 0
fi

if [ ! -d "$ENVIR_SOURCE" ]; then
  echo "Zone Agent E2E environment files not found: $ENVIR_SOURCE; skipping." >&2
  exit 0
fi

if [ ! -d "$MAP_SOURCE" ]; then
  echo "Zone Agent E2E maps not found: $MAP_SOURCE; skipping." >&2
  exit 0
fi

for tool in curl openssl python3; do
  command -v "$tool" >/dev/null 2>&1 || { echo "$tool is required." >&2; exit 2; }
done

TMP="$(mktemp -d)"
API_PID=""
AGENT_PID=""
PROXY_PID=""
cleanup() {
  local rc=$?
  if [ "$rc" -ne 0 ]; then
    echo "--- Control API log ---" >&2
    tail -n 120 "$TMP/api.log" 2>/dev/null || true
    echo "--- Agent log ---" >&2
    tail -n 160 "$TMP/agent.log" 2>/dev/null || true
    echo "--- Artifact proxy log ---" >&2
    tail -n 80 "$TMP/proxy.log" 2>/dev/null || true
    echo "--- Zone logs ---" >&2
    find "$TMP/agent/logs" -type f -maxdepth 1 -print -exec tail -n 80 {} \; 2>/dev/null || true
  fi
  if [ -n "$AGENT_PID" ]; then
    kill "$AGENT_PID" 2>/dev/null || true
    for _ in $(seq 1 80); do kill -0 "$AGENT_PID" 2>/dev/null || break; sleep 0.1; done
    kill -9 "$AGENT_PID" 2>/dev/null || true
    wait "$AGENT_PID" 2>/dev/null || true
  fi
  if [ -n "$PROXY_PID" ]; then
    kill "$PROXY_PID" 2>/dev/null || true
    wait "$PROXY_PID" 2>/dev/null || true
  fi
  if [ -n "$API_PID" ]; then
    kill "$API_PID" 2>/dev/null || true
    for _ in $(seq 1 40); do kill -0 "$API_PID" 2>/dev/null || break; sleep 0.1; done
    kill -9 "$API_PID" 2>/dev/null || true
    wait "$API_PID" 2>/dev/null || true
  fi
  rm -rf "$TMP"
}
trap cleanup EXIT

free_port() {
  python3 - <<'PY'
import socket
s = socket.socket()
s.bind(("127.0.0.1", 0))
print(s.getsockname()[1])
s.close()
PY
}

json_value() {
  python3 -c 'import json,sys
value=json.load(open(sys.argv[1]))
for key in sys.argv[2].split("."):
    value=value[int(key)] if isinstance(value, list) else value[key]
print(value)' "$1" "$2"
}

openssl genpkey -algorithm EC -pkeyopt ec_paramgen_curve:P-256 \
  -out "$TMP/release-private.pem" >/dev/null 2>&1
openssl pkey -in "$TMP/release-private.pem" -pubout \
  -out "$TMP/release-public.pem" >/dev/null 2>&1

cd "$ROOT"
"$DOTNET" build tools/Crystal.LegacyImporter/Crystal.LegacyImporter.csproj -c "$CONFIGURATION" --nologo >/dev/null
"$DOTNET" build src/Crystal.Control.Api/Crystal.Control.Api.csproj -c "$CONFIGURATION" --nologo >/dev/null
"$DOTNET" build src/Crystal.ZoneServer/Crystal.ZoneServer.csproj -c "$CONFIGURATION" --nologo >/dev/null
"$DOTNET" build src/Crystal.ZoneAgent/Crystal.ZoneAgent.csproj -c "$CONFIGURATION" --nologo >/dev/null

CONTENT_ROOT="$TMP/content"
cp -a "$CONTENT_FIXTURE" "$CONTENT_ROOT"
cp -a "$CONFIG_SOURCE" "$CONTENT_ROOT/Configs"
cp "$GOLDEN_DB" "$CONTENT_ROOT/Server.MirDB"

"$DOTNET" run --project tools/Crystal.LegacyImporter/Crystal.LegacyImporter.csproj \
  -c "$CONFIGURATION" --no-build -- \
  --db "$CONTENT_ROOT/Server.MirDB" \
  --npc-script-manifest "$TMP/npc-scripts.txt" \
  --drop-manifest "$TMP/drop-paths.txt" >/dev/null
python3 - "$CONTENT_ROOT" "$TMP/npc-scripts.txt" "$TMP/drop-paths.txt" <<'PY'
import sys
from pathlib import Path

root = Path(sys.argv[1])
for name in Path(sys.argv[2]).read_text(encoding="utf-8").splitlines():
    name = name.strip().replace("\\", "/").strip("/")
    if not name:
        continue
    path = root / "Envir" / "NPCs" / f"{name}.txt"
    if path.exists():
        continue
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("[@MAIN]\n", encoding="utf-8")

for name in Path(sys.argv[3]).read_text(encoding="utf-8").splitlines():
    name = name.strip().replace("\\", "/").strip("/")
    if not name:
        continue
    if not name.lower().endswith(".txt"):
        name += ".txt"
    path = root / "Envir" / "Drops" / name
    if path.exists():
        continue
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("; Generated minimal drop fixture.\n1/1 GreenPoison\n", encoding="utf-8")
PY

SUFFIX="$(date -u +%Y%m%d%H%M%S)$$"
RELEASE_VERSION_1="agent.one.$SUFFIX"
RELEASE_VERSION_2="agent.two.$SUFFIX"
RELEASE_VERSION_FAIL="fail.agent.$SUFFIX"
RELEASE_VERSION_TAMPER="tamper.agent.$SUFFIX"

mkdir -p "$TMP/releases"
"$DOTNET" run --project tools/Crystal.LegacyImporter/Crystal.LegacyImporter.csproj \
  -c "$CONFIGURATION" --no-build -- \
  --db "$CONTENT_ROOT/Server.MirDB" \
  --out "$TMP/import-snapshot" \
  --content-root "$CONTENT_ROOT" \
  --release "$RELEASE_VERSION_1" \
  --dsn "$DSN" >"$TMP/import.log" 2>&1

API_PORT="$(free_port)"
BASE="http://127.0.0.1:$API_PORT"
API_KEY="zone-agent-test-key"
JWT_SIGNING_KEY="zone-agent-test-signing-key-0123456789abcdef"

ASPNETCORE_ENVIRONMENT=Production \
ASPNETCORE_URLS="$BASE" \
ControlApi__DatabaseConnectionString="$DSN" \
ControlApi__ApiKey="$API_KEY" \
ControlApi__JwtSigningKey="$JWT_SIGNING_KEY" \
ControlApi__AuthRateLimitPermitLimit=1000 \
ControlApi__ApiRateLimitPermitLimit=10000 \
ControlApi__ReleaseRoot="$TMP/releases" \
ControlApi__ReleaseSigningPrivateKeyPath="$TMP/release-private.pem" \
ControlApi__ReleaseSigningPublicKeyPath="$TMP/release-public.pem" \
ControlApi__ReleaseSigningKeyId="zone-agent-test" \
"$DOTNET" "$ROOT/src/Crystal.Control.Api/bin/$CONFIGURATION/net8.0/Crystal.Control.Api.dll" \
  >"$TMP/api.log" 2>&1 &
API_PID=$!

for _ in $(seq 1 120); do
  if curl -fsS --max-time 2 "$BASE/health/ready" >/dev/null 2>&1; then break; fi
  if ! kill -0 "$API_PID" 2>/dev/null; then
    echo "Control API exited before ready." >&2
    exit 1
  fi
  sleep 0.25
done
curl -fsS --max-time 2 "$BASE/health/ready" >/dev/null

api() {
  local method="$1"
  local path="$2"
  local body="${3:-}"
  local args=(-fsS --max-time 180 -X "$method" -H "X-Api-Key: $API_KEY")
  if [ -n "$body" ]; then args+=(-H "Content-Type: application/json" --data "$body"); fi
  curl "${args[@]}" "$BASE$path"
}

api GET "/api/v1/import-runs?limit=1" >"$TMP/import-runs.json"
RUN_ID="$(python3 -c 'import json,sys; value=json.load(open(sys.argv[1])); print(value[0]["id"] if isinstance(value,list) else value["items"][0]["id"])' "$TMP/import-runs.json")"

create_release() {
  local version="$1"
  local output="$2"
  api POST "/api/v1/releases" \
    "$(printf '{"importRunId":%s,"version":"%s","notes":"zone agent e2e"}' "$RUN_ID" "$version")" \
    >"$output"
  local id
  id="$(json_value "$output" 'id')"
  local status
  status="$(curl -sS --max-time 180 -o "$TMP/validate-$id.json" -w '%{http_code}' \
    -X POST -H "X-Api-Key: $API_KEY" "$BASE/api/v1/releases/$id/validate")"
  if [ "$status" != "200" ]; then
    echo "Release validation failed for $version:" >&2
    cat "$TMP/validate-$id.json" >&2
    return 1
  fi
  status="$(curl -sS --max-time 180 -o "$TMP/compile-$id.json" -w '%{http_code}' \
    -X POST -H "X-Api-Key: $API_KEY" "$BASE/api/v1/releases/$id/compile")"
  if [ "$status" != "200" ]; then
    echo "Release compile failed for $version:" >&2
    cat "$TMP/compile-$id.json" >&2
    return 1
  fi
  api POST "/api/v1/releases/$id/sign" >/dev/null
  api POST "/api/v1/releases/$id/publish" >/dev/null
  printf '%s' "$id"
}

RELEASE_ID_1="$(create_release "$RELEASE_VERSION_1" "$TMP/release-1.json")"
RELEASE_ID_2="$(create_release "$RELEASE_VERSION_2" "$TMP/release-2.json")"
RELEASE_ID_FAIL="$(create_release "$RELEASE_VERSION_FAIL" "$TMP/release-fail.json")"
RELEASE_ID_TAMPER="$(create_release "$RELEASE_VERSION_TAMPER" "$TMP/release-tamper.json")"

PROXY_PORT="$(free_port)"
PROXY_BASE="http://127.0.0.1:$PROXY_PORT"
python3 - "$BASE" "$PROXY_PORT" "$RELEASE_ID_TAMPER" >"$TMP/proxy.log" 2>&1 <<'PY' &
import http.server
import io
import sys
import urllib.error
import urllib.request
import zipfile

real_base = sys.argv[1].rstrip("/")
port = int(sys.argv[2])
tamper_release_id = int(sys.argv[3])
artifact_path = f"/api/v1/releases/{tamper_release_id}/artifact"
opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))

def corrupt_artifact(data):
    source = zipfile.ZipFile(io.BytesIO(data))
    output = io.BytesIO()
    changed = False
    with zipfile.ZipFile(output, "w", zipfile.ZIP_DEFLATED) as target:
        for entry in source.infolist():
            payload = source.read(entry.filename)
            if not changed and entry.filename.startswith("entities/") and entry.filename.endswith(".jsonl"):
                payload += b"\n"
                changed = True
            target.writestr(entry, payload)
    if not changed:
        raise RuntimeError("No entity JSONL entry found to tamper.")
    return output.getvalue()

class Handler(http.server.BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def read_request_body(self):
        if "chunked" in (self.headers.get("Transfer-Encoding") or "").lower():
            chunks = []
            while True:
                line = self.rfile.readline().strip()
                size = int(line.split(b";", 1)[0], 16)
                if size == 0:
                    while True:
                        trailer = self.rfile.readline()
                        if trailer in (b"\r\n", b"\n", b""):
                            break
                    break
                chunks.append(self.rfile.read(size))
                self.rfile.read(2)
            return b"".join(chunks) or None
        length = int(self.headers.get("Content-Length") or 0)
        return self.rfile.read(length) if length else None

    def forward(self, method):
        body = self.read_request_body()
        request = urllib.request.Request(real_base + self.path, data=body, method=method)
        for name, value in self.headers.items():
            if name.lower() in ("host", "content-length", "connection", "transfer-encoding"):
                continue
            request.add_header(name, value)
        try:
            response = opener.open(request, timeout=300)
            status = response.status
            headers = response.headers
            data = response.read()
        except urllib.error.HTTPError as error:
            status = error.code
            headers = error.headers
            data = error.read()
        if method == "GET" and self.path.split("?", 1)[0] == artifact_path and status == 200:
            data = corrupt_artifact(data)
        self.send_response(status)
        for name, value in headers.items():
            if name.lower() in ("content-length", "transfer-encoding", "connection"):
                continue
            self.send_header(name, value)
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self):
        self.forward("GET")

    def do_POST(self):
        self.forward("POST")

    def log_message(self, fmt, *args):
        pass

http.server.ThreadingHTTPServer(("127.0.0.1", port), Handler).serve_forever()
PY
PROXY_PID=$!
for _ in $(seq 1 80); do
  if curl -fsS --max-time 2 "$PROXY_BASE/health/ready" >/dev/null 2>&1; then break; fi
  kill -0 "$PROXY_PID" 2>/dev/null || { echo "Artifact proxy exited before ready." >&2; exit 1; }
  sleep 0.1
done
curl -fsS --max-time 2 "$PROXY_BASE/health/ready" >/dev/null

RUNTIME_ROOT="$TMP/runtime"
mkdir -p "$RUNTIME_ROOT"
cp -a "$CONTENT_ROOT/Configs" "$RUNTIME_ROOT/Configs"
cp -a "$ENVIR_SOURCE" "$RUNTIME_ROOT/Envir"
ln -s "$MAP_SOURCE" "$RUNTIME_ROOT/Maps"

ZONE_DLL="$ROOT/src/Crystal.ZoneServer/bin/$CONFIGURATION/Crystal.ZoneServer.dll"
AGENT_HOME="$TMP/agent"
WRAPPER="$TMP/zone-wrapper.sh"
cat >"$WRAPPER" <<EOF
#!/usr/bin/env bash
set -euo pipefail
release=""
args=("\$@")
for ((i=0; i<\${#args[@]}; i++)); do
  if [ "\${args[\$i]}" = "--content-release" ]; then
    release="\${args[\$((i+1))]}"
  fi
done
if [ -n "\$release" ] && [ -f "\$release/manifest.json" ]; then
  version="\$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["releaseVersion"])' "\$release/manifest.json")"
  if [[ "\$version" == fail.* ]]; then
    echo "intentional ZoneServer startup failure for \$version" >&2
    exit 42
  fi
fi
exec "$DOTNET" "$ZONE_DLL" "\$@"
EOF
chmod +x "$WRAPPER"

ZONE_PORT="$(free_port)"
STATUS_PORT="$(free_port)"
ZONE_ID="zone-agent-$SUFFIX"

start_agent() {
  CRYSTAL_ZONE_IP=127.0.0.1 \
  CRYSTAL_ZONE_PORT="$ZONE_PORT" \
  CRYSTAL_ZONE_STATUS_PORT="$STATUS_PORT" \
  CRYSTAL_ZONE_MAX_USER=5 \
  CRYSTAL_ZONE_THREADS=1 \
  CRYSTAL_ZONE_SAVE_DELAY_MINUTES=1 \
  "$DOTNET" "$ROOT/src/Crystal.ZoneAgent/bin/$CONFIGURATION/net8.0/Crystal.ZoneAgent.dll" \
    --control-api "$PROXY_BASE" \
    --api-key "$API_KEY" \
    --zone-id "$ZONE_ID" \
    --zone-name "Agent E2E Zone" \
    --agent-id "agent-$SUFFIX" \
    --agent-home "$AGENT_HOME" \
    --zone-executable "$WRAPPER" \
    --content-root "$RUNTIME_ROOT" \
    --content-public-key "$TMP/release-public.pem" \
    --content-key-id "zone-agent-test" \
    --poll-seconds 1 \
    --heartbeat-seconds 1 \
    --ready-timeout-seconds 120 \
    --stop-timeout-seconds 20 \
    --lease-seconds 15 \
    >>"$TMP/agent.log" 2>&1 &
  AGENT_PID=$!
}

wait_zone_registered() {
  for _ in $(seq 1 120); do
    if api GET "/api/v1/zones/$ZONE_ID" >"$TMP/zone.json" 2>/dev/null; then
      if python3 - "$TMP/zone.json" <<'PY'
import json,sys
zone=json.load(open(sys.argv[1]))
raise SystemExit(0 if zone.get("lastHeartbeatAt") else 1)
PY
      then return 0; fi
    fi
    sleep 0.25
  done
  echo "Zone did not register." >&2
  return 1
}

wait_zone_metrics() {
  local expected_source="$1"
  local require_positive="$2"
  local deadline=$((SECONDS + 60))
  while [ "$SECONDS" -lt "$deadline" ]; do
    if api GET "/api/v1/zones/$ZONE_ID" >"$TMP/zone.json" 2>/dev/null; then
      if python3 - "$TMP/zone.json" "$expected_source" "$require_positive" <<'PY'
import json,sys
zone=json.load(open(sys.argv[1]))
metadata=zone.get("metadata") or {}
source=metadata.get("metricsSource")
positive=sys.argv[3] == "1"
ok=source == sys.argv[2]
if positive:
    ok = ok and float(zone.get("tickP95Ms") or 0) > 0
else:
    ok = ok and float(zone.get("playerCount") or 0) == 0
    ok = ok and float(zone.get("tickP95Ms") or 0) == 0
    ok = ok and float(zone.get("errorRate") or 0) == 0
raise SystemExit(0 if ok else 1)
PY
      then return 0; fi
    fi
    sleep 0.5
  done
  echo "Zone metrics did not reach $expected_source (positive=$require_positive)." >&2
  return 1
}

wait_metrics_file() {
  local deadline=$((SECONDS + 60))
  while [ "$SECONDS" -lt "$deadline" ]; do
    if [ -f "$AGENT_HOME/state/metrics.json" ] && python3 - "$AGENT_HOME/state/metrics.json" <<'PY'
import json,sys
metrics=json.load(open(sys.argv[1]))
raise SystemExit(0 if metrics.get("tickP95Ms", 0) > 0 and metrics.get("completedTicks", 0) > 0 and metrics.get("sampleCount", 0) > 0 else 1)
PY
    then return 0; fi
    sleep 0.5
  done
  echo "Zone metrics file was not populated." >&2
  return 1
}

create_command() {
  local type="$1"
  local body="$2"
  api POST "/api/v1/zones/$ZONE_ID/commands" \
    "$(printf '{"type":"%s"%s}' "$type" "$body")"
}

wait_command() {
  local command_id="$1"
  local timeout="${2:-300}"
  local deadline=$((SECONDS + timeout))
  while [ "$SECONDS" -lt "$deadline" ]; do
    api GET "/api/v1/zones/$ZONE_ID/commands?limit=200" >"$TMP/commands.json"
    local status
    status="$(python3 - "$TMP/commands.json" "$command_id" <<'PY'
import json,sys
items=json.load(open(sys.argv[1])).get("items", [])
match=next((item for item in items if item.get("commandId")==sys.argv[2]), None)
print(match.get("status") if match else "missing")
PY
)"
    case "$status" in
      succeeded|failed|rejected|expired)
        printf '%s' "$status"
        return 0
        ;;
      missing)
        echo "Command $command_id disappeared." >&2
        return 1
        ;;
    esac
    sleep 1
  done
  echo "Command $command_id timed out." >&2
  return 1
}

command_field() {
  local command_id="$1"
  local key="$2"
  python3 - "$TMP/commands.json" "$command_id" "$key" <<'PY'
import json,sys
items=json.load(open(sys.argv[1])).get("items", [])
match=next(item for item in items if item.get("commandId")==sys.argv[2])
value=match
for part in sys.argv[3].split("."):
    if value is None: break
    value=value.get(part)
print("" if value is None else value)
PY
}

start_agent
wait_zone_registered

# Exercise the CLI surface against the live API.
CRYSTAL_CONTROL_URL="$BASE" CRYSTAL_CONTROL_API_KEY="$API_KEY" \
  "$ROOT/scripts/zone-control.sh" zones >"$TMP/cli-zones.json"
python3 - "$TMP/cli-zones.json" "$ZONE_ID" <<'PY'
import json,sys
zones=json.load(open(sys.argv[1])).get("items", [])
assert any(zone.get("zoneId")==sys.argv[2] for zone in zones), zones
PY

STAGE_RESPONSE="$(create_command content.stage ",\"payload\":{\"releaseId\":$RELEASE_ID_1}")"
STAGE_COMMAND="$(printf '%s' "$STAGE_RESPONSE" | python3 -c 'import json,sys; print(json.load(sys.stdin)["commandId"])')"
[ "$(wait_command "$STAGE_COMMAND" 180)" = "succeeded" ] || { echo "Stage failed." >&2; exit 1; }

api GET "/api/v1/zones/$ZONE_ID" >"$TMP/zone.json"
[ "$(json_value "$TMP/zone.json" 'stagedReleaseId')" = "$RELEASE_ID_1" ] || { echo "Staged release not recorded." >&2; exit 1; }

ACTIVATE_1="$(create_command content.activate ",\"payload\":{\"releaseId\":$RELEASE_ID_1}" | \
  python3 -c 'import json,sys; print(json.load(sys.stdin)["commandId"])')"
[ "$(wait_command "$ACTIVATE_1" 300)" = "succeeded" ] || { echo "First activation failed." >&2; exit 1; }
[ -f "$AGENT_HOME/state/ready.json" ] || { echo "Zone ready file was not created." >&2; exit 1; }
wait_metrics_file || exit 1
python3 - "$AGENT_HOME/state/metrics.json" "$AGENT_HOME/state/ready.json" "$ZONE_ID" <<'PY'
import json,sys
metrics=json.load(open(sys.argv[1]))
ready=json.load(open(sys.argv[2]))
assert metrics["schemaVersion"] == 1, metrics
assert metrics["zoneId"] == sys.argv[3], metrics
assert metrics["pid"] == ready["pid"], (metrics, ready)
assert metrics["tickP95Ms"] > 0, metrics
assert metrics["completedTicks"] > 0, metrics
assert metrics["sampleCount"] > 0, metrics
PY
wait_zone_metrics zone-runtime 1 || exit 1
api GET "/api/v1/zones/$ZONE_ID" >"$TMP/zone.json"
[ "$(json_value "$TMP/zone.json" 'activeReleaseId')" = "$RELEASE_ID_1" ] || { echo "Active release 1 not recorded." >&2; exit 1; }

TAMPER_STAGE="$(create_command content.stage ",\"payload\":{\"releaseId\":$RELEASE_ID_TAMPER}" | \
  python3 -c 'import json,sys; print(json.load(sys.stdin)["commandId"])')"
[ "$(wait_command "$TAMPER_STAGE" 180)" = "failed" ] || { echo "Tampered release stage was not rejected." >&2; exit 1; }
api GET "/api/v1/zones/$ZONE_ID" >"$TMP/zone.json"
[ "$(json_value "$TMP/zone.json" 'activeReleaseId')" = "$RELEASE_ID_1" ] || { echo "Tampered stage changed the active release." >&2; exit 1; }
[ -f "$AGENT_HOME/state/ready.json" ] || { echo "Tampered stage stopped the running zone." >&2; exit 1; }

ACTIVATE_2="$(create_command content.activate ",\"payload\":{\"releaseId\":$RELEASE_ID_2}" | \
  python3 -c 'import json,sys; print(json.load(sys.stdin)["commandId"])')"
[ "$(wait_command "$ACTIVATE_2" 300)" = "succeeded" ] || { echo "Second activation failed." >&2; exit 1; }
python3 - "$AGENT_HOME/state/active.json" "$RELEASE_ID_2" "$RELEASE_ID_1" <<'PY'
import json,sys
active=json.load(open(sys.argv[1]))
assert active["releaseId"] == int(sys.argv[2]), active
assert active["previousReleaseId"] == int(sys.argv[3]), active
PY

ROLLBACK_ID="$(create_command content.rollback ",\"payload\":{\"releaseId\":$RELEASE_ID_1}" | \
  python3 -c 'import json,sys; print(json.load(sys.stdin)["commandId"])')"
[ "$(wait_command "$ROLLBACK_ID" 300)" = "succeeded" ] || { echo "Rollback failed." >&2; exit 1; }
api GET "/api/v1/zones/$ZONE_ID" >"$TMP/zone.json"
[ "$(json_value "$TMP/zone.json" 'activeReleaseId')" = "$RELEASE_ID_1" ] || { echo "Rollback active release not recorded." >&2; exit 1; }

FAIL_ACTIVATE="$(create_command content.activate ",\"payload\":{\"releaseId\":$RELEASE_ID_FAIL}" | \
  python3 -c 'import json,sys; print(json.load(sys.stdin)["commandId"])')"
[ "$(wait_command "$FAIL_ACTIVATE" 300)" = "failed" ] || { echo "Failure activation was not reported as failed." >&2; exit 1; }
[ "$(command_field "$FAIL_ACTIVATE" 'resultPayload.restoredPrevious')" = "True" ] || { echo "Previous release was not restored." >&2; exit 1; }
api GET "/api/v1/zones/$ZONE_ID" >"$TMP/zone.json"
[ "$(json_value "$TMP/zone.json" 'activeReleaseId')" = "$RELEASE_ID_1" ] || { echo "Previous active release changed after failed activation." >&2; exit 1; }

# Re-posting the same result must be idempotent.
python3 - "$AGENT_HOME/state/command-results/$ROLLBACK_ID.json" "$TMP/duplicate-result.json" <<'PY'
import json,sys
stored=json.load(open(sys.argv[1]))
json.dump(stored["result"], open(sys.argv[2], "w"))
PY
DUPLICATE_STATUS="$(curl -sS --max-time 30 -o /dev/null -w '%{http_code}' -X POST \
  -H "X-Api-Key: $API_KEY" -H 'Content-Type: application/json' \
  --data-binary @"$TMP/duplicate-result.json" \
  "$BASE/api/v1/zones/$ZONE_ID/commands/$ROLLBACK_ID/result")"
[ "$DUPLICATE_STATUS" = "200" ] || { echo "Duplicate command result was not idempotent: $DUPLICATE_STATUS." >&2; exit 1; }

# Agent restart must restore the persisted active release without reusing old process metrics.
api GET "/api/v1/zones/$ZONE_ID" >"$TMP/zone.json"
OLD_ZONE_PID="$(json_value "$TMP/zone.json" 'metadata.zonePid')"
[ -n "$OLD_ZONE_PID" ] || { echo "Zone PID was not reported before Agent restart." >&2; exit 1; }
kill -TERM "$AGENT_PID"
for _ in $(seq 1 160); do kill -0 "$AGENT_PID" 2>/dev/null || break; sleep 0.1; done
wait "$AGENT_PID" 2>/dev/null || true
AGENT_PID=""
rm -f "$AGENT_HOME/state/ready.json"
start_agent
wait_zone_registered
for _ in $(seq 1 300); do
  if [ -f "$AGENT_HOME/state/ready.json" ]; then break; fi
  kill -0 "$AGENT_PID" 2>/dev/null || { echo "Agent exited after restart." >&2; exit 1; }
  sleep 0.2
done
[ -f "$AGENT_HOME/state/ready.json" ] || { echo "Restarted Agent did not restore the zone." >&2; exit 1; }
api GET "/api/v1/zones/$ZONE_ID" >"$TMP/zone.json"
[ "$(json_value "$TMP/zone.json" 'activeReleaseId')" = "$RELEASE_ID_1" ] || { echo "Restarted Agent lost active release." >&2; exit 1; }
NEW_ZONE_PID="$(json_value "$AGENT_HOME/state/ready.json" 'pid')"
[ "$NEW_ZONE_PID" != "$OLD_ZONE_PID" ] || { echo "Restarted ZoneServer reused the old PID." >&2; exit 1; }
wait_metrics_file || exit 1
python3 - "$AGENT_HOME/state/metrics.json" "$NEW_ZONE_PID" "$OLD_ZONE_PID" <<'PY'
import json,sys
metrics=json.load(open(sys.argv[1]))
assert metrics["pid"] == int(sys.argv[2]), metrics
assert metrics["pid"] != int(sys.argv[3]), metrics
PY
wait_zone_metrics zone-runtime 1 || exit 1

STOP_RESPONSE="$(CRYSTAL_CONTROL_URL="$BASE" CRYSTAL_CONTROL_API_KEY="$API_KEY" \
  "$ROOT/scripts/zone-control.sh" stop "$ZONE_ID")"
STOP_COMMAND="$(printf '%s' "$STOP_RESPONSE" | sed -n 's/^commandId=//p')"
[ "$(wait_command "$STOP_COMMAND" 120)" = "succeeded" ] || { echo "Stop command failed." >&2; exit 1; }
wait_zone_metrics process-stopped 0 || exit 1
api GET "/api/v1/zones/$ZONE_ID" >"$TMP/zone.json"
[ "$(json_value "$TMP/zone.json" 'activeReleaseId')" = "None" ] || { echo "Stop did not clear active release." >&2; exit 1; }
[ "$(json_value "$TMP/zone.json" 'metadata.metricsSource')" = "process-stopped" ] || { echo "Stopped zone did not report process-stopped." >&2; exit 1; }
python3 - "$TMP/zone.json" <<'PY'
import json, sys
zone = json.load(open(sys.argv[1]))
assert int(zone.get("playerCount") or 0) == 0, zone
assert float(zone.get("tickP95Ms") or 0) == 0, zone
assert float(zone.get("errorRate") or 0) == 0, zone
PY

echo "Zone Agent E2E passed: register, runtime metrics, stage, activate, rollback, failure restore, idempotent result, agent restart without stale metrics and process-stopped zeros."
