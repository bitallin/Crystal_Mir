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
  local rc=$?
  if [ "$rc" -ne 0 ]; then
    echo "--- Control API log ---" >&2
    tail -n 160 "$TMP/api.log" 2>/dev/null || true
  fi
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

CONTENT_ROOT="$TMP/content"
cp -a "$ROOT/tests/fixtures/content" "$CONTENT_ROOT"
"$DOTNET" run --project tools/Crystal.LegacyImporter/Crystal.LegacyImporter.csproj \
  -c "$CONFIGURATION" --no-build -- \
  --db tests/fixtures/golden/Server.MirDB \
  --npc-script-manifest "$TMP/npc-scripts.txt" >/dev/null
python3 - "$CONTENT_ROOT" "$TMP/npc-scripts.txt" <<'PY'
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
PY

"$DOTNET" run --project tools/Crystal.LegacyImporter/Crystal.LegacyImporter.csproj \
  -c "$CONFIGURATION" --no-build -- \
  --db tests/fixtures/golden/Server.MirDB \
  --out "$TMP/snapshot" \
  --content-root "$CONTENT_ROOT" \
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
ControlApi__AssetRoot="$CONTENT_ROOT" \
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

UNAUTHORIZED_SCHEMA_STATUS="$(curl -sS --max-time 10 -o /dev/null -w '%{http_code}' "$BASE/api/v1/editor-schemas/item")"
if [ "$UNAUTHORIZED_SCHEMA_STATUS" != "401" ]; then
  echo "Expected unauthenticated editor schema request to return 401, got $UNAUTHORIZED_SCHEMA_STATUS." >&2
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
curl -fsS --max-time 10 "${AUTH[@]}" "$BASE/api/v1/content/script?pageSize=1" >"$TMP/scripts.json"
curl -fsS --max-time 10 "${AUTH[@]}" "$BASE/api/v1/content/script?q=Transport&pageSize=5" >"$TMP/script-transport.json"
curl -fsS --max-time 10 "${AUTH[@]}" "$BASE/api/v1/content/asset?pageSize=5" >"$TMP/assets.json"
curl -fsS --max-time 10 "${AUTH[@]}" "$BASE/api/v1/content/recipe/1/relations" >"$TMP/recipe-relations.json"
TRANSPORT_SCRIPT_INDEX="$(curl -fsS --max-time 10 -H "X-Api-Key: $API_KEY" \
  "$BASE/api/v1/content/script/key/script.BichonProvince.BorderVillage.Transport" | \
  python3 -c 'import json,sys; print(json.load(sys.stdin)["localIndex"])')"
curl -fsS --max-time 10 "${AUTH[@]}" "$BASE/api/v1/content/script/$TRANSPORT_SCRIPT_INDEX/validation" >"$TMP/script-validation.json"
curl -fsS --max-time 10 "${AUTH[@]}" "$BASE/api/v1/content/asset/1/file" >"$TMP/asset-file.bin"
curl -fsS --max-time 10 "${AUTH[@]}" "$BASE/api/v1/content/npc/1/script" >"$TMP/npc-script.json"
curl -fsS --max-time 10 "${AUTH[@]}" "$BASE/api/v1/editor-schemas/item" >"$TMP/item-schema.json"
curl -fsS --max-time 10 "${AUTH[@]}" "$BASE/api/v1/editor-schemas/monster" >"$TMP/monster-schema.json"
curl -fsS --max-time 10 "${AUTH[@]}" "$BASE/api/v1/editor-schemas/map" >"$TMP/map-schema.json"
UNKNOWN_SCHEMA_STATUS="$(curl -sS --max-time 10 -o /dev/null -w '%{http_code}' -H "X-Api-Key: $API_KEY" "$BASE/api/v1/editor-schemas/unknown")"
if [ "$UNKNOWN_SCHEMA_STATUS" != "404" ]; then
  echo "Expected unknown editor schema to return 404, got $UNKNOWN_SCHEMA_STATUS." >&2
  exit 1
fi

RUN_ID="$(python3 - "$TMP/runs.json" <<'PY'
import json, sys
runs = json.load(open(sys.argv[1]))
assert runs, "no import runs returned"
assert runs[0]["status"] == "imported", runs[0]
assert runs[0]["entityCount"] == 10160, runs[0]
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

schema = load("item-schema.json")
assert schema["entityType"] == "item", schema
assert schema["schemaVersion"] == 1, schema
schema_fields = [field for section in schema["sections"] for field in section.get("fields", [])]
random_fields = [field for field in schema_fields if field["path"].startswith("RandomStats.")]
assert len(random_fields) == 70, len(random_fields)
assert any(field["path"] == "Stats" and field["kind"] == "stat-map" for field in schema_fields), schema_fields
assert any(field["path"] == "Bind" and field["kind"] == "flags" for field in schema_fields), schema_fields
assert any(field["path"] == "Type" and field["kind"] == "enum" and len(field.get("options", [])) >= 40 for field in schema_fields), schema_fields
assert {section["id"] for section in schema["sections"]} >= {"identity", "requirements", "inventory", "flags", "stats", "random-stats"}, schema["sections"]

monster = load("monster.json")
assert monster["entityType"] == "monster", monster
assert monster["total"] == 555, monster["total"]
assert len(monster["items"]) == 1, monster

monster_schema = load("monster-schema.json")
assert monster_schema["entityType"] == "monster", monster_schema
assert monster_schema["schemaVersion"] == 1, monster_schema
monster_sections = {section["id"]: section for section in monster_schema["sections"]}
assert set(monster_sections) == {"identity", "basics", "combat", "behavior", "stats"}, monster_schema["sections"]
monster_fields = [field for section in monster_schema["sections"] for field in section.get("fields", [])]
monster_paths = {field["path"] for field in monster_fields}
assert len(monster_fields) == 20, monster_fields
assert monster_paths == {
    "Index", "Name", "Image", "DropPath",
    "AI", "Effect", "Level", "ViewRange", "CoolEye", "Light",
    "AttackSpeed", "MoveSpeed", "Experience",
    "CanTame", "CanPush", "AutoRev", "Undead", "CanRecall", "IsBoss",
    "Stats",
}, monster_paths
assert {"Drops", "HasSpawnScript", "HasDieScript"}.isdisjoint(monster_paths), monster_paths
image_field = next(field for field in monster_fields if field["path"] == "Image")
assert image_field["kind"] == "enum" and len(image_field.get("options", [])) >= 296, image_field
assert any(option["value"] == "ArcherGuard" for option in image_field["options"]), image_field
stats_field = next(field for field in monster_fields if field["path"] == "Stats")
assert stats_field["kind"] == "stat-map", stats_field
assert {option["value"] for option in stats_field["options"]} == {
    "MinAC", "MaxAC", "MinMAC", "MaxMAC", "MinDC", "MaxDC",
    "MinMC", "MaxMC", "MinSC", "MaxSC", "HP", "Accuracy", "Agility",
}, stats_field["options"]
assert all(next(field for field in monster_fields if field["path"] == path)["kind"] == "boolean" for path in (
    "CanTame", "CanPush", "AutoRev", "Undead", "CanRecall", "IsBoss"
))

map_schema = load("map-schema.json")
assert map_schema["entityType"] == "map", map_schema
assert map_schema["schemaVersion"] == 1, map_schema
map_sections = {section["id"]: section for section in map_schema["sections"]}
assert set(map_sections) == {"identity", "presentation", "access", "combat", "population", "territory"}, map_schema["sections"]
map_fields = [field for section in map_schema["sections"] for field in section.get("fields", [])]
map_paths = {field["path"] for field in map_fields}
assert len(map_fields) == 44, map_fields
assert map_paths == {
    "Index", "FileName", "Title",
    "MiniMap", "BigMap", "Music", "Light", "MapDarkLight", "WeatherParticles",
    "NoTeleport", "NoReconnect", "NoReconnectMap", "NoRandom", "NoEscape", "NoRecall",
    "NoTownTeleport", "NoReincarnation", "NeedHole", "NoPosition",
    "Fight", "NoFight", "Fire", "FireDamage", "Lightning", "LightningDamage",
    "FireWallLimit", "FireWallCount", "NoDrug", "NoThrowItem", "NoDropPlayer", "NoDropMonster",
    "NoNames", "NoMount", "NeedBridle", "NoGroup", "RequiredGroup", "RequiredGroupSize",
    "NoPets", "NoIntelligentCreatures", "NoHero", "NoExperience",
    "GT", "GTIndex", "MineIndex",
}, map_paths
assert {"Movements", "SafeZones", "Respawns", "NPCs", "MineZones", "ActiveCoords"}.isdisjoint(map_paths), map_paths
assert next(field for field in map_fields if field["path"] == "Index")["readOnly"] is True, map_fields
weather_field = next(field for field in map_fields if field["path"] == "WeatherParticles")
assert weather_field["kind"] == "string" and weather_field["readOnly"] is True, weather_field
light_field = next(field for field in map_fields if field["path"] == "Light")
assert light_field["kind"] == "enum", light_field
assert [option["value"] for option in light_field["options"]] == ["Normal", "Dawn", "Day", "Evening", "Night"], light_field

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
assert relations["npcs"][1]["script"] is not None, relations["npcs"][1]
assert relations["npcs"][1]["script"]["contentKey"].startswith("script."), relations["npcs"][1]
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
assert scripts["total"] == 364, scripts["total"]
script = load("script-transport.json")["items"][0]
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
    "drop": 1, "recipe": 1, "buff": 59, "script": 364, "asset": 1,
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

