#!/usr/bin/env bash
set -euo pipefail
ulimit -c 0 2>/dev/null || true

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET="${DOTNET:-$HOME/.dotnet/dotnet}"
CONFIGURATION="${CONFIGURATION:-Release}"
DSN="${1:-${CRYSTAL_DATABASE_URL:-${CRYSTAL_DB_CONNECTION:-}}}"
ADMIN_PASSWORD="${ADMIN_PASSWORD:-ControlTest!Passw0rd2026}"

if [ -z "$DSN" ]; then
  echo "Set CRYSTAL_DATABASE_URL/CRYSTAL_DB_CONNECTION or pass a PostgreSQL connection string." >&2
  exit 2
fi

if ! python3 -c 'from playwright.sync_api import sync_playwright' >/dev/null 2>&1; then
  echo "Python Playwright is required for Control Web integration tests." >&2
  exit 2
fi

if [ "${SKIP_API_SETUP:-0}" != "1" ]; then
  "$ROOT/scripts/test-control-api.sh" "$DSN"
fi

pick_port() {
  python3 - <<'PY'
import socket
s = socket.socket()
s.bind(("127.0.0.1", 0))
print(s.getsockname()[1])
s.close()
PY
}

API_PORT="$(pick_port)"
WEB_PORT="$(pick_port)"
BASE="http://127.0.0.1:$API_PORT"
WEB="http://127.0.0.1:$WEB_PORT"
API_PID=""
WEB_PID=""

cleanup() {
  for pid in "$API_PID" "$WEB_PID"; do
    if [ -n "$pid" ]; then
      kill "$pid" 2>/dev/null || true
      wait "$pid" 2>/dev/null || true
    fi
  done
}
trap cleanup EXIT

cd "$ROOT"
mkdir -p "$ROOT/artifacts"
if ! command -v openssl >/dev/null 2>&1; then
  echo "openssl is required to generate Control Web release signing keys." >&2
  exit 2
fi
KEY_DIR="$ROOT/artifacts/control-web-signing"
RELEASE_ROOT="$ROOT/artifacts/control-web-releases"
mkdir -p "$KEY_DIR" "$RELEASE_ROOT"
openssl genpkey -algorithm EC -pkeyopt ec_paramgen_curve:P-256 \
  -out "$KEY_DIR/private.pem" >/dev/null 2>&1
openssl pkey -in "$KEY_DIR/private.pem" -pubout \
  -out "$KEY_DIR/public.pem" >/dev/null 2>&1
"$DOTNET" build src/Crystal.Control.Api/Crystal.Control.Api.csproj -c "$CONFIGURATION" --nologo >/dev/null

ASPNETCORE_ENVIRONMENT=Production \
ASPNETCORE_URLS="$BASE" \
ControlApi__DatabaseConnectionString="$DSN" \
ControlApi__JwtSigningKey="control-web-test-signing-key-0123456789abcdef" \
ControlApi__AuthRateLimitPermitLimit=1000 \
ControlApi__ApiRateLimitPermitLimit=5000 \
ControlApi__CorsOrigins="$WEB" \
ControlApi__BootstrapAdminPassword="$ADMIN_PASSWORD" \
ControlApi__AssetRoot="$ROOT/tests/fixtures/content" \
ControlApi__ReleaseRoot="$RELEASE_ROOT" \
ControlApi__ReleaseSigningPrivateKeyPath="$KEY_DIR/private.pem" \
ControlApi__ReleaseSigningPublicKeyPath="$KEY_DIR/public.pem" \
ControlApi__ReleaseSigningKeyId="control-web-test" \
"$DOTNET" "$ROOT/src/Crystal.Control.Api/bin/$CONFIGURATION/net8.0/Crystal.Control.Api.dll" \
  >"$ROOT/artifacts/control-web-api.log" 2>&1 &
API_PID=$!

python3 -m http.server "$WEB_PORT" --bind 127.0.0.1 --directory "$ROOT/src/Crystal.Control.Web" \
  >"$ROOT/artifacts/control-web-static.log" 2>&1 &
WEB_PID=$!

READY=0
for _ in $(seq 1 80); do
  if curl -fsS --max-time 2 "$BASE/health/ready" >/dev/null 2>&1; then
    READY=1
    break
  fi
  if ! kill -0 "$API_PID" 2>/dev/null; then
    break
  fi
  sleep 0.25
done

if [ "$READY" -ne 1 ]; then
  echo "Control API did not become ready." >&2
  cat "$ROOT/artifacts/control-web-api.log" >&2
  exit 1
fi

SUFFIX="$(date -u +%s)$$"
FINANCE_USER="web_finance_${SUFFIX}"
FINANCE_PASSWORD="WebIntegration!Passw0rd2026"
UI_USER="web_ui_${SUFFIX}"
UI_PASSWORD="WebUiIntegration!Passw0rd2026"
ADMIN_LOGIN_BODY="$(printf '{"username":"admin","password":"%s"}' "$ADMIN_PASSWORD")"
ADMIN_ACCESS="$(curl -fsS --max-time 15 -X POST -H 'Content-Type: application/json'   --data "$ADMIN_LOGIN_BODY" "$BASE/api/v1/auth/login" | python3 -c 'import json,sys; print(json.load(sys.stdin)["accessToken"])')"
curl -fsS --max-time 15 -X POST -H "Authorization: Bearer $ADMIN_ACCESS"   -H 'Content-Type: application/json'   --data "$(printf '{"username":"%s","displayName":"Web Finance","password":"%s","roles":["finance"]}' "$FINANCE_USER" "$FINANCE_PASSWORD")"   "$BASE/api/v1/accounts" >/dev/null

python3 - "$WEB" "$BASE" "$ADMIN_PASSWORD" "$FINANCE_USER" "$FINANCE_PASSWORD" "$UI_USER" "$UI_PASSWORD" <<'PY'
import hashlib
import json
import re
import sys
import time
import urllib.parse
import urllib.request
from datetime import datetime, timezone
from pathlib import Path
from playwright.sync_api import sync_playwright

web, api, password, finance_user, finance_password, ui_user, ui_password = sys.argv[1:8]
login_url = f"{web}/?api={api}#/login"

def is_expected_console_error(text):
    return any(f"status of {status}" in text for status in ("400", "401", "404", "409"))

