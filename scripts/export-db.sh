#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET="${DOTNET:-$HOME/.dotnet/dotnet}"
CONFIGURATION="${CONFIGURATION:-Release}"

DB="${1:-tests/fixtures/golden/Server.MirDB}"
OUT="${2:-artifacts/legacy-export/Server.MirDB}"
shift $(( $# < 2 ? $# : 2 ))

if [ ! -x "$DOTNET" ]; then
  echo "dotnet not found at $DOTNET. Set DOTNET=/path/to/dotnet." >&2
  exit 1
fi

cd "$ROOT"
"$DOTNET" run --project tools/Crystal.LegacyImporter/Crystal.LegacyImporter.csproj \
  -c "$CONFIGURATION" -- \
  --db "$DB" --export "$OUT" "$@"