ZONE_ID="api-zone-${SUFFIX}"
ZONE_REGISTER_BODY="$(printf '{"zoneId":"%s","zoneName":"API Zone","agentId":"agent-%s","agentVersion":"0.1.0","protocolVersion":1,"contentSchemaVersion":1,"startedAt":"2026-11-17T00:00:00Z","capabilities":["process-supervisor"]}' "$ZONE_ID" "$SUFFIX")"
curl -fsS --max-time 20 -X POST -H 'Content-Type: application/json' \
  --data "$ZONE_REGISTER_BODY" "$BASE/api/v1/zones/register" >"$TMP/zone-registered.json"
curl -fsS --max-time 20 "$BASE/api/v1/zones/$ZONE_ID" >"$TMP/zone-detail.json"
curl -fsS --max-time 20 "$BASE/api/v1/zones?limit=200" >"$TMP/zone-list.json"

assert_status() {
  local expected="$1"
  local actual="$2"
  local label="$3"
  if [ "$actual" != "$expected" ]; then
    echo "$label: expected HTTP $expected, got $actual." >&2
    exit 1
  fi
}

assert_status 404 "$(curl -sS --max-time 20 -o /dev/null -w '%{http_code}' \
  "$BASE/api/v1/zones/unknown-$SUFFIX")" "Unknown zone lookup"
assert_status 404 "$(curl -sS --max-time 20 -o /dev/null -w '%{http_code}' -X POST \
  -H 'Content-Type: application/json' \
  --data '{"zoneId":"unknown-'"$SUFFIX"'","agentId":"missing","observedAt":"2026-11-17T00:00:00Z","status":"online","playerCount":0,"tickP95Ms":0,"errorRate":0}' \
  "$BASE/api/v1/zones/unknown-$SUFFIX/heartbeat")" "Unknown zone heartbeat"
assert_status 400 "$(curl -sS --max-time 20 -o /dev/null -w '%{http_code}' -X POST \
  -H 'Content-Type: application/json' \
  --data '{"zoneId":"'"$ZONE_ID"'","agentId":"agent-'"$SUFFIX"'","observedAt":"2026-11-17T00:00:00Z","status":"banana","playerCount":0,"tickP95Ms":0,"errorRate":0}' \
  "$BASE/api/v1/zones/$ZONE_ID/heartbeat")" "Invalid heartbeat status"
assert_status 401 "$(curl -sS --max-time 20 -o /dev/null -w '%{http_code}' -X POST \
  -H 'Content-Type: application/json' --data '{"type":"zone.stop"}' \
  "$BASE/api/v1/zones/$ZONE_ID/commands")" "Unauthenticated zone command"
assert_status 400 "$(curl -sS --max-time 20 -o /dev/null -w '%{http_code}' -X POST \
  -H "Authorization: Bearer $ADMIN_ACCESS" -H 'Content-Type: application/json' \
  --data '{"type":"not-a-command"}' "$BASE/api/v1/zones/$ZONE_ID/commands")" "Invalid zone command type"
assert_status 400 "$(curl -sS --max-time 20 -o /dev/null -w '%{http_code}' -X POST \
  -H "Authorization: Bearer $ADMIN_ACCESS" -H 'Content-Type: application/json' \
  --data '{"type":"content.activate","payload":{}}' "$BASE/api/v1/zones/$ZONE_ID/commands")" "Missing release payload"
assert_status 400 "$(curl -sS --max-time 20 -o /dev/null -w '%{http_code}' -X POST \
  -H "Authorization: Bearer $ADMIN_ACCESS" -H 'Content-Type: application/json' \
  --data '{"type":"content.stage","payload":{"releaseId":999999999}}' \
  "$BASE/api/v1/zones/$ZONE_ID/commands")" "Unknown release command"
assert_status 400 "$(curl -sS --max-time 20 -o /dev/null -w '%{http_code}' -X POST \
  -H "Authorization: Bearer $ADMIN_ACCESS" -H 'Content-Type: application/json' \
  --data '{"type":"zone.stop","expiresAt":"2026-11-19T00:00:00Z"}' \
  "$BASE/api/v1/zones/$ZONE_ID/commands")" "Command expiry over 24 hours"
assert_status 404 "$(curl -sS --max-time 20 -o /dev/null -w '%{http_code}' -X POST \
  -H "Authorization: Bearer $ADMIN_ACCESS" -H 'Content-Type: application/json' \
  --data '{"type":"zone.stop"}' "$BASE/api/v1/zones/unknown-$SUFFIX/commands")" "Unknown zone command"

curl -fsS --max-time 20 -X POST -H "Authorization: Bearer $ADMIN_ACCESS" \
  -H 'Content-Type: application/json' --data '{"type":"content.reload"}' \
  "$BASE/api/v1/zones/$ZONE_ID/commands" >"$TMP/zone-command-reload.json"
ZONE_RELOAD_COMMAND="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["commandId"])' "$TMP/zone-command-reload.json")"
assert_status 409 "$(curl -sS --max-time 20 -o /dev/null -w '%{http_code}' -X POST \
  -H "Authorization: Bearer $ADMIN_ACCESS" -H 'Content-Type: application/json' \
  --data '{"type":"zone.stop"}' "$BASE/api/v1/zones/$ZONE_ID/commands")" "Concurrent lifecycle command"

curl -fsS --max-time 20 "$BASE/api/v1/zones/$ZONE_ID/commands?claim=true&limit=10&leaseSeconds=5" \
  >"$TMP/zone-command-claimed.json"
sleep 6
curl -fsS --max-time 20 "$BASE/api/v1/zones/$ZONE_ID/commands?claim=true&limit=10&leaseSeconds=5" \
  >"$TMP/zone-command-reclaimed.json"
curl -fsS --max-time 20 "$BASE/api/v1/zones/$ZONE_ID/commands?limit=200" \
  >"$TMP/zone-command-history.json"

ZONE_RESULT_BODY="$(python3 - "$ZONE_RELOAD_COMMAND" "$ZONE_ID" <<'PY'
import json, sys
print(json.dumps({
    "commandId": sys.argv[1],
    "zoneId": sys.argv[2],
    "status": "succeeded",
    "startedAt": "2026-11-17T00:00:01Z",
    "completedAt": "2026-11-17T00:00:02Z",
    "error": None,
    "payload": {"mode": "test"}
}))
PY
)"
curl -fsS --max-time 20 -X POST -H 'Content-Type: application/json' \
  --data "$ZONE_RESULT_BODY" \
  "$BASE/api/v1/zones/$ZONE_ID/commands/$ZONE_RELOAD_COMMAND/result" >"$TMP/zone-result.json"
curl -fsS --max-time 20 -X POST -H 'Content-Type: application/json' \
  --data "$ZONE_RESULT_BODY" \
  "$BASE/api/v1/zones/$ZONE_ID/commands/$ZONE_RELOAD_COMMAND/result" >"$TMP/zone-result-repeat.json"
ZONE_CONFLICT_BODY="$(python3 - "$ZONE_RELOAD_COMMAND" "$ZONE_ID" <<'PY'
import json, sys
print(json.dumps({
    "commandId": sys.argv[1],
    "zoneId": sys.argv[2],
    "status": "failed",
    "startedAt": "2026-11-17T00:00:01Z",
    "completedAt": "2026-11-17T00:00:03Z",
    "error": "conflicting final state"
}))
PY
)"
assert_status 409 "$(curl -sS --max-time 20 -o /dev/null -w '%{http_code}' -X POST \
  -H 'Content-Type: application/json' --data "$ZONE_CONFLICT_BODY" \
  "$BASE/api/v1/zones/$ZONE_ID/commands/$ZONE_RELOAD_COMMAND/result")" "Conflicting command result"
assert_status 404 "$(curl -sS --max-time 20 -o /dev/null -w '%{http_code}' -X POST \
  -H 'Content-Type: application/json' \
  --data '{"commandId":"00000000-0000-0000-0000-000000000000","zoneId":"'"$ZONE_ID"'","status":"succeeded","startedAt":"2026-11-17T00:00:01Z"}' \
  "$BASE/api/v1/zones/$ZONE_ID/commands/00000000-0000-0000-0000-000000000000/result")" "Unknown command result"

EXPIRING_EXPIRES_AT="$(python3 -c 'from datetime import datetime, timedelta, timezone; print((datetime.now(timezone.utc) + timedelta(seconds=2)).isoformat().replace("+00:00", "Z"))')"
curl -fsS --max-time 20 -X POST -H "Authorization: Bearer $ADMIN_ACCESS" \
  -H 'Content-Type: application/json' \
  --data "{\"type\":\"content.reload\",\"expiresAt\":\"$EXPIRING_EXPIRES_AT\"}" \
  "$BASE/api/v1/zones/$ZONE_ID/commands" >"$TMP/zone-command-expiring.json"
