#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if ! command -v node >/dev/null 2>&1 || ! command -v npm >/dev/null 2>&1; then
  echo "Node.js and npm are required to build the browser client." >&2
  exit 2
fi

cd "$ROOT/web-client"
if [ ! -d node_modules ]; then
  npm ci
fi

npm run typecheck
npm test
npm run build

node - <<'NODE'
const fs = require("node:fs");
const value = JSON.parse(fs.readFileSync("dist/client-build.json", "utf8"));
console.log(`clientBuildHash=${value.clientBuildHash}`);
console.log("Pass this value to Crystal.Web.Gateway as CRYSTAL_WEB_CLIENT_BUILD_HASH");
console.log("and to Crystal.WebAssetCompiler as --build-hash.");
NODE
