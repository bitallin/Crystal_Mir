#!/usr/bin/env bash
set -euo pipefail
ulimit -c 0 2>/dev/null || true

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET="${DOTNET:-$HOME/.dotnet/dotnet}"
CONFIGURATION="${CONFIGURATION:-Release}"
DSN="${1:-${CRYSTAL_DATABASE_URL:-${CRYSTAL_DB_CONNECTION:-}}}"

if [ -z "$DSN" ]; then
  echo "Set CRYSTAL_DATABASE_URL/CRYSTAL_DB_CONNECTION or pass a PostgreSQL connection string." >&2
  exit 2
fi

if ! command -v curl >/dev/null 2>&1; then
  echo "curl is required for Control API integration tests." >&2
  exit 2
fi

TMP="$(mktemp -d)"
API_PID=""
cleanup() {
  if [ -n "$API_PID" ]; then
    kill "$API_PID" 2>/dev/null || true
    for _ in $(seq 1 20); do
      kill -0 "$API_PID" 2>/dev/null || break
      sleep 0.1
    done
    kill -9 "$API_PID" 2>/dev/null || true
    wait "$API_PID" 2>/dev/null || true
  fi
  rm -rf "$TMP"
}
trap cleanup EXIT

if ! command -v openssl >/dev/null 2>&1; then
  echo "openssl is required to generate release signing test keys." >&2
  exit 2
fi

openssl genpkey -algorithm EC -pkeyopt ec_paramgen_curve:P-256 \
  -out "$TMP/release-private.pem" >/dev/null 2>&1
openssl pkey -in "$TMP/release-private.pem" -pubout \
  -out "$TMP/release-public.pem" >/dev/null 2>&1

cd "$ROOT"
"$DOTNET" build tools/Crystal.LegacyImporter/Crystal.LegacyImporter.csproj -c "$CONFIGURATION" --nologo >/dev/null
"$DOTNET" build src/Crystal.Control.Api/Crystal.Control.Api.csproj -c "$CONFIGURATION" --nologo >/dev/null

"$DOTNET" run --project tools/Crystal.LegacyImporter/Crystal.LegacyImporter.csproj \
  -c "$CONFIGURATION" --no-build -- \
  --db tests/fixtures/golden/Server.MirDB \
  --out "$TMP/snapshot" \
  --content-root tests/fixtures/content \
  --release "api.$(date -u +%Y%m%d%H%M%S)" \
  --dsn "$DSN" >/dev/null

"$DOTNET" run --project tools/Crystal.LegacyImporter/Crystal.LegacyImporter.csproj \
  -c "$CONFIGURATION" --no-build -- \
  --db tests/fixtures/golden/Server.MirDB \
  --export "$TMP/direct/Server.MirDB" >/dev/null

"$DOTNET" run --project tools/Crystal.LegacyImporter/Crystal.LegacyImporter.csproj \
  -c "$CONFIGURATION" --no-build -- \
  --from-postgres "$DSN" \
  --export "$TMP/postgres-rebuild/Server.MirDB" >/dev/null

cmp "$TMP/direct/Server.MirDB" "$TMP/postgres-rebuild/Server.MirDB" >/dev/null

PORT="$(python3 - <<'PY'
import socket
s = socket.socket()
s.bind(("127.0.0.1", 0))
print(s.getsockname()[1])
s.close()
PY
)"
BASE="http://127.0.0.1:$PORT"
API_KEY="control-api-test-key"
JWT_SIGNING_KEY="control-api-test-signing-key-0123456789abcdef"
ADMIN_USERNAME="admin"
ADMIN_PASSWORD="ControlTest!Passw0rd2026"
TEST_PASSWORD="Integration!Passw0rd2026"
RESET_PASSWORD="ResetIntegration!Passw0rd2026"
FINAL_PASSWORD="SelfChange!Passw0rd2026"
WEB_ORIGIN="http://127.0.0.1:4173"

ASPNETCORE_ENVIRONMENT=Production \
ASPNETCORE_URLS="$BASE" \
ControlApi__DatabaseConnectionString="$DSN" \
ControlApi__ApiKey="$API_KEY" \
ControlApi__JwtSigningKey="$JWT_SIGNING_KEY" \
ControlApi__AuthRateLimitPermitLimit=1000 \
ControlApi__ApiRateLimitPermitLimit=5000 \
ControlApi__BootstrapAdminUsername="$ADMIN_USERNAME" \
ControlApi__BootstrapAdminPassword="$ADMIN_PASSWORD" \
ControlApi__CorsOrigins="$WEB_ORIGIN" \
ControlApi__AssetRoot="$ROOT/tests/fixtures/content" \
ControlApi__ReleaseRoot="$TMP/releases" \
ControlApi__ReleaseSigningPrivateKeyPath="$TMP/release-private.pem" \
ControlApi__ReleaseSigningPublicKeyPath="$TMP/release-public.pem" \
ControlApi__ReleaseSigningKeyId="integration-test" \
"$DOTNET" "$ROOT/src/Crystal.Control.Api/bin/$CONFIGURATION/net8.0/Crystal.Control.Api.dll" \
  >"$TMP/api.log" 2>&1 &
API_PID=$!

READY=0
for _ in $(seq 1 80); do
  if curl -fsS --max-time 2 "$BASE/health/ready" >/dev/null 2>&1; then
    READY=1
    break
  fi
  if ! kill -0 "$API_PID" 2>/dev/null; then
    break
  fi
  sleep 0.25
done

if [ "$READY" -ne 1 ]; then
  echo "Control API did not become ready." >&2
  cat "$TMP/api.log" >&2
  exit 1
fi

UNAUTHORIZED_STATUS="$(curl -sS --max-time 10 -o /dev/null -w '%{http_code}' "$BASE/api/v1/import-runs")"
if [ "$UNAUTHORIZED_STATUS" != "401" ]; then
  echo "Expected unauthenticated request to return 401, got $UNAUTHORIZED_STATUS." >&2
  exit 1
fi

CORS_HEADERS="$(curl -sS --max-time 10 -D - -o /dev/null -X OPTIONS   -H "Origin: $WEB_ORIGIN"   -H 'Access-Control-Request-Method: POST'   -H 'Access-Control-Request-Headers: content-type,authorization'   "$BASE/api/v1/auth/login")"
if ! grep -qi "^Access-Control-Allow-Origin: $WEB_ORIGIN" <<<"$CORS_HEADERS"; then
  echo "Expected Control API CORS preflight to allow $WEB_ORIGIN." >&2
  echo "$CORS_HEADERS" >&2
  exit 1