ZONE_EXPIRING_COMMAND="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["commandId"])' "$TMP/zone-command-expiring.json")"
sleep 3
curl -fsS --max-time 20 "$BASE/api/v1/zones/$ZONE_ID/commands?limit=200" \
  >"$TMP/zone-command-expired.json"

curl -fsS --max-time 20 -X POST -H "Authorization: Bearer $ADMIN_ACCESS" \
  -H 'Content-Type: application/json' --data '{"type":"zone.stop"}' \
  "$BASE/api/v1/zones/$ZONE_ID/commands" >"$TMP/zone-command-stop.json"
ZONE_STOP_COMMAND="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["commandId"])' "$TMP/zone-command-stop.json")"
ZONE_STOP_RESULT="$(python3 - "$ZONE_STOP_COMMAND" "$ZONE_ID" <<'PY'
import json, sys
print(json.dumps({
    "commandId": sys.argv[1],
    "zoneId": sys.argv[2],
    "status": "succeeded",
    "startedAt": "2026-11-17T00:00:04Z",
    "completedAt": "2026-11-17T00:00:05Z",
    "payload": {"stopped": True}
}))
PY
)"
curl -fsS --max-time 20 -X POST -H 'Content-Type: application/json' \
  --data "$ZONE_STOP_RESULT" \
  "$BASE/api/v1/zones/$ZONE_ID/commands/$ZONE_STOP_COMMAND/result" >/dev/null
curl -fsS --max-time 20 "$BASE/api/v1/zones/$ZONE_ID" >"$TMP/zone-after-stop.json"

python3 - "$TMP" "$ZONE_ID" "$ZONE_RELOAD_COMMAND" "$ZONE_EXPIRING_COMMAND" <<'PY'
import json, os, sys
root, zone_id, reload_command, expiring_command = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4]
def load(name):
    with open(os.path.join(root, name), encoding="utf-8") as f:
        return json.load(f)

registered = load("zone-registered.json")
assert registered["zoneId"] == zone_id, registered
assert registered["status"] == "online", registered
assert load("zone-detail.json")["agentId"].startswith("agent-"), load("zone-detail.json")
assert any(zone["zoneId"] == zone_id for zone in load("zone-list.json")["items"]), load("zone-list.json")

claimed = load("zone-command-claimed.json")["items"]
reclaimed = load("zone-command-reclaimed.json")["items"]
assert len(claimed) == 1 and claimed[0]["commandId"] == reload_command, claimed
assert len(reclaimed) == 1 and reclaimed[0]["commandId"] == reload_command, reclaimed
history = {item["commandId"]: item for item in load("zone-command-history.json")["items"]}
assert history[reload_command]["attempts"] == 2, history[reload_command]
assert history[reload_command]["status"] == "delivered", history[reload_command]
expired = {item["commandId"]: item for item in load("zone-command-expired.json")["items"]}
assert expired[expiring_command]["status"] == "expired", expired[expiring_command]

result = load("zone-result.json")
assert result["status"] == "succeeded", result
assert load("zone-result-repeat.json")["status"] == "succeeded", load("zone-result-repeat.json")
after_stop = load("zone-after-stop.json")
assert after_stop["activeReleaseId"] is None, after_stop
assert after_stop["activeContentHash"] is None, after_stop
assert after_stop["status"] == "degraded", after_stop
PY

APPROVER_DRAFT_CREATE_STATUS="$(curl -sS --max-time 20 -o /dev/null -w '%{http_code}' -X POST \
  -H "Authorization: Bearer $APPROVER_ACCESS" -H 'Content-Type: application/json' \
  --data "$(printf '{"baseImportRunId":%s,"name":"denied draft"}' "$RUN_ID")" \
  "$BASE/api/v1/drafts")"
if [ "$APPROVER_DRAFT_CREATE_STATUS" != "403" ]; then
  echo "Expected release approver draft create to return 403, got $APPROVER_DRAFT_CREATE_STATUS." >&2
  exit 1
fi

DRAFT_NAME="integration-draft-${SUFFIX}"
curl -fsS --max-time 60 -X POST -H "Authorization: Bearer $AUTHOR_ACCESS" \
  -H 'Content-Type: application/json' \
  --data "$(printf '{"baseImportRunId":%s,"name":"%s"}' "$RUN_ID" "$DRAFT_NAME")" \
  "$BASE/api/v1/drafts" >"$TMP/draft-created.json"
DRAFT_ID="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["id"])' "$TMP/draft-created.json")"

APPROVER_SCHEMA_STATUS="$(curl -sS --max-time 20 -o /dev/null -w '%{http_code}' \
  -H "Authorization: Bearer $APPROVER_ACCESS" "$BASE/api/v1/editor-schemas/item")"
if [ "$APPROVER_SCHEMA_STATUS" != "200" ]; then
  echo "Expected release approver to read item editor schema, got $APPROVER_SCHEMA_STATUS." >&2
  exit 1
fi

APPROVER_ENTITY_CREATE_STATUS="$(curl -sS --max-time 20 -o /dev/null -w '%{http_code}' -X POST \
  -H "Authorization: Bearer $APPROVER_ACCESS" -H 'Content-Type: application/json' \
  --data '{"schemaVersion":1,"data":{"Name":"Denied"}}' \
  "$BASE/api/v1/drafts/$DRAFT_ID/content/item")"
if [ "$APPROVER_ENTITY_CREATE_STATUS" != "403" ]; then
  echo "Expected release approver item create to return 403, got $APPROVER_ENTITY_CREATE_STATUS." >&2
  exit 1
fi

APPROVER_ENTITY_UPDATE_STATUS="$(curl -sS --max-time 20 -o /dev/null -w '%{http_code}' -X PUT \
  -H "Authorization: Bearer $APPROVER_ACCESS" -H 'Content-Type: application/json' \
  --data '{"contentKey":"item.1","schemaVersion":1,"expectedRevision":1,"data":{"Index":1,"Name":"Denied"}}' \
  "$BASE/api/v1/drafts/$DRAFT_ID/content/item/1")"
if [ "$APPROVER_ENTITY_UPDATE_STATUS" != "403" ]; then
  echo "Expected release approver item update to return 403, got $APPROVER_ENTITY_UPDATE_STATUS." >&2
  exit 1
fi

curl -fsS --max-time 20 -H "Authorization: Bearer $AUTHOR_ACCESS" \
  "$BASE/api/v1/drafts?limit=20" >"$TMP/drafts-list.json"
curl -fsS --max-time 20 -H "Authorization: Bearer $AUTHOR_ACCESS" \
  "$BASE/api/v1/drafts/$DRAFT_ID" >"$TMP/draft.json"
curl -fsS --max-time 20 -H "Authorization: Bearer $AUTHOR_ACCESS" \
  "$BASE/api/v1/drafts/$DRAFT_ID/content?entityType=item&page=1&pageSize=5" >"$TMP/draft-items.json"
curl -fsS --max-time 20 -H "Authorization: Bearer $AUTHOR_ACCESS" \
  "$BASE/api/v1/drafts/$DRAFT_ID/content/item/1" >"$TMP/draft-item.json"
curl -fsS --max-time 20 -H "Authorization: Bearer $AUTHOR_ACCESS" \
  "$BASE/api/v1/drafts/$DRAFT_ID/content/monster/1" >"$TMP/draft-monster.json"
curl -fsS --max-time 20 -H "Authorization: Bearer $AUTHOR_ACCESS" \
  "$BASE/api/v1/drafts/$DRAFT_ID/content/map/1" >"$TMP/draft-map.json"

python3 - "$TMP/draft-item.json" "$TMP/draft-update-body.json" <<'PY'
import json, sys
entity = json.load(open(sys.argv[1]))
data = entity["data"]
data["Name"] = "SpiritBladeDraft"
data["Price"] = 12345
body = {
    "contentKey": entity["contentKey"],
    "schemaVersion": 1,
    "expectedRevision": entity["revision"],
    "reason": "integration draft update",
    "data": data,
}
json.dump(body, open(sys.argv[2], "w", encoding="utf-8"), ensure_ascii=False)
PY
DRAFT_UPDATE_BODY="$(cat "$TMP/draft-update-body.json")"
curl -fsS --max-time 20 -X PUT -H "Authorization: Bearer $AUTHOR_ACCESS" \
  -H 'Content-Type: application/json' --data "$DRAFT_UPDATE_BODY" \
  "$BASE/api/v1/drafts/$DRAFT_ID/content/item/1" >"$TMP/draft-item-updated.json"

STALE_REVISION_STATUS="$(curl -sS --max-time 20 -o /dev/null -w '%{http_code}' -X PUT \
  -H "Authorization: Bearer $AUTHOR_ACCESS" -H 'Content-Type: application/json' \
  --data "$DRAFT_UPDATE_BODY" "$BASE/api/v1/drafts/$DRAFT_ID/content/item/1")"
if [ "$STALE_REVISION_STATUS" != "409" ]; then
  echo "Expected stale draft revision update to return 409, got $STALE_REVISION_STATUS." >&2
  exit 1
