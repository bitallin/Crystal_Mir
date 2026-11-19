#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET="${DOTNET:-$HOME/.dotnet/dotnet}"
MAP_ROOT="${CRYSTAL_CLIENT_MAP_ROOT:-$ROOT/.external/Crystal.Database/Jev/Maps}"

if [ ! -d "$MAP_ROOT" ]; then
  echo "Real client map root not found: $MAP_ROOT" >&2
  echo "Set CRYSTAL_CLIENT_MAP_ROOT to a directory containing real .map files." >&2
  exit 1
fi

MAP_COUNT="$(python3 - "$MAP_ROOT" <<'PY'
from pathlib import Path
import sys
print(len({path.stem.lower() for path in Path(sys.argv[1]).iterdir() if path.is_file() and path.suffix.lower() == ".map"}))
PY
)"
if [ "$MAP_COUNT" -eq 0 ]; then
  echo "No .map files found in $MAP_ROOT" >&2
  exit 1
fi

if [ "${CRYSTAL_SKIP_WEB_BUILD:-0}" != "1" ]; then
  "$ROOT/scripts/build-web-client.sh"
fi

HASH="$(node -e 'const fs=require("fs"); console.log(JSON.parse(fs.readFileSync(process.argv[1], "utf8")).clientBuildHash)' "$ROOT/web-client/dist/client-build.json")"
TMP="$(mktemp -d /tmp/crystal-real-maps-XXXXXX)"
OUT="${CRYSTAL_WEB_REAL_MAP_OUT:-$ROOT/artifacts/web-assets/jev-maps-$(date -u +%Y%m%d%H%M%S)}"

cleanup() {
  find "$TMP" -depth -delete 2>/dev/null || true
}
trap cleanup EXIT

mkdir -p "$TMP/client/Data" "$TMP/client/Map" "$OUT"
while IFS= read -r -d '' file; do
  ln -s "$file" "$TMP/client/Map/$(basename "$file")"
done < <(find "$MAP_ROOT" -maxdepth 1 -type f -print0)

"$DOTNET" run --project "$ROOT/tools/Crystal.WebAssetCompiler/Crystal.WebAssetCompiler.csproj" \
  -c Release -- \
  --client-root "$TMP/client" \
  --out "$OUT" \
  --version "real-maps-$(date -u +%Y%m%d%H%M%S)" \
  --build-hash "$HASH" \
  --all-maps >"$TMP/compile.out" 2>"$TMP/compile.err"

cat "$TMP/compile.out"
echo "map files: $MAP_COUNT"
echo "warnings: $(grep -c '^WARN ' "$TMP/compile.err" || true)"
echo "output: $OUT"

python3 - "$OUT" "$HASH" "$MAP_COUNT" >"$TMP/maps.txt" <<'PY'
import collections
import hashlib
import json
import os
import sys

out, expected_hash, expected_maps = sys.argv[1], sys.argv[2], int(sys.argv[3])
manifest_path = os.path.join(out, "manifest.json")
manifest = json.load(open(manifest_path, encoding="utf-8"))
maps = manifest["maps"]
if len(maps) != expected_maps:
    raise SystemExit(f"compiled {len(maps)} maps, expected {expected_maps}")
if manifest["clientBuildHash"] != expected_hash:
    raise SystemExit("manifest clientBuildHash does not match web-client/dist/client-build.json")
formats = collections.Counter(entry["format"] for entry in maps.values())
for required in (1, 100):
    if formats[required] == 0:
        raise SystemExit(f"real map set has no format {required} sample")
v1 = next(key for key, entry in sorted(maps.items()) if entry["format"] == 1)
v100 = next(key for key, entry in sorted(maps.items()) if entry["format"] == 100)
print(f"formats: {dict(sorted(formats.items()))}")
print(f"cells: {sum(entry['width'] * entry['height'] for entry in maps.values())}")
print(f"manifest sha256: {hashlib.sha256(open(manifest_path, 'rb').read()).hexdigest()}")
print(v1, v100)
PY
V1_MAP="$(tail -n 1 "$TMP/maps.txt" | awk '{print $1}')"
V100_MAP="$(tail -n 1 "$TMP/maps.txt" | awk '{print $2}')"
head -n -1 "$TMP/maps.txt"

for map in "$V1_MAP" "$V100_MAP"; do
  echo "browser smoke: real map $map"
  CRYSTAL_BROWSER_MAP="$map" \
  CRYSTAL_BROWSER_ASSET_MANIFEST_URL=/assets/manifest.json \
    python3 "$ROOT/scripts/test-browser-client.py" "$ROOT" "$OUT"
done

echo "Real map Web asset and browser checks passed."