fi

AUTH=(-H "X-Api-Key: $API_KEY")
curl -fsS --max-time 10 "${AUTH[@]}" "$BASE/api/v1/import-runs?limit=1" >"$TMP/runs.json"
curl -fsS --max-time 10 "${AUTH[@]}" "$BASE/api/v1/content/item?page=1&pageSize=5" >"$TMP/items.json"
curl -fsS --max-time 10 "${AUTH[@]}" "$BASE/api/v1/content/item/1" >"$TMP/item.json"
curl -fsS --max-time 10 "${AUTH[@]}" "$BASE/api/v1/content/item/key/item.1" >"$TMP/item-key.json"
curl -fsS --max-time 10 "${AUTH[@]}" "$BASE/api/v1/content/monster?pageSize=1&sort=name&direction=asc" >"$TMP/monster.json"
curl -fsS --max-time 10 "${AUTH[@]}" "$BASE/api/v1/content/map/catalog" >"$TMP/map-catalog.json"
curl -fsS --max-time 10 "${AUTH[@]}" "$BASE/api/v1/content/map/catalog?q=map.1&pageSize=5" >"$TMP/map-catalog-search.json"
curl -fsS --max-time 10 "${AUTH[@]}" "$BASE/api/v1/content/map/1/relations?respawnPage=2&respawnPageSize=5" >"$TMP/map-relations.json"
curl -fsS --max-time 10 "${AUTH[@]}" "$BASE/api/v1/content/monster/1/drops" >"$TMP/monster-drops.json"
curl -fsS --max-time 10 "${AUTH[@]}" "$BASE/api/v1/content/recipe?pageSize=5" >"$TMP/recipes.json"
curl -fsS --max-time 10 "${AUTH[@]}" "$BASE/api/v1/content/buff?pageSize=5" >"$TMP/buffs.json"
curl -fsS --max-time 10 "${AUTH[@]}" "$BASE/api/v1/content/script?pageSize=5" >"$TMP/scripts.json"
curl -fsS --max-time 10 "${AUTH[@]}" "$BASE/api/v1/content/asset?pageSize=5" >"$TMP/assets.json"
curl -fsS --max-time 10 "${AUTH[@]}" "$BASE/api/v1/content/recipe/1/relations" >"$TMP/recipe-relations.json"
curl -fsS --max-time 10 "${AUTH[@]}" "$BASE/api/v1/content/script/1/validation" >"$TMP/script-validation.json"
curl -fsS --max-time 10 "${AUTH[@]}" "$BASE/api/v1/content/asset/1/file" >"$TMP/asset-file.bin"
curl -fsS --max-time 10 "${AUTH[@]}" "$BASE/api/v1/content/npc/1/script" >"$TMP/npc-script.json"

RUN_ID="$(python3 - "$TMP/runs.json" <<'PY'
import json, sys
runs = json.load(open(sys.argv[1]))
assert runs, "no import runs returned"
assert runs[0]["status"] == "imported", runs[0]
assert runs[0]["entityCount"] == 9797, runs[0]
print(runs[0]["id"])
PY
)"

curl -fsS --max-time 10 "${AUTH[@]}" "$BASE/api/v1/import-runs/$RUN_ID/content-types" >"$TMP/counts.json"

python3 - "$TMP" <<'PY'
import hashlib, json, os, sys
root = sys.argv[1]
def load(name):
    with open(os.path.join(root, name), encoding="utf-8") as f:
        return json.load(f)

items = load("items.json")
assert items["entityType"] == "item", items
assert items["page"] == 1 and items["pageSize"] == 5, items
assert items["total"] == 1628, items["total"]
assert len(items["items"]) == 5, len(items["items"])
assert items["items"][0]["data"]["Index"] == 1, items["items"][0]

item = load("item.json")
assert item["contentKey"] == "item.1", item
assert item["data"]["Name"] == "SpiritBlade", item
assert item["data"]["Index"] == 1, item

item_by_key = load("item-key.json")
assert item_by_key["id"] == item["id"], (item_by_key, item)

monster = load("monster.json")
assert monster["entityType"] == "monster", monster
assert monster["total"] == 555, monster["total"]
assert len(monster["items"]) == 1, monster

map_catalog = load("map-catalog.json")
assert map_catalog["entityType"] == "map", map_catalog
assert map_catalog["page"] == 1 and map_catalog["pageSize"] == 500, map_catalog
assert map_catalog["total"] == 463, map_catalog["total"]
assert len(map_catalog["items"]) == 463, len(map_catalog["items"])
assert map_catalog["items"][0] == {
    "localIndex": 1,
    "contentKey": "map.1",
    "index": 1,
    "fileName": "0",
    "title": "BichonProvince",
}, map_catalog["items"][0]

map_catalog_search = load("map-catalog-search.json")
assert map_catalog_search["total"] >= 1, map_catalog_search
assert any(item["contentKey"] == "map.1" for item in map_catalog_search["items"]), map_catalog_search

relations = load("map-relations.json")
assert relations["map"]["contentKey"] == "map.1", relations["map"]
assert relations["map"]["fileName"] == "0", relations["map"]
assert relations["map"]["title"] == "BichonProvince", relations["map"]
assert relations["asset"]["contentKey"] == "asset.maps.0.map", relations["asset"]
assert relations["asset"]["relativePath"] == "Maps/0.map", relations["asset"]
assert relations["asset"]["map"] == {
    "valid": True, "format": 0, "formatName": "mir2-v0",
    "width": 10, "height": 10, "cellCount": 100,
    "walkableCells": 99, "blockedCells": 1, "doorCells": 1,
    "error": None,
}, relations["asset"]
assert len(relations["movements"]) == 72, len(relations["movements"])
assert relations["movements"][0]["targetMapIndex"] == 2, relations["movements"][0]
assert relations["movements"][0]["targetMap"]["contentKey"] == "map.2", relations["movements"][0]
assert relations["movements"][0]["source"] == {"x": 347, "y": 188}, relations["movements"][0]
assert len(relations["safeZones"]) == 6, relations["safeZones"]
assert relations["safeZones"][0]["startPoint"] is True, relations["safeZones"][0]
assert len(relations["npcs"]) == 46, len(relations["npcs"])
assert relations["npcs"][0]["name"] == "Teleport_Gilbert", relations["npcs"][0]
assert relations["npcs"][0]["script"]["contentKey"] == "script.BichonProvince.BorderVillage.Transport", relations["npcs"][0]
assert relations["npcs"][0]["script"]["lineCount"] == 95, relations["npcs"][0]
assert relations["npcs"][1]["script"] is None, relations["npcs"][1]
assert relations["respawns"]["page"] == 2, relations["respawns"]
assert relations["respawns"]["pageSize"] == 5, relations["respawns"]
assert relations["respawns"]["total"] == 360, relations["respawns"]
assert len(relations["respawns"]["items"]) == 5, relations["respawns"]
assert relations["respawns"]["items"][0]["contentKey"] == "respawn.map1.6", relations["respawns"]
assert relations["respawns"]["items"][0]["monsterName"] == "HookingCat", relations["respawns"]

