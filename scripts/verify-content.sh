#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET="${DOTNET:-$HOME/.dotnet/dotnet}"
CONFIGURATION="${CONFIGURATION:-Release}"

if [ ! -x "$DOTNET" ]; then
  echo "dotnet not found at $DOTNET. Set DOTNET=/path/to/dotnet." >&2
  exit 1
fi

TMP="$(mktemp -d)"
cleanup() {
  rm -rf "$TMP"
}
trap cleanup EXIT

cd "$ROOT"
"$DOTNET" run --project tools/Crystal.LegacyImporter/Crystal.LegacyImporter.csproj \
  -c "$CONFIGURATION" -- \
  --db tests/fixtures/golden/Server.MirDB \
  --out "$TMP/snapshot" \
  --release "ci.$(date -u +%Y%m%d%H%M%S)" >/dev/null

"$DOTNET" run --project tools/Crystal.LegacyImporter/Crystal.LegacyImporter.csproj \
  -c "$CONFIGURATION" -- \
  --db tests/fixtures/golden/Server.MirDB \
  --export "$TMP/export/Server.MirDB" >"$TMP/export.json"

"$DOTNET" run --project tools/Crystal.LegacyImporter/Crystal.LegacyImporter.csproj \
  -c "$CONFIGURATION" -- \
  --snapshot "$TMP/snapshot" \
  --export "$TMP/rebuilt/Server.MirDB" >"$TMP/rebuilt.json"

"$DOTNET" run --project tools/Crystal.LegacyImporter/Crystal.LegacyImporter.csproj \
  -c "$CONFIGURATION" -- \
  --db "$TMP/export/Server.MirDB" \
  --out "$TMP/export-snapshot" \
  --release "ci.$(date -u +%Y%m%d%H%M%S)" >/dev/null

"$DOTNET" run --project tools/Crystal.LegacyImporter/Crystal.LegacyImporter.csproj \
  -c "$CONFIGURATION" -- \
  --db "$TMP/rebuilt/Server.MirDB" \
  --out "$TMP/rebuilt-snapshot" \
  --release "ci.$(date -u +%Y%m%d%H%M%S)" >/dev/null

"$DOTNET" run --project tools/Crystal.LegacyImporter/Crystal.LegacyImporter.csproj \
  -c "$CONFIGURATION" -- \
  --db tests/fixtures/golden/Server.MirDB \
  --out "$TMP/drop-snapshot" \
  --content-root tests/fixtures/content \
  --release "ci.$(date -u +%Y%m%d%H%M%S)" >/dev/null

openssl genpkey -algorithm EC -pkeyopt ec_paramgen_curve:P-256 \
  -out "$TMP/content-private.pem" >/dev/null 2>&1
openssl pkey -in "$TMP/content-private.pem" -pubout \
  -out "$TMP/content-public.pem" >/dev/null 2>&1
"$DOTNET" run --project tools/Crystal.ContentTool/Crystal.ContentTool.csproj \
  -c "$CONFIGURATION" -- \
  sign --snapshot "$TMP/drop-snapshot" \
  --key "$TMP/content-private.pem" \
  --key-id "ci-test-key" \
  --signed-at "2026-11-11T00:00:00Z" >/dev/null
"$DOTNET" run --project tools/Crystal.ContentTool/Crystal.ContentTool.csproj \
  -c "$CONFIGURATION" -- \
  verify --snapshot "$TMP/drop-snapshot" \
  --public-key "$TMP/content-public.pem" \
  --key-id "ci-test-key" >"$TMP/signature-verify.json"

"$DOTNET" run --project tools/Crystal.LegacyImporter/Crystal.LegacyImporter.csproj \
  -c "$CONFIGURATION" -- \
  --snapshot "$TMP/drop-snapshot" \
  --export "$TMP/drop-rebuilt/Server.MirDB" >/dev/null

diff -r "$TMP/snapshot/entities" "$TMP/export-snapshot/entities" >/dev/null
diff -r "$TMP/snapshot/entities" "$TMP/rebuilt-snapshot/entities" >/dev/null
cmp "$TMP/export/Server.MirDB" "$TMP/rebuilt/Server.MirDB" >/dev/null
cmp "$TMP/export/Server.MirDB" "$TMP/drop-rebuilt/Server.MirDB" >/dev/null

cp -a "$TMP/drop-snapshot" "$TMP/tampered-snapshot"
python3 - "$TMP/tampered-snapshot/manifest.json" <<'PY'
import json
import sys

path = sys.argv[1]
manifest = json.load(open(path, encoding="utf-8"))
manifest["releaseVersion"] = "tampered"
with open(path, "w", encoding="utf-8") as handle:
    json.dump(manifest, handle, ensure_ascii=False, indent=2)
    handle.write("\n")
