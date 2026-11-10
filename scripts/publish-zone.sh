#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET="${DOTNET:-$HOME/.dotnet/dotnet}"
CONFIGURATION="${CONFIGURATION:-Release}"
RID="${RID:-linux-x64}"
SELF_CONTAINED="${SELF_CONTAINED:-true}"
OUTPUT="${1:-$ROOT/artifacts/zone}"

if [ "${1:-}" = "-h" ] || [ "${1:-}" = "--help" ]; then
  cat <<'EOF'
Usage: ./scripts/publish-zone.sh [output-directory]

Environment:
  CONFIGURATION    Build configuration (default: Release)
  RID              Target runtime (default: linux-x64)
  SELF_CONTAINED   Include the .NET runtime (default: true)
  DOTNET           Path to the dotnet executable
EOF
  exit 0
fi

if [ ! -x "$DOTNET" ]; then
  echo "dotnet not found at $DOTNET. Set DOTNET=/path/to/dotnet." >&2
  exit 1
fi

mkdir -p "$OUTPUT"

cd "$ROOT"
"$DOTNET" publish src/Crystal.ZoneServer/Crystal.ZoneServer.csproj \
  -c "$CONFIGURATION" \
  -r "$RID" \
  --self-contained "$SELF_CONTAINED" \
  -p:PublishSingleFile=false \
  -o "$OUTPUT" \
  --nologo

echo "Zone server published to $OUTPUT"
echo "Self-contained: $SELF_CONTAINED; RID: $RID"