drops = load("monster-drops.json")
assert drops["contentKey"] == "monster.1", drops
assert drops["name"] == "ArcherGuard", drops
assert drops["dropPath"] == "Misc\\ArcherGuard", drops
assert drops["dropContentKey"] == "drop.monster.1", drops
assert len(drops["drops"]) == 1, drops
assert drops["drops"][0]["chance"] == 1, drops
assert drops["drops"][0]["item"]["contentKey"] == "item.710", drops
assert drops["drops"][0]["item"]["name"] == "GreenPoison", drops

recipes = load("recipes.json")
assert recipes["entityType"] == "recipe", recipes
assert recipes["total"] == 1, recipes["total"]
recipe = recipes["items"][0]
assert recipe["contentKey"] == "recipe.GreenPoison", recipe
assert recipe["data"]["name"] == "GreenPoison", recipe
assert recipe["data"]["output"]["itemIndex"] == 710, recipe
assert recipe["data"]["output"]["count"] == 2, recipe

buffs = load("buffs.json")
assert buffs["entityType"] == "buff", buffs
assert buffs["total"] == 59, buffs["total"]
assert buffs["items"][0]["contentKey"] == "buff.TemporalFlux", buffs["items"][0]
assert buffs["items"][0]["data"]["StackType"] == "ResetDuration", buffs["items"][0]

scripts = load("scripts.json")
assert scripts["entityType"] == "script", scripts
assert scripts["total"] == 1, scripts["total"]
script = scripts["items"][0]
assert script["contentKey"] == "script.BichonProvince.BorderVillage.Transport", script
assert script["data"]["npcIndexes"] == [1], script
assert script["data"]["lineCount"] == 95, script

assets = load("assets.json")
assert assets["entityType"] == "asset", assets
assert assets["total"] == 1, assets["total"]
asset = assets["items"][0]
assert asset["contentKey"] == "asset.maps.0.map", asset
assert asset["data"]["assetType"] == "map", asset
assert asset["data"]["relativePath"] == "Maps/0.map", asset
assert asset["data"]["sizeBytes"] == 1252, asset
assert len(asset["data"]["sha256"]) == 64, asset
assert asset["data"]["map"]["width"] == 10 and asset["data"]["map"]["height"] == 10, asset
assert asset["data"]["map"]["blockedCells"] == 1, asset

script_validation = load("script-validation.json")
assert script_validation["available"] is True, script_validation
assert script_validation["contentKey"] == "script.BichonProvince.BorderVillage.Transport", script_validation
assert script_validation["validation"]["valid"] is True, script_validation
assert script_validation["validation"]["errorCount"] == 0, script_validation
assert script_validation["validation"]["warningCount"] == 1, script_validation
assert script_validation["validation"]["compiledSha256"] == "c0e3f43676a6d5bd978d57c2586636efa404e17fd84014102a3d7d377ecdf8ff", script_validation
assert len(script_validation["ast"]["pages"]) == 11, script_validation
assert script_validation["ast"]["pages"][0]["key"] == "@MAIN", script_validation
assert script_validation["issues"][0]["code"] == "empty-page", script_validation

asset_bytes = open(os.path.join(root, "asset-file.bin"), "rb").read()
assert len(asset_bytes) == asset["data"]["sizeBytes"], (len(asset_bytes), asset)
assert hashlib.sha256(asset_bytes).hexdigest() == asset["data"]["sha256"], asset

npc_script = load("npc-script.json")
assert npc_script["npcContentKey"] == "npc.1", npc_script
assert npc_script["name"] == "Teleport_Gilbert", npc_script
assert npc_script["found"] is True, npc_script
assert npc_script["scriptContentKey"] == "script.BichonProvince.BorderVillage.Transport", npc_script
assert npc_script["scriptPath"] == "Envir/NPCs/BichonProvince/BorderVillage/Transport.txt", npc_script
assert npc_script["language"] == "CrystalNpc", npc_script
assert npc_script["lineCount"] == 95, npc_script
assert npc_script["pages"][0] == "@MAIN", npc_script
assert "[@MAIN]" in npc_script["source"], npc_script
assert npc_script["validation"]["valid"] is True, npc_script
assert npc_script["validation"]["warningCount"] == 1, npc_script

recipe_relations = load("recipe-relations.json")
assert recipe_relations["contentKey"] == "recipe.GreenPoison", recipe_relations
assert recipe_relations["name"] == "GreenPoison", recipe_relations
assert recipe_relations["fileName"] == "Envir/Recipe/GreenPoison.txt", recipe_relations
assert recipe_relations["output"]["contentKey"] == "item.710", recipe_relations
assert recipe_relations["output"]["name"] == "GreenPoison", recipe_relations
assert recipe_relations["output"]["count"] == 2, recipe_relations
assert recipe_relations["ingredients"][0]["contentKey"] == "item.710", recipe_relations
assert recipe_relations["ingredients"][0]["count"] == 3, recipe_relations
assert recipe_relations["tools"][0]["contentKey"] == "item.710", recipe_relations
assert recipe_relations["chance"] == 75, recipe_relations
assert recipe_relations["gold"] == 123, recipe_relations
assert recipe_relations["requiredLevel"] == 22, recipe_relations
assert recipe_relations["requiredGender"] == "Female", recipe_relations
assert recipe_relations["requiredClasses"] == ["Taoist"], recipe_relations
assert recipe_relations["requiredFlags"] == [1], recipe_relations
assert recipe_relations["requiredQuests"] == [2], recipe_relations

