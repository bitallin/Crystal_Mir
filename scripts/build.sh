#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET="${DOTNET:-$HOME/.dotnet/dotnet}"
CONFIGURATION="${CONFIGURATION:-Release}"

if [ ! -x "$DOTNET" ]; then
  echo "dotnet not found at $DOTNET. Set DOTNET=/path/to/dotnet." >&2
  exit 1
fi

cd "$ROOT"
echo "== Shared =="
"$DOTNET" build Shared/Shared.csproj -c "$CONFIGURATION" --nologo

echo "== Server.Library =="
"$DOTNET" build Server/Server.Library.csproj -c "$CONFIGURATION" --nologo

echo "== Server host (cross-build) =="
"$DOTNET" build Server.MirForms/Server.csproj -c "$CONFIGURATION" -p:EnableWindowsTargeting=true --nologo

echo "== Client (cross-build) =="
"$DOTNET" build Client/Client.csproj -c "$CONFIGURATION" -p:EnableWindowsTargeting=true --nologo

echo "== Content =="
"$DOTNET" build src/Crystal.Content/Crystal.Content.csproj -c "$CONFIGURATION" --nologo

echo "== Control API =="
"$DOTNET" build src/Crystal.Control.Api/Crystal.Control.Api.csproj -c "$CONFIGURATION" --nologo

echo "== Zone Server =="
"$DOTNET" build src/Crystal.ZoneServer/Crystal.ZoneServer.csproj -c "$CONFIGURATION" --nologo

echo "== Tools =="
"$DOTNET" build tools/Crystal.DbInspector/Crystal.DbInspector.csproj -c "$CONFIGURATION" --nologo
"$DOTNET" build tools/Crystal.FieldInventory/Crystal.FieldInventory.csproj -c "$CONFIGURATION" --nologo
"$DOTNET" build tools/Crystal.LegacyImporter/Crystal.LegacyImporter.csproj -c "$CONFIGURATION" --nologo
"$DOTNET" build tools/Crystal.ContentTool/Crystal.ContentTool.csproj -c "$CONFIGURATION" --nologo

echo "Build completed."
