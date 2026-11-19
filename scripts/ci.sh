#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export DOTNET="${DOTNET:-$HOME/.dotnet/dotnet}"

cd "$ROOT"
./scripts/build.sh
./scripts/test.sh
./scripts/build-web-client.sh
./scripts/test-client-updates.sh
./scripts/check-boundaries.sh
./scripts/inspect-db.sh
./scripts/verify-content.sh

if [ -f .external/Crystal.Database/Jev/Server.MirDB ]; then
  ./scripts/zone-smoke.sh
else
  echo "Skipping Zone Server smoke test: .external/Crystal.Database/Jev/Server.MirDB is missing."
fi

for script in src/Crystal.Control.Web/assets/*.js; do
  node --check "$script"
done

if [ -n "${CRYSTAL_DATABASE_URL:-}" ] || [ -n "${CRYSTAL_DB_CONNECTION:-}" ]; then
  ./scripts/test-control-api.sh
  ./scripts/test-zone-agent.sh
else
  echo "Skipping Control API and Zone Agent integration: CRYSTAL_DATABASE_URL/CRYSTAL_DB_CONNECTION is not set."
fi
