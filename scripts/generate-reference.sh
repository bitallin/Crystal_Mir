#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET="${DOTNET:-$HOME/.dotnet/dotnet}"
CONFIGURATION="${CONFIGURATION:-Release}"

cd "$ROOT"
"$DOTNET" run --project tools/Crystal.FieldInventory/Crystal.FieldInventory.csproj -c "$CONFIGURATION" -- docs/reference
