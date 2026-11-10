#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
port="${PORT:-4173}"

printf 'MIR Control demo:    http://127.0.0.1:%s/#/dashboard\n' "$port"
printf 'MIR Control with API: http://127.0.0.1:%s/?api=http://127.0.0.1:5080#/login\n' "$port"
exec python3 -m http.server "$port" --bind 127.0.0.1 --directory "$root/src/Crystal.Control.Web"