with sync_playwright() as playwright:
    browser = playwright.chromium.launch()
    page = browser.new_page(viewport={"width": 1440, "height": 1000})
    page.on("dialog", lambda dialog: dialog.accept())
    errors = []
    failed = []
    page.on("pageerror", lambda exc: errors.append(str(exc)))
    page.on("console", lambda msg: errors.append(f"{msg.type}:{msg.text}") if msg.type == "error" and not is_expected_console_error(msg.text) else None)
    page.on("requestfailed", lambda request: failed.append(f"{request.method} {request.url}: {request.failure}") if "ERR_ABORTED" not in str(request.failure) else None)
    last_editor_request = {"entityType": None, "localIndex": None}
    def capture_editor_request(request):
        path = urllib.parse.urlparse(request.url).path
        match = re.search(r"/api/v1/drafts/\d+/content/([^/]+)/(\d+)$", path)
        if match:
            last_editor_request["entityType"] = match.group(1)
            last_editor_request["localIndex"] = int(match.group(2))
    page.on("request", capture_editor_request)

    page.goto(login_url, wait_until="networkidle")
    page.fill('input[name="username"]', "admin")
    page.fill('input[name="password"]', "wrong-password")
    page.click("button.login-submit")
    page.wait_for_selector("#login-error:not([hidden])", timeout=15000)
    assert "用户名或密码错误" in page.locator("#login-error").inner_text()
    assert page.evaluate("location.hash") == "#/login"

    page.fill('input[name="password"]', password)
    page.click("button.login-submit")
    page.wait_for_function("location.hash === '#/dashboard'", timeout=15000)
    page.wait_for_timeout(400)
    assert page.locator(".api-badge.success").count() >= 1

    def api_request(method, path, body=None, token=None):
        data = json.dumps(body).encode("utf-8") if body is not None else None
        request = urllib.request.Request(api + path, data=data, method=method)
        request.add_header("Accept", "application/json")
        if data is not None:
            request.add_header("Content-Type", "application/json")
        if token:
            request.add_header("Authorization", f"Bearer {token}")
        with urllib.request.urlopen(request, timeout=60) as response:
            payload = response.read().decode("utf-8")
            return json.loads(payload) if payload else None

    admin_token = page.evaluate("JSON.parse(sessionStorage.getItem('mir.control.session')).accessToken")
    zone_stamp = str(time.time_ns())
    zone_live_id = f"web-zone-{zone_stamp}"
    zone_offline_id = f"web-offline-{zone_stamp}"
    zone_agent_id = f"web-agent-{zone_stamp}"

    def register_zone(zone_id, zone_name):
        return api_request("POST", "/api/v1/zones/register", {
            "zoneId": zone_id,
            "zoneName": zone_name,
            "agentId": zone_agent_id,
            "agentVersion": "web-e2e",
            "protocolVersion": 1,
            "contentSchemaVersion": 1,
            "startedAt": "2026-11-18T00:00:00Z",
            "capabilities": ["process-supervisor"]
        }, admin_token)

    register_zone(zone_live_id, "Web Live Zone")
    register_zone(zone_offline_id, "Web Offline Zone")

    def open_first_draft_editor(route, type_id, entity_type, query=None):
        page.goto(f"{web}/?api={api}#/{route}", wait_until="networkidle")
        page.wait_for_selector("#content-table tbody tr.content-row", timeout=20000)
        if query is not None:
            page.fill(f"#content-search-{type_id}", query)
            page.wait_for_timeout(1200)
            page.wait_for_selector("#content-table tbody tr.content-row", timeout=15000)
        last_editor_request["entityType"] = None
        last_editor_request["localIndex"] = None
        page.locator("#content-table tbody tr.content-row").first.click()
        page.wait_for_selector(f'#content-editor[data-editor-entity-type="{entity_type}"] .js-content-editor', timeout=15000)
        assert last_editor_request["entityType"] == entity_type, last_editor_request
        assert last_editor_request["localIndex"] is not None, last_editor_request
        return last_editor_request["localIndex"]

    page.goto(f"{web}/?api={api}#/content-overview", wait_until="networkidle")
    page.wait_for_function("document.querySelector('.main-content').innerText.includes('10,160')", timeout=15000)
    overview_text = page.locator(".main-content").inner_text()
    assert "1,628" in overview_text, overview_text[:900]
    assert "批次 #" in overview_text, overview_text[:900]

    page.goto(f"{web}/?api={api}#/import-runs", wait_until="networkidle")
    page.wait_for_selector("#import-table tbody tr", timeout=15000)
    page.wait_for_timeout(400)
    import_text = page.locator(".main-content").inner_text()
    assert "1,628" in import_text, import_text[:800]

    page.goto(f"{web}/?api={api}#/content/items", wait_until="networkidle")
    page.wait_for_selector("#content-table tbody tr.content-row", timeout=15000)
    assert "SpiritBlade" in page.locator("#content-table tbody").inner_text()
    assert "1,628" in page.locator(".paginator").inner_text()
    page.click('.page-btn[data-type="items"][data-page="2"]')
    page.wait_for_function("document.querySelector('.paginator').innerText.includes('51')", timeout=15000)
    assert "51–100" in page.locator(".paginator").inner_text()
    page.click('.page-btn[data-type="items"][data-page="1"]')
    page.wait_for_function("document.querySelector('.paginator').innerText.includes('1–50')", timeout=15000)
    page.fill("#content-search-items", "Spirit")
    page.wait_for_timeout(1400)
    assert page.evaluate("document.activeElement && document.activeElement.id") == "content-search-items"
    assert "SpiritBlade" in page.locator("#content-table tbody").inner_text()

    old_token = page.evaluate("JSON.parse(sessionStorage.getItem('mir.control.session')).accessToken")
    page.evaluate("""() => {
      const key = 'mir.control.session';
      const data = JSON.parse(sessionStorage.getItem(key));
      data.accessTokenExpiresAt = new Date(Date.now() - 60000).toISOString();
      sessionStorage.setItem(key, JSON.stringify(data));
    }""")
    page.goto(f"{web}/?api={api}#/content/monsters")
    page.reload(wait_until="networkidle")
    page.wait_for_selector("#content-table tbody tr.content-row", timeout=15000)
    assert "555" in page.locator(".paginator").inner_text()
    new_token = page.evaluate("JSON.parse(sessionStorage.getItem('mir.control.session')).accessToken")
    assert new_token and new_token != old_token

    admin_token = page.evaluate("JSON.parse(sessionStorage.getItem('mir.control.session')).accessToken")
    page.goto(f"{web}/?api={api}#/content/items", wait_until="networkidle")
    page.wait_for_selector("#content-table tbody tr.content-row", timeout=15000)
    page.wait_for_selector("[data-live-draft-select]", timeout=15000)
    previous_draft = page.locator("[data-live-draft-select]").input_value()
    page.click('[data-action="open-modal"][data-modal="create-draft"]')
    page.wait_for_selector('.modal input[name="name"]', timeout=5000)
    page.fill('.modal input[name="name"]', f"Web UI Item Draft {time.time_ns()}")
    page.click('.modal [data-action="submit-draft-create"]')
    page.wait_for_function("!document.querySelector('.modal')", timeout=60000)
    page.wait_for_function(
        "old => { const select = document.querySelector('[data-live-draft-select]'); return select && select.value && select.value !== old; }",
        arg=previous_draft,
        timeout=15000)
    draft_ui_id = int(page.locator("[data-live-draft-select]").input_value())
    assert draft_ui_id > 0, draft_ui_id

    page.fill("#content-search-items", "")
    page.wait_for_timeout(500)
    page.fill("#content-search-items", "Spirit")
    page.wait_for_timeout(1400)
    page.wait_for_selector("#content-table tbody tr.content-row", timeout=15000)
    page.locator("#content-table tbody tr.content-row").first.click()
    page.wait_for_selector('#content-editor[data-editor-entity-type="item"] .js-content-editor', timeout=15000)
    assert page.locator("#content-editor .editor-field").count() >= 100
    assert page.locator('#content-editor [data-edit-path^="RandomStats."]').count() == 70
    page.fill('#content-editor [data-edit-path="Name"]', "SpiritBladeUI")
    page.fill('#content-editor [data-edit-path="Price"]', "23456")
    page.fill('#content-editor [data-edit-path="RandomStats.AccuracyChance"]', "42")
    page.click('#content-editor [data-action="editor-add-stat"]')
    stat_row = page.locator("#content-editor .editor-stat-row").last
    stat_row.locator("[data-stat-key]").select_option("Luck")
    stat_row.locator("[data-stat-value]").fill("7")
    page.fill("#editor-reason", "Web UI item editor full-field update")
    page.wait_for_function("document.querySelector('#editor-diff')?.innerText.includes('SpiritBladeUI')", timeout=5000)
    assert "Price" in page.locator("#editor-diff").inner_text()
    page.click('#content-editor [data-action="editor-save"]')
    page.wait_for_function("!document.querySelector('#content-editor')", timeout=30000)
    page.wait_for_function("document.querySelector('#content-table tbody')?.innerText.includes('SpiritBladeUI')", timeout=15000)

    edited_entity = api_request("GET", f"/api/v1/drafts/{draft_ui_id}/content/item/1", token=admin_token)
    assert edited_entity["revision"] == 2, edited_entity
    assert edited_entity["data"]["Name"] == "SpiritBladeUI", edited_entity["data"]
    assert edited_entity["data"]["Price"] == 23456, edited_entity["data"]
    assert edited_entity["data"]["RandomStats"]["AccuracyChance"] == 42, edited_entity["data"]["RandomStats"]
    assert edited_entity["data"]["Stats"]["Values"]["Luck"] == 7, edited_entity["data"]["Stats"]

    page.locator("#content-table tbody tr.content-row").first.click()
    page.wait_for_selector('#content-editor[data-editor-entity-type="item"] .js-content-editor', timeout=15000)
    page.click('#content-editor [data-action="editor-validate"]')
    page.wait_for_selector("#content-editor [data-editor-validation]", timeout=60000)
    validation_text = page.locator("#content-editor [data-editor-validation]").inner_text()
    assert "通过" in validation_text, validation_text[:1200]

    concurrent = api_request("GET", f"/api/v1/drafts/{draft_ui_id}/content/item/1", token=admin_token)
    concurrent_data = concurrent["data"]
    concurrent_data["Name"] = "SpiritBladeConcurrent"
    api_request("PUT", f"/api/v1/drafts/{draft_ui_id}/content/item/1", {
        "contentKey": concurrent["contentKey"],
        "schemaVersion": concurrent["schemaVersion"],
        "data": concurrent_data,
        "expectedRevision": concurrent["revision"],
        "reason": "Web UI simulate concurrent edit"
    }, admin_token)
    page.fill('#content-editor [data-edit-path="Name"]', "SpiritBladeConflictSave")
    page.click('#content-editor [data-action="editor-save"]')
    page.wait_for_selector("#content-editor [data-editor-conflict]", timeout=15000)
    assert page.locator('#content-editor [data-edit-path="Name"]').input_value() == "SpiritBladeConflictSave"
    page.click('#content-editor [data-action="editor-reload"]')
    page.wait_for_function(
        "() => document.querySelector('#content-editor [data-edit-path=Name]')?.value === 'SpiritBladeConcurrent'",
        timeout=15000)

    page.fill('#content-editor [data-edit-path="Price"]', "-1")
    page.click('#content-editor [data-action="editor-save"]')
    page.wait_for_selector('#content-editor .editor-field[data-field-path="Price"].has-error', timeout=15000)
    assert "between" in page.locator('#content-editor .editor-field[data-field-path="Price"]').inner_text()
    page.fill('#content-editor [data-edit-path="Price"]', "20000")
    page.click('#content-editor [data-action="editor-save"]')
    page.wait_for_function("!document.querySelector('#content-editor')", timeout=30000)

    page.fill("#content-search-items", "")
    page.wait_for_timeout(500)
    page.fill("#content-search-items", "UITempItem")
    page.wait_for_timeout(1200)
    page.click('[data-action="open-content-editor-new"][data-entity-type="item"]')
    page.wait_for_selector('#content-editor[data-editor-entity-type="item"] .js-content-editor', timeout=15000)
    page.fill('#content-editor [data-edit-path="Name"]', "UITempItem")
    page.fill("#editor-reason", "Web UI create temporary item")
    page.click('#content-editor [data-action="editor-save"]')
    page.wait_for_function("!document.querySelector('#content-editor')", timeout=30000)
    page.wait_for_function("document.querySelector('#content-table tbody')?.innerText.includes('UITempItem')", timeout=15000)
    page.locator("#content-table tbody tr.content-row").first.click()
    page.wait_for_selector('#content-editor[data-editor-entity-type="item"] .js-content-editor', timeout=15000)
    page.click('#content-editor [data-action="editor-delete-request"]')
    page.wait_for_selector('#content-editor [data-action="editor-delete-confirm"]', timeout=5000)
    page.click('#content-editor [data-action="editor-delete-confirm"]')
    page.wait_for_function("!document.querySelector('#content-editor')", timeout=30000)
    page.wait_for_function("!document.querySelector('#content-table tbody')?.innerText.includes('UITempItem')", timeout=15000)

    page.goto(f"{web}/?api={api}#/content/monsters", wait_until="networkidle")
    page.wait_for_selector("#content-table tbody tr.content-row", timeout=15000)
    page.fill("#content-search-monsters", "Archer")
    page.wait_for_timeout(1200)
    assert "ArcherGuard" in page.locator("#content-table tbody").inner_text()
    page.locator("#content-table tbody tr.content-row").first.click()
    page.wait_for_selector('#content-editor[data-editor-entity-type="monster"] .js-content-editor', timeout=15000)
    assert page.locator('#content-editor .editor-field').count() == 19
    assert page.locator('#content-editor [data-stat-map="Stats"]').count() == 1
    assert page.locator('#content-editor [data-edit-path="Image"]').count() == 1
    assert page.locator('#content-editor [data-edit-path="CanTame"]').count() == 1
    page.wait_for_function("document.querySelector('#content-editor')?.innerText.includes('掉落关系（只读）')", timeout=15000)
    page.wait_for_function("document.querySelector('#content-editor')?.innerText.toLowerCase().includes('drop.monster.1')", timeout=15000)
    page.fill('#content-editor [data-edit-path="Name"]', "ArcherGuardUI")
    page.fill('#content-editor [data-edit-path="AI"]', "58")
    page.locator('#content-editor .editor-stat-row:has([data-stat-key] option[value="HP"]:checked) [data-stat-value]').fill("8888")
    page.locator('#content-editor [data-edit-path="CanTame"]').check()
    page.fill("#editor-reason", "Web UI monster editor update")
    page.wait_for_function("document.querySelector('#editor-diff')?.innerText.includes('ArcherGuardUI')", timeout=5000)
    diff_text = page.locator("#editor-diff").inner_text()
    assert "HP" in diff_text and "CanTame" in diff_text, diff_text[:1200]
    page.click('#content-editor [data-action="editor-save"]')
    page.wait_for_function("!document.querySelector('#content-editor')", timeout=30000)
    page.wait_for_function("document.querySelector('#content-table tbody')?.innerText.includes('ArcherGuardUI')", timeout=15000)

    edited_monster = api_request("GET", f"/api/v1/drafts/{draft_ui_id}/content/monster/1", token=admin_token)
    assert edited_monster["revision"] == 2, edited_monster
    assert edited_monster["data"]["Name"] == "ArcherGuardUI", edited_monster["data"]
    assert edited_monster["data"]["AI"] == 58, edited_monster["data"]
    assert edited_monster["data"]["Stats"]["Values"]["HP"] == 8888, edited_monster["data"]["Stats"]
    assert edited_monster["data"]["CanTame"] is True, edited_monster["data"]

    page.locator("#content-table tbody tr.content-row").first.click()
    page.wait_for_selector('#content-editor[data-editor-entity-type="monster"] .js-content-editor', timeout=15000)
    page.click('#content-editor [data-action="editor-validate"]')
    page.wait_for_selector("#content-editor [data-editor-validation]", timeout=60000)
    monster_validation_text = page.locator("#content-editor [data-editor-validation]").inner_text()
    assert "通过" in monster_validation_text, monster_validation_text[:1200]

    concurrent_monster = api_request("GET", f"/api/v1/drafts/{draft_ui_id}/content/monster/1", token=admin_token)
    concurrent_monster_data = concurrent_monster["data"]
    concurrent_monster_data["Name"] = "ArcherGuardConcurrent"
    api_request("PUT", f"/api/v1/drafts/{draft_ui_id}/content/monster/1", {
        "contentKey": concurrent_monster["contentKey"],
        "schemaVersion": concurrent_monster["schemaVersion"],
        "data": concurrent_monster_data,
        "expectedRevision": concurrent_monster["revision"],
        "reason": "Web UI simulate concurrent monster edit"
    }, admin_token)
    page.fill('#content-editor [data-edit-path="Name"]', "ArcherGuardConflictSave")
    page.click('#content-editor [data-action="editor-save"]')
    page.wait_for_selector("#content-editor [data-editor-conflict]", timeout=15000)
    assert page.locator('#content-editor [data-edit-path="Name"]').input_value() == "ArcherGuardConflictSave"
    page.click('#content-editor [data-action="editor-reload"]')
    page.wait_for_function(
        "() => document.querySelector('#content-editor [data-edit-path=Name]')?.value === 'ArcherGuardConcurrent'",
        timeout=15000)

    page.fill('#content-editor [data-edit-path="Level"]', "65536")
    page.click('#content-editor [data-action="editor-save"]')
    page.wait_for_selector('#content-editor .editor-field[data-field-path="Level"].has-error', timeout=15000)
    assert "between" in page.locator('#content-editor .editor-field[data-field-path="Level"]').inner_text()
    page.fill('#content-editor [data-edit-path="Level"]', "65")
    page.click('#content-editor [data-action="editor-save"]')
    page.wait_for_function("!document.querySelector('#content-editor')", timeout=30000)

    page.fill("#content-search-monsters", "")
    page.wait_for_timeout(500)
    page.fill("#content-search-monsters", "UITempMonster")
    page.wait_for_timeout(1200)
    page.click('[data-action="open-content-editor-new"][data-entity-type="monster"]')
    page.wait_for_selector('#content-editor[data-editor-entity-type="monster"] .js-content-editor', timeout=15000)
    assert "保存时自动分配本地索引" in page.locator("#content-editor").inner_text()
    page.fill('#content-editor [data-edit-path="Name"]', "UITempMonster")
    page.fill("#editor-reason", "Web UI create temporary monster")
    page.click('#content-editor [data-action="editor-save"]')
    page.wait_for_function("!document.querySelector('#content-editor')", timeout=30000)
    page.wait_for_function("document.querySelector('#content-table tbody')?.innerText.includes('UITempMonster')", timeout=15000)
    page.locator("#content-table tbody tr.content-row").first.click()
    page.wait_for_selector('#content-editor[data-editor-entity-type="monster"] .js-content-editor', timeout=15000)
    assert "掉落关系（只读）" in page.locator("#content-editor").inner_text()
    assert "掉落关系读取失败" not in page.locator("#content-editor").inner_text()
    page.click('#content-editor [data-action="editor-delete-request"]')
    page.wait_for_selector('#content-editor [data-action="editor-delete-confirm"]', timeout=5000)
    page.click('#content-editor [data-action="editor-delete-confirm"]')
    page.wait_for_function("!document.querySelector('#content-editor')", timeout=30000)
    page.wait_for_function("!document.querySelector('#content-table tbody')?.innerText.includes('UITempMonster')", timeout=15000)

    page.goto(f"{web}/?api={api}#/content/maps", wait_until="networkidle")
    page.wait_for_selector("#content-table tbody tr.content-row", timeout=15000)
    page.fill("#content-search-maps", "Bichon")
    page.wait_for_timeout(1200)
    page.locator("#content-table tbody tr.content-row").first.click()
    page.wait_for_selector('#content-editor[data-editor-entity-type="map"] .js-content-editor', timeout=15000)
    assert page.locator("#content-editor .editor-field").count() == 44
    assert page.locator('#content-editor [data-edit-path="WeatherParticles"]').is_disabled()
    assert page.locator('#content-editor [data-edit-path="Movements"]').count() == 0
    page.wait_for_function("document.querySelector('#content-editor [data-editor-map-relations]')?.innerText.includes('72')", timeout=15000)
    map_relations_text = page.locator("#content-editor [data-editor-map-relations]").inner_text()
    assert "360" in map_relations_text and "读取失败" not in map_relations_text, map_relations_text[:1200]
    page.fill('#content-editor [data-edit-path="Title"]', "BichonProvinceUI")
    page.fill('#content-editor [data-edit-path="MiniMap"]', "127")
    page.select_option('#content-editor [data-edit-path="Light"]', "Dawn")
    page.check('#content-editor [data-edit-path="NoTeleport"]')
    page.fill('#content-editor [data-edit-path="FireDamage"]', "30")
    page.fill("#editor-reason", "Web UI map metadata update")
    page.wait_for_function("document.querySelector('#editor-diff')?.innerText.includes('BichonProvinceUI')", timeout=5000)
    assert "MiniMap" in page.locator("#editor-diff").inner_text()
    page.click('#content-editor [data-action="editor-save"]')
    page.wait_for_function("!document.querySelector('#content-editor')", timeout=30000)
    page.wait_for_function("document.querySelector('#content-table tbody')?.innerText.includes('BichonProvinceUI')", timeout=15000)

    edited_map = api_request("GET", f"/api/v1/drafts/{draft_ui_id}/content/map/1", token=admin_token)
    assert edited_map["revision"] == 2, edited_map
    assert edited_map["data"]["Title"] == "BichonProvinceUI", edited_map["data"]
    assert edited_map["data"]["MiniMap"] == 127, edited_map["data"]
    assert edited_map["data"]["Light"] == "Dawn", edited_map["data"]
    assert edited_map["data"]["NoTeleport"] is True, edited_map["data"]
    assert edited_map["data"]["FireDamage"] == 30, edited_map["data"]
    assert len(edited_map["data"]["Movements"]) == 72, edited_map["data"]
    assert len(edited_map["data"]["SafeZones"]) == 6, edited_map["data"]
    assert len(edited_map["data"]["Respawns"]) == 360, edited_map["data"]

    page.locator("#content-table tbody tr.content-row").first.click()
    page.wait_for_selector('#content-editor[data-editor-entity-type="map"] .js-content-editor', timeout=15000)
    page.click('#content-editor [data-action="editor-validate"]')
    page.wait_for_selector("#content-editor [data-editor-validation]", timeout=60000)
    map_validation_text = page.locator("#content-editor [data-editor-validation]").inner_text()
    assert "通过" in map_validation_text, map_validation_text[:1200]

    page.click('[data-action="close-drawer"]')
    relation_map_source = api_request("GET", f"/api/v1/drafts/{draft_ui_id}/content/map/1", token=admin_token)["data"]
    relation_map_source["Title"] = f"UIRelationMap{draft_ui_id}"
    relation_map_source["FileName"] = "0"
    for relation_property in ["Movements", "SafeZones", "MineZones", "ActiveCoords", "Respawns", "NPCs"]:
        relation_map_source[relation_property] = []
    created_relation_map = api_request("POST", f"/api/v1/drafts/{draft_ui_id}/content/map", {
        "schemaVersion": 1,
        "data": relation_map_source,
        "reason": "Web UI map relation fixture"
    }, admin_token)
    relation_map_index = created_relation_map["localIndex"]
    page.goto(f"{web}/?api={api}#/content/maps", wait_until="networkidle")
    page.wait_for_selector("#content-table tbody tr.content-row", timeout=15000)
    page.fill("#content-search-maps", relation_map_source["Title"])
    page.wait_for_function(
        "title => document.querySelector('#content-table tbody')?.innerText.includes(title)",
        arg=relation_map_source["Title"],
        timeout=15000)
    page.locator("#content-table tbody tr.content-row", has_text=relation_map_source["Title"]).first.click()
    page.wait_for_selector('#content-editor[data-editor-entity-type="map"] .js-content-editor', timeout=15000)
    page.wait_for_function("document.querySelector('#content-editor [data-editor-map-relations]')?.innerText.toLowerCase().includes('revision 1')", timeout=15000)

    def map_group(group):
        locator = page.locator(f'[data-map-relation-group="{group}"]')
        if locator.count():
            locator.evaluate("element => element.open = true")
        return locator

    safe_group = map_group("safezones")
    safe_group.locator('[data-action="editor-relation-row-add"]').click()
    safe_row = safe_group.locator(".relation-edit-rows [data-map-relation-row]").last
    safe_row.locator('[data-rel-field="Location.x"]').fill("1")
    safe_row.locator('[data-rel-field="Location.y"]').fill("1")
    safe_row.locator('[data-rel-field="Size"]').fill("3")
    with page.expect_response(lambda response: f"/content/map/{relation_map_index}/safezones" in response.url and response.request.method == "PUT") as response_info:
        safe_group.locator('[data-action="editor-relation-group-save"]').click()
    assert response_info.value.ok, response_info.value.status
    relations_after_safe = api_request("GET", f"/api/v1/drafts/{draft_ui_id}/content/map/{relation_map_index}/relations", token=admin_token)
    assert any(zone["Location"] == {"x": 1, "y": 1} and zone["Size"] == 3 for zone in relations_after_safe["safeZones"]), relations_after_safe["safeZones"]
    page.wait_for_function(
        "revision => document.querySelector('[data-editor-map-relations]')?.innerText.toLowerCase().includes(`revision ${revision}`.toLowerCase())",
        arg=relations_after_safe["revision"],
        timeout=15000)

    mine_group = map_group("minezones")
    mine_group.locator('[data-action="editor-relation-row-add"]').click()
    mine_row = mine_group.locator(".relation-edit-rows [data-map-relation-row]").last
    mine_row.locator('[data-rel-field="Location.x"]').fill("2")
    mine_row.locator('[data-rel-field="Location.y"]').fill("2")
    mine_row.locator('[data-rel-field="Size"]').fill("4")
    mine_row.locator('[data-rel-field="Mine"]').fill("1")
    with page.expect_response(lambda response: f"/content/map/{relation_map_index}/minezones" in response.url and response.request.method == "PUT") as response_info:
        mine_group.locator('[data-action="editor-relation-group-save"]').click()
    assert response_info.value.ok, response_info.value.status
    relations_after_mine = api_request("GET", f"/api/v1/drafts/{draft_ui_id}/content/map/{relation_map_index}/relations", token=admin_token)
    assert any(zone["Location"] == {"x": 2, "y": 2} and zone["Size"] == 4 and zone["Mine"] == 1 for zone in relations_after_mine["mineZones"]), relations_after_mine["mineZones"]
    page.wait_for_function(
        "revision => document.querySelector('[data-editor-map-relations]')?.innerText.toLowerCase().includes(`revision ${revision}`.toLowerCase())",
        arg=relations_after_mine["revision"],
        timeout=15000)

    active_group = map_group("activecoords")
    active_group.locator('[data-action="editor-relation-row-add"]').click()
    active_row = active_group.locator(".relation-edit-rows [data-map-relation-row]").last
    active_row.locator('[data-rel-field="x"]').fill("3")
    active_row.locator('[data-rel-field="y"]').fill("3")
    with page.expect_response(lambda response: f"/content/map/{relation_map_index}/activecoords" in response.url and response.request.method == "PUT") as response_info:
        active_group.locator('[data-action="editor-relation-group-save"]').click()
    assert response_info.value.ok, response_info.value.status
    relations_after_active = api_request("GET", f"/api/v1/drafts/{draft_ui_id}/content/map/{relation_map_index}/relations", token=admin_token)
    assert {"x": 3, "y": 3} in relations_after_active["activeCoords"], relations_after_active["activeCoords"]
    page.wait_for_function(
        "revision => document.querySelector('[data-editor-map-relations]')?.innerText.toLowerCase().includes(`revision ${revision}`.toLowerCase())",
        arg=relations_after_active["revision"],
        timeout=15000)

    respawn_group = page.locator('[data-relation-entity-group="respawn"]')
    respawn_group.evaluate("element => element.open = true")
    respawn_group.locator('[data-action="editor-relation-entity-add"]').click()
    new_respawn_row = respawn_group.locator('[data-relation-entity-list] > [data-relation-entity-row]').first
    monster_select = new_respawn_row.locator('[data-rel-field="MonsterIndex"]')
    monster_values = monster_select.locator("option").evaluate_all(
        "options => options.map(option => option.value)")
    monster_value = next(value for value in monster_values if value)
    monster_select.select_option(value=monster_value)
    new_respawn_row.locator('[data-rel-field="Location.x"]').fill("4")
    new_respawn_row.locator('[data-rel-field="Location.y"]').fill("4")
    new_respawn_row.locator('[data-rel-field="Count"]').fill("2")
    with page.expect_response(lambda response: response.url.endswith(f"/content/map/{relation_map_index}/respawns") and response.request.method == "POST") as response_info:
        new_respawn_row.locator('[data-action="editor-relation-entity-save"]').click()
    assert response_info.value.ok, response_info.value.status
    relations_with_respawn = api_request("GET", f"/api/v1/drafts/{draft_ui_id}/content/map/{relation_map_index}/relations", token=admin_token)
    created_respawn = next(item for item in relations_with_respawn["respawns"]
                           if item["data"].get("Location") == {"x": 4, "y": 4} and item["data"].get("Count") == 2)
    page.wait_for_function(
        "revision => document.querySelector('[data-editor-map-relations]')?.innerText.toLowerCase().includes(`revision ${revision}`.toLowerCase())",
        arg=relations_with_respawn["revision"],
        timeout=15000)

    respawn_group.evaluate("element => element.open = true")
    existing_respawn = relations_with_respawn["respawns"][0]
    respawn_row = respawn_group.locator(f'[data-relation-entity-row][data-index="{existing_respawn["localIndex"]}"]')
    respawn_row.wait_for(state="visible", timeout=15000)
    respawn_count = int(existing_respawn["data"].get("Count", 1))
    respawn_row.locator('[data-rel-field="Count"]').fill(str(respawn_count + 1))
    with page.expect_response(lambda response: f"/content/map/{relation_map_index}/respawns/{existing_respawn['localIndex']}" in response.url and response.request.method == "PUT") as response_info:
        respawn_row.locator('[data-action="editor-relation-entity-save"]').click()
    assert response_info.value.ok, response_info.value.status
    updated_respawn = api_request("GET", f"/api/v1/drafts/{draft_ui_id}/content/respawn/{existing_respawn['localIndex']}", token=admin_token)
    assert updated_respawn["data"]["Count"] == respawn_count + 1 and updated_respawn["revision"] == 2, updated_respawn
    relations_after_respawn_update = api_request("GET", f"/api/v1/drafts/{draft_ui_id}/content/map/{relation_map_index}/relations", token=admin_token)
    page.wait_for_function(
        "revision => document.querySelector('[data-editor-map-relations]')?.innerText.toLowerCase().includes(`revision ${revision}`.toLowerCase())",
        arg=relations_after_respawn_update["revision"],
        timeout=15000)

    respawn_group.evaluate("element => element.open = true")
    respawn_row = respawn_group.locator(f'[data-relation-entity-row][data-index="{existing_respawn["localIndex"]}"]')
    respawn_row.wait_for(state="visible", timeout=15000)
    with page.expect_response(lambda response: f"/content/map/{relation_map_index}/respawns/{existing_respawn['localIndex']}" in response.url and response.request.method == "DELETE") as response_info:
        respawn_row.locator('[data-action="editor-relation-entity-delete"]').click()
    assert response_info.value.ok, response_info.value.status
    relations_after_respawn_delete = api_request("GET", f"/api/v1/drafts/{draft_ui_id}/content/map/{relation_map_index}/relations", token=admin_token)
    assert all(item["localIndex"] != existing_respawn["localIndex"] for item in relations_after_respawn_delete["respawns"])
    page.wait_for_function(
        "index => !document.querySelector(`[data-relation-entity-row][data-index=\"${index}\"]`)",
        arg=existing_respawn["localIndex"],
        timeout=15000)

    npc_group = page.locator('[data-relation-entity-group="npc"]')
    npc_group.evaluate("element => element.open = true")
    npc_group.locator('[data-action="editor-relation-entity-add"]').click()
    new_npc_row = npc_group.locator('[data-relation-entity-list] > [data-relation-entity-row]').first
    npc_relation_name = f"UIRelationNpc{time.time_ns()}"
    new_npc_row.locator('[data-rel-field="Name"]').fill(npc_relation_name)
    script_select = new_npc_row.locator('[data-rel-field="FileName"]')
    script_values = script_select.locator("option").evaluate_all(
        "options => options.map(option => option.value)")
    script_select.select_option(value=next(value for value in script_values if value))
    new_npc_row.locator('[data-rel-field="Location.x"]').fill("5")
    new_npc_row.locator('[data-rel-field="Location.y"]').fill("5")
    with page.expect_response(lambda response: response.url.endswith(f"/content/map/{relation_map_index}/npcs") and response.request.method == "POST") as response_info:
        new_npc_row.locator('[data-action="editor-relation-entity-save"]').click()
    assert response_info.value.ok, response_info.value.status
    relations_with_npc = api_request("GET", f"/api/v1/drafts/{draft_ui_id}/content/map/{relation_map_index}/relations", token=admin_token)
    created_npc = next(item for item in relations_with_npc["npcs"] if item["data"].get("Name") == npc_relation_name)
    page.wait_for_function(
        "args => { const [revision, index] = args; return document.querySelector('[data-editor-map-relations]')?.innerText.toLowerCase().includes(`revision ${revision}`.toLowerCase()) && document.querySelector(`[data-relation-entity-row][data-index=\"${index}\"]`); }",
        arg=[relations_with_npc["revision"], created_npc["localIndex"]],
        timeout=15000)

    npc_group.evaluate("element => element.open = true")
    npc_row = npc_group.locator(f'[data-relation-entity-row][data-index="{created_npc["localIndex"]}"]')
    assert npc_row.count() == 1, created_npc
    npc_row.locator('[data-rel-field="Location.x"]').fill("6")
    with page.expect_response(lambda response: f"/content/map/{relation_map_index}/npcs/{created_npc['localIndex']}" in response.url and response.request.method == "PUT") as response_info:
        npc_row.locator('[data-action="editor-relation-entity-save"]').click()
    assert response_info.value.ok, response_info.value.status
    updated_npc_relation = api_request("GET", f"/api/v1/drafts/{draft_ui_id}/content/npc/{created_npc['localIndex']}", token=admin_token)
    assert updated_npc_relation["data"]["Location"] == {"x": 6, "y": 5}, updated_npc_relation
    relations_after_npc_update = api_request("GET", f"/api/v1/drafts/{draft_ui_id}/content/map/{relation_map_index}/relations", token=admin_token)
    page.wait_for_function(
        "revision => document.querySelector('[data-editor-map-relations]')?.innerText.toLowerCase().includes(`revision ${revision}`.toLowerCase())",
        arg=relations_after_npc_update["revision"],
        timeout=15000)

    npc_group.evaluate("element => element.open = true")
    npc_row = npc_group.locator(f'[data-relation-entity-row][data-index="{created_npc["localIndex"]}"]')
    with page.expect_response(lambda response: f"/content/map/{relation_map_index}/npcs/{created_npc['localIndex']}" in response.url and response.request.method == "DELETE") as response_info:
        npc_row.locator('[data-action="editor-relation-entity-delete"]').click()
    assert response_info.value.ok, response_info.value.status
    relations_after_npc_delete = api_request("GET", f"/api/v1/drafts/{draft_ui_id}/content/map/{relation_map_index}/relations", token=admin_token)
    assert all(item["localIndex"] != created_npc["localIndex"] for item in relations_after_npc_delete["npcs"])
    page.wait_for_function(
        "index => !document.querySelector(`[data-relation-entity-row][data-index=\"${index}\"]`)",
        arg=created_npc["localIndex"],
        timeout=15000)

    stale_relations = api_request("GET", f"/api/v1/drafts/{draft_ui_id}/content/map/{relation_map_index}/relations", token=admin_token)
    stale_safe_zones = list(stale_relations["safeZones"]) + [{"Location": {"x": 9, "y": 9}, "Size": 1, "StartPoint": False}]
    advanced_map = api_request("PUT", f"/api/v1/drafts/{draft_ui_id}/content/map/{relation_map_index}/safezones", {
        "expectedRevision": stale_relations["revision"],
        "items": stale_safe_zones,
        "reason": "Web UI map relation stale revision setup"
    }, admin_token)
    with page.expect_response(lambda response: f"/content/map/{relation_map_index}/safezones" in response.url and response.request.method == "PUT") as response_info:
        safe_group = map_group("safezones")
        safe_group.locator('[data-action="editor-relation-group-save"]').click()
    assert response_info.value.status == 409, response_info.value.status
    page.wait_for_function(
        "revision => document.querySelector('[data-editor-map-relations]')?.innerText.toLowerCase().includes(`revision ${revision}`.toLowerCase())",
        arg=advanced_map["revision"],
        timeout=15000)

    page.click('[data-action="close-drawer"]')
    relation_map_current = api_request("GET", f"/api/v1/drafts/{draft_ui_id}/content/map/{relation_map_index}", token=admin_token)
    api_request("DELETE", f"/api/v1/drafts/{draft_ui_id}/content/map/{relation_map_index}", {
        "expectedRevision": relation_map_current["revision"],
        "reason": "Web UI map relation fixture cleanup"
    }, admin_token)
    page.goto(f"{web}/?api={api}#/content/maps", wait_until="networkidle")
    page.wait_for_selector("#content-table tbody tr.content-row", timeout=15000)
    page.fill("#content-search-maps", "Bichon")
    page.wait_for_function(
        "() => document.querySelector('#content-table tbody')?.innerText.includes('Bichon')",
        timeout=15000)
    page.locator("#content-table tbody tr.content-row", has_text="Bichon").first.click()
    page.wait_for_selector('#content-editor[data-editor-entity-type="map"] .js-content-editor', timeout=15000)

    concurrent_map = api_request("GET", f"/api/v1/drafts/{draft_ui_id}/content/map/1", token=admin_token)
    concurrent_map_data = concurrent_map["data"]
    concurrent_map_data["Title"] = "BichonProvinceConcurrent"
    api_request("PUT", f"/api/v1/drafts/{draft_ui_id}/content/map/1", {
        "contentKey": concurrent_map["contentKey"],
        "schemaVersion": concurrent_map["schemaVersion"],
        "data": concurrent_map_data,
        "expectedRevision": concurrent_map["revision"],
        "reason": "Web UI simulate concurrent map edit"
    }, admin_token)
    page.fill('#content-editor [data-edit-path="Title"]', "BichonProvinceConflictSave")
    page.click('#content-editor [data-action="editor-save"]')
    page.wait_for_selector("#content-editor [data-editor-conflict]", timeout=15000)
    assert page.locator('#content-editor [data-edit-path="Title"]').input_value() == "BichonProvinceConflictSave"
    page.click('#content-editor [data-action="editor-reload"]')
    page.wait_for_function(
        "() => document.querySelector('#content-editor [data-edit-path=Title]')?.value === 'BichonProvinceConcurrent'",
        timeout=15000)

    page.fill('#content-editor [data-edit-path="MiniMap"]', "-1")
    page.click('#content-editor [data-action="editor-save"]')
    page.wait_for_selector('#content-editor .editor-field[data-field-path="MiniMap"].has-error', timeout=15000)
    assert "between" in page.locator('#content-editor .editor-field[data-field-path="MiniMap"]').inner_text()
    page.fill('#content-editor [data-edit-path="MiniMap"]', "127")
    page.click('#content-editor [data-action="editor-save"]')
    page.wait_for_function("!document.querySelector('#content-editor')", timeout=30000)

    page.fill("#content-search-maps", "")
    page.wait_for_timeout(500)
    page.fill("#content-search-maps", "UITempMap")
    page.wait_for_timeout(1200)
    page.click('[data-action="open-content-editor-new"][data-entity-type="map"]')
    page.wait_for_selector('#content-editor[data-editor-entity-type="map"] .js-content-editor', timeout=15000)
    assert "保存时自动分配本地索引" in page.locator("#content-editor").inner_text()
    page.fill('#content-editor [data-edit-path="FileName"]', "UITempMap")
    page.fill('#content-editor [data-edit-path="Title"]', "UITempMap")
    page.fill("#editor-reason", "Web UI create temporary map")
    page.click('#content-editor [data-action="editor-save"]')
    page.wait_for_function("!document.querySelector('#content-editor')", timeout=30000)
    page.wait_for_function("document.querySelector('#content-table tbody')?.innerText.includes('UITempMap')", timeout=15000)
    page.locator("#content-table tbody tr.content-row").first.click()
    page.wait_for_selector('#content-editor[data-editor-entity-type="map"] .js-content-editor', timeout=15000)
    page.wait_for_function("document.querySelector('#content-editor [data-editor-map-relations]')?.innerText.includes('传送与出入口')", timeout=15000)
    assert "地图关系读取失败" not in page.locator("#content-editor").inner_text()
    page.click('#content-editor [data-action="editor-delete-request"]')
    page.wait_for_selector('#content-editor [data-action="editor-delete-confirm"]', timeout=5000)
    page.click('#content-editor [data-action="editor-delete-confirm"]')
    page.wait_for_function("!document.querySelector('#content-editor')", timeout=30000)
    page.wait_for_function("!document.querySelector('#content-table tbody')?.innerText.includes('UITempMap')", timeout=15000)

    editor_stamp = str(time.time_ns())
    npc_index = open_first_draft_editor("content/npcs", "npcs", "npc")
    npc_name = f"UINpc{editor_stamp}"
    page.fill('#content-editor [data-edit-path="Name"]', npc_name)
    script_search = page.locator('#content-editor .editor-reference-picker:has([data-edit-path="FileName"]) [data-reference-search]')
    script_search.fill("Transport")
    page.wait_for_timeout(1000)
    npc_script_select = page.locator('#content-editor [data-edit-path="FileName"]')
    assert npc_script_select.locator("option").count() > 1
    npc_script = npc_script_select.locator("option").nth(1).get_attribute("value")
    npc_script_select.select_option(index=1)
    page.fill("#editor-reason", "Web UI NPC editor")
    page.click('#content-editor [data-action="editor-save"]')
    page.wait_for_function("!document.querySelector('#content-editor')", timeout=30000)
    edited_npc = api_request("GET", f"/api/v1/drafts/{draft_ui_id}/content/npc/{npc_index}", token=admin_token)
    assert edited_npc["revision"] == 2, edited_npc
    assert edited_npc["data"]["Name"] == npc_name, edited_npc["data"]
    assert edited_npc["data"]["FileName"] == npc_script, edited_npc["data"]

    drop_index = open_first_draft_editor("content/drops", "drops", "drop")
    page.fill('#content-editor [data-edit-path="monsterName"]', f"UIDrop{editor_stamp}")
    page.locator('[data-drop-table="drops"] > [data-action="editor-add-drop"]').click()
    drop_row = page.locator('[data-drop-table="drops"] > .editor-drop-rows > [data-drop-row]').last
    drop_row.locator('[data-drop-mode]').select_option("group")
    drop_row.locator('[data-drop-field="Chance"]').fill("100")
    drop_row.locator('[data-drop-pane="group"] [data-action="editor-add-drop"]').click()
    child_drop = drop_row.locator('[data-drop-group-rows] > [data-drop-row]').last
    child_drop.locator('[data-reference-search]').fill("Spirit")
    page.wait_for_timeout(1000)
    assert child_drop.locator('[data-drop-item] option').count() > 1
    child_drop.locator('[data-drop-item]').select_option(index=1)
    child_drop.locator('[data-drop-field="Chance"]').fill("50")
    page.fill("#editor-reason", "Web UI nested drop group")
    page.click('#content-editor [data-action="editor-save"]')
    page.wait_for_function("!document.querySelector('#content-editor')", timeout=30000)
    edited_drop = api_request("GET", f"/api/v1/drafts/{draft_ui_id}/content/drop/{drop_index}", token=admin_token)
    assert edited_drop["revision"] == 2, edited_drop
    assert edited_drop["data"]["monsterName"] == f"UIDrop{editor_stamp}", edited_drop["data"]
    nested_drop = edited_drop["data"]["drops"][-1]
    assert nested_drop["GroupedDrop"]["items"][0]["Chance"] == 50, nested_drop
    assert nested_drop["GroupedDrop"]["items"][0]["Item"]["$ref"] == "item", nested_drop

    magic_index = open_first_draft_editor("content/skills", "skills", "magic", query="FireBall")
    magic_name = f"UIMagic{editor_stamp}"
    page.fill('#content-editor [data-edit-path="Name"]', magic_name)
    assert page.locator('#content-editor [data-edit-path="MultiplierBase"]').count() == 1
    page.fill("#editor-reason", "Web UI magic editor")
    page.click('#content-editor [data-action="editor-save"]')
    page.wait_for_function("!document.querySelector('#content-editor')", timeout=30000)
    edited_magic = api_request("GET", f"/api/v1/drafts/{draft_ui_id}/content/magic/{magic_index}", token=admin_token)
    assert edited_magic["revision"] == 2, edited_magic
    assert edited_magic["data"]["Name"] == magic_name, edited_magic["data"]
    assert edited_magic["data"]["Spell"] == "FireBall", edited_magic["data"]

    quest_index = open_first_draft_editor("content/quests", "quests", "quest")
    quest_name = f"UIQuest{editor_stamp}"
    page.fill('#content-editor [data-edit-path="Name"]', quest_name)
    assert page.locator('#content-editor [data-edit-path="Description"]').count() == 1
    page.fill("#editor-reason", "Web UI quest editor")
    page.click('#content-editor [data-action="editor-save"]')
    page.wait_for_function("!document.querySelector('#content-editor')", timeout=30000)
    edited_quest = api_request("GET", f"/api/v1/drafts/{draft_ui_id}/content/quest/{quest_index}", token=admin_token)
    assert edited_quest["revision"] == 2, edited_quest
    assert edited_quest["data"]["Name"] == quest_name, edited_quest["data"]

    shop_index = open_first_draft_editor("content/shops", "shops", "gameshop")
    shop_category = f"UIShop{editor_stamp}"
    page.fill('#content-editor [data-edit-path="Category"]', shop_category)
    item_search = page.locator('#content-editor .editor-reference-picker:has([data-edit-path="ItemIndex"]) [data-reference-search]')
    item_search.fill("Spirit")
    page.wait_for_timeout(1000)
    shop_item_select = page.locator('#content-editor [data-edit-path="ItemIndex"]')
    assert shop_item_select.locator("option").count() > 1
    shop_item_index = int(shop_item_select.locator("option").nth(1).get_attribute("value"))
    shop_item_select.select_option(index=1)
    page.fill("#editor-reason", "Web UI game shop editor")
    page.click('#content-editor [data-action="editor-save"]')
    page.wait_for_function("!document.querySelector('#content-editor')", timeout=30000)
    edited_shop = api_request("GET", f"/api/v1/drafts/{draft_ui_id}/content/gameshop/{shop_index}", token=admin_token)
    assert edited_shop["revision"] == 2, edited_shop
    assert edited_shop["data"]["Category"] == shop_category, edited_shop["data"]
    assert edited_shop["data"]["ItemIndex"] == shop_item_index, edited_shop["data"]
    assert edited_shop["data"]["Info"]["index"] == shop_item_index, edited_shop["data"]

    page.goto(f"{web}/?api={api}#/content/items", wait_until="networkidle")
    page.wait_for_selector("#content-search-items", timeout=15000)
    page.fill("#content-search-items", "")
    page.wait_for_timeout(800)
    page.wait_for_selector("#content-table tbody tr.content-row", timeout=15000)
    import_source = api_request("GET", f"/api/v1/drafts/{draft_ui_id}/content/item/1", token=admin_token)
    imported_item_index = 9_100_000 + draft_ui_id
    imported_item_name = f"M2WebJsonlItem{draft_ui_id}"
    imported_item_data = json.loads(json.dumps(import_source["data"]))
    imported_item_data["Index"] = imported_item_index
    imported_item_data["Name"] = imported_item_name
    imported_item_line = json.dumps({
        "entityType": "item",
        "localIndex": imported_item_index,
        "contentKey": f"item.{imported_item_index}",
        "schemaVersion": 1,
        "data": imported_item_data
    }, ensure_ascii=False, separators=(",", ":"))
    page.click('[data-action="open-content-import"][data-entity-type="item"]')
    page.wait_for_selector("#content-import-lines", timeout=5000)
    page.fill("#content-import-lines", imported_item_line)
    page.fill("#content-import-reason", "Web UI JSONL dry-run")
    with page.expect_response(lambda response: "/content/item/import" in response.url and "dryRun=true" in response.url and response.request.method == "POST") as response_info:
        page.click('[data-action="content-import-dryrun"]')
    assert response_info.value.ok, response_info.value.status
    page.wait_for_function("document.querySelector('#content-import-result')?.innerText.toLowerCase().includes('dry-run 结果') && document.querySelector('#content-import-result')?.innerText.includes('通过')", timeout=15000)
    page.fill("#content-import-reason", "Web UI JSONL apply")
    with page.expect_response(lambda response: "/content/item/import" in response.url and "dryRun=false" in response.url and response.request.method == "POST") as response_info:
        page.click('[data-action="content-import-apply"]')
    assert response_info.value.ok, response_info.value.status
    page.wait_for_function("!document.querySelector('.modal')", timeout=30000)
    page.fill("#content-search-items", imported_item_name)
    page.wait_for_timeout(1200)
    page.wait_for_function(
        "name => document.querySelector('#content-table tbody')?.innerText.includes(name)",
        arg=imported_item_name,
        timeout=15000)
    imported_entity = api_request("GET", f"/api/v1/drafts/{draft_ui_id}/content/item/{imported_item_index}", token=admin_token)
    assert imported_entity["data"]["Name"] == imported_item_name, imported_entity

    with page.expect_download(timeout=30000) as download_info:
        page.click('[data-action="export-content"][data-entity-type="item"]')
    export_download = download_info.value
    assert export_download.suggested_filename == "item.jsonl", export_download.suggested_filename
    exported_text = Path(export_download.path()).read_text(encoding="utf-8")
    exported_lines = [json.loads(line) for line in exported_text.splitlines() if line.strip()]
    assert exported_lines and exported_lines == sorted(exported_lines, key=lambda row: row["localIndex"])
    assert any(row["contentKey"] == f"item.{imported_item_index}" and row["data"]["Name"] == imported_item_name for row in exported_lines)
    api_request("DELETE", f"/api/v1/drafts/{draft_ui_id}/content/item/{imported_item_index}", {
        "expectedRevision": imported_entity["revision"],
        "reason": "Web UI JSONL cleanup"
    }, admin_token)

    page.goto(f"{web}/?api={api}#/content/recipes", wait_until="networkidle")
    page.wait_for_selector("#content-table tbody tr.content-row", timeout=15000)
    recipe_table_text = page.locator("#content-table tbody").inner_text()
    assert "GreenPoison" in recipe_table_text, recipe_table_text[:1200]
    page.locator("#content-table tbody tr.content-row").first.click()
    page.wait_for_selector('#content-editor[data-editor-entity-type="recipe"] .js-content-editor', timeout=15000)
    assert page.locator('#content-editor [data-edit-path="output.itemIndex"]').count() == 1
    recipe_editor_text = page.locator("#content-editor").inner_text()
    assert "产出与材料" in recipe_editor_text and "合成条件" in recipe_editor_text, recipe_editor_text[:1200]
    page.click('[data-action="close-drawer"]')

    page.goto(f"{web}/?api={api}#/content/buffs", wait_until="networkidle")
    page.wait_for_selector("#content-table tbody tr.content-row", timeout=15000)
    buff_table_text = page.locator("#content-table tbody").inner_text()
    assert "TemporalFlux" in buff_table_text and "ResetDuration" in buff_table_text, buff_table_text[:1200]

    page.goto(f"{web}/?api={api}#/content/npc-scripts", wait_until="networkidle")
    page.wait_for_selector("#content-table tbody tr.content-row", timeout=15000)
    page.fill("#content-search-npc-scripts", "Transport")
    page.wait_for_timeout(1200)
    script_table_text = page.locator("#content-table tbody").inner_text()
    assert "BorderVillage/Transport" in script_table_text, script_table_text[:1200]
    page.locator("#content-table tbody tr.content-row").first.click()
    page.wait_for_selector('#content-editor[data-editor-entity-type="script"] .js-content-editor', timeout=15000)
    assert "[@MAIN]" in page.locator('#content-editor [data-edit-path="source"]').input_value()
    page.click('#content-editor [data-action="editor-script-validate"]')
    page.wait_for_selector("#content-editor [data-editor-script-validation]", timeout=30000)
    script_editor_text = page.locator("#content-editor [data-editor-script-validation]").inner_text()
    assert "源码 SHA256" in script_editor_text and "编译哈希" in script_editor_text, script_editor_text[:1200]
    assert "c0e3f43676a6d5bd978d57c2586636efa404e17fd84014102a3d7d377ecdf8ff" in script_editor_text, script_editor_text[:1200]
    page.click('[data-action="close-drawer"]')

    page.goto(f"{web}/?api={api}#/content/resources", wait_until="networkidle")
    page.wait_for_selector("#content-table tbody tr.content-row", timeout=15000)
    asset_table_text = page.locator("#content-table tbody").inner_text()
    assert "Maps/0.map" in asset_table_text and "10 × 10" in asset_table_text, asset_table_text[:1200]
    page.locator("#content-table tbody tr.content-row").first.click()
    page.wait_for_function("document.querySelector('#live-drawer-relations')?.innerText.includes('下载并校验资源')", timeout=15000)
    asset_drawer_text = page.locator(".drawer").inner_text()
    assert "10 × 10 / 99 / 1" in asset_drawer_text, asset_drawer_text[:1200]
    assert "3ab00f38634c7c9c839a3085819a1807b14d0e00841a833a36e147c97aff237f" in asset_drawer_text, asset_drawer_text[:1200]
    page.click('#live-drawer-relations [data-action="preview-asset"]')
    page.wait_for_selector("#asset-inline-preview .asset-map-grid", timeout=15000)
    asset_preview_text = page.locator("#asset-inline-preview").inner_text()
    assert "10 × 10" in asset_preview_text and "可走" in asset_preview_text and "阻挡" in asset_preview_text, asset_preview_text[:1200]
    assert "." in page.locator("#asset-inline-preview .asset-map-grid").inner_text(), asset_preview_text[:1200]
    with page.expect_download(timeout=10000) as download_info:
        page.click('#live-drawer-relations [data-action="download-asset"]')
    download = download_info.value
    assert download.suggested_filename == "0.map", download.suggested_filename
    asset_bytes = Path(download.path()).read_bytes()
    assert len(asset_bytes) == 1252, len(asset_bytes)
    assert hashlib.sha256(asset_bytes).hexdigest() == "3ab00f38634c7c9c839a3085819a1807b14d0e00841a833a36e147c97aff237f"
    page.click('[data-action="close-drawer"]')

    page.goto(f"{web}/?api={api}#/world-map", wait_until="networkidle")
    page.wait_for_selector("#live-relation-panel", timeout=15000)
    page.wait_for_function("document.querySelectorAll('#live-map-select option').length === 463", timeout=15000)
    page.wait_for_function("document.querySelector('#live-relation-panel').innerText.includes('72 / 6 / 46 / 360')", timeout=15000)
    relation_text = page.locator("#live-relation-panel").inner_text()
    assert "463 / 463" in relation_text, relation_text[:1200]
    assert "BichonProvince" in relation_text, relation_text[:1200]
    assert "72 / 6 / 46 / 360" in relation_text, relation_text[:1200]
    relation_data_text = page.locator("#live-relation-data").inner_text()
    assert "script.BichonProvince.BorderVillage.Transport" in relation_data_text, relation_data_text[:1200]
    page.locator('#live-relation-data [data-action="open-live-drops"]').first.click()
    page.wait_for_function("document.getElementById('live-drop-body')?.innerText.includes('掉落路径')", timeout=15000)
    page.click('[data-action="close-modal"]')
    page.select_option("#live-map-select", "300")
    page.wait_for_function("document.querySelector('#live-relation-panel').innerText.includes('map.300')", timeout=15000)
    page.fill("#live-map-search", "BichonProvince")
    page.wait_for_function("document.querySelectorAll('#live-map-select option').length > 0 && document.querySelectorAll('#live-map-select option').length < 463", timeout=15000)
    assert page.locator('#live-map-select option[value="1"]').count() == 1

    admin_token = page.evaluate("JSON.parse(sessionStorage.getItem('mir.control.session')).accessToken")
    runs = api_request("GET", "/api/v1/import-runs?limit=20", token=admin_token)
    run_id = next(item["id"] for item in runs if item["status"] == "imported")
    releases_before = api_request("GET", "/api/v1/releases?limit=200", token=admin_token)["items"]
    active_before = next((item for item in releases_before if item["status"] == "active"), None)
    web_suffix = str(time.time_ns())
    draft = api_request("POST", "/api/v1/drafts", {
        "baseImportRunId": run_id,
        "name": f"web-draft-{web_suffix}"
    }, admin_token)
    version1 = f"web.draft.{web_suffix}"
    version2 = f"web.rollback.{web_suffix}"

    page.goto(f"{web}/?api={api}#/content-releases", wait_until="networkidle")
    page.wait_for_selector("#release-table tbody tr", timeout=15000)

    def release_row(version):
        return page.locator("#release-table tbody tr", has_text=version)

    def wait_release_status(version, label):
        page.wait_for_function(
            "([version, label]) => { const rows = [...document.querySelectorAll('#release-table tbody tr')]; const row = rows.find(item => item.innerText.includes(version)); return row && row.innerText.includes(label); }",
            arg=[version, label],
            timeout=30000)

    page.click('[data-action="open-modal"][data-modal="create-release"]')
    page.wait_for_selector('.modal select[name="source"]', timeout=5000)
    page.fill('.modal input[name="version"]', version1)
    page.select_option('.modal select[name="source"]', value=f"draft:{draft['id']}")
    page.fill('.modal textarea[name="notes"]', "web integration draft release")
    page.click('.modal [data-action="submit-release-create"]')
    release_row(version1).wait_for(timeout=20000)

    release_row(version1).locator('button[title="校验"]').click()
    page.wait_for_function("document.body.innerText.includes('校验通过')", timeout=30000)
    page.wait_for_timeout(800)
    release_row(version1).locator('button[title="编译"]').click()
    wait_release_status(version1, "已编译")
    release_row(version1).locator('button[title="签名"]').click()
    wait_release_status(version1, "已签名")
    page.wait_for_function("document.querySelector('.main-content').innerText.includes('ecdsa-p256-sha256')", timeout=20000)
    release_row(version1).locator('button[title="发布"]').click()
    wait_release_status(version1, "已发布")

    release_row(version1).locator('button[title="查看版本"]').click()
    page.wait_for_selector('.modal', timeout=5000)
    modal_text = page.locator('.modal').inner_text()
    assert "ecdsa-p256-sha256" in modal_text, modal_text[:1200]
    assert "control-web-test" in modal_text, modal_text[:1200]
    page.click('.modal [data-action="close-modal"]')

    release1 = next(
        item for item in api_request("GET", "/api/v1/releases?limit=200", token=admin_token)["items"]
        if item["version"] == version1)
    assert release1["status"] == "published" and release1["draftId"] == draft["id"], release1

    page.click('[data-action="open-modal"][data-modal="create-release"]')
    page.wait_for_selector('.modal select[name="source"]', timeout=5000)
    page.fill('.modal input[name="version"]', version2)
    page.select_option('.modal select[name="source"]', value=f"import:{run_id}")
    page.fill('.modal textarea[name="notes"]', "web integration rollback target")
    page.click('.modal [data-action="submit-release-create"]')
    release_row(version2).wait_for(timeout=20000)

    release_row(version2).locator('button[title="校验"]').click()
    page.wait_for_function("document.body.innerText.includes('校验通过')", timeout=30000)
    page.wait_for_timeout(800)
    release_row(version2).locator('button[title="编译"]').click()
    wait_release_status(version2, "已编译")
    release_row(version2).locator('button[title="签名"]').click()
    wait_release_status(version2, "已签名")
    release_row(version2).locator('button[title="发布"]').click()
    wait_release_status(version2, "已发布")
    release2 = next(
        item for item in api_request("GET", "/api/v1/releases?limit=200", token=admin_token)["items"]
        if item["version"] == version2)
    assert release2["status"] == "published", release2

    rollback_source = active_before["version"] if active_before else version2
    rollback_target = release2["id"] if active_before else release1["id"]
    active_version = version2 if active_before else version1
    release_row(rollback_source).locator('button[title="回滚"]').click()
    page.wait_for_selector('.modal select[name="targetReleaseId"]', timeout=5000)
    page.select_option('.modal select[name="targetReleaseId"]', value=str(rollback_target))
    page.fill('.modal textarea[name="reason"]', "web integration rollback reason")
    page.click('.modal [data-action="submit-release-rollback"]')
    wait_release_status(rollback_source, "已回滚")
    wait_release_status(active_version, "生效中")
    releases_after = api_request("GET", "/api/v1/releases?limit=200", token=admin_token)["items"]
    rolled_back_release = next(item for item in releases_after if item["version"] == rollback_source)
    activated_release = next(item for item in releases_after if item["version"] == active_version)
    assert rolled_back_release["status"] == "rolled_back", rolled_back_release
    assert activated_release["status"] == "active", activated_release
    assert rolled_back_release["rollbackTargetId"] == activated_release["id"], (rolled_back_release, activated_release)
    release_page_text = page.locator('.main-content').inner_text()
    assert "Ed25519" not in release_page_text, release_page_text[:1200]
    assert "区服灰度" not in release_page_text, release_page_text[:1200]
    assert "已回滚版本" in release_page_text, release_page_text[:1200]

    admin_token = page.evaluate("JSON.parse(sessionStorage.getItem('mir.control.session')).accessToken")
    zone_release = release1

    def heartbeat_zone(zone_id):
        now = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
        return api_request("POST", f"/api/v1/zones/{zone_id}/heartbeat", {
            "zoneId": zone_id,
            "agentId": zone_agent_id,
            "observedAt": now,
            "status": "online",
            "activeReleaseId": zone_release["id"],
            "activeContentHash": zone_release["contentHash"],
            "playerCount": 37,
            "tickP95Ms": 42.5,
            "errorRate": 0.03,
            "metadata": {
                "metricsSource": "zone-runtime",
                "metricsObservedAt": now,
                "metricsWindowSeconds": "60",
                "metricsSampleCount": "600",
                "restartFailures": "2",
                "zonePid": "4321",
                "previousReleaseId": str(release2["id"])
            }
        }, admin_token)

    def complete_zone_command(zone_id, command_id, payload=None):
        now = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
        return api_request("POST", f"/api/v1/zones/{zone_id}/commands/{command_id}/result", {
            "commandId": command_id,
            "zoneId": zone_id,
            "status": "succeeded",
            "startedAt": now,
            "completedAt": now,
            "error": None,
            "payload": payload
        }, admin_token)

    heartbeat_zone(zone_live_id)
    zone_detail_url = f"{web}/?api={api}#/zone/{zone_live_id}"
    page.goto(f"{web}/?api={api}#/zones", wait_until="networkidle")
    live_zone_row = page.locator("#zone-table tbody tr", has_text=zone_live_id)
    live_zone_row.wait_for(timeout=15000)
    live_zone_text = live_zone_row.inner_text()
    assert "37" in live_zone_text and "42.5" in live_zone_text and "3.00%" in live_zone_text, live_zone_text

    page.locator("#zone-table tbody tr", has_text=zone_live_id).click()
    page.wait_for_selector("#zone-command-table", timeout=15000)
    page.wait_for_function(
        "zoneId => { const text = document.querySelector('.main-content')?.innerText.toLowerCase() || ''; return text.includes(zoneId.toLowerCase()) && text.includes('zone-runtime'); }",
        arg=zone_live_id,
        timeout=15000)
    zone_detail_text = page.locator(".main-content").inner_text()
    assert "42.5" in zone_detail_text and "3.00" in zone_detail_text, zone_detail_text[:1500]
    assert "37" in zone_detail_text and zone_release["version"] in zone_detail_text, zone_detail_text[:1500]
    assert "4321" in zone_detail_text and str(release2["id"]) in zone_detail_text, zone_detail_text[:1500]
    command_buttons = page.locator('[data-action="open-modal"][data-modal="zone-command"]')
    assert command_buttons.count() == 5, command_buttons.count()

    command_ids = []
    release_commands = {
        "content.stage": True,
        "content.activate": True,
        "content.rollback": True,
        "content.reload": False
    }
    for command_type, needs_release in release_commands.items():
        heartbeat_zone(zone_live_id)
        page.reload(wait_until="networkidle")
        page.wait_for_selector("#zone-command-table", timeout=15000)
        button = page.locator(f'[data-action="open-modal"][data-modal="zone-command"][data-command="{command_type}"]')
        button.wait_for(timeout=15000)
        assert button.is_enabled(), command_type
        button.click()
        page.wait_for_selector('.modal [data-action="submit-zone-command"]', timeout=5000)
        expected_body = {"type": command_type}
        if needs_release:
            select = page.locator('.modal select[name="releaseId"]')
            select.wait_for(timeout=5000)
            select.select_option(value=str(zone_release["id"]))
            expected_body["payload"] = {"releaseId": zone_release["id"]}
        with page.expect_response(lambda response: response.request.method == "POST" and response.url.endswith(f"/zones/{zone_live_id}/commands")) as response_info:
            page.click('.modal [data-action="submit-zone-command"]')
        response = response_info.value
        assert response.status == 201, (command_type, response.status)
        posted = response.request.post_data_json
        assert posted == expected_body, (command_type, posted, expected_body)
        created = response.json()
        command_ids.append(created["commandId"])
        if needs_release:
            assert created["releaseId"] == zone_release["id"], created
            complete_zone_command(zone_live_id, created["commandId"], {"contentHash": zone_release["contentHash"]})
        else:
            assert created["releaseId"] is None, created
            complete_zone_command(zone_live_id, created["commandId"], {"contentHash": zone_release["contentHash"]})
        page.wait_for_function("!document.querySelector('.modal')", timeout=15000)

    # A stale modal must surface the API's lifecycle conflict and the page must disable writes while it is queued.
    heartbeat_zone(zone_live_id)
    page.reload(wait_until="networkidle")
    page.wait_for_selector("#zone-command-table", timeout=15000)
    page.click('[data-action="open-modal"][data-modal="zone-command"][data-command="content.reload"]')
    page.wait_for_selector('.modal [data-action="submit-zone-command"]', timeout=5000)
    conflict_command = api_request("POST", f"/api/v1/zones/{zone_live_id}/commands", {
        "type": "content.reload"
    }, admin_token)
    with page.expect_response(lambda response: response.request.method == "POST" and response.url.endswith(f"/zones/{zone_live_id}/commands")) as response_info:
        page.click('.modal [data-action="submit-zone-command"]')
    assert response_info.value.status == 409, response_info.value.status
    page.wait_for_function("document.body.innerText.includes('unfinished lifecycle command')", timeout=10000)
    page.click('.modal [data-action="close-modal"]')
    page.reload(wait_until="networkidle")
    page.wait_for_function("document.querySelector('.main-content')?.innerText.includes('已有生命周期命令')", timeout=15000)
    assert page.locator('[data-action="open-modal"][data-modal="zone-command"]:disabled').count() == 5
    complete_zone_command(zone_live_id, conflict_command["commandId"], {"contentHash": zone_release["contentHash"]})

    heartbeat_zone(zone_live_id)
    page.reload(wait_until="networkidle")
    page.wait_for_selector("#zone-command-table tbody tr", timeout=15000)
    page.click('[data-action="open-modal"][data-modal="zone-command"][data-command="zone.stop"]')
    page.wait_for_selector('.modal [data-action="submit-zone-command"]', timeout=5000)
    with page.expect_response(lambda response: response.request.method == "POST" and response.url.endswith(f"/zones/{zone_live_id}/commands")) as response_info:
        page.click('.modal [data-action="submit-zone-command"]')
    response = response_info.value
    assert response.status == 201, response.status
    assert response.request.post_data_json == {"type": "zone.stop"}, response.request.post_data_json
    stop_command = response.json()
    command_ids.append(stop_command["commandId"])
    complete_zone_command(zone_live_id, stop_command["commandId"], {"stopped": True})
    page.wait_for_function("!document.querySelector('.modal')", timeout=15000)
    page.reload(wait_until="networkidle")
    page.wait_for_function("document.querySelector('#zone-command-table tbody')?.innerText.includes('停止区服')", timeout=15000)
    command_history_text = page.locator("#zone-command-table tbody").inner_text()
    for label in ["下载并暂存", "激活内容", "回滚内容", "重载当前版本", "停止区服"]:
        assert label in command_history_text, command_history_text[:2000]
    assert len(command_ids) == 5, command_ids

    # The registered-but-stale zone must become offline through the 30 second heartbeat projection.
    offline_deadline = time.time() + 40
    while time.time() < offline_deadline:
        offline_zone = api_request("GET", f"/api/v1/zones/{zone_offline_id}", token=admin_token)
        if offline_zone["status"] == "offline":
            break
        time.sleep(1)
    assert offline_zone["status"] == "offline", offline_zone
    page.goto(f"{web}/?api={api}#/zone/{zone_offline_id}", wait_until="networkidle")
    page.wait_for_function("document.querySelector('.main-content')?.innerText.includes('区服已离线')", timeout=15000)
    assert page.locator('[data-action="open-modal"][data-modal="zone-command"]:disabled').count() == 5

    page.goto(f"{web}/?api={api}#/accounts", wait_until="networkidle")
    page.wait_for_selector("#account-table tbody tr", timeout=15000)
    page.wait_for_function("!document.querySelector('#account-table tbody').innerText.includes('正在从 Control API')", timeout=15000)
    account_text = page.locator("#account-table tbody").inner_text()
    assert "admin" in account_text and "platform_admin" in account_text, account_text[:900]

    page.click('[data-action="open-modal"][data-modal="new-account"]')
    page.wait_for_selector('.modal input[name="username"]', timeout=5000)
    page.fill('.modal input[name="username"]', ui_user)
    page.fill('.modal input[name="displayName"]', "Web UI User")
    page.fill('.modal input[name="email"]', f"{ui_user}@example.com")
    page.fill('.modal input[name="password"]', ui_password)
    page.uncheck('.modal input[name="roles"][value="content_author"]')
    page.check('.modal input[name="roles"][value="finance"]')
    page.click('.modal [data-action="submit-account"]')
    page.wait_for_function("user => document.querySelector('#account-table')?.innerText.includes(user)", arg=ui_user, timeout=15000)

    def ui_row():
        return page.locator("#account-table tbody tr", has_text=ui_user)

    ui_row().locator('[data-action="account-toggle-status"]').click()
    page.wait_for_selector('.modal [data-action="submit-account-status"]', timeout=5000)
    page.click('.modal [data-action="submit-account-status"]')
    page.wait_for_function("user => { const row = [...document.querySelectorAll('#account-table tbody tr')].find(item => item.innerText.includes(user)); return row && row.innerText.includes('停用'); }", arg=ui_user, timeout=15000)

    ui_row().locator('[data-action="account-toggle-status"]').click()
    page.wait_for_selector('.modal [data-action="submit-account-status"]', timeout=5000)
    page.click('.modal [data-action="submit-account-status"]')
    page.wait_for_function("user => { const row = [...document.querySelectorAll('#account-table tbody tr')].find(item => item.innerText.includes(user)); return row && row.innerText.includes('启用'); }", arg=ui_user, timeout=15000)

    ui_row().locator('[data-action="open-modal"][data-modal="reset-account-password"]').click()
    page.wait_for_selector('.modal input[name="newPassword"]', timeout=5000)
    page.fill('.modal input[name="newPassword"]', f"{ui_password}Reset")
    page.click('.modal [data-action="submit-account-password"]')
    page.wait_for_function("document.body.innerText.includes('密码已重置')", timeout=15000)

    ui_row().locator('[data-action="open-modal"][data-modal="edit-account-roles"]').click()
    page.wait_for_selector('.modal input[name="roles"][value="auditor"]', timeout=5000)
    page.uncheck('.modal input[name="roles"][value="finance"]')
    page.check('.modal input[name="roles"][value="auditor"]')
    page.click('.modal [data-action="submit-account-roles"]')
    page.wait_for_function("user => { const row = [...document.querySelectorAll('#account-table tbody tr')].find(item => item.innerText.includes(user)); return row && row.innerText.includes('审计'); }", arg=ui_user, timeout=15000)

    page.goto(f"{web}/?api={api}#/roles", wait_until="networkidle")
    page.wait_for_selector('#role-list [data-action="select-role"]', timeout=15000)
    role_text = page.locator("#role-list").inner_text()
    assert "platform_admin" in role_text and "content_author" in role_text, role_text[:900]
    page.click('[data-action="select-role"][data-code="platform_admin"]')
    page.wait_for_function("document.querySelector('.main-content').innerText.includes('release:publish')", timeout=5000)

    page.goto(f"{web}/?api={api}#/audit", wait_until="networkidle")
    page.wait_for_selector("#audit-table tbody tr", timeout=15000)
    page.wait_for_timeout(400)
    audit_text = page.locator("#audit-table tbody").inner_text()
    assert "auth.login" in audit_text, audit_text[:600]
    assert "account.status" in audit_text and "account.roles.update" in audit_text and "account.password.reset" in audit_text, audit_text[:1200]
    with page.expect_download(timeout=5000) as download_info:
        page.click('[data-action="export-audit"]')
    download = download_info.value
    assert download.suggested_filename.endswith(".csv"), download.suggested_filename
    export_path = Path(download.path())
    exported = export_path.read_text(encoding="utf-8-sig")
    assert "auth.login" in exported and "request_id" in exported, exported[:600]

    page.click(".topbar-user")
    page.wait_for_selector(".modal", timeout=5000)
    page.click('.modal [data-action="logout"]')
    page.wait_for_function("location.hash === '#/login'", timeout=5000)
    page.wait_for_timeout(500)
    assert page.evaluate("sessionStorage.getItem('mir.control.session')") is None

    finance_page = browser.new_page(viewport={"width": 1440, "height": 1000})
    finance_errors = []
    finance_requests = []
    finance_page.on("pageerror", lambda exc: finance_errors.append(str(exc)))
    finance_page.on("console", lambda msg: finance_errors.append(f"{msg.type}:{msg.text}") if msg.type == "error" and not is_expected_console_error(msg.text) else None)
    finance_page.on("request", lambda request: finance_requests.append(request.url))
    finance_page.goto(login_url, wait_until="networkidle")
    finance_page.fill('input[name="username"]', finance_user)
    finance_page.fill('input[name="password"]', finance_password)
    finance_page.click("button.login-submit")
    finance_page.wait_for_function("location.hash === '#/dashboard'", timeout=15000)
    finance_page.wait_for_timeout(300)
    assert finance_page.locator('[data-nav-item="content/items"]').count() == 0
    assert finance_page.locator('[data-nav-item="accounts"]').count() == 0
    assert finance_page.locator('[data-nav-item="roles"]').count() == 0
    assert finance_page.locator('[data-nav-item="audit"]').count() == 0
    assert finance_page.locator('[data-nav-item="orders"]').count() == 1

    finance_page.goto(f"{web}/?api={api}#/content/items", wait_until="networkidle")
    finance_page.wait_for_function("document.querySelector('.main-content').innerText.includes('403')", timeout=5000)
    denied_text = finance_page.locator(".main-content").inner_text()
    assert "content:read" in denied_text, denied_text[:800]
    assert finance_page.locator("#content-table").count() == 0

    finance_page.goto(f"{web}/?api={api}#/accounts", wait_until="networkidle")
    finance_page.wait_for_function("document.querySelector('.main-content').innerText.includes('403')", timeout=5000)
    denied_text = finance_page.locator(".main-content").inner_text()
    assert "account:read" in denied_text, denied_text[:800]
    assert finance_page.locator("#account-table").count() == 0
    requested_paths = [url.split(api, 1)[-1] for url in finance_requests if url.startswith(api)]
    assert not any(path.startswith("/api/v1/content") or path.startswith("/api/v1/import-runs") or path.startswith("/api/v1/accounts") for path in requested_paths), requested_paths

    auditor_page = browser.new_page(viewport={"width": 1440, "height": 1000})
    auditor_errors = []
    auditor_page.on("pageerror", lambda exc: auditor_errors.append(str(exc)))
    auditor_page.on("console", lambda msg: auditor_errors.append(f"{msg.type}:{msg.text}") if msg.type == "error" and not is_expected_console_error(msg.text) else None)
    auditor_page.goto(login_url, wait_until="networkidle")
    auditor_page.fill('input[name="username"]', ui_user)
    auditor_page.fill('input[name="password"]', f"{ui_password}Reset")
    auditor_page.click("button.login-submit")
    auditor_page.wait_for_function("location.hash === '#/dashboard'", timeout=15000)
    auditor_page.goto(f"{web}/?api={api}#/zones", wait_until="networkidle")
    auditor_page.wait_for_selector("#zone-table tbody tr", timeout=15000)
    assert auditor_page.locator('[data-action="open-modal"][data-modal="zone-command"]').count() == 0
    auditor_page.locator("#zone-table tbody tr", has_text=zone_live_id).click()
    auditor_page.wait_for_selector("#zone-command-table", timeout=15000)
    assert auditor_page.locator('[data-action="open-modal"][data-modal="zone-command"]').count() == 0
    assert "只读账号" in auditor_page.locator(".main-content").inner_text()
    assert auditor_page.locator('[data-nav-item="content-releases"]').count() == 1
    auditor_page.goto(f"{web}/?api={api}#/content-releases", wait_until="networkidle")
    auditor_page.wait_for_selector("#release-table tbody tr", timeout=15000)
    assert auditor_page.locator('[data-action="release-perform"]').count() == 0
    assert auditor_page.locator('[data-action="open-modal"][data-modal="create-release"]').count() == 0
    assert auditor_page.locator('button[title="回滚"]').count() == 0
    assert auditor_page.locator('button[title="查看版本"]').count() >= 1
    assert auditor_page.locator('.main-content').inner_text().find("只读") >= 0 or auditor_page.locator('button[title="查看版本"]').count() >= 1

    auditor_page.goto(f"{web}/?api={api}#/content/items", wait_until="networkidle")
    auditor_page.wait_for_selector("#content-table tbody tr.content-row", timeout=15000)
    assert auditor_page.locator('[data-action="open-content-editor-new"][data-entity-type="item"]').count() == 0
    assert auditor_page.locator('[data-action="open-modal"][data-modal="create-draft"]').count() == 0
    auditor_page.locator("#content-table tbody tr.content-row").first.click()
    auditor_page.wait_for_selector(".drawer", timeout=10000)
    assert auditor_page.locator("#content-editor").count() == 0
    assert auditor_page.locator('[data-action="editor-save"]').count() == 0

    auditor_page.goto(f"{web}/?api={api}#/content/monsters", wait_until="networkidle")
    auditor_page.wait_for_selector("#content-table tbody tr.content-row", timeout=15000)
    assert auditor_page.locator('[data-action="open-content-editor-new"][data-entity-type="monster"]').count() == 0
    assert auditor_page.locator('[data-action="open-modal"][data-modal="create-draft"]').count() == 0
    auditor_page.locator("#content-table tbody tr.content-row").first.click()
    auditor_page.wait_for_selector("#live-drawer-relations", timeout=10000)
    auditor_page.wait_for_function("document.querySelector('#live-drawer-relations')?.innerText.includes('真实掉落关系')", timeout=15000)
    assert auditor_page.locator("#content-editor").count() == 0
    assert auditor_page.locator('[data-action="editor-save"]').count() == 0

    auditor_page.goto(f"{web}/?api={api}#/content/maps", wait_until="networkidle")
    auditor_page.wait_for_selector("#content-table tbody tr.content-row", timeout=15000)
    assert auditor_page.locator('[data-action="open-content-editor-new"][data-entity-type="map"]').count() == 0
    auditor_page.locator("#content-table tbody tr.content-row").first.click()
    auditor_page.wait_for_selector("#live-drawer-relations", timeout=10000)
    assert auditor_page.locator("#content-editor").count() == 0
    assert auditor_page.locator('[data-action="editor-save"]').count() == 0

    for route, entity_type in [
        ("content/npcs", "npc"),
        ("content/drops", "drop"),
        ("content/skills", "magic"),
        ("content/quests", "quest"),
        ("content/shops", "gameshop"),
        ("content/recipes", "recipe"),
        ("content/npc-scripts", "script"),
    ]:
        auditor_page.goto(f"{web}/?api={api}#/{route}", wait_until="networkidle")
        auditor_page.wait_for_selector("#content-table tbody tr.content-row", timeout=15000)
        assert auditor_page.locator(f'[data-action="open-content-editor-new"][data-entity-type="{entity_type}"]').count() == 0, route
        auditor_page.locator("#content-table tbody tr.content-row").first.click()
        auditor_page.wait_for_selector("#live-drawer-relations, .drawer", timeout=10000)
        assert auditor_page.locator("#content-editor").count() == 0, route
        assert auditor_page.locator('[data-action="editor-save"]').count() == 0, route
        if auditor_page.locator('[data-action="close-drawer"]').count():
            auditor_page.click('[data-action="close-drawer"]')

    assert not errors, errors
    assert not failed, failed
    assert not finance_errors, finance_errors
    assert not auditor_errors, auditor_errors
    print("Control Web integration verified: login, live overview, import runs, ten draft editors, content query/search, map relation CRUD/stale recovery, JSONL dry-run/apply/export, live relations, script AST validation, map preview, asset download/hash, buffs, live release create/validate/compile/sign/publish/manifest/rollback, live zones/metrics/commands/offline/409/read-only permissions, accounts, roles, token refresh, audit export, RBAC 403, logout, CORS.")
    browser.close()
PY
