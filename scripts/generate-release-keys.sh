#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="${1:-$ROOT/artifacts/control-signing}"

if ! command -v openssl >/dev/null 2>&1; then
  echo "openssl is required to generate release signing keys." >&2
  exit 1
fi

mkdir -p "$OUT"
chmod 700 "$OUT"
openssl genpkey -algorithm EC -pkeyopt ec_paramgen_curve:P-256 \
  -out "$OUT/private.pem"
chmod 600 "$OUT/private.pem"
openssl pkey -in "$OUT/private.pem" -pubout -out "$OUT/public.pem"
chmod 644 "$OUT/public.pem"
echo "Release signing keys written to $OUT"
