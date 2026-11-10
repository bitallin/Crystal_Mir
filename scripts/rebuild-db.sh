#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET="${DOTNET:-$HOME/.dotnet/dotnet}"
CONFIGURATION="${CONFIGURATION:-Release}"

if [ ! -x "$DOTNET" ]; then
  echo "dotnet not found at $DOTNET. Set DOTNET=/path/to/dotnet." >&2
  exit 1
fi

usage() {
  cat >&2 <<'USAGE'
Usage:
  ./scripts/rebuild-db.sh <snapshot-dir> <output-MirDB> [extra args]
  ./scripts/rebuild-db.sh --postgres <connection-string> <output-MirDB> [extra args]
USAGE
}

if [ "$#" -lt 2 ]; then
  usage
  exit 2
fi

if [ "$1" = "--postgres" ]; then
  if [ "$#" -lt 3 ]; then
    usage
    exit 2
  fi
  DSN="$2"
  OUT="$3"
  shift 3
  ARGS=(--from-postgres "$DSN" --export "$OUT")
else
  SNAPSHOT="$1"
  OUT="$2"
  shift 2
  ARGS=(--snapshot "$SNAPSHOT" --export "$OUT")
fi

cd "$ROOT"
"$DOTNET" run --project tools/Crystal.LegacyImporter/Crystal.LegacyImporter.csproj \
  -c "$CONFIGURATION" -- "${ARGS[@]}" "$@"
