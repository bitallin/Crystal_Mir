#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET="${DOTNET:-$HOME/.dotnet/dotnet}"
CONFIGURATION="${CONFIGURATION:-Release}"

TMP="$(mktemp -d)"
cleanup() {
  cd "$ROOT"
  rm -rf "$TMP"
}
trap cleanup EXIT

cd "$TMP"
"$DOTNET" run --project "$ROOT/Tests/ItemSecurity/ItemSecurity.csproj" -c "$CONFIGURATION"
"$DOTNET" run --project "$ROOT/Tests/ContentSnapshot/ContentSnapshot.csproj" -c "$CONFIGURATION"
"$DOTNET" run --project "$ROOT/Tests/ContractBoundary/ContractBoundary.csproj" -c "$CONFIGURATION"
"$DOTNET" run --project "$ROOT/Tests/ContentEditor/ContentEditor.csproj" -c "$CONFIGURATION"
"$DOTNET" run --project "$ROOT/Tests/ZoneMetrics/ZoneMetrics.csproj" -c "$CONFIGURATION"
"$DOTNET" run --project "$ROOT/Tests/ZoneHealth/ZoneHealth.csproj" -c "$CONFIGURATION"
"$DOTNET" run --project "$ROOT/Tests/ClientUpdates/ClientUpdates.csproj" -c "$CONFIGURATION"
