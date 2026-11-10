#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET="${DOTNET:-$HOME/.dotnet/dotnet}"
CONFIGURATION="${CONFIGURATION:-Release}"
DB_PATH="${1:-tests/fixtures/golden/Server.MirDB}"

cd "$ROOT"
"$DOTNET" run --project tools/Crystal.DbInspector/Crystal.DbInspector.csproj -c "$CONFIGURATION" -- "$DB_PATH"