fi

python3 - "$TMP/draft-monster.json" "$TMP/draft-monster-update-body.json" <<'PY'
import json, sys
entity = json.load(open(sys.argv[1]))
data = entity["data"]
data["Name"] = "ArcherGuardDraft"
data["AI"] = 58
data["CanTame"] = True
data["Stats"]["Values"]["HP"] = 8888
data["LegacyEditorNote"] = {"source": "integration"}
body = {
    "contentKey": entity["contentKey"],
    "schemaVersion": 1,
    "expectedRevision": entity["revision"],
    "reason": "integration monster draft update",
    "data": data,
}
json.dump(body, open(sys.argv[2], "w", encoding="utf-8"), ensure_ascii=False)
PY
DRAFT_MONSTER_UPDATE_BODY="$(cat "$TMP/draft-monster-update-body.json")"
curl -fsS --max-time 20 -X PUT -H "Authorization: Bearer $AUTHOR_ACCESS" \
  -H 'Content-Type: application/json' --data "$DRAFT_MONSTER_UPDATE_BODY" \
  "$BASE/api/v1/drafts/$DRAFT_ID/content/monster/1" >"$TMP/draft-monster-updated.json"

STALE_MONSTER_REVISION_STATUS="$(curl -sS --max-time 20 -o /dev/null -w '%{http_code}' -X PUT \
  -H "Authorization: Bearer $AUTHOR_ACCESS" -H 'Content-Type: application/json' \
  --data "$DRAFT_MONSTER_UPDATE_BODY" "$BASE/api/v1/drafts/$DRAFT_ID/content/monster/1")"
if [ "$STALE_MONSTER_REVISION_STATUS" != "409" ]; then
  echo "Expected stale monster revision update to return 409, got $STALE_MONSTER_REVISION_STATUS." >&2
  exit 1
fi

python3 - "$TMP/draft-map.json" "$TMP/draft-map-update-body.json" <<'PY'
import json, sys
entity = json.load(open(sys.argv[1]))
data = entity["data"]
data["Title"] = "BichonProvinceDraft"
data["MiniMap"] = 126
data["Light"] = "Dawn"
data["NoTeleport"] = True
data["FireDamage"] = 25
data["LegacyEditorNote"] = {"source": "integration"}
body = {
    "contentKey": entity["contentKey"],
    "schemaVersion": 1,
    "expectedRevision": entity["revision"],
    "reason": "integration map draft update",
    "data": data,
}
json.dump(body, open(sys.argv[2], "w", encoding="utf-8"), ensure_ascii=False)
PY
DRAFT_MAP_UPDATE_BODY="$(cat "$TMP/draft-map-update-body.json")"
curl -fsS --max-time 20 -X PUT -H "Authorization: Bearer $AUTHOR_ACCESS" \
  -H 'Content-Type: application/json' --data "$DRAFT_MAP_UPDATE_BODY" \
  "$BASE/api/v1/drafts/$DRAFT_ID/content/map/1" >"$TMP/draft-map-updated.json"

STALE_MAP_REVISION_STATUS="$(curl -sS --max-time 20 -o /dev/null -w '%{http_code}' -X PUT \
  -H "Authorization: Bearer $AUTHOR_ACCESS" -H 'Content-Type: application/json' \
  --data "$DRAFT_MAP_UPDATE_BODY" "$BASE/api/v1/drafts/$DRAFT_ID/content/map/1")"
if [ "$STALE_MAP_REVISION_STATUS" != "409" ]; then
  echo "Expected stale map revision update to return 409, got $STALE_MAP_REVISION_STATUS." >&2
  exit 1
fi

put_invalid_map() {
  local output="$1"
  local data="$2"
  local content_key="${3:-map.1}"
  curl -sS --max-time 20 -o "$output" -w '%{http_code}' -X PUT \
    -H "Authorization: Bearer $AUTHOR_ACCESS" -H 'Content-Type: application/json' \
    --data "$(printf '{"contentKey":"%s","schemaVersion":1,"expectedRevision":2,"data":%s}' "$content_key" "$data")" \
    "$BASE/api/v1/drafts/$DRAFT_ID/content/map/1"
}

INVALID_MAP_RANGE_STATUS="$(put_invalid_map "$TMP/invalid-map-range-error.json" '{"Index":1,"MiniMap":65536}')"
if [ "$INVALID_MAP_RANGE_STATUS" != "400" ]; then
  echo "Expected out-of-range map MiniMap to return 400, got $INVALID_MAP_RANGE_STATUS." >&2
  exit 1
fi

INVALID_MAP_LIGHT_STATUS="$(put_invalid_map "$TMP/invalid-map-light-error.json" '{"Index":1,"Light":"Noon"}')"
if [ "$INVALID_MAP_LIGHT_STATUS" != "400" ]; then
  echo "Expected invalid map Light to return 400, got $INVALID_MAP_LIGHT_STATUS." >&2
  exit 1
fi

INVALID_MAP_BOOLEAN_STATUS="$(put_invalid_map "$TMP/invalid-map-boolean-error.json" '{"Index":1,"NoTeleport":"yes"}')"
if [ "$INVALID_MAP_BOOLEAN_STATUS" != "400" ]; then
  echo "Expected invalid map boolean to return 400, got $INVALID_MAP_BOOLEAN_STATUS." >&2
  exit 1
fi

INVALID_MAP_INDEX_STATUS="$(put_invalid_map "$TMP/invalid-map-index-error.json" '{"Index":2,"Title":"WrongMapIndex"}')"
if [ "$INVALID_MAP_INDEX_STATUS" != "400" ]; then
  echo "Expected mismatched map Index to return 400, got $INVALID_MAP_INDEX_STATUS." >&2
  exit 1
fi

INVALID_MAP_KEY_STATUS="$(put_invalid_map "$TMP/invalid-map-key-error.json" '{"Index":1,"Title":"WrongMapKey"}' 'map.999999')"
if [ "$INVALID_MAP_KEY_STATUS" != "400" ]; then
  echo "Expected mismatched map contentKey to return 400, got $INVALID_MAP_KEY_STATUS." >&2
  exit 1
fi

put_invalid_monster() {
  local output="$1"
  local data="$2"
  local content_key="${3:-monster.1}"
  curl -sS --max-time 20 -o "$output" -w '%{http_code}' -X PUT \
    -H "Authorization: Bearer $AUTHOR_ACCESS" -H 'Content-Type: application/json' \
    --data "$(printf '{"contentKey":"%s","schemaVersion":1,"expectedRevision":2,"data":%s}' "$content_key" "$data")" \
    "$BASE/api/v1/drafts/$DRAFT_ID/content/monster/1"
}

INVALID_MONSTER_AI_STATUS="$(put_invalid_monster "$TMP/invalid-monster-ai-error.json" '{"Index":1,"AI":"fast"}')"
if [ "$INVALID_MONSTER_AI_STATUS" != "400" ]; then
  echo "Expected invalid monster AI type to return 400, got $INVALID_MONSTER_AI_STATUS." >&2
  exit 1
fi

INVALID_MONSTER_IMAGE_STATUS="$(put_invalid_monster "$TMP/invalid-monster-image-error.json" '{"Index":1,"Image":"NotAMonsterImage"}')"
if [ "$INVALID_MONSTER_IMAGE_STATUS" != "400" ]; then
  echo "Expected invalid monster image enum to return 400, got $INVALID_MONSTER_IMAGE_STATUS." >&2
  exit 1
fi

INVALID_MONSTER_LEVEL_STATUS="$(put_invalid_monster "$TMP/invalid-monster-level-error.json" '{"Index":1,"Level":65536}')"
if [ "$INVALID_MONSTER_LEVEL_STATUS" != "400" ]; then
  echo "Expected out-of-range monster level to return 400, got $INVALID_MONSTER_LEVEL_STATUS." >&2
  exit 1
fi

INVALID_MONSTER_BOOLEAN_STATUS="$(put_invalid_monster "$TMP/invalid-monster-boolean-error.json" '{"Index":1,"CanTame":"yes"}')"
if [ "$INVALID_MONSTER_BOOLEAN_STATUS" != "400" ]; then
  echo "Expected invalid monster boolean to return 400, got $INVALID_MONSTER_BOOLEAN_STATUS." >&2
  exit 1
fi

INVALID_MONSTER_STAT_KEY_STATUS="$(put_invalid_monster "$TMP/invalid-monster-stat-key-error.json" '{"Index":1,"Stats":{"Values":{"NotAStat":1}}}')"
if [ "$INVALID_MONSTER_STAT_KEY_STATUS" != "400" ]; then
  echo "Expected unknown monster stat key to return 400, got $INVALID_MONSTER_STAT_KEY_STATUS." >&2
  exit 1
fi

INVALID_MONSTER_STAT_RANGE_STATUS="$(put_invalid_monster "$TMP/invalid-monster-stat-range-error.json" '{"Index":1,"Stats":{"Values":{"HP":2147483648}}}')"
if [ "$INVALID_MONSTER_STAT_RANGE_STATUS" != "400" ]; then
  echo "Expected out-of-range monster stat to return 400, got $INVALID_MONSTER_STAT_RANGE_STATUS." >&2
  exit 1