counts = load("counts.json")
actual = {entry["entityType"]: entry["count"] for entry in counts["counts"]}
expected = {
    "item": 1628, "monster": 555, "map": 463, "npc": 375, "quest": 154,
    "magic": 111, "gameshop": 105, "conquest": 1, "dragon": 1, "respawn": 6341,
    "drop": 1, "recipe": 1, "buff": 59, "script": 1, "asset": 1,
}
assert actual == expected, (actual, expected)
PY

INVALID_PAGE_STATUS="$(curl -sS --max-time 10 -o /dev/null -w '%{http_code}' "${AUTH[@]}" "$BASE/api/v1/content/item?pageSize=201")"
if [ "$INVALID_PAGE_STATUS" != "400" ]; then
  echo "Expected invalid pageSize to return 400, got $INVALID_PAGE_STATUS." >&2
  exit 1
fi

INVALID_MAP_CATALOG_PAGE_STATUS="$(curl -sS --max-time 10 -o /dev/null -w '%{http_code}' \
  "${AUTH[@]}" "$BASE/api/v1/content/map/catalog?pageSize=501")"
if [ "$INVALID_MAP_CATALOG_PAGE_STATUS" != "400" ]; then
  echo "Expected invalid map catalog pageSize to return 400, got $INVALID_MAP_CATALOG_PAGE_STATUS." >&2
  exit 1
fi

UNKNOWN_TYPE_STATUS="$(curl -sS --max-time 10 -o /dev/null -w '%{http_code}' "${AUTH[@]}" "$BASE/api/v1/content/not-a-type")"
if [ "$UNKNOWN_TYPE_STATUS" != "400" ]; then
  echo "Expected unknown entity type to return 400, got $UNKNOWN_TYPE_STATUS." >&2
  exit 1
fi

INVALID_RESPAWN_PAGE_STATUS="$(curl -sS --max-time 10 -o /dev/null -w '%{http_code}' \
  "${AUTH[@]}" "$BASE/api/v1/content/map/1/relations?respawnPageSize=501")"
if [ "$INVALID_RESPAWN_PAGE_STATUS" != "400" ]; then
  echo "Expected invalid respawnPageSize to return 400, got $INVALID_RESPAWN_PAGE_STATUS." >&2
  exit 1
fi

json_tokens() {
  python3 - "$1" "$2" "$3" <<'PY'
import json
import sys
payload = json.load(open(sys.argv[1], encoding="utf-8"))
assert payload["tokenType"] == "Bearer", payload
assert payload["accessToken"], payload
assert payload["refreshToken"], payload
open(sys.argv[2], "w", encoding="utf-8").write(payload["accessToken"])
open(sys.argv[3], "w", encoding="utf-8").write(payload["refreshToken"])
PY
}

LOGIN_BODY="$(printf '{"username":"%s","password":"%s"}' "$ADMIN_USERNAME" "$ADMIN_PASSWORD")"
curl -fsS --max-time 15 -X POST -H 'Content-Type: application/json' \
  --data "$LOGIN_BODY" "$BASE/api/v1/auth/login" >"$TMP/admin-login.json"
json_tokens "$TMP/admin-login.json" "$TMP/admin.access" "$TMP/admin.refresh"
ADMIN_ACCESS="$(cat "$TMP/admin.access")"

curl -fsS --max-time 10 -H "Authorization: Bearer $ADMIN_ACCESS" "$BASE/api/v1/auth/me" >"$TMP/admin-me.json"
curl -fsS --max-time 10 -H "Authorization: Bearer $ADMIN_ACCESS" "$BASE/api/v1/content/item?pageSize=1" >"$TMP/jwt-item.json"

SUFFIX="$(date -u +%s)$$"
FINANCE_USER="finance_${SUFFIX}"
AUDITOR_USER="auditor_${SUFFIX}"
AUTHOR_USER="author_${SUFFIX}"
APPROVER_USER="approver_${SUFFIX}"

create_account() {
  local username="$1"
  local role="$2"
  local output="$3"
  curl -fsS --max-time 15 -X POST -H "Authorization: Bearer $ADMIN_ACCESS" \
    -H 'Content-Type: application/json' \
    --data "$(printf '{"username":"%s","displayName":"%s","password":"%s","roles":["%s"]}' "$username" "$username" "$TEST_PASSWORD" "$role")" \
    "$BASE/api/v1/accounts" >"$output"
}

create_account "$FINANCE_USER" "finance" "$TMP/finance-account.json"
create_account "$AUDITOR_USER" "auditor" "$TMP/auditor-account.json"
create_account "$AUTHOR_USER" "content_author" "$TMP/author-account.json"
create_account "$APPROVER_USER" "release_approver" "$TMP/approver-account.json"

login_account() {
  local username="$1"
  local output="$2"
  local access="$3"
  local refresh="$4"
  curl -fsS --max-time 15 -X POST -H 'Content-Type: application/json' \
    --data "$(printf '{"username":"%s","password":"%s"}' "$username" "$TEST_PASSWORD")" \
    "$BASE/api/v1/auth/login" >"$output"
  json_tokens "$output" "$access" "$refresh"
}

login_account "$AUTHOR_USER" "$TMP/author-login.json" "$TMP/author.access" "$TMP/author.refresh"
login_account "$APPROVER_USER" "$TMP/approver-login.json" "$TMP/approver.access" "$TMP/approver.refresh"
AUTHOR_ACCESS="$(cat "$TMP/author.access")"
APPROVER_ACCESS="$(cat "$TMP/approver.access")"

RELEASE_VERSION="api.release.${SUFFIX}"
RELEASE_BODY="$(printf '{"importRunId":%s,"version":"%s","notes":"integration release"}' "$RUN_ID" "$RELEASE_VERSION")"
curl -fsS --max-time 60 -X POST -H "Authorization: Bearer $AUTHOR_ACCESS" \
  -H 'Content-Type: application/json' --data "$RELEASE_BODY" \
  "$BASE/api/v1/releases" >"$TMP/release-created.json"
RELEASE_ID="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["id"])' "$TMP/release-created.json")"

