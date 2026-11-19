#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET="${DOTNET:-$HOME/.dotnet/dotnet}"

python3 - <<'PY'
import aiohttp
import playwright
PY

"$ROOT/scripts/build-web-client.sh"

HASH="$(node -e 'console.log(require("./web-client/dist/client-build.json").clientBuildHash)' 2>/dev/null || \
  node -e 'const fs=require("node:fs"); console.log(JSON.parse(fs.readFileSync("web-client/dist/client-build.json","utf8")).clientBuildHash)')"

TMP="$(mktemp -d)"
cleanup() {
  rm -rf "$TMP"
}
trap cleanup EXIT

"$DOTNET" run --project "$ROOT/tools/Crystal.WebAssetCompiler/Crystal.WebAssetCompiler.csproj" \
  -c Release -- \
  --client-root "$ROOT" \
  --out "$TMP/assets/dev" \
  --version dev \
  --build-hash "$HASH" \
  --map "$ROOT/tests/fixtures/content/Maps/0.map"

python3 "$ROOT/scripts/test-browser-client.py" "$ROOT" "$TMP/assets"