fi

INVALID_MONSTER_INDEX_STATUS="$(put_invalid_monster "$TMP/invalid-monster-index-error.json" '{"Index":2,"Name":"WrongMonsterIndex"}')"
if [ "$INVALID_MONSTER_INDEX_STATUS" != "400" ]; then
  echo "Expected mismatched monster Index to return 400, got $INVALID_MONSTER_INDEX_STATUS." >&2
  exit 1
fi

INVALID_MONSTER_KEY_STATUS="$(put_invalid_monster "$TMP/invalid-monster-key-error.json" '{"Index":1,"Name":"WrongMonsterKey"}' 'monster.999999')"
if [ "$INVALID_MONSTER_KEY_STATUS" != "400" ]; then
  echo "Expected mismatched monster contentKey to return 400, got $INVALID_MONSTER_KEY_STATUS." >&2
  exit 1
fi

INVALID_ITEM_TYPE_STATUS="$(curl -sS --max-time 20 -o "$TMP/invalid-item-type-error.json" -w '%{http_code}' -X PUT \
  -H "Authorization: Bearer $AUTHOR_ACCESS" -H 'Content-Type: application/json' \
  --data '{"contentKey":"item.1","schemaVersion":1,"expectedRevision":2,"data":{"Index":1,"Name":123}}' \
  "$BASE/api/v1/drafts/$DRAFT_ID/content/item/1")"
if [ "$INVALID_ITEM_TYPE_STATUS" != "400" ]; then
  echo "Expected invalid item string type to return 400, got $INVALID_ITEM_TYPE_STATUS." >&2
  exit 1
fi

INVALID_ITEM_ENUM_STATUS="$(curl -sS --max-time 20 -o "$TMP/invalid-item-enum-error.json" -w '%{http_code}' -X PUT \
  -H "Authorization: Bearer $AUTHOR_ACCESS" -H 'Content-Type: application/json' \
  --data '{"contentKey":"item.1","schemaVersion":1,"expectedRevision":2,"data":{"Index":1,"Type":"NotAnItemType"}}' \
  "$BASE/api/v1/drafts/$DRAFT_ID/content/item/1")"
if [ "$INVALID_ITEM_ENUM_STATUS" != "400" ]; then
  echo "Expected invalid item enum to return 400, got $INVALID_ITEM_ENUM_STATUS." >&2
  exit 1
fi

INVALID_ITEM_RANGE_STATUS="$(curl -sS --max-time 20 -o "$TMP/invalid-item-range-error.json" -w '%{http_code}' -X PUT \
  -H "Authorization: Bearer $AUTHOR_ACCESS" -H 'Content-Type: application/json' \
  --data '{"contentKey":"item.1","schemaVersion":1,"expectedRevision":2,"data":{"Index":1,"Weight":256}}' \
  "$BASE/api/v1/drafts/$DRAFT_ID/content/item/1")"
if [ "$INVALID_ITEM_RANGE_STATUS" != "400" ]; then
  echo "Expected out-of-range item integer to return 400, got $INVALID_ITEM_RANGE_STATUS." >&2
  exit 1
fi

INVALID_ITEM_FLAGS_STATUS="$(curl -sS --max-time 20 -o "$TMP/invalid-item-flags-error.json" -w '%{http_code}' -X PUT \
  -H "Authorization: Bearer $AUTHOR_ACCESS" -H 'Content-Type: application/json' \
  --data '{"contentKey":"item.1","schemaVersion":1,"expectedRevision":2,"data":{"Index":1,"Bind":"DontTrade,NotAFlag"}}' \
  "$BASE/api/v1/drafts/$DRAFT_ID/content/item/1")"
if [ "$INVALID_ITEM_FLAGS_STATUS" != "400" ]; then
  echo "Expected unknown item flag to return 400, got $INVALID_ITEM_FLAGS_STATUS." >&2
  exit 1
fi

INVALID_ITEM_BOOLEAN_STATUS="$(curl -sS --max-time 20 -o "$TMP/invalid-item-boolean-error.json" -w '%{http_code}' -X PUT \
  -H "Authorization: Bearer $AUTHOR_ACCESS" -H 'Content-Type: application/json' \
  --data '{"contentKey":"item.1","schemaVersion":1,"expectedRevision":2,"data":{"Index":1,"StartItem":"yes"}}' \
  "$BASE/api/v1/drafts/$DRAFT_ID/content/item/1")"
if [ "$INVALID_ITEM_BOOLEAN_STATUS" != "400" ]; then
  echo "Expected invalid item boolean to return 400, got $INVALID_ITEM_BOOLEAN_STATUS." >&2
  exit 1
fi

INVALID_ITEM_INDEX_STATUS="$(curl -sS --max-time 20 -o "$TMP/invalid-item-index-error.json" -w '%{http_code}' -X PUT \
  -H "Authorization: Bearer $AUTHOR_ACCESS" -H 'Content-Type: application/json' \
  --data '{"contentKey":"item.1","schemaVersion":1,"expectedRevision":2,"data":{"Index":2,"Name":"WrongIndex"}}' \
  "$BASE/api/v1/drafts/$DRAFT_ID/content/item/1")"
if [ "$INVALID_ITEM_INDEX_STATUS" != "400" ]; then
  echo "Expected mismatched item Index to return 400, got $INVALID_ITEM_INDEX_STATUS." >&2
  exit 1
fi

INVALID_ITEM_KEY_STATUS="$(curl -sS --max-time 20 -o "$TMP/invalid-item-key-error.json" -w '%{http_code}' -X PUT \
  -H "Authorization: Bearer $AUTHOR_ACCESS" -H 'Content-Type: application/json' \
  --data '{"contentKey":"item.999999","schemaVersion":1,"expectedRevision":2,"data":{"Index":1,"Name":"WrongKey"}}' \
  "$BASE/api/v1/drafts/$DRAFT_ID/content/item/1")"
if [ "$INVALID_ITEM_KEY_STATUS" != "400" ]; then
  echo "Expected mismatched item contentKey to return 400, got $INVALID_ITEM_KEY_STATUS." >&2
  exit 1
fi

ADDED_ITEM_BODY='{"schemaVersion":1,"reason":"integration draft add","data":{"Name":"DraftAddedItem","Price":1,"Stats":{}}}'
curl -fsS --max-time 20 -X POST -H "Authorization: Bearer $AUTHOR_ACCESS" \
  -H 'Content-Type: application/json' \
  --data "$ADDED_ITEM_BODY" \
  "$BASE/api/v1/drafts/$DRAFT_ID/content/item" >"$TMP/draft-item-added.json"
ADDED_ITEM_INDEX="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["localIndex"])' "$TMP/draft-item-added.json")"
ADDED_ITEM_KEY="item.${ADDED_ITEM_INDEX}"

ADDED_MONSTER_BODY='{"schemaVersion":1,"reason":"integration draft add monster","data":{"Name":"DraftIncompleteMonster","Stats":{}}}'
curl -fsS --max-time 20 -X POST -H "Authorization: Bearer $AUTHOR_ACCESS" \
  -H 'Content-Type: application/json' \
  --data "$ADDED_MONSTER_BODY" \
  "$BASE/api/v1/drafts/$DRAFT_ID/content/monster" >"$TMP/draft-monster-added.json"
ADDED_MONSTER_INDEX="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["localIndex"])' "$TMP/draft-monster-added.json")"
ADDED_MONSTER_KEY="monster.${ADDED_MONSTER_INDEX}"

ADDED_MAP_BODY='{"schemaVersion":1,"reason":"integration draft add duplicate map","data":{"FileName":"0","Title":"DuplicateMap"}}'
curl -fsS --max-time 20 -X POST -H "Authorization: Bearer $AUTHOR_ACCESS" \
  -H 'Content-Type: application/json' \
  --data "$ADDED_MAP_BODY" \
  "$BASE/api/v1/drafts/$DRAFT_ID/content/map" >"$TMP/draft-map-added.json"
ADDED_MAP_INDEX="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["localIndex"])' "$TMP/draft-map-added.json")"
ADDED_MAP_KEY="map.${ADDED_MAP_INDEX}"

ADDED_INCOMPLETE_MAP_BODY='{"schemaVersion":1,"reason":"integration draft add incomplete map","data":{"FileName":"DraftIncompleteMap"}}'
curl -fsS --max-time 20 -X POST -H "Authorization: Bearer $AUTHOR_ACCESS" \
  -H 'Content-Type: application/json' \
  --data "$ADDED_INCOMPLETE_MAP_BODY" \
  "$BASE/api/v1/drafts/$DRAFT_ID/content/map" >"$TMP/draft-map-incomplete-added.json"
ADDED_INCOMPLETE_MAP_INDEX="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["localIndex"])' "$TMP/draft-map-incomplete-added.json")"
ADDED_INCOMPLETE_MAP_KEY="map.${ADDED_INCOMPLETE_MAP_INDEX}"