PUBLISH_BEFORE_COMPILE_STATUS="$(curl -sS --max-time 20 -o /dev/null -w '%{http_code}' -X POST \
  -H "Authorization: Bearer $APPROVER_ACCESS" "$BASE/api/v1/releases/$RELEASE_ID/publish")"
if [ "$PUBLISH_BEFORE_COMPILE_STATUS" != "409" ]; then
  echo "Expected unsigned release publish to return 409, got $PUBLISH_BEFORE_COMPILE_STATUS." >&2
  exit 1
fi

curl -fsS --max-time 60 -X POST -H "Authorization: Bearer $AUTHOR_ACCESS" \
  "$BASE/api/v1/releases/$RELEASE_ID/validate" >"$TMP/release-validated.json"
curl -fsS --max-time 60 -X POST -H "Authorization: Bearer $AUTHOR_ACCESS" \
  "$BASE/api/v1/releases/$RELEASE_ID/compile" >"$TMP/release-compiled.json"
curl -fsS --max-time 20 -H "Authorization: Bearer $AUTHOR_ACCESS" \
  "$BASE/api/v1/releases/$RELEASE_ID/manifest" >"$TMP/release-manifest-unsigned.json"
curl -fsS --max-time 60 -X POST -H "Authorization: Bearer $AUTHOR_ACCESS" \
  "$BASE/api/v1/releases/$RELEASE_ID/sign" >"$TMP/release-signed.json"
curl -fsS --max-time 20 -H "Authorization: Bearer $AUTHOR_ACCESS" \
  "$BASE/api/v1/releases/$RELEASE_ID/manifest" >"$TMP/release-manifest-signed.json"

AUTHOR_PUBLISH_STATUS="$(curl -sS --max-time 20 -o /dev/null -w '%{http_code}' -X POST \
  -H "Authorization: Bearer $AUTHOR_ACCESS" "$BASE/api/v1/releases/$RELEASE_ID/publish")"
if [ "$AUTHOR_PUBLISH_STATUS" != "403" ]; then
  echo "Expected content author publish to return 403, got $AUTHOR_PUBLISH_STATUS." >&2
  exit 1
fi

APPROVER_CREATE_STATUS="$(curl -sS --max-time 20 -o /dev/null -w '%{http_code}' -X POST \
  -H "Authorization: Bearer $APPROVER_ACCESS" -H 'Content-Type: application/json' \
  --data "$(printf '{"importRunId":%s,"version":"%s.denied"}' "$RUN_ID" "$RELEASE_VERSION")" \
  "$BASE/api/v1/releases")"
if [ "$APPROVER_CREATE_STATUS" != "403" ]; then
  echo "Expected release approver create to return 403, got $APPROVER_CREATE_STATUS." >&2
  exit 1
fi

curl -fsS --max-time 20 -X POST -H "Authorization: Bearer $APPROVER_ACCESS" \
  "$BASE/api/v1/releases/$RELEASE_ID/publish" >"$TMP/release-published.json"
curl -fsS --max-time 20 -X POST -H "Authorization: Bearer $APPROVER_ACCESS" \
  "$BASE/api/v1/releases/$RELEASE_ID/publish" >"$TMP/release-published-again.json"
curl -fsS --max-time 20 -H "Authorization: Bearer $APPROVER_ACCESS" \
  "$BASE/api/v1/releases?limit=10&status=published" >"$TMP/release-list.json"

DUPLICATE_VERSION_STATUS="$(curl -sS --max-time 20 -o /dev/null -w '%{http_code}' -X POST \
  -H "Authorization: Bearer $AUTHOR_ACCESS" -H 'Content-Type: application/json' \
  --data "$RELEASE_BODY" "$BASE/api/v1/releases")"
if [ "$DUPLICATE_VERSION_STATUS" != "409" ]; then
  echo "Expected duplicate release version to return 409, got $DUPLICATE_VERSION_STATUS." >&2
  exit 1
fi

INVALID_VERSION_STATUS="$(curl -sS --max-time 20 -o /dev/null -w '%{http_code}' -X POST \
  -H "Authorization: Bearer $AUTHOR_ACCESS" -H 'Content-Type: application/json' \
  --data '{"version":"../escape"}' "$BASE/api/v1/releases")"
if [ "$INVALID_VERSION_STATUS" != "400" ]; then
  echo "Expected invalid release version to return 400, got $INVALID_VERSION_STATUS." >&2
  exit 1
fi

FINANCE_LOGIN="$(printf '{"username":"%s","password":"%s"}' "$FINANCE_USER" "$TEST_PASSWORD")"
curl -fsS --max-time 15 -X POST -H 'Content-Type: application/json' \
  --data "$FINANCE_LOGIN" "$BASE/api/v1/auth/login" >"$TMP/finance-login.json"
json_tokens "$TMP/finance-login.json" "$TMP/finance.access" "$TMP/finance.refresh"
FINANCE_ACCESS="$(cat "$TMP/finance.access")"

FINANCE_CONTENT_STATUS="$(curl -sS --max-time 10 -o /dev/null -w '%{http_code}' \
  -H "Authorization: Bearer $FINANCE_ACCESS" "$BASE/api/v1/content/item?pageSize=1")"
if [ "$FINANCE_CONTENT_STATUS" != "403" ]; then
  echo "Expected finance account content access to return 403, got $FINANCE_CONTENT_STATUS." >&2
  exit 1
fi

FINANCE_ID="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["id"])' "$TMP/finance-account.json")"
ADMIN_ID="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["id"])' "$TMP/admin-me.json")"

SELF_DISABLE_STATUS="$(curl -sS --max-time 10 -o /dev/null -w '%{http_code}' -X POST \
  -H "Authorization: Bearer $ADMIN_ACCESS" -H 'Content-Type: application/json' \
  --data '{"status":"disabled"}' "$BASE/api/v1/accounts/$ADMIN_ID/status")"
if [ "$SELF_DISABLE_STATUS" != "409" ]; then
  echo "Expected self-disable to return 409, got $SELF_DISABLE_STATUS." >&2
  exit 1
fi

SELF_ROLE_REMOVAL_STATUS="$(curl -sS --max-time 10 -o /dev/null -w '%{http_code}' -X PUT \
  -H "Authorization: Bearer $ADMIN_ACCESS" -H 'Content-Type: application/json' \
  --data '{"roles":["auditor"]}' "$BASE/api/v1/accounts/$ADMIN_ID/roles")"
