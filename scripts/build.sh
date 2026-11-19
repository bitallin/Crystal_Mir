#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET="${DOTNET:-$HOME/.dotnet/dotnet}"
CONFIGURATION="${CONFIGURATION:-Release}"

if ! command -v "$DOTNET" >/dev/null 2>&1; then
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

echo "== Legacy Runtime (GPL boundary) =="
"$DOTNET" build src/Crystal.LegacyRuntime/Crystal.LegacyRuntime.csproj -c "$CONFIGURATION" --nologo

echo "== GameCore =="
"$DOTNET" build src/Crystal.GameCore/Crystal.GameCore.csproj -c "$CONFIGURATION" --nologo

echo "== Agent Contracts =="
"$DOTNET" build src/Crystal.Agent.Contracts/Crystal.Agent.Contracts.csproj -c "$CONFIGURATION" --nologo

echo "== Zone Agent =="
"$DOTNET" build src/Crystal.ZoneAgent/Crystal.ZoneAgent.csproj -c "$CONFIGURATION" --nologo

echo "== Patcher =="
"$DOTNET" build src/Crystal.Patcher/Crystal.Patcher.csproj -c "$CONFIGURATION" --nologo

echo "== Patcher Tool =="
"$DOTNET" build tools/Crystal.PatcherTool/Crystal.PatcherTool.csproj -c "$CONFIGURATION" --nologo

echo "== Contract boundary test =="
"$DOTNET" build Tests/ContractBoundary/ContractBoundary.csproj -c "$CONFIGURATION" --nologo

echo "== Content editor test =="
"$DOTNET" build Tests/ContentEditor/ContentEditor.csproj -c "$CONFIGURATION" --nologo

echo "== Zone metrics test =="
"$DOTNET" build Tests/ZoneMetrics/ZoneMetrics.csproj -c "$CONFIGURATION" --nologo

echo "== Zone version gate test =="
"$DOTNET" build Tests/ZoneVersionGate/ZoneVersionGate.csproj -c "$CONFIGURATION" --nologo

echo "== Client updates test =="
"$DOTNET" build Tests/ClientUpdates/ClientUpdates.csproj -c "$CONFIGURATION" --nologo

echo "== Web Assets =="
"$DOTNET" build src/Crystal.WebAssets/Crystal.WebAssets.csproj -c "$CONFIGURATION" --nologo
"$DOTNET" build tools/Crystal.WebAssetCompiler/Crystal.WebAssetCompiler.csproj -c "$CONFIGURATION" --nologo

echo "== Web Gateway =="
"$DOTNET" build src/Crystal.Web.Gateway/Crystal.Web.Gateway.csproj -c "$CONFIGURATION" --nologo

echo "== Protocol Samples =="
"$DOTNET" build tools/Crystal.ProtocolSamples/Crystal.ProtocolSamples.csproj -c "$CONFIGURATION" --nologo

echo "== Web tests =="
"$DOTNET" build Tests/WebAssets/WebAssets.csproj -c "$CONFIGURATION" --nologo
"$DOTNET" build Tests/WebGateway/WebGateway.csproj -c "$CONFIGURATION" --nologo

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
