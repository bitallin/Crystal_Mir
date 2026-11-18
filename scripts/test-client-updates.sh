#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET="${DOTNET:-$HOME/.dotnet/dotnet}"
CONFIGURATION="${CONFIGURATION:-Release}"

if ! command -v openssl >/dev/null 2>&1; then
  echo "openssl is required for the client update CLI test." >&2
  exit 1
fi

TMP="$(mktemp -d)"
cleanup() {
  rm -rf "$TMP"
}
trap cleanup EXIT

run_tool() {
  "$DOTNET" run --project "$ROOT/tools/Crystal.PatcherTool/Crystal.PatcherTool.csproj" \
    -c "$CONFIGURATION" --no-build -- "$@"
}

mkdir -p "$TMP/source/nested" "$TMP/client"
printf 'alpha-v1' > "$TMP/source/a.txt"
printf 'bravo-v1' > "$TMP/source/nested/b.txt"
printf 'alpha-v0' > "$TMP/client/a.txt"
printf 'keep-me' > "$TMP/client/user.ini"

openssl genpkey -algorithm EC -pkeyopt ec_paramgen_curve:P-256 -out "$TMP/private.pem" 2>/dev/null
openssl pkey -in "$TMP/private.pem" -pubout -out "$TMP/public.pem" 2>/dev/null

run_tool pack \
  --root "$TMP/source" \
  --output "$TMP/manifest.json" \
  --channel stable \
  --release 2026.11.18.1 \
  --url-prefix . \
  --min-client-version 1.0.0 \
  --min-launcher-version 1.0.0 \
  --generated-at 2026-11-18T00:00:00Z \
  --expires-at 2026-11-19T00:00:00Z >/dev/null

run_tool sign \
  --manifest "$TMP/manifest.json" \
  --key "$TMP/private.pem" \
  --key-id test-key \
  --signed-at 2026-11-18T00:00:00Z >/dev/null

run_tool verify \
  --manifest "$TMP/manifest.json" \
  --public-key "$TMP/public.pem" \
  --key-id test-key \
  --channel stable \
  --client-version 1.2.0 \
  --launcher-version 1.2.0 \
  --now 2026-11-18T01:00:00Z >/dev/null

run_tool apply \
  --root "$TMP/client" \
  --manifest "$TMP/manifest.json" \
  --public-key "$TMP/public.pem" \
  --key-id test-key \
  --manifest-base "file://$TMP/source/" \
  --client-version 1.2.0 \
  --launcher-version 1.2.0 \
  --now 2026-11-18T01:00:00Z >/dev/null

test "$(cat "$TMP/client/a.txt")" = "alpha-v1"
test "$(cat "$TMP/client/nested/b.txt")" = "bravo-v1"
test "$(cat "$TMP/client/user.ini")" = "keep-me"
test -f "$TMP/client/.crystal-update/state.json"

run_tool state --root "$TMP/client" >/dev/null
run_tool rollback --root "$TMP/client" >/dev/null

test "$(cat "$TMP/client/a.txt")" = "alpha-v0"
test ! -e "$TMP/client/nested/b.txt"
test "$(cat "$TMP/client/user.ini")" = "keep-me"
test ! -e "$TMP/client/.crystal-update/state.json"

echo "Client update CLI verified: pack, sign, verify, apply, state, rollback."
