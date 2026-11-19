#!/usr/bin/env python3
import asyncio
import json
import mimetypes
import socket
import struct
import sys
import threading
from pathlib import Path

from aiohttp import WSMsgType, web
from playwright.sync_api import Browser, Page, sync_playwright

ROOT = Path(sys.argv[1]).resolve()
ASSETS = Path(sys.argv[2]).resolve()
DIST = ROOT / "web-client" / "dist"
BUILD = json.loads((DIST / "client-build.json").read_text(encoding="utf-8"))
BUILD_HASH = BUILD["clientBuildHash"]
FIXTURES = {
    case["name"]: bytes.fromhex(case["hex"])
    for case in json.loads((ROOT / "tests" / "fixtures" / "protocol" / "cases.json").read_text(encoding="utf-8"))["cases"]
}


def pack_string(value: str) -> bytes:
    encoded = value.encode("utf-8")
    length = len(encoded)
    prefix = bytearray()
    while length >= 0x80:
        prefix.append((length & 0x7F) | 0x80)
        length >>= 7
    prefix.append(length)
    return bytes(prefix) + encoded


def frame(packet_id: int, body: bytes = b"") -> bytes:
    return struct.pack("<Hh", len(body) + 4, packet_id) + body


def login_success() -> bytes:
    character = struct.pack("<i", 0) + pack_string("测试勇士") + struct.pack("<HBBq", 35, 0, 0, 0)
    return frame(9, struct.pack("<i", 1) + character)


def map_information() -> bytes:
    body = struct.pack("<i", 0)
    body += pack_string("0")
    body += pack_string("测试地图")
    body += struct.pack("<HHBBBHH", 0, 0, 4, 0, 0, 0, 0)
    return frame(17, body)


def user_information() -> bytes:
    return FIXTURES["server.userInformation"]


def split_packets(data: bytes):
    offset = 0
    while offset + 4 <= len(data):
        length, packet_id = struct.unpack_from("<Hh", data, offset)
        if length < 4 or offset + length > len(data):
            raise ValueError(f"invalid packet frame length {length}")
        yield packet_id, data[offset + 4:offset + length]
        offset += length


async def websocket_handler(request: web.Request) -> web.WebSocketResponse:
    socket = web.WebSocketResponse(protocols=("crystal-v1",))
    await socket.prepare(request)
    await socket.send_bytes(frame(0))
    async for message in socket:
        if message.type == WSMsgType.BINARY:
            for packet_id, body in split_packets(message.data):
                if packet_id == 0:
                    await socket.send_bytes(frame(1, b"\x01"))
                elif packet_id == 5:
                    await socket.send_bytes(login_success())
                elif packet_id == 8:
                    await socket.send_bytes(frame(14, struct.pack("<Bi", 4, 0)))
                    await socket.send_bytes(map_information())
                    await socket.send_bytes(user_information())
                elif packet_id == 14:
                    grid, source, target = struct.unpack_from("<Bii", body)
                    await socket.send_bytes(frame(37, struct.pack("<Bii?", grid, source, target, True)))
        elif message.type in (WSMsgType.ERROR, WSMsgType.CLOSE):
            break
    return socket


async def bootstrap_handler(_request: web.Request) -> web.Response:
    return web.json_response({
        "protocol": "crystal-v1",
        "zoneId": "default",
        "wsUrl": "/ws/game?zone=default",
        "assetVersion": "dev",
        "assetManifestUrl": "/assets/dev/manifest.json",
        "clientBuildHash": BUILD_HASH,
    })


async def static_handler(request: web.Request) -> web.StreamResponse:
    path = request.match_info["path"]
    prefix = request.path.split("/")[1]
    root = DIST if prefix == "game" else ASSETS
    candidate = (root / path).resolve()
    if root.resolve() not in candidate.parents and candidate != root.resolve():
        raise web.HTTPNotFound()
    if not candidate.is_file():
        raise web.HTTPNotFound()
    return web.FileResponse(candidate, headers={"Cache-Control": "no-store"})


async def index_handler(_request: web.Request) -> web.FileResponse:
    return web.FileResponse(DIST / "index.html", headers={"Cache-Control": "no-store"})


