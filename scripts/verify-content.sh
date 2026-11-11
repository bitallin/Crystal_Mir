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

"$DOTNET" run --project tools/Crystal.LegacyImporter/Crystal.LegacyImporter.csproj \
  -c "$CONFIGURATION" -- \
  --snapshot "$TMP/drop-snapshot" \
  --export "$TMP/drop-rebuilt/Server.MirDB" >/dev/null

diff -r "$TMP/snapshot/entities" "$TMP/export-snapshot/entities" >/dev/null
diff -r "$TMP/snapshot/entities" "$TMP/rebuilt-snapshot/entities" >/dev/null
cmp "$TMP/export/Server.MirDB" "$TMP/rebuilt/Server.MirDB" >/dev/null
cmp "$TMP/export/Server.MirDB" "$TMP/drop-rebuilt/Server.MirDB" >/dev/null

python3 - "$TMP/drop-snapshot" <<'PY'
import json
import sys

root = sys.argv[1]
manifest = json.load(open(f"{root}/manifest.json", encoding="utf-8"))
assert manifest["counts"]["drop"] == 1, manifest["counts"]
drops = [json.loads(line) for line in open(f"{root}/entities/drop.jsonl", encoding="utf-8")]
assert len(drops) == 1, drops
drop = drops[0]
assert drop["contentKey"] == "drop.monster.1", drop
assert drop["data"]["monsterName"] == "ArcherGuard", drop
assert drop["data"]["drops"][0]["Item"]["index"] == 710, drop
PY

echo "Golden content import, drop import, snapshot rebuild and binary export round-trip verified."