curl -fsS --max-time 60 -X POST -H "Authorization: Bearer $AUTHOR_ACCESS" \
  "$BASE/api/v1/drafts/$DRAFT_ID/validate" >"$TMP/draft-incomplete-report.json"

curl -fsS --max-time 20 -X DELETE -H "Authorization: Bearer $AUTHOR_ACCESS" \
  -H 'Content-Type: application/json' \
  --data '{"expectedRevision":1,"reason":"integration incomplete map draft delete"}' \
  "$BASE/api/v1/drafts/$DRAFT_ID/content/map/$ADDED_INCOMPLETE_MAP_INDEX"

curl -fsS --max-time 20 -X DELETE -H "Authorization: Bearer $AUTHOR_ACCESS" \
  -H 'Content-Type: application/json' \
  --data '{"expectedRevision":1,"reason":"integration duplicate map draft delete"}' \
  "$BASE/api/v1/drafts/$DRAFT_ID/content/map/$ADDED_MAP_INDEX"

curl -fsS --max-time 20 -X DELETE -H "Authorization: Bearer $AUTHOR_ACCESS" \
  -H 'Content-Type: application/json' \
  --data '{"expectedRevision":1,"reason":"integration monster draft delete"}' \
  "$BASE/api/v1/drafts/$DRAFT_ID/content/monster/$ADDED_MONSTER_INDEX"

curl -fsS --max-time 20 -X DELETE -H "Authorization: Bearer $AUTHOR_ACCESS" \
  -H 'Content-Type: application/json' \
  --data '{"expectedRevision":1,"reason":"integration draft delete"}' \
  "$BASE/api/v1/drafts/$DRAFT_ID/content/item/$ADDED_ITEM_INDEX"

STALE_DELETE_STATUS="$(curl -sS --max-time 20 -o /dev/null -w '%{http_code}' -X DELETE \
  -H "Authorization: Bearer $AUTHOR_ACCESS" -H 'Content-Type: application/json' \
  --data '{"expectedRevision":1,"reason":"stale delete"}' \
  "$BASE/api/v1/drafts/$DRAFT_ID/content/item/1")"
if [ "$STALE_DELETE_STATUS" != "409" ]; then
  echo "Expected stale draft revision delete to return 409, got $STALE_DELETE_STATUS." >&2
  exit 1
fi

curl -fsS --max-time 20 -X PUT -H "Authorization: Bearer $AUTHOR_ACCESS" \
  -H 'Content-Type: application/json' \
  --data '{"contentKey":"script.999998","schemaVersion":1,"expectedRevision":0,"reason":"create invalid script","data":{"validation":{"errorCount":1}}}' \
  "$BASE/api/v1/drafts/$DRAFT_ID/content/script/999998" >"$TMP/draft-invalid-script.json"

curl -fsS --max-time 60 -X POST -H "Authorization: Bearer $AUTHOR_ACCESS" \
  "$BASE/api/v1/drafts/$DRAFT_ID/validate" >"$TMP/draft-invalid-report.json"

curl -fsS --max-time 20 -X DELETE -H "Authorization: Bearer $AUTHOR_ACCESS" \
  -H 'Content-Type: application/json' \
  --data '{"expectedRevision":1,"reason":"remove invalid script"}' \
  "$BASE/api/v1/drafts/$DRAFT_ID/content/script/999998"

curl -fsS --max-time 60 -X POST -H "Authorization: Bearer $AUTHOR_ACCESS" \
  "$BASE/api/v1/drafts/$DRAFT_ID/validate" >"$TMP/draft-valid-report.json"
curl -fsS --max-time 20 -H "Authorization: Bearer $AUTHOR_ACCESS" \
  "$BASE/api/v1/drafts/$DRAFT_ID/revisions?limit=50" >"$TMP/draft-revisions.json"

DRAFT_RELEASE_VERSION="api.draft.${SUFFIX}"
python3 "$ROOT/scripts/test-m2-content.py" "$BASE" "$DRAFT_ID" "$AUTHOR_ACCESS" "$RUN_ID"

curl -fsS --max-time 60 -X POST -H "Authorization: Bearer $AUTHOR_ACCESS" \
  -H 'Content-Type: application/json' \
  --data "$(printf '{"draftId":%s,"version":"%s","notes":"release from editable draft"}' "$DRAFT_ID" "$DRAFT_RELEASE_VERSION")" \
  "$BASE/api/v1/releases" >"$TMP/draft-release-created.json"
DRAFT_RELEASE_ID="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["id"])' "$TMP/draft-release-created.json")"

BOTH_RELEASE_SOURCES_STATUS="$(curl -sS --max-time 20 -o /dev/null -w '%{http_code}' -X POST \
  -H "Authorization: Bearer $AUTHOR_ACCESS" -H 'Content-Type: application/json' \
  --data "$(printf '{"importRunId":%s,"draftId":%s,"version":"%s.both"}' "$RUN_ID" "$DRAFT_ID" "$DRAFT_RELEASE_VERSION")" \
  "$BASE/api/v1/releases")"
if [ "$BOTH_RELEASE_SOURCES_STATUS" != "400" ]; then
  echo "Expected release with both sources to return 400, got $BOTH_RELEASE_SOURCES_STATUS." >&2
  exit 1
fi

curl -fsS --max-time 60 -X POST -H "Authorization: Bearer $AUTHOR_ACCESS" \
  "$BASE/api/v1/releases/$DRAFT_RELEASE_ID/validate" >"$TMP/draft-release-validated.json"
curl -fsS --max-time 60 -X POST -H "Authorization: Bearer $AUTHOR_ACCESS" \
  "$BASE/api/v1/releases/$DRAFT_RELEASE_ID/compile" >"$TMP/draft-release-compiled.json"
curl -fsS --max-time 60 -X POST -H "Authorization: Bearer $AUTHOR_ACCESS" \
  "$BASE/api/v1/releases/$DRAFT_RELEASE_ID/sign" >"$TMP/draft-release-signed.json"
curl -fsS --max-time 20 -X POST -H "Authorization: Bearer $APPROVER_ACCESS" \
  "$BASE/api/v1/releases/$DRAFT_RELEASE_ID/publish" >"$TMP/draft-release-published.json"

python3 - "$TMP/draft-item-updated.json" "$TMP/draft-after-release-body.json" <<'PY'
import json, sys
entity = json.load(open(sys.argv[1]))
data = entity["data"]
data["Name"] = "SpiritBladeAfterRelease"
data["Price"] = 54321
body = {
    "contentKey": entity["contentKey"],
    "schemaVersion": 1,
    "expectedRevision": entity["revision"],
    "reason": "mutate draft after release",
    "data": data,
}
json.dump(body, open(sys.argv[2], "w", encoding="utf-8"), ensure_ascii=False)
PY
curl -fsS --max-time 20 -X PUT -H "Authorization: Bearer $AUTHOR_ACCESS" \
  -H 'Content-Type: application/json' \
  --data "$(cat "$TMP/draft-after-release-body.json")" \
  "$BASE/api/v1/drafts/$DRAFT_ID/content/item/1" >"$TMP/draft-item-after-release.json"
curl -fsS --max-time 60 -X POST -H "Authorization: Bearer $AUTHOR_ACCESS" \
  "$BASE/api/v1/releases/$DRAFT_RELEASE_ID/validate" >"$TMP/draft-release-validated-after-change.json"

ROLLBACK_RELEASE_VERSION="api.rollback.${SUFFIX}"
curl -fsS --max-time 60 -X POST -H "Authorization: Bearer $AUTHOR_ACCESS" \
  -H 'Content-Type: application/json' \
  --data "$(printf '{"importRunId":%s,"version":"%s","notes":"explicit rollback target test"}' "$RUN_ID" "$ROLLBACK_RELEASE_VERSION")" \
  "$BASE/api/v1/releases" >"$TMP/rollback-release-created.json"
ROLLBACK_RELEASE_ID="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["id"])' "$TMP/rollback-release-created.json")"
curl -fsS --max-time 60 -X POST -H "Authorization: Bearer $AUTHOR_ACCESS" \
  "$BASE/api/v1/releases/$ROLLBACK_RELEASE_ID/validate" >/dev/null
curl -fsS --max-time 60 -X POST -H "Authorization: Bearer $AUTHOR_ACCESS" \
  "$BASE/api/v1/releases/$ROLLBACK_RELEASE_ID/compile" >/dev/null
curl -fsS --max-time 60 -X POST -H "Authorization: Bearer $AUTHOR_ACCESS" \
  "$BASE/api/v1/releases/$ROLLBACK_RELEASE_ID/sign" >/dev/null
curl -fsS --max-time 20 -X POST -H "Authorization: Bearer $APPROVER_ACCESS" \
  "$BASE/api/v1/releases/$ROLLBACK_RELEASE_ID/publish" >/dev/null

# The shared integration database may retain an active release from an earlier run.
# Release rollback only accepts the active release (or a published release when none is
# active), so make this run's rollback target active before exercising the endpoint.
curl -fsS --max-time 20 -H "Authorization: Bearer $APPROVER_ACCESS" \
  "$BASE/api/v1/releases?limit=200&status=active" >"$TMP/active-releases.json"