if [ "$SELF_ROLE_REMOVAL_STATUS" != "409" ]; then
  echo "Expected last-admin role removal to return 409, got $SELF_ROLE_REMOVAL_STATUS." >&2
  exit 1
fi

curl -fsS --max-time 10 -X POST -H "Authorization: Bearer $ADMIN_ACCESS" \
  -H 'Content-Type: application/json' --data '{"status":"disabled"}' \
  "$BASE/api/v1/accounts/$FINANCE_ID/status" >"$TMP/finance-disabled.json"
python3 - "$TMP/finance-disabled.json" <<'PY'
import json, sys
account = json.load(open(sys.argv[1]))
assert account["status"] == "disabled", account
assert account["roles"] == ["finance"], account
PY

DISABLED_ACCESS_STATUS="$(curl -sS --max-time 10 -o /dev/null -w '%{http_code}' \
  -H "Authorization: Bearer $FINANCE_ACCESS" "$BASE/api/v1/auth/me")"
if [ "$DISABLED_ACCESS_STATUS" != "401" ]; then
  echo "Expected disabled account session to return 401, got $DISABLED_ACCESS_STATUS." >&2
  exit 1
fi

DISABLED_LOGIN_STATUS="$(curl -sS --max-time 15 -o /dev/null -w '%{http_code}' -X POST \
  -H 'Content-Type: application/json' --data "$FINANCE_LOGIN" "$BASE/api/v1/auth/login")"
if [ "$DISABLED_LOGIN_STATUS" != "401" ]; then
  echo "Expected disabled account login to return 401, got $DISABLED_LOGIN_STATUS." >&2
  exit 1
fi

curl -fsS --max-time 10 -X POST -H "Authorization: Bearer $ADMIN_ACCESS" \
  -H 'Content-Type: application/json' --data '{"status":"active"}' \
  "$BASE/api/v1/accounts/$FINANCE_ID/status" >"$TMP/finance-enabled.json"
python3 - "$TMP/finance-enabled.json" <<'PY'
import json, sys
account = json.load(open(sys.argv[1]))
assert account["status"] == "active", account
PY

curl -fsS --max-time 15 -X POST -H 'Content-Type: application/json' \
  --data "$FINANCE_LOGIN" "$BASE/api/v1/auth/login" >"$TMP/finance-login-2.json"
json_tokens "$TMP/finance-login-2.json" "$TMP/finance-2.access" "$TMP/finance-2.refresh"
FINANCE_ACCESS_2="$(cat "$TMP/finance-2.access")"

RESET_STATUS="$(curl -sS --max-time 15 -o /dev/null -w '%{http_code}' -X POST \
  -H "Authorization: Bearer $ADMIN_ACCESS" -H 'Content-Type: application/json' \
  --data "$(printf '{"newPassword":"%s"}' "$RESET_PASSWORD")" \
  "$BASE/api/v1/accounts/$FINANCE_ID/password-reset")"
if [ "$RESET_STATUS" != "204" ]; then
  echo "Expected admin password reset to return 204, got $RESET_STATUS." >&2
  exit 1
fi

RESET_SESSION_STATUS="$(curl -sS --max-time 10 -o /dev/null -w '%{http_code}' \
  -H "Authorization: Bearer $FINANCE_ACCESS_2" "$BASE/api/v1/auth/me")"
if [ "$RESET_SESSION_STATUS" != "401" ]; then
  echo "Expected password reset to revoke the existing session, got $RESET_SESSION_STATUS." >&2
  exit 1
fi

OLD_PASSWORD_LOGIN_STATUS="$(curl -sS --max-time 15 -o /dev/null -w '%{http_code}' -X POST \
  -H 'Content-Type: application/json' --data "$FINANCE_LOGIN" "$BASE/api/v1/auth/login")"
if [ "$OLD_PASSWORD_LOGIN_STATUS" != "401" ]; then
  echo "Expected old password login after reset to return 401, got $OLD_PASSWORD_LOGIN_STATUS." >&2
  exit 1
fi

RESET_LOGIN="$(printf '{"username":"%s","password":"%s"}' "$FINANCE_USER" "$RESET_PASSWORD")"
curl -fsS --max-time 15 -X POST -H 'Content-Type: application/json' \
  --data "$RESET_LOGIN" "$BASE/api/v1/auth/login" >"$TMP/finance-reset-login.json"
json_tokens "$TMP/finance-reset-login.json" "$TMP/finance-reset.access" "$TMP/finance-reset.refresh"
FINANCE_RESET_ACCESS="$(cat "$TMP/finance-reset.access")"

WRONG_CURRENT_STATUS="$(curl -sS --max-time 10 -o /dev/null -w '%{http_code}' -X POST \
  -H "Authorization: Bearer $FINANCE_RESET_ACCESS" -H 'Content-Type: application/json' \
  --data "$(printf '{"currentPassword":"wrong-password","newPassword":"%s"}' "$FINAL_PASSWORD")" \
  "$BASE/api/v1/auth/change-password")"
if [ "$WRONG_CURRENT_STATUS" != "400" ]; then
  echo "Expected wrong current password to return 400, got $WRONG_CURRENT_STATUS." >&2
  exit 1
fi

SELF_CHANGE_STATUS="$(curl -sS --max-time 10 -o /dev/null -w '%{http_code}' -X POST \
  -H "Authorization: Bearer $FINANCE_RESET_ACCESS" -H 'Content-Type: application/json' \
  --data "$(printf '{"currentPassword":"%s","newPassword":"%s"}' "$RESET_PASSWORD" "$FINAL_PASSWORD")" \
  "$BASE/api/v1/auth/change-password")"
if [ "$SELF_CHANGE_STATUS" != "204" ]; then
  echo "Expected self password change to return 204, got $SELF_CHANGE_STATUS." >&2
  exit 1
fi

KEPT_SESSION_STATUS="$(curl -sS --max-time 10 -o /dev/null -w '%{http_code}' \
  -H "Authorization: Bearer $FINANCE_RESET_ACCESS" "$BASE/api/v1/auth/me")"
if [ "$KEPT_SESSION_STATUS" != "200" ]; then
  echo "Expected current session to remain active after password change, got $KEPT_SESSION_STATUS." >&2
  exit 1
