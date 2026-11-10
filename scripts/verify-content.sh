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

diff -r "$TMP/snapshot/entities" "$TMP/export-snapshot/entities" >/dev/null
diff -r "$TMP/snapshot/entities" "$TMP/rebuilt-snapshot/entities" >/dev/null
cmp "$TMP/export/Server.MirDB" "$TMP/rebuilt/Server.MirDB" >/dev/null

echo "Golden content import, snapshot rebuild and binary export round-trip verified."