PY
if "$DOTNET" run --project tools/Crystal.ContentTool/Crystal.ContentTool.csproj \
  -c "$CONFIGURATION" -- \
  verify --snapshot "$TMP/tampered-snapshot" \
  --public-key "$TMP/content-public.pem" >/dev/null 2>&1; then
  echo "Expected tampered content signature verification to fail." >&2
  exit 1
fi

python3 - "$TMP/snapshot" "$TMP/drop-snapshot" <<'PY'
import base64
import json
import sys

base_root = sys.argv[1]
content_root = sys.argv[2]
base_manifest = json.load(open(f"{base_root}/manifest.json", encoding="utf-8"))
assert base_manifest["counts"]["buff"] == 59, base_manifest["counts"]
assert "recipe" not in base_manifest["counts"], base_manifest["counts"]
assert "script" not in base_manifest["counts"], base_manifest["counts"]
assert "asset" not in base_manifest["counts"], base_manifest["counts"]

manifest = json.load(open(f"{content_root}/manifest.json", encoding="utf-8"))
assert manifest["counts"]["drop"] == 1, manifest["counts"]
assert manifest["counts"]["recipe"] == 1, manifest["counts"]
assert manifest["counts"]["script"] == 1, manifest["counts"]
assert manifest["counts"]["asset"] == 1, manifest["counts"]
assert manifest["counts"]["buff"] == 59, manifest["counts"]
assert manifest["signature"]["algorithm"] == "ecdsa-p256-sha256", manifest["signature"]
assert manifest["signature"]["keyId"] == "ci-test-key", manifest["signature"]
assert len(base64.b64decode(manifest["signature"]["value"])) == 64, manifest["signature"]
drops = [json.loads(line) for line in open(f"{content_root}/entities/drop.jsonl", encoding="utf-8")]
assert len(drops) == 1, drops
drop = drops[0]
assert drop["contentKey"] == "drop.monster.1", drop
assert drop["data"]["monsterName"] == "ArcherGuard", drop
assert drop["data"]["drops"][0]["Item"]["index"] == 710, drop

recipes = [json.loads(line) for line in open(f"{content_root}/entities/recipe.jsonl", encoding="utf-8")]
assert len(recipes) == 1, recipes
recipe = recipes[0]
assert recipe["contentKey"] == "recipe.GreenPoison", recipe
assert recipe["data"]["output"]["itemIndex"] == 710, recipe
assert recipe["data"]["output"]["count"] == 2, recipe
assert recipe["data"]["ingredients"][0]["count"] == 3, recipe
assert recipe["data"]["chance"] == 75, recipe
assert recipe["data"]["gold"] == 123, recipe

scripts = [json.loads(line) for line in open(f"{content_root}/entities/script.jsonl", encoding="utf-8")]
assert len(scripts) == 1, scripts
script = scripts[0]
assert script["contentKey"] == "script.BichonProvince.BorderVillage.Transport", script
assert script["data"]["fileName"] == "BichonProvince/BorderVillage/Transport", script
assert script["data"]["npcIndexes"] == [1], script
assert script["data"]["lineCount"] == 95, script
assert "@tele" in script["data"]["pages"], script
assert "[@MAIN]" in script["data"]["source"], script
assert len(script["data"]["ast"]["pages"]) == 11, script
assert script["data"]["ast"]["pages"][0]["key"] == "@MAIN", script
assert script["data"]["validation"]["valid"] is True, script
assert script["data"]["validation"]["errorCount"] == 0, script
assert script["data"]["validation"]["warningCount"] == 1, script
assert script["data"]["validation"]["compiledSha256"] == "c0e3f43676a6d5bd978d57c2586636efa404e17fd84014102a3d7d377ecdf8ff", script

assets = [json.loads(line) for line in open(f"{content_root}/entities/asset.jsonl", encoding="utf-8")]
assert len(assets) == 1, assets
asset = assets[0]
assert asset["contentKey"] == "asset.maps.0.map", asset
assert asset["data"]["assetType"] == "map", asset
assert asset["data"]["relativePath"] == "Maps/0.map", asset
assert asset["data"]["sizeBytes"] == 1252, asset
assert len(asset["data"]["sha256"]) == 64, asset
assert asset["data"]["map"]["valid"] is True, asset
assert asset["data"]["map"]["width"] == 10, asset
assert asset["data"]["map"]["height"] == 10, asset
assert asset["data"]["map"]["blockedCells"] == 1, asset
assert asset["data"]["map"]["doorCells"] == 1, asset

buffs = [json.loads(line) for line in open(f"{content_root}/entities/buff.jsonl", encoding="utf-8")]
assert len(buffs) == 59, len(buffs)
assert buffs[0]["contentKey"] == "buff.TemporalFlux", buffs[0]
PY

python3 - "$TMP/signature-verify.json" <<'PY'
import json
import sys

result = json.load(open(sys.argv[1], encoding="utf-8"))
assert result["valid"] is True, result
assert result["signature"]["keyId"] == "ci-test-key", result
PY

echo "Golden content import, drop/recipe/script/asset import, content signature, snapshot rebuild and binary export round-trip verified."