fi

PRE_CHANGE_LOGIN_STATUS="$(curl -sS --max-time 15 -o /dev/null -w '%{http_code}' -X POST \
  -H 'Content-Type: application/json' --data "$RESET_LOGIN" "$BASE/api/v1/auth/login")"
if [ "$PRE_CHANGE_LOGIN_STATUS" != "401" ]; then
  echo "Expected pre-change password login to return 401, got $PRE_CHANGE_LOGIN_STATUS." >&2
  exit 1
fi

FINAL_LOGIN="$(printf '{"username":"%s","password":"%s"}' "$FINANCE_USER" "$FINAL_PASSWORD")"
curl -fsS --max-time 15 -X POST -H 'Content-Type: application/json' \
  --data "$FINAL_LOGIN" "$BASE/api/v1/auth/login" >"$TMP/finance-final-login.json"
json_tokens "$TMP/finance-final-login.json" "$TMP/finance-final.access" "$TMP/finance-final.refresh"
FINANCE_FINAL_ACCESS="$(cat "$TMP/finance-final.access")"

curl -fsS --max-time 10 -X PUT -H "Authorization: Bearer $ADMIN_ACCESS" \
  -H 'Content-Type: application/json' --data '{"roles":["auditor"]}' \
  "$BASE/api/v1/accounts/$FINANCE_ID/roles" >"$TMP/finance-roles.json"
python3 - "$TMP/finance-roles.json" <<'PY'
import json, sys
account = json.load(open(sys.argv[1]))
assert account["roles"] == ["auditor"], account
PY

ROLE_CHANGED_SESSION_STATUS="$(curl -sS --max-time 10 -o /dev/null -w '%{http_code}' \
  -H "Authorization: Bearer $FINANCE_FINAL_ACCESS" "$BASE/api/v1/auth/me")"
if [ "$ROLE_CHANGED_SESSION_STATUS" != "401" ]; then
  echo "Expected role change to revoke the existing session, got $ROLE_CHANGED_SESSION_STATUS." >&2
  exit 1
fi

curl -fsS --max-time 15 -X POST -H 'Content-Type: application/json' \
  --data "$FINAL_LOGIN" "$BASE/api/v1/auth/login" >"$TMP/finance-auditor-login.json"
json_tokens "$TMP/finance-auditor-login.json" "$TMP/finance-auditor.access" "$TMP/finance-auditor.refresh"
FINANCE_AUDITOR_ACCESS="$(cat "$TMP/finance-auditor.access")"

curl -fsS --max-time 10 -H "Authorization: Bearer $FINANCE_AUDITOR_ACCESS" \
  "$BASE/api/v1/content/item?pageSize=1" >"$TMP/finance-auditor-content.json"

AUDITOR_WRITE_STATUS="$(curl -sS --max-time 10 -o /dev/null -w '%{http_code}' -X POST \
  -H "Authorization: Bearer $FINANCE_AUDITOR_ACCESS" -H 'Content-Type: application/json' \
  --data '{"status":"disabled"}' "$BASE/api/v1/accounts/$ADMIN_ID/status")"
if [ "$AUDITOR_WRITE_STATUS" != "403" ]; then
  echo "Expected auditor account write to return 403, got $AUDITOR_WRITE_STATUS." >&2
  exit 1
fi

UNKNOWN_ACCOUNT_STATUS="$(curl -sS --max-time 10 -o /dev/null -w '%{http_code}' -X POST \
  -H "Authorization: Bearer $ADMIN_ACCESS" -H 'Content-Type: application/json' \
  --data '{"status":"active"}' "$BASE/api/v1/accounts/999999999/status")"
if [ "$UNKNOWN_ACCOUNT_STATUS" != "404" ]; then
  echo "Expected unknown account status update to return 404, got $UNKNOWN_ACCOUNT_STATUS." >&2
  exit 1
fi

SHORT_PASSWORD_STATUS="$(curl -sS --max-time 10 -o /dev/null -w '%{http_code}' -X POST \
  -H "Authorization: Bearer $ADMIN_ACCESS" -H 'Content-Type: application/json' \
  --data '{"newPassword":"short"}' "$BASE/api/v1/accounts/$FINANCE_ID/password-reset")"
if [ "$SHORT_PASSWORD_STATUS" != "400" ]; then
  echo "Expected short reset password to return 400, got $SHORT_PASSWORD_STATUS." >&2
  exit 1
fi

AUDITOR_LOGIN="$(printf '{"username":"%s","password":"%s"}' "$AUDITOR_USER" "$TEST_PASSWORD")"
curl -fsS --max-time 15 -X POST -H 'Content-Type: application/json' \
  --data "$AUDITOR_LOGIN" "$BASE/api/v1/auth/login" >"$TMP/auditor-login.json"
json_tokens "$TMP/auditor-login.json" "$TMP/auditor.access" "$TMP/auditor.refresh"
AUDITOR_ACCESS="$(cat "$TMP/auditor.access")"
AUDITOR_REFRESH="$(cat "$TMP/auditor.refresh")"

curl -fsS --max-time 10 -H "Authorization: Bearer $AUDITOR_ACCESS" "$BASE/api/v1/audit/events?limit=20" >"$TMP/auditor-audit.json"

curl -fsS --max-time 15 -X POST -H 'Content-Type: application/json' \
  --data "$(printf '{"refreshToken":"%s"}' "$AUDITOR_REFRESH")" \
  "$BASE/api/v1/auth/refresh" >"$TMP/auditor-refresh.json"
json_tokens "$TMP/auditor-refresh.json" "$TMP/auditor-rotated.access" "$TMP/auditor-rotated.refresh"
AUDITOR_ROTATED_ACCESS="$(cat "$TMP/auditor-rotated.access")"
AUDITOR_ROTATED_REFRESH="$(cat "$TMP/auditor-rotated.refresh")"

OLD_REFRESH_STATUS="$(curl -sS --max-time 10 -o /dev/null -w '%{http_code}' -X POST \
  -H 'Content-Type: application/json' --data "$(printf '{"refreshToken":"%s"}' "$AUDITOR_REFRESH")" \
  "$BASE/api/v1/auth/refresh")"
if [ "$OLD_REFRESH_STATUS" != "401" ]; then
  echo "Expected rotated refresh token reuse to return 401, got $OLD_REFRESH_STATUS." >&2
  exit 1
