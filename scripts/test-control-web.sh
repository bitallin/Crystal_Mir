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
import sys
import time
import urllib.request
from pathlib import Path
from playwright.sync_api import sync_playwright

web, api, password, finance_user, finance_password, ui_user, ui_password = sys.argv[1:8]
login_url = f"{web}/?api={api}#/login"

def is_expected_console_error(text):
    return any(f"status of {status}" in text for status in ("400", "401", "409"))

with sync_playwright() as playwright:
    browser = playwright.chromium.launch()
    page = browser.new_page(viewport={"width": 1440, "height": 1000})
    errors = []
    failed = []
    page.on("pageerror", lambda exc: errors.append(str(exc)))
    page.on("console", lambda msg: errors.append(f"{msg.type}:{msg.text}") if msg.type == "error" and not is_expected_console_error(msg.text) else None)
    page.on("requestfailed", lambda request: failed.append(f"{request.method} {request.url}: {request.failure}") if "ERR_ABORTED" not in str(request.failure) else None)

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
            return json.loads(response.read().decode("utf-8"))

    page.goto(f"{web}/?api={api}#/content-overview", wait_until="networkidle")
    page.wait_for_function("document.querySelector('.main-content').innerText.includes('9,797')", timeout=15000)
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
    page.wait_for_selector("#item-editor .js-item-editor", timeout=15000)
    assert page.locator("#item-editor .editor-field").count() >= 100
    assert page.locator('#item-editor [data-edit-path^="RandomStats."]').count() == 70
    page.fill('#item-editor [data-edit-path="Name"]', "SpiritBladeUI")
    page.fill('#item-editor [data-edit-path="Price"]', "23456")
    page.fill('#item-editor [data-edit-path="RandomStats.AccuracyChance"]', "42")
    page.click('#item-editor [data-action="editor-add-stat"]')
    stat_row = page.locator("#item-editor .editor-stat-row").last
    stat_row.locator("[data-stat-key]").select_option("Luck")
    stat_row.locator("[data-stat-value]").fill("7")
    page.fill("#editor-reason", "Web UI item editor full-field update")
    page.wait_for_function("document.querySelector('#editor-diff')?.innerText.includes('SpiritBladeUI')", timeout=5000)
    assert "Price" in page.locator("#editor-diff").inner_text()
    page.click('#item-editor [data-action="editor-save"]')
    page.wait_for_function("!document.querySelector('#item-editor')", timeout=30000)
    page.wait_for_function("document.querySelector('#content-table tbody')?.innerText.includes('SpiritBladeUI')", timeout=15000)

    edited_entity = api_request("GET", f"/api/v1/drafts/{draft_ui_id}/content/item/1", token=admin_token)
    assert edited_entity["revision"] == 2, edited_entity
    assert edited_entity["data"]["Name"] == "SpiritBladeUI", edited_entity["data"]
    assert edited_entity["data"]["Price"] == 23456, edited_entity["data"]
    assert edited_entity["data"]["RandomStats"]["AccuracyChance"] == 42, edited_entity["data"]["RandomStats"]
    assert edited_entity["data"]["Stats"]["Values"]["Luck"] == 7, edited_entity["data"]["Stats"]

    page.locator("#content-table tbody tr.content-row").first.click()
    page.wait_for_selector("#item-editor .js-item-editor", timeout=15000)
    page.click('#item-editor [data-action="editor-validate"]')
    page.wait_for_selector("#item-editor [data-editor-validation]", timeout=60000)
    validation_text = page.locator("#item-editor [data-editor-validation]").inner_text()
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
    page.fill('#item-editor [data-edit-path="Name"]', "SpiritBladeConflictSave")
    page.click('#item-editor [data-action="editor-save"]')
    page.wait_for_selector("#item-editor [data-editor-conflict]", timeout=15000)
    assert page.locator('#item-editor [data-edit-path="Name"]').input_value() == "SpiritBladeConflictSave"
    page.click('#item-editor [data-action="editor-reload"]')
    page.wait_for_function(
        "() => document.querySelector('#item-editor [data-edit-path=Name]')?.value === 'SpiritBladeConcurrent'",
        timeout=15000)

    page.fill('#item-editor [data-edit-path="Price"]', "-1")
    page.click('#item-editor [data-action="editor-save"]')
    page.wait_for_selector('#item-editor .editor-field[data-field-path="Price"].has-error', timeout=15000)
    assert "between" in page.locator('#item-editor .editor-field[data-field-path="Price"]').inner_text()
    page.fill('#item-editor [data-edit-path="Price"]', "20000")
    page.click('#item-editor [data-action="editor-save"]')
    page.wait_for_function("!document.querySelector('#item-editor')", timeout=30000)

    page.fill("#content-search-items", "UITempItem")
    page.wait_for_timeout(1200)
    page.click('[data-action="open-item-editor-new"]')
    page.wait_for_selector("#item-editor .js-item-editor", timeout=15000)
    page.fill('#item-editor [data-edit-path="Name"]', "UITempItem")
    page.fill("#editor-reason", "Web UI create temporary item")
    page.click('#item-editor [data-action="editor-save"]')
    page.wait_for_function("!document.querySelector('#item-editor')", timeout=30000)
    page.wait_for_function("document.querySelector('#content-table tbody')?.innerText.includes('UITempItem')", timeout=15000)
    page.locator("#content-table tbody tr.content-row").first.click()
    page.wait_for_selector("#item-editor .js-item-editor", timeout=15000)
    page.click('#item-editor [data-action="editor-delete-request"]')
    page.wait_for_selector('#item-editor [data-action="editor-delete-confirm"]', timeout=5000)
    page.click('#item-editor [data-action="editor-delete-confirm"]')
    page.wait_for_function("!document.querySelector('#item-editor')", timeout=30000)
    page.wait_for_function("!document.querySelector('#content-table tbody')?.innerText.includes('UITempItem')", timeout=15000)

    page.goto(f"{web}/?api={api}#/content/maps", wait_until="networkidle")
    page.wait_for_selector("#content-table tbody tr.content-row", timeout=15000)
    page.locator("#content-table tbody tr.content-row").first.click()
    page.wait_for_function("document.querySelector('#live-drawer-relations')?.innerText.includes('真实世界关系')", timeout=15000)
    map_drawer_text = page.locator("#live-drawer-relations").inner_text()
    assert "360" in map_drawer_text and "map.1" in map_drawer_text, map_drawer_text[:1200]
    assert "script.BichonProvince.BorderVillage.Transport" in map_drawer_text, map_drawer_text[:1200]
    assert "Maps/0.map" in map_drawer_text and "10 × 10 · 1 · 1" in map_drawer_text, map_drawer_text[:1200]
    page.click('[data-action="close-drawer"]')

    page.goto(f"{web}/?api={api}#/content/monsters", wait_until="networkidle")
    page.wait_for_selector("#content-table tbody tr.content-row", timeout=15000)
    page.locator("#content-table tbody tr.content-row").first.click()
    page.wait_for_function("document.querySelector('#live-drawer-relations')?.innerText.includes('真实掉落关系')", timeout=15000)
    monster_drawer_text = page.locator("#live-drawer-relations").inner_text()
    assert "GreenPoison" in monster_drawer_text and "drop.monster.1" in monster_drawer_text.lower(), monster_drawer_text[:1200]
    page.click('[data-action="close-drawer"]')

    page.goto(f"{web}/?api={api}#/content/recipes", wait_until="networkidle")
    page.wait_for_selector("#content-table tbody tr.content-row", timeout=15000)
    recipe_table_text = page.locator("#content-table tbody").inner_text()
    assert "GreenPoison" in recipe_table_text, recipe_table_text[:1200]
    page.locator("#content-table tbody tr.content-row").first.click()
    page.wait_for_function("document.querySelector('#live-drawer-relations')?.innerText.includes('配方产出与材料')", timeout=15000)
    recipe_drawer_text = page.locator("#live-drawer-relations").inner_text()
    assert "item.710" in recipe_drawer_text and "Taoist" in recipe_drawer_text, recipe_drawer_text[:1200]
    page.click('[data-action="close-drawer"]')

    page.goto(f"{web}/?api={api}#/content/buffs", wait_until="networkidle")
    page.wait_for_selector("#content-table tbody tr.content-row", timeout=15000)
    buff_table_text = page.locator("#content-table tbody").inner_text()
    assert "TemporalFlux" in buff_table_text and "ResetDuration" in buff_table_text, buff_table_text[:1200]

    page.goto(f"{web}/?api={api}#/content/npc-scripts", wait_until="networkidle")
    page.wait_for_selector("#content-table tbody tr.content-row", timeout=15000)
    script_table_text = page.locator("#content-table tbody").inner_text()
    assert "BorderVillage/Transport" in script_table_text, script_table_text[:1200]
    page.locator("#content-table tbody tr.content-row").first.click()
    page.wait_for_function("document.querySelector('#live-drawer-relations')?.innerText.includes('AST 与编译校验')", timeout=15000)
    script_drawer_text = page.locator(".drawer").inner_text()
    assert "script.BichonProvince.BorderVillage.Transport" in script_drawer_text and "@tele" in script_drawer_text, script_drawer_text[:1200]
    assert "11 / 26" in script_drawer_text and "0 / 1" in script_drawer_text, script_drawer_text[:1200]
    assert "c0e3f43676a6d5bd978d57c2586636efa404e17fd84014102a3d7d377ecdf8ff" in script_drawer_text, script_drawer_text[:1200]
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
    assert auditor_page.locator('[data-action="open-item-editor-new"]').count() == 0
    assert auditor_page.locator('[data-action="open-modal"][data-modal="create-draft"]').count() == 0
    auditor_page.locator("#content-table tbody tr.content-row").first.click()
    auditor_page.wait_for_selector(".drawer", timeout=10000)
    assert auditor_page.locator("#item-editor").count() == 0
    assert auditor_page.locator('[data-action="editor-save"]').count() == 0

    assert not errors, errors
    assert not failed, failed
    assert not finance_errors, finance_errors
    assert not auditor_errors, auditor_errors
    print("Control Web integration verified: login, live overview, import runs, content query/search, live map/monster/recipe/script/asset relations, script AST validation, asset download/hash, buffs, live release create/validate/compile/sign/publish/manifest/rollback, read-only release permissions, accounts, roles, token refresh, audit export, RBAC 403, logout, CORS.")
    browser.close()
PY
