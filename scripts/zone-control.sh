#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTROL_URL="${CRYSTAL_CONTROL_URL:-http://127.0.0.1:18088}"
API_KEY="${CRYSTAL_CONTROL_API_KEY:-}"

usage() {
  cat <<'EOF'
Usage: ./scripts/zone-control.sh [--url <control-api>] [--api-key <key>] <command> [args]

Commands:
  zones [--limit <n>]                   List registered zones
  zone <zoneId>                         Show one zone
  commands <zoneId> [--limit <n>]       Show command history
  stage <zoneId> <releaseId>            Stage a published signed release
  activate <zoneId> <releaseId>         Activate a release with a process restart
  rollback <zoneId> <releaseId>         Activate an explicit rollback target
  reload <zoneId>                       Restart the active release
  stop <zoneId>                         Stop the zone and clear its active release
  wait <zoneId> <commandId> [seconds]   Wait for a command to reach a final status

Environment:
  CRYSTAL_CONTROL_URL       Control API base URL (default: http://127.0.0.1:18088)
  CRYSTAL_CONTROL_API_KEY   Existing Control API service key (X-Api-Key)
EOF
}

die() {
  echo "$*" >&2
  exit 2
}

while [ "$#" -gt 0 ]; do
  case "$1" in
    --url)
      [ "$#" -ge 2 ] || die "--url requires a value"
      CONTROL_URL="$2"
      shift 2
      ;;
    --api-key)
      [ "$#" -ge 2 ] || die "--api-key requires a value"
      API_KEY="$2"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      break
      ;;
  esac
done

[ "$#" -ge 1 ] || { usage >&2; exit 2; }
command -v curl >/dev/null 2>&1 || die "curl is required."
command -v python3 >/dev/null 2>&1 || die "python3 is required."

CONTROL_URL="${CONTROL_URL%/}"

api() {
  local method="$1"
  local path="$2"
  local body="${3:-}"
  local args=(-fsS --max-time 120 -X "$method" -H "Accept: application/json")
  [ -n "$API_KEY" ] && args+=(-H "X-Api-Key: $API_KEY")
  if [ -n "$body" ]; then
    args+=(-H "Content-Type: application/json" --data "$body")
  fi
  curl "${args[@]}" "$CONTROL_URL$path"
}

create_command() {
  local zone_id="$1"
  local type="$2"
  local payload="${3:-}"
  local body
  if [ -n "$payload" ]; then
    body="$(printf '{"type":"%s","payload":%s}' "$type" "$payload")"
  else
    body="$(printf '{"type":"%s"}' "$type")"
  fi
  api POST "/api/v1/zones/$zone_id/commands" "$body"
}

command_spec() {
  case "$1" in
    stage) echo "content.stage" ;;
    activate) echo "content.activate" ;;
    rollback) echo "content.rollback" ;;
    reload) echo "content.reload" ;;
    stop) echo "zone.stop" ;;
    *) die "Unknown command: $1" ;;
  esac
}

case "$1" in
  zones)
    shift
    limit="100"
    while [ "$#" -gt 0 ]; do
      case "$1" in
        --limit) [ "$#" -ge 2 ] || die "--limit requires a value"; limit="$2"; shift 2 ;;
        *) die "Unknown argument: $1" ;;
      esac
    done
    api GET "/api/v1/zones?limit=$limit" | python3 -m json.tool
    ;;
  zone)
    [ "$#" -eq 2 ] || die "zone requires <zoneId>"
    api GET "/api/v1/zones/$2" | python3 -m json.tool
    ;;
  commands)
    [ "$#" -ge 2 ] || die "commands requires <zoneId>"
    zone_id="$2"
    shift 2
    limit="50"
    while [ "$#" -gt 0 ]; do
      case "$1" in
        --limit) [ "$#" -ge 2 ] || die "--limit requires a value"; limit="$2"; shift 2 ;;
        *) die "Unknown argument: $1" ;;
      esac
    done
    api GET "/api/v1/zones/$zone_id/commands?limit=$limit" | python3 -m json.tool
    ;;
  stage|activate|rollback)
    [ "$#" -eq 3 ] || die "$1 requires <zoneId> <releaseId>"
    zone_id="$2"
    release_id="$3"
    [[ "$release_id" =~ ^[1-9][0-9]*$ ]] || die "releaseId must be a positive integer"
    response="$(create_command "$zone_id" "$(command_spec "$1")" "{\"releaseId\":$release_id}")"
    printf '%s\n' "$response" | python3 -m json.tool
    printf 'commandId=%s\n' "$(printf '%s' "$response" | python3 -c 'import json,sys; print(json.load(sys.stdin)["commandId"])')"
    ;;
  reload|stop)
    [ "$#" -eq 2 ] || die "$1 requires <zoneId>"
    response="$(create_command "$2" "$(command_spec "$1")")"
    printf '%s\n' "$response" | python3 -m json.tool
    printf 'commandId=%s\n' "$(printf '%s' "$response" | python3 -c 'import json,sys; print(json.load(sys.stdin)["commandId"])')"
    ;;
  wait)
    [ "$#" -ge 3 ] || die "wait requires <zoneId> <commandId> [seconds]"
    zone_id="$2"
    command_id="$3"
    timeout="${4:-600}"
    deadline=$((SECONDS + timeout))
    while [ "$SECONDS" -lt "$deadline" ]; do
      response="$(api GET "/api/v1/zones/$zone_id/commands?limit=200")"
      status="$(printf '%s' "$response" | python3 -c '
import json,sys
command_id=sys.argv[1]
items=json.load(sys.stdin).get("items", [])
match=next((item for item in items if item.get("commandId")==command_id), None)
print(match.get("status") if match else "missing")
' "$command_id")"
      case "$status" in
        succeeded|failed|rejected|expired)
          printf '%s\n' "$response" | python3 -c '
import json,sys
command_id=sys.argv[1]
items=json.load(sys.stdin).get("items", [])
match=next((item for item in items if item.get("commandId")==command_id), None)
print(json.dumps(match, indent=2))
' "$command_id"
          exit 0
          ;;
        missing)
          die "Command $command_id was not found for zone $zone_id."
          ;;
      esac
      sleep 2
    done
    die "Timed out waiting for command $command_id."
    ;;
  *)
    usage >&2
    exit 2
    ;;
esac
