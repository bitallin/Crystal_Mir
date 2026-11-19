#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET="${DOTNET:-$HOME/.dotnet/dotnet}"
CONTENT_ROOT="${CRYSTAL_ZONE_CONTENT:-$ROOT/.external/Crystal.Database/Jev}"

if [ ! -f "$CONTENT_ROOT/Server.MirDB" ]; then
  echo "Zone content not found: $CONTENT_ROOT/Server.MirDB" >&2
  echo "Set CRYSTAL_ZONE_CONTENT to a prepared content directory." >&2
  exit 1
fi

if [ ! -f "$ROOT/web-client/dist/client-build.json" ]; then
  "$ROOT/scripts/build-web-client.sh"
fi

HASH="$(node -e 'const fs=require("fs"); console.log(JSON.parse(fs.readFileSync(process.argv[1], "utf8")).clientBuildHash)' "$ROOT/web-client/dist/client-build.json")"
TMP="$(mktemp -d /tmp/crystal-version-gate-XXXXXX)"
ZONE_PORT="$(python3 -c 'import socket; s=socket.socket(); s.bind(("127.0.0.1", 0)); print(s.getsockname()[1]); s.close()')"
STATUS_PORT="$(python3 -c 'import socket; s=socket.socket(); s.bind(("127.0.0.1", 0)); print(s.getsockname()[1]); s.close()')"
GATEWAY_PORT="$(python3 -c 'import socket; s=socket.socket(); s.bind(("127.0.0.1", 0)); print(s.getsockname()[1]); s.close()')"
ZONE_LOG="$TMP/zone.log"
GATEWAY_LOG="$TMP/gateway.log"

cleanup() {
  if [ -n "${ZONE_PID:-}" ]; then kill -TERM "$ZONE_PID" 2>/dev/null || true; fi
  if [ -n "${GATEWAY_PID:-}" ]; then kill -TERM "$GATEWAY_PID" 2>/dev/null || true; fi
  if [ -n "${ZONE_PID:-}" ]; then wait "$ZONE_PID" 2>/dev/null || true; fi
  if [ -n "${GATEWAY_PID:-}" ]; then wait "$GATEWAY_PID" 2>/dev/null || true; fi
  find "$TMP" -depth -delete 2>/dev/null || true
}
trap cleanup EXIT

mkdir -p "$TMP/content"
cp -a --reflink=auto "$CONTENT_ROOT/Configs" "$TMP/content/Configs"
ln -s "$CONTENT_ROOT/Envir" "$TMP/content/Envir"
ln -s "$CONTENT_ROOT/Maps" "$TMP/content/Maps"
cp -a --reflink=auto "$CONTENT_ROOT/Server.MirDB" "$TMP/content/Server.MirDB"
sed -i -E 's/^CheckVersion=.*/CheckVersion=True/' "$TMP/content/Configs/Setup.ini"
grep -q '^CheckVersion=True' "$TMP/content/Configs/Setup.ini"

"$DOTNET" build "$ROOT/src/Crystal.ZoneServer/Crystal.ZoneServer.csproj" -c Release --nologo >/dev/null
"$DOTNET" build "$ROOT/src/Crystal.Web.Gateway/Crystal.Web.Gateway.csproj" -c Release --nologo >/dev/null

CRYSTAL_ZONE_ID=version-gate \
CRYSTAL_ZONE_IP=127.0.0.1 \
CRYSTAL_ZONE_PORT="$ZONE_PORT" \
CRYSTAL_ZONE_STATUS_PORT="$STATUS_PORT" \
CRYSTAL_ZONE_VERSION_HASH_VALUES="$HASH" \
CRYSTAL_ZONE_READY_FILE="$TMP/ready.json" \
  "$DOTNET" "$ROOT/src/Crystal.ZoneServer/bin/Release/Crystal.ZoneServer.dll" \
    --content-root "$TMP/content" --startup-timeout 180 >"$ZONE_LOG" 2>&1 &
ZONE_PID=$!

for _ in $(seq 1 900); do
  if grep -q "Zone ready:" "$ZONE_LOG"; then break; fi
  if ! kill -0 "$ZONE_PID" 2>/dev/null; then
    tail -n 80 "$ZONE_LOG" >&2
    exit 1
  fi
  sleep 0.2
done
grep -q "Zone ready:" "$ZONE_LOG" || { tail -n 80 "$ZONE_LOG" >&2; exit 1; }

ASPNETCORE_URLS="http://127.0.0.1:$GATEWAY_PORT" \
ASPNETCORE_ENVIRONMENT=Development \
CRYSTAL_WEB_ZONE_ID=default \
CRYSTAL_WEB_ZONE_TCP="127.0.0.1:$ZONE_PORT" \
CRYSTAL_WEB_ALLOWED_ORIGINS="http://127.0.0.1:$GATEWAY_PORT" \
CRYSTAL_WEB_CLIENT_BUILD_HASH="$HASH" \
CRYSTAL_WEB_ASSET_VERSION=version-gate \
  "$DOTNET" "$ROOT/src/Crystal.Web.Gateway/bin/Release/net8.0/Crystal.Web.Gateway.dll" >"$GATEWAY_LOG" 2>&1 &
GATEWAY_PID=$!

for _ in $(seq 1 200); do
  if curl -fsS "http://127.0.0.1:$GATEWAY_PORT/health/ready" >/dev/null 2>&1; then break; fi
  if ! kill -0 "$GATEWAY_PID" 2>/dev/null; then
    cat "$GATEWAY_LOG" >&2
    exit 1
  fi
  sleep 0.1
done

python3 - "$GATEWAY_PORT" "$HASH" <<'PY'
import asyncio
import struct
import sys
import websockets

port = int(sys.argv[1])
good_hash = sys.argv[2].encode()
bad_hash = b"sha256:" + b"0" * 64
origin = f"http://127.0.0.1:{port}"


def frame(packet_id, body=b""):
    return struct.pack("<Hh", len(body) + 4, packet_id) + body


def version_frame(value):
    return frame(0, struct.pack("<i", len(value)) + value)


async def read_version_result(websocket):
    buffer = bytearray()
    while True:
        message = await asyncio.wait_for(websocket.recv(), timeout=10)
        if isinstance(message, str):
            raise AssertionError("gateway returned a text frame")
        buffer.extend(message)
        while len(buffer) >= 4:
            length, packet_id = struct.unpack_from("<Hh", buffer)
            if length < 4:
                raise AssertionError(f"invalid frame length {length}")
            if len(buffer) < length:
                break
            body = bytes(buffer[4:length])
            del buffer[:length]
            if packet_id == 1:
                if len(body) != 1:
                    raise AssertionError(f"unexpected ClientVersion body length {len(body)}")
                return body[0]


async def check(value, expected):
    async with websockets.connect(
        f"ws://127.0.0.1:{port}/ws/game?zone=default",
        subprotocols=["crystal-v1"],
        origin=origin,
        open_timeout=10,
    ) as websocket:
        assert websocket.subprotocol == "crystal-v1"
        await websocket.send(version_frame(value))
        result = await read_version_result(websocket)
        assert result == expected, f"expected ClientVersion result {expected}, got {result}"


async def main():
    await check(good_hash, 1)
    print("accepted current clientBuildHash through real ZoneServer + Gateway")
    # Legacy ZoneServer blocks the same IP for 5 seconds after each connection.
    await asyncio.sleep(6)
    await check(bad_hash, 0)
    print("rejected mismatched clientBuildHash through real ZoneServer + Gateway")


asyncio.run(main())
PY

echo "Web version gate integration passed."