fi

LOGOUT_STATUS="$(curl -sS --max-time 10 -o /dev/null -w '%{http_code}' -X POST \
  -H "Authorization: Bearer $AUDITOR_ROTATED_ACCESS" "$BASE/api/v1/auth/logout")"
if [ "$LOGOUT_STATUS" != "204" ]; then
  echo "Expected logout to return 204, got $LOGOUT_STATUS." >&2
  exit 1
fi

REVOKED_ACCESS_STATUS="$(curl -sS --max-time 10 -o /dev/null -w '%{http_code}' \
  -H "Authorization: Bearer $AUDITOR_ROTATED_ACCESS" "$BASE/api/v1/auth/me")"
if [ "$REVOKED_ACCESS_STATUS" != "401" ]; then
  echo "Expected logged-out access token to return 401, got $REVOKED_ACCESS_STATUS." >&2
  exit 1
fi

REVOKED_REFRESH_STATUS="$(curl -sS --max-time 10 -o /dev/null -w '%{http_code}' -X POST \
  -H 'Content-Type: application/json' --data "$(printf '{"refreshToken":"%s"}' "$AUDITOR_ROTATED_REFRESH")" \
  "$BASE/api/v1/auth/refresh")"
if [ "$REVOKED_REFRESH_STATUS" != "401" ]; then
  echo "Expected logged-out refresh token to return 401, got $REVOKED_REFRESH_STATUS." >&2
  exit 1
fi

INVALID_LOGIN_STATUS="$(curl -sS --max-time 15 -o /dev/null -w '%{http_code}' -X POST \
  -H 'Content-Type: application/json' --data "$(printf '{"username":"%s","password":"wrong-password"}' "$ADMIN_USERNAME")" \
  "$BASE/api/v1/auth/login")"
if [ "$INVALID_LOGIN_STATUS" != "401" ]; then
  echo "Expected invalid login to return 401, got $INVALID_LOGIN_STATUS." >&2
  exit 1
fi

curl -fsS --max-time 10 -H "Authorization: Bearer $ADMIN_ACCESS" "$BASE/api/v1/audit/events?limit=200" >"$TMP/audit-events.json"

python3 - "$TMP" <<'PY'
import json
import os
import sys

root = sys.argv[1]

def load(name):
    with open(os.path.join(root, name), encoding="utf-8") as f:
        return json.load(f)

me = load("admin-me.json")
assert me["principalType"] == "account", me
assert me["username"] == "admin", me
assert "platform_admin" in me["roles"], me
assert "content:read" in me["permissions"], me
assert "audit:read" in me["permissions"], me

assert load("jwt-item.json")["total"] == 1628
assert load("finance-account.json")["roles"] == ["finance"]
assert load("auditor-account.json")["roles"] == ["auditor"]
assert load("finance-auditor-content.json")["total"] == 1628

raw_audit = load("audit-events.json")
events = raw_audit["items"]
assert any(e["action"] == "account.create" and e["result"] == "success" for e in events), events
assert any(e["action"] == "auth.login" and e["result"] == "success" for e in events), events
assert any(e["action"] == "auth.login" and e["result"] == "failure" for e in events), events
assert any(e["action"] == "auth.refresh" and e["result"] == "success" for e in events), events
assert any(e["action"] == "auth.logout" and e["result"] == "success" for e in events), events
assert any(e["action"] == "authz.denied" and e["result"] == "denied" for e in events), events
assert any(e["action"] == "account.status" and e["metadata"].get("afterStatus") == "disabled" for e in events), events
assert any(e["action"] == "account.password.reset" and e["result"] == "success" for e in events), events
assert any(e["action"] == "auth.password.change" and e["result"] == "success" for e in events), events
assert any(e["action"] == "account.roles.update" and e["metadata"].get("afterRoles") == ["auditor"] for e in events), events
assert any(e["action"] == "release.create" and e["result"] == "success" for e in events), events
assert any(e["action"] == "release.validate" and e["result"] == "success" for e in events), events
assert any(e["action"] == "release.compile" and e["result"] == "success" for e in events), events
assert any(e["action"] == "release.sign" and e["result"] == "success" for e in events), events
assert any(e["action"] == "release.publish" and e["result"] == "success" for e in events), events
audit_text = json.dumps(raw_audit, ensure_ascii=False)
assert "password_hash" not in audit_text, audit_text[:1000]
for secret in ("Integration!Passw0rd2026", "ResetIntegration!Passw0rd2026", "SelfChange!Passw0rd2026"):
    assert secret not in audit_text, secret

created = load("release-created.json")
assert created["status"] == "draft", created
assert created["importRunId"] is not None, created
assert len(created["contentHash"]) == 64, created
expected_counts = {entry["entityType"]: entry["count"] for entry in load("counts.json")["counts"]}
assert {entry["entityType"]: entry["count"] for entry in created["counts"]} == expected_counts, created["counts"]

validated = load("release-validated.json")
assert validated["valid"] is True, validated
assert validated["issues"] == [], validated
assert validated["entityCount"] == 9797, validated

compiled = load("release-compiled.json")
assert compiled["status"] == "compiled", compiled
assert compiled["manifestObjectKey"] == compiled["version"], compiled
assert compiled["contentHash"] == created["contentHash"], (compiled, created)

unsigned_manifest = load("release-manifest-unsigned.json")
assert unsigned_manifest.get("signature") is None, unsigned_manifest

signed = load("release-signed.json")
assert signed["status"] == "signed", signed
signed_manifest = load("release-manifest-signed.json")
assert signed_manifest["signature"]["algorithm"] == "ecdsa-p256-sha256", signed_manifest
assert signed_manifest["signature"]["keyId"] == "integration-test", signed_manifest
assert signed_manifest["signature"]["value"], signed_manifest
assert signed_manifest["contentHash"] == created["contentHash"], signed_manifest

published = load("release-published.json")
assert published["status"] == "published", published
assert published["publishedAt"], published
published_again = load("release-published-again.json")
assert published_again["status"] == "published", published_again
release_list = load("release-list.json")
assert any(release["id"] == created["id"] for release in release_list["items"]), release_list
PY

echo "Control API integration verified (run $RUN_ID, 15 entity types, auth/RBAC/audit/relations/release lifecycle)."
