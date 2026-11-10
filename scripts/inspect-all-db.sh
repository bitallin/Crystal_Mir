#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET="${DOTNET:-$HOME/.dotnet/dotnet}"
CONFIGURATION="${CONFIGURATION:-Release}"
DATABASE_ROOT="${1:-$ROOT/.external/Crystal.Database}"

if [ ! -d "$DATABASE_ROOT" ]; then
  echo "Database root not found: $DATABASE_ROOT" >&2
  echo "Run scripts/fetch-external.sh first." >&2
  exit 1
fi

cd "$ROOT"
failed=0
while IFS= read -r db; do
  echo "=== $db ==="
  if ! "$DOTNET" run --project tools/Crystal.DbInspector/Crystal.DbInspector.csproj -c "$CONFIGURATION" -- "$db"; then
    failed=$((failed + 1))
  fi
done < <(find "$DATABASE_ROOT" -name 'Server.MirDB' -type f | sort)

if [ "$failed" -ne 0 ]; then
  echo "$failed database(s) failed inspection." >&2
  exit 1
fi