async def run_server(ready: asyncio.Event, result: dict) -> None:
    app = web.Application()
    app.router.add_get("/api/web/bootstrap", bootstrap_handler)
    app.router.add_get("/ws/game", websocket_handler)
    app.router.add_get("/game/", index_handler)
    app.router.add_get("/game/{path:.*}", static_handler)
    app.router.add_get("/assets/{path:.*}", static_handler)
    app.router.add_get("/favicon.ico", lambda _request: web.Response(status=204))
    runner = web.AppRunner(app)
    await runner.setup()
    site = web.TCPSite(runner, "127.0.0.1", 0)
    await site.start()
    result["port"] = site._server.sockets[0].getsockname()[1]
    ready.set()
    await asyncio.Event().wait()


def run_browser(base_url: str) -> None:
    with sync_playwright() as playwright:
        browser = playwright.chromium.launch(
            headless=True,
            args=["--use-angle=swiftshader", "--enable-unsafe-swiftshader"],
        )
        try:
            desktop = browser.new_context(viewport={"width": 1440, "height": 900})
            enter_game(desktop.new_page(), base_url, interactions=True)
            desktop.close()

            landscape = browser.new_context(
                viewport={"width": 844, "height": 390},
                has_touch=True,
                is_mobile=True,
                device_scale_factor=2,
            )
            landscape_page = enter_game(landscape.new_page(), base_url)
            assert landscape_page.locator(".joystick").is_visible(), "virtual joystick is hidden in landscape"
            assert landscape_page.get_by_role("button", name="攻击").last.is_visible(), "mobile attack button is hidden in landscape"
            landscape.close()

            portrait = browser.new_context(
                viewport={"width": 390, "height": 844},
                has_touch=True,
                is_mobile=True,
                device_scale_factor=2,
            )
            portrait_page = enter_game(portrait.new_page(), base_url)
            assert portrait_page.locator(".rotate-gate").is_visible(), "portrait rotation gate is hidden"
            portrait.close()
        finally:
            browser.close()


def enter_game(page: Page, base_url: str, interactions: bool = False) -> Page:
    errors: list[str] = []
    page.on("pageerror", lambda error: errors.append(str(error)))
    page.on("console", lambda message: errors.append(message.text) if message.type == "error" else None)
    page.goto(base_url + "/game/", wait_until="domcontentloaded")
    try:
        page.get_by_role("heading", name="登录你的账号").wait_for(timeout=15000)
    except Exception:
        print(page.url)
        print(page.locator("body").inner_text())
        print("console errors:", errors)
        raise
    page.locator('input[name="account"]').fill("smoke-user")
    page.locator('input[name="password"]').fill("smoke-password")
    page.locator('form.stack-form button[type="submit"]').click()
    try:
        page.get_by_role("heading", name="谁将踏入石门？").wait_for(timeout=15000)
    except Exception:
        print(page.locator("body").inner_text())
        print("console errors:", errors)
        raise
    page.get_by_role("button", name="进入世界").click()
    page.locator(".world-host").wait_for(timeout=15000)
    try:
        page.locator(".player-status").wait_for(timeout=15000)
    except Exception:
        print(page.locator("body").inner_text())
        print("console errors:", errors)
        raise
    page.wait_for_timeout(500)
    if interactions:
        page.keyboard.press("i")
        page.get_by_role("dialog", name="行囊").wait_for(timeout=5000)
        slots = page.locator(".item-slot")
        assert "has-item" in (slots.nth(0).get_attribute("class") or ""), "inventory fixture did not render an item"
        slots.nth(0).click()
        slots.nth(5).click()
        page.wait_for_function(
            "document.querySelectorAll('.item-slot')[5].classList.contains('has-item')",
            timeout=5000,
        )
        assert "has-item" not in (slots.nth(0).get_attribute("class") or ""), "inventory move did not clear the source slot"
        page.keyboard.press("Escape")
        page.keyboard.press("Enter")
        page.locator(".chat-entry input").fill("你好玛法")
        page.keyboard.press("Enter")
        page.locator(".chat-line", has_text="你好玛法").wait_for(timeout=5000)
    if errors:
        raise AssertionError("browser console errors: " + " | ".join(errors))
    return page


def main() -> None:
    loop = asyncio.new_event_loop()
    ready = asyncio.Event()
    result: dict = {}

    def serve() -> None:
        asyncio.set_event_loop(loop)
        loop.run_until_complete(run_server(ready, result))

    thread = threading.Thread(target=serve, daemon=True)
    thread.start()
    for _ in range(200):
        if ready.is_set():
            break
        threading.Event().wait(0.025)
    if not ready.is_set():
        raise RuntimeError("browser smoke server did not start")

    run_browser(f"http://127.0.0.1:{result['port']}")
    print("Browser client smoke checks passed.")


if __name__ == "__main__":
    main()
