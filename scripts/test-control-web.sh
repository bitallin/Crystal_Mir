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
"$DOTNET" build src/Crystal.Control.Api/Crystal.Control.Api.csproj -c "$CONFIGURATION" --nologo >/dev/null

ASPNETCORE_ENVIRONMENT=Production \
ASPNETCORE_URLS="$BASE" \
ControlApi__DatabaseConnectionString="$DSN" \
ControlApi__JwtSigningKey="control-web-test-signing-key-0123456789abcdef" \
ControlApi__AuthRateLimitPermitLimit=1000 \
ControlApi__ApiRateLimitPermitLimit=5000 \
ControlApi__CorsOrigins="$WEB" \
ControlApi__BootstrapAdminPassword="$ADMIN_PASSWORD" \
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
import sys
from pathlib import Path
from playwright.sync_api import sync_playwright

web, api, password, finance_user, finance_password, ui_user, ui_password = sys.argv[1:8]
login_url = f"{web}/?api={api}#/login"

with sync_playwright() as playwright:
    browser = playwright.chromium.launch()
    page = browser.new_page(viewport={"width": 1440, "height": 1000})
    errors = []
    failed = []
    page.on("pageerror", lambda exc: errors.append(str(exc)))
    page.on("console", lambda msg: errors.append(f"{msg.type}:{msg.text}") if msg.type == "error" and "401" not in msg.text else None)
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

    page.goto(f"{web}/?api={api}#/content-overview", wait_until="networkidle")
    page.wait_for_function("document.querySelector('.main-content').innerText.includes('9,735')", timeout=15000)
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

    page.goto(f"{web}/?api={api}#/content/maps", wait_until="networkidle")
    page.wait_for_selector("#content-table tbody tr.content-row", timeout=15000)
    page.locator("#content-table tbody tr.content-row").first.click()
    page.wait_for_function("document.querySelector('#live-drawer-relations')?.innerText.includes('真实世界关系')", timeout=15000)
    map_drawer_text = page.locator("#live-drawer-relations").inner_text()
    assert "360" in map_drawer_text and "map.1" in map_drawer_text, map_drawer_text[:1200]
    page.click('[data-action="close-drawer"]')

    page.goto(f"{web}/?api={api}#/content/monsters", wait_until="networkidle")
    page.wait_for_selector("#content-table tbody tr.content-row", timeout=15000)
    page.locator("#content-table tbody tr.content-row").first.click()
    page.wait_for_function("document.querySelector('#live-drawer-relations')?.innerText.includes('真实掉落关系')", timeout=15000)
    monster_drawer_text = page.locator("#live-drawer-relations").inner_text()
    assert "GreenPoison" in monster_drawer_text and "drop.monster.1" in monster_drawer_text.lower(), monster_drawer_text[:1200]
    page.click('[data-action="close-drawer"]')

    page.goto(f"{web}/?api={api}#/world-map", wait_until="networkidle")
    page.wait_for_selector("#live-relation-panel", timeout=15000)
    page.wait_for_function("document.querySelectorAll('#live-map-select option').length === 463", timeout=15000)
    page.wait_for_function("document.querySelector('#live-relation-panel').innerText.includes('72 / 6 / 46 / 360')", timeout=15000)
    relation_text = page.locator("#live-relation-panel").inner_text()
    assert "463 / 463" in relation_text, relation_text[:1200]
    assert "BichonProvince" in relation_text, relation_text[:1200]
    assert "72 / 6 / 46 / 360" in relation_text, relation_text[:1200]
    page.locator('#live-relation-data [data-action="open-live-drops"]').first.click()
    page.wait_for_function("document.getElementById('live-drop-body')?.innerText.includes('掉落路径')", timeout=15000)
    page.click('[data-action="close-modal"]')
    page.select_option("#live-map-select", "300")
    page.wait_for_function("document.querySelector('#live-relation-panel').innerText.includes('map.300')", timeout=15000)
    page.fill("#live-map-search", "BichonProvince")
    page.wait_for_function("document.querySelectorAll('#live-map-select option').length > 0 && document.querySelectorAll('#live-map-select option').length < 463", timeout=15000)
    assert page.locator('#live-map-select option[value="1"]').count() == 1

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
    finance_page.on("console", lambda msg: finance_errors.append(f"{msg.type}:{msg.text}") if msg.type == "error" and "401" not in msg.text else None)
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

    assert not errors, errors
    assert not failed, failed
    assert not finance_errors, finance_errors
    print("Control Web integration verified: login, live overview, import runs, content query/search, live relations, accounts, roles, token refresh, audit export, RBAC 403, logout, CORS.")
    browser.close()
PY
