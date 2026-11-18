#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

fail() {
  echo "Boundary violation: $*" >&2
  exit 1
}

require_file() {
  [ -f "$1" ] || fail "missing $1"
}

require_file src/Crystal.GameCore/Crystal.GameCore.csproj
require_file src/Crystal.Agent.Contracts/Crystal.Agent.Contracts.csproj
require_file src/Crystal.ZoneAgent/Crystal.ZoneAgent.csproj
require_file src/Crystal.Control.Api/Crystal.Control.Api.csproj
require_file src/Crystal.Content/Crystal.Content.csproj
require_file src/Crystal.LegacyRuntime/Crystal.LegacyRuntime.csproj
require_file src/Crystal.Patcher/Crystal.Patcher.csproj

if grep -nE 'ProjectReference[^>]*(Server\.Library|Server\.MirForms|Crystal\.ZoneServer|Crystal\.LegacyRuntime)' \
  src/Crystal.Control.Api/Crystal.Control.Api.csproj src/Crystal.Content/Crystal.Content.csproj \
  src/Crystal.ZoneAgent/Crystal.ZoneAgent.csproj >/dev/null; then
  fail "control-plane or content project references GPL game projects"
fi

if grep -nE 'ProjectReference[^>]*(Server\.Library|Server\.MirForms|Crystal\.GameCore|Crystal\.ZoneServer|Crystal\.Control\.Api)' \
  src/Crystal.Agent.Contracts/Crystal.Agent.Contracts.csproj >/dev/null; then
  fail "Agent contracts reference a service or game implementation"
fi

if grep -RInE 'Microsoft\.EntityFrameworkCore|Microsoft\.AspNetCore|System\.Windows\.Forms|Server\.Mir' \
  src/Crystal.GameCore src/Crystal.Agent.Contracts --include='*.cs' --include='*.csproj' >/dev/null; then
  fail "GameCore or Agent contracts depend on a forbidden framework"
fi

if grep -n 'ProjectReference' src/Crystal.Agent.Contracts/Crystal.Agent.Contracts.csproj >/dev/null; then
  fail "Agent contracts must remain dependency-free"
fi

if grep -n 'ProjectReference' src/Crystal.ZoneAgent/Crystal.ZoneAgent.csproj | \
  grep -vE 'Crystal\.Agent\.Contracts/Crystal\.Agent\.Contracts\.csproj|Crystal\.Content/Crystal\.Content\.csproj|Crystal\.GameCore/Crystal\.GameCore\.csproj' >/dev/null; then
  fail "ZoneAgent may only reference Agent Contracts, Content and GameCore"
fi

if ! grep -q 'ProjectReference Include="../../Shared/Shared.csproj"' src/Crystal.GameCore/Crystal.GameCore.csproj; then
  fail "GameCore must declare its Shared dependency"
fi

if grep -n 'ProjectReference' src/Crystal.GameCore/Crystal.GameCore.csproj | grep -v 'Shared/Shared.csproj' >/dev/null; then
  fail "GameCore may only reference Crystal.Shared in M1"
fi

if grep -n 'ProjectReference' src/Crystal.Patcher/Crystal.Patcher.csproj >/dev/null; then
  fail "Crystal.Patcher must remain dependency-free"
fi

if grep -RInE 'Microsoft\.EntityFrameworkCore|Microsoft\.AspNetCore|System\.Windows\.Forms|Server\.Mir' \
  src/Crystal.Patcher --include='*.cs' --include='*.csproj' >/dev/null; then
  fail "Crystal.Patcher depends on a forbidden framework"
fi

echo "Dependency boundary checks passed."