ACTIVE_RELEASE_ID="$(python3 -c 'import json,sys; items=json.load(open(sys.argv[1])).get("items", []); print(items[0]["id"] if items else "")' "$TMP/active-releases.json")"
if [ -n "$ACTIVE_RELEASE_ID" ] && [ "$ACTIVE_RELEASE_ID" != "$ROLLBACK_RELEASE_ID" ]; then
  curl -fsS --max-time 30 -X POST -H "Authorization: Bearer $APPROVER_ACCESS" \
    -H 'Content-Type: application/json' \
    --data "$(printf '{"reason":"normalize shared test active release","targetReleaseId":%s}' "$ROLLBACK_RELEASE_ID")" \
    "$BASE/api/v1/releases/$ACTIVE_RELEASE_ID/rollback" >/dev/null
fi

SHORT_ROLLBACK_REASON_STATUS="$(curl -sS --max-time 20 -o /dev/null -w '%{http_code}' -X POST \
  -H "Authorization: Bearer $APPROVER_ACCESS" -H 'Content-Type: application/json' \
  --data '{"reason":"short"}' "$BASE/api/v1/releases/$ROLLBACK_RELEASE_ID/rollback")"
if [ "$SHORT_ROLLBACK_REASON_STATUS" != "400" ]; then
  echo "Expected short rollback reason to return 400, got $SHORT_ROLLBACK_REASON_STATUS." >&2
  exit 1
fi

AUTHOR_ROLLBACK_STATUS="$(curl -sS --max-time 20 -o /dev/null -w '%{http_code}' -X POST \
  -H "Authorization: Bearer $AUTHOR_ACCESS" -H 'Content-Type: application/json' \
  --data '{"reason":"author cannot rollback releases"}' \
  "$BASE/api/v1/releases/$ROLLBACK_RELEASE_ID/rollback")"
if [ "$AUTHOR_ROLLBACK_STATUS" != "403" ]; then
  echo "Expected content author rollback to return 403, got $AUTHOR_ROLLBACK_STATUS." >&2
  exit 1
fi

ROLLBACK_REASON="integration rollback ${SUFFIX}"
ROLLBACK_EXPLICIT_STATUS="$(curl -sS --max-time 30 -o "$TMP/rollback-explicit.json" -w '%{http_code}' -X POST \
  -H "Authorization: Bearer $APPROVER_ACCESS" \
  -H 'Content-Type: application/json' \
  --data "$(printf '{"reason":"%s","targetReleaseId":%s}' "$ROLLBACK_REASON" "$RELEASE_ID")" \
  "$BASE/api/v1/releases/$ROLLBACK_RELEASE_ID/rollback")"
if [ "$ROLLBACK_EXPLICIT_STATUS" != "200" ]; then
  echo "Explicit rollback failed with HTTP $ROLLBACK_EXPLICIT_STATUS:" >&2
  cat "$TMP/rollback-explicit.json" >&2
  curl -sS --max-time 20 -H "Authorization: Bearer $APPROVER_ACCESS" \
    "$BASE/api/v1/releases/$RELEASE_ID" >&2 || true
  exit 1
fi

curl -fsS --max-time 30 -X POST -H "Authorization: Bearer $APPROVER_ACCESS" \
  -H 'Content-Type: application/json' \
  --data "$(printf '{"reason":"%s default target"}' "$ROLLBACK_REASON")" \
  "$BASE/api/v1/releases/$RELEASE_ID/rollback" >"$TMP/rollback-default.json"

REPEAT_ROLLBACK_STATUS="$(curl -sS --max-time 20 -o /dev/null -w '%{http_code}' -X POST \
  -H "Authorization: Bearer $APPROVER_ACCESS" -H 'Content-Type: application/json' \
  --data '{"reason":"repeat rollback should conflict"}' \
  "$BASE/api/v1/releases/$ROLLBACK_RELEASE_ID/rollback")"
if [ "$REPEAT_ROLLBACK_STATUS" != "409" ]; then
  echo "Expected repeated rollback to return 409, got $REPEAT_ROLLBACK_STATUS." >&2
  exit 1
fi

curl -fsS --max-time 20 -H "Authorization: Bearer $APPROVER_ACCESS" \
  "$BASE/api/v1/releases?limit=200" >"$TMP/release-final-list.json"

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
assert validated["entityCount"] == 10160, validated

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

draft_created = load("draft-created.json")
assert draft_created["status"] == "editing", draft_created
assert draft_created["baseImportRunId"] is not None, draft_created
assert draft_created["entityCount"] == 10160, draft_created
assert {entry["entityType"]: entry["count"] for entry in draft_created["counts"]} == expected_counts, draft_created["counts"]
assert any(draft["id"] == draft_created["id"] for draft in load("drafts-list.json")["items"]), load("drafts-list.json")

draft_items = load("draft-items.json")
assert draft_items["entityType"] == "item", draft_items
assert draft_items["total"] == 1628, draft_items
assert len(draft_items["items"]) == 5, draft_items
draft_item = load("draft-item.json")
assert draft_item["contentKey"] == "item.1", draft_item
assert draft_item["data"]["Name"] == "SpiritBlade", draft_item

draft_item_updated = load("draft-item-updated.json")
assert draft_item_updated["revision"] == 2, draft_item_updated
assert draft_item_updated["data"]["Name"] == "SpiritBladeDraft", draft_item_updated
assert draft_item_updated["data"]["Index"] == 1, draft_item_updated
assert len(draft_item_updated["data"]["RandomStats"]) == 70, draft_item_updated["data"]["RandomStats"]
assert draft_item_updated["data"]["Stats"]["Values"]["MaxDC"] == 20, draft_item_updated["data"]["Stats"]
draft_item_added = load("draft-item-added.json")
assert draft_item_added["revision"] == 1, draft_item_added
assert draft_item_added["contentKey"] == f"item.{draft_item_added['localIndex']}", draft_item_added
assert draft_item_added["data"]["Index"] == draft_item_added["localIndex"], draft_item_added

assert "Name" in load("invalid-item-type-error.json")["errors"], load("invalid-item-type-error.json")
assert "Type" in load("invalid-item-enum-error.json")["errors"], load("invalid-item-enum-error.json")
assert "Weight" in load("invalid-item-range-error.json")["errors"], load("invalid-item-range-error.json")
assert "Bind" in load("invalid-item-flags-error.json")["errors"], load("invalid-item-flags-error.json")
assert "StartItem" in load("invalid-item-boolean-error.json")["errors"], load("invalid-item-boolean-error.json")
assert "Index" in load("invalid-item-index-error.json")["errors"], load("invalid-item-index-error.json")
assert "contentKey" in load("invalid-item-key-error.json")["errors"], load("invalid-item-key-error.json")

draft_monster = load("draft-monster.json")
assert draft_monster["contentKey"] == "monster.1", draft_monster
assert draft_monster["data"]["Name"] == "ArcherGuard", draft_monster["data"]
draft_monster_updated = load("draft-monster-updated.json")
assert draft_monster_updated["revision"] == 2, draft_monster_updated
assert draft_monster_updated["data"]["Name"] == "ArcherGuardDraft", draft_monster_updated
assert draft_monster_updated["data"]["AI"] == 58, draft_monster_updated["data"]
assert draft_monster_updated["data"]["Stats"]["Values"]["HP"] == 8888, draft_monster_updated["data"]
assert draft_monster_updated["data"]["CanTame"] is True, draft_monster_updated["data"]
assert draft_monster_updated["data"]["LegacyEditorNote"] == {"source": "integration"}, draft_monster_updated["data"]
draft_monster_added = load("draft-monster-added.json")
assert draft_monster_added["revision"] == 1, draft_monster_added
assert draft_monster_added["contentKey"] == f"monster.{draft_monster_added['localIndex']}", draft_monster_added
assert draft_monster_added["data"]["Index"] == draft_monster_added["localIndex"], draft_monster_added
assert draft_monster_added["data"]["Name"] == "DraftIncompleteMonster", draft_monster_added

assert "AI" in load("invalid-monster-ai-error.json")["errors"], load("invalid-monster-ai-error.json")
assert "Image" in load("invalid-monster-image-error.json")["errors"], load("invalid-monster-image-error.json")
assert "Level" in load("invalid-monster-level-error.json")["errors"], load("invalid-monster-level-error.json")
assert "CanTame" in load("invalid-monster-boolean-error.json")["errors"], load("invalid-monster-boolean-error.json")
assert "Stats.Values.NotAStat" in load("invalid-monster-stat-key-error.json")["errors"], load("invalid-monster-stat-key-error.json")
assert "Stats.Values.HP" in load("invalid-monster-stat-range-error.json")["errors"], load("invalid-monster-stat-range-error.json")
assert "Index" in load("invalid-monster-index-error.json")["errors"], load("invalid-monster-index-error.json")
assert "contentKey" in load("invalid-monster-key-error.json")["errors"], load("invalid-monster-key-error.json")

draft_map = load("draft-map.json")
assert draft_map["contentKey"] == "map.1", draft_map
assert draft_map["data"]["Title"] == "BichonProvince", draft_map["data"]
assert len(draft_map["data"]["Movements"]) == 72, draft_map["data"]
assert len(draft_map["data"]["SafeZones"]) == 6, draft_map["data"]
assert len(draft_map["data"]["Respawns"]) == 360, draft_map["data"]
draft_map_updated = load("draft-map-updated.json")
assert draft_map_updated["revision"] == 2, draft_map_updated
assert draft_map_updated["data"]["Title"] == "BichonProvinceDraft", draft_map_updated["data"]
assert draft_map_updated["data"]["MiniMap"] == 126, draft_map_updated["data"]
assert draft_map_updated["data"]["Light"] == "Dawn", draft_map_updated["data"]
assert draft_map_updated["data"]["NoTeleport"] is True, draft_map_updated["data"]
assert draft_map_updated["data"]["FireDamage"] == 25, draft_map_updated["data"]
assert draft_map_updated["data"]["WeatherParticles"] == "None", draft_map_updated["data"]
assert draft_map_updated["data"]["LegacyEditorNote"] == {"source": "integration"}, draft_map_updated["data"]
assert len(draft_map_updated["data"]["Movements"]) == 72, draft_map_updated["data"]
assert len(draft_map_updated["data"]["SafeZones"]) == 6, draft_map_updated["data"]
assert len(draft_map_updated["data"]["Respawns"]) == 360, draft_map_updated["data"]
draft_map_added = load("draft-map-added.json")
assert draft_map_added["revision"] == 1, draft_map_added
assert draft_map_added["contentKey"] == f"map.{draft_map_added['localIndex']}", draft_map_added
assert draft_map_added["data"]["Index"] == draft_map_added["localIndex"], draft_map_added
assert draft_map_added["data"]["FileName"] == "0", draft_map_added
draft_incomplete_map_added = load("draft-map-incomplete-added.json")
assert draft_incomplete_map_added["revision"] == 1, draft_incomplete_map_added
assert draft_incomplete_map_added["contentKey"] == f"map.{draft_incomplete_map_added['localIndex']}", draft_incomplete_map_added
assert draft_incomplete_map_added["data"]["FileName"] == "DraftIncompleteMap", draft_incomplete_map_added

assert "MiniMap" in load("invalid-map-range-error.json")["errors"], load("invalid-map-range-error.json")
assert "Light" in load("invalid-map-light-error.json")["errors"], load("invalid-map-light-error.json")
assert "NoTeleport" in load("invalid-map-boolean-error.json")["errors"], load("invalid-map-boolean-error.json")
assert "Index" in load("invalid-map-index-error.json")["errors"], load("invalid-map-index-error.json")
assert "contentKey" in load("invalid-map-key-error.json")["errors"], load("invalid-map-key-error.json")

incomplete_report = load("draft-incomplete-report.json")
assert incomplete_report["valid"] is False, incomplete_report
assert any(issue["code"] == "item-completeness" and issue["contentKey"] == draft_item_added["contentKey"] for issue in incomplete_report["issues"]), incomplete_report
assert any(issue["message"] == "Missing required item field 'Stats.Values'." and issue["contentKey"] == draft_item_added["contentKey"] for issue in incomplete_report["issues"]), incomplete_report
assert any(issue["code"] == "monster-completeness" and issue["contentKey"] == draft_monster_added["contentKey"] for issue in incomplete_report["issues"]), incomplete_report
assert any(issue["message"] == "Missing required monster field 'Stats.Values'." and issue["contentKey"] == draft_monster_added["contentKey"] for issue in incomplete_report["issues"]), incomplete_report
assert any(issue["code"] == "map-completeness" and issue["contentKey"] == draft_incomplete_map_added["contentKey"] for issue in incomplete_report["issues"]), incomplete_report
assert any(issue["message"] == "Missing required map field 'Title'." and issue["contentKey"] == draft_incomplete_map_added["contentKey"] for issue in incomplete_report["issues"]), incomplete_report
assert any(issue["code"] == "map-file-name-duplicate" and issue["contentKey"] == draft_map_added["contentKey"] for issue in incomplete_report["issues"]), incomplete_report
assert any(issue["code"] == "map-file-name-duplicate" and issue["contentKey"] == "map.1" for issue in incomplete_report["issues"]), incomplete_report

invalid_report = load("draft-invalid-report.json")
assert invalid_report["valid"] is False, invalid_report
assert any(issue["code"] == "script-validation-error" for issue in invalid_report["issues"]), invalid_report
valid_report = load("draft-valid-report.json")
assert valid_report["valid"] is True, valid_report
assert valid_report["issues"] == [], valid_report
assert valid_report["entityCount"] == 10160, valid_report

revisions = load("draft-revisions.json")["items"]
assert any(revision["action"] == "update" and revision["contentKey"] == "item.1" and revision["reason"] == "integration draft update" for revision in revisions), revisions
assert any(revision["action"] == "create" and revision["contentKey"] == draft_item_added["contentKey"] for revision in revisions), revisions
assert any(revision["action"] == "delete" and revision["contentKey"] == draft_item_added["contentKey"] for revision in revisions), revisions
assert any(revision["action"] == "update" and revision["contentKey"] == "monster.1" and revision["reason"] == "integration monster draft update" for revision in revisions), revisions
assert any(revision["action"] == "create" and revision["contentKey"] == draft_monster_added["contentKey"] for revision in revisions), revisions
assert any(revision["action"] == "delete" and revision["contentKey"] == draft_monster_added["contentKey"] for revision in revisions), revisions
assert any(revision["action"] == "update" and revision["contentKey"] == "map.1" and revision["reason"] == "integration map draft update" for revision in revisions), revisions
assert any(revision["action"] == "create" and revision["contentKey"] == draft_map_added["contentKey"] for revision in revisions), revisions
assert any(revision["action"] == "delete" and revision["contentKey"] == draft_map_added["contentKey"] for revision in revisions), revisions
assert any(revision["action"] == "create" and revision["contentKey"] == draft_incomplete_map_added["contentKey"] for revision in revisions), revisions
assert any(revision["action"] == "delete" and revision["contentKey"] == draft_incomplete_map_added["contentKey"] for revision in revisions), revisions
assert any(revision["beforeData"] is not None and revision["afterData"] is not None for revision in revisions), revisions

draft_release_created = load("draft-release-created.json")
assert draft_release_created["status"] == "draft", draft_release_created
assert draft_release_created["draftId"] == draft_created["id"], draft_release_created
assert draft_release_created["importRunId"] == draft_created["baseImportRunId"], draft_release_created
assert {entry["entityType"]: entry["count"] for entry in draft_release_created["counts"]} == expected_counts, draft_release_created["counts"]
draft_release_validated = load("draft-release-validated.json")
assert draft_release_validated["valid"] is True, draft_release_validated
draft_release_published = load("draft-release-published.json")
assert draft_release_published["status"] == "published", draft_release_published

after_release = load("draft-item-after-release.json")
assert after_release["revision"] == 3, after_release
assert after_release["data"]["Name"] == "SpiritBladeAfterRelease", after_release
release_validated_after_change = load("draft-release-validated-after-change.json")
assert release_validated_after_change["valid"] is True, release_validated_after_change
assert release_validated_after_change["contentHash"] == draft_release_created["contentHash"], release_validated_after_change

rollback_release_created = load("rollback-release-created.json")
explicit_rollback = load("rollback-explicit.json")
assert explicit_rollback["rolledBack"]["id"] == rollback_release_created["id"], explicit_rollback
assert explicit_rollback["active"]["id"] == created["id"], explicit_rollback
assert explicit_rollback["rolledBack"]["status"] == "rolled_back", explicit_rollback
assert explicit_rollback["rolledBack"]["rollbackTargetId"] == created["id"], explicit_rollback
assert explicit_rollback["reason"].startswith("integration rollback"), explicit_rollback

default_rollback = load("rollback-default.json")
assert default_rollback["rolledBack"]["id"] == created["id"], default_rollback
assert default_rollback["active"]["id"] == draft_release_created["id"], default_rollback
assert default_rollback["active"]["status"] == "active", default_rollback

final_releases = {release["id"]: release for release in load("release-final-list.json")["items"]}
assert final_releases[rollback_release_created["id"]]["status"] == "rolled_back", final_releases
assert final_releases[created["id"]]["status"] == "rolled_back", final_releases
assert final_releases[draft_release_created["id"]]["status"] == "active", final_releases

assert any(e["action"] == "draft.create" and e["result"] == "success" for e in events), events
assert any(e["action"] == "draft.update" and e["result"] == "success" for e in events), events
assert any(e["action"] == "draft.delete" and e["result"] == "success" for e in events), events
assert any(e["action"] == "draft.validate" and e["result"] == "success" for e in events), events
assert any(e["action"] == "draft.validate" and e["result"] == "failure" for e in events), events
assert any(e["action"] == "release.rollback" and e["result"] == "success" and e["metadata"].get("targetReleaseId") == created["id"] for e in events), events
PY

echo "Control API integration verified (run $RUN_ID, 15 entity types, item/monster/map editors, drafts/revisions, auth/RBAC/audit/relations/release lifecycle, explicit/default rollback)."
