(function () {
  const D = window.MirData;
  const C = window.MirContentTypes;
  const W = window.MirWorld;
  const I = window.MirIcons;
  const API = window.MirControlApi;
  const app = document.getElementById("app");
  const modalRoot = document.getElementById("modal-root");
  const toastRoot = document.getElementById("toast-root");

  const CONTENT_BY_ID = Object.fromEntries(C.map(type => [type.id, type]));
  const ZONE_BY_ID = Object.fromEntries(D.ZONES.map(zone => [zone.id, zone]));
  const MAP_BY_KEY = Object.fromEntries(W.MAPS.map(map => [map.key, map]));
  const SPAWN_BY_ID = Object.fromEntries(W.SPAWNS.map(spawn => [spawn.id, spawn]));
  const DROP_BY_KEY = Object.fromEntries(W.DROP_GROUPS.map(group => [group.key, group]));
  const NPC_POINT_BY_KEY = Object.fromEntries(W.NPC_POINTS.map(point => [point.npcKey, point]));
  const state = {
    route: "dashboard",
    sidebarOpen: false,
    collapsedGroups: new Set(),
    spotlightOpen: false,
    selectedContent: null,
    selectedZone: null,
    selectedWorldMap: "3.map",
    relationScenario: "zuma",
    selectedRoleCode: null,
    pendingConfirm: null,
    apiStatus: "demo",
    apiError: "",
    user: null,
    live: emptyLiveState(),
    contentPages: Object.create(null),
    contentQueries: Object.create(null),
    focusContentSearch: null,
    contextPromise: null,
    inflight: new Set(),
    failed: new Set()
  };

  function emptyLiveState() {
    return {
      runs: null,
      run: null,
      counts: null,
      content: Object.create(null),
      audit: null,
      accounts: null,
      roles: null,
      relations: {
        catalog: null,
        catalogTotal: 0,
        catalogQuery: "",
        catalogLoading: false,
        catalogPromise: null,
        selectedMapIndex: null,
        response: null,
        loadedKey: null,
        inflightKey: null,
        error: null,
        loading: false,
        drops: Object.create(null)
      }
    };
  }

  const LIVE_ENTITY_TYPES = new Set(["item", "monster", "map", "npc", "quest", "magic", "gameshop", "conquest", "dragon", "respawn", "drop", "recipe", "buff", "script", "asset"]);

  const ROUTE_PERMISSIONS = Object.freeze({
    "content-overview": "content:read",
    "world-map": "content:read",
    "content-relations": "content:read",
    "content/scripts": "content:read",
    "import-runs": "content:read",
    "content-releases": "release:read",
    "assets": "asset:read",
    "zones": "zone:read",
    "zone-templates": "zone:read",
    "gm": "gm:operate",
    "announcements": "zone:read",
    "merges": "zone:merge",
    "players": "zone:read",
    "orders": "payment:read",
    "reconciliation": "payment:read",
    "accounts": "account:read",
    "roles": "role:read",
    "audit": "audit:read",
    "settings": "system:configure"
  });

  const ROLE_LABELS = Object.freeze({
    platform_admin: "平台管理员",
    content_author: "版本作者",
    release_approver: "发布审核",
    gm: "GM",
    finance: "财务",
    auditor: "审计"
  });

  const PERMISSION_RESOURCES = Object.freeze({
    account: "账号",
    role: "角色",
    audit: "审计",
    content: "内容",
    release: "发布",
    asset: "资源",
    zone: "区服",
    payment: "支付",
    gm: "GM",
    system: "系统"
  });

  const PERMISSION_ACTIONS = Object.freeze([
    ["read", "查看", false],
    ["write", "编辑", false],
    ["publish", "发布", true],
    ["rollback", "回滚", true],
    ["merge", "合区", true],
    ["refund", "退款", true],
    ["operate", "执行", true],
    ["configure", "配置", true]
  ]);

  const META = {
    dashboard: { title: "运营驾驶舱", parent: "总览", icon: "home" },
    "prototype-map": { title: "后台原型地图", parent: "总览", icon: "flow" },
    "content-overview": { title: "内容总览", parent: "内容中心", icon: "layers" },
    "content/scripts": { title: "脚本工作台", parent: "内容中心", icon: "code" },
    "world-map": { title: "地图拓扑与坐标", parent: "内容中心", icon: "mapPin" },
    "content-relations": { title: "世界关系图谱", parent: "内容中心", icon: "flow" },
    "import-runs": { title: "导入批次", parent: "内容中心", icon: "upload" },
    "content-releases": { title: "发布中心", parent: "内容中心", icon: "rocket" },
    assets: { title: "资源库", parent: "内容中心", icon: "image" },
    zones: { title: "区服总览", parent: "区服运营", icon: "server" },
    "zone-templates": { title: "开区模板", parent: "区服运营", icon: "copy" },
    gm: { title: "GM 工作台", parent: "区服运营", icon: "command" },
    announcements: { title: "公告与活动", parent: "区服运营", icon: "megaphone" },
    merges: { title: "合区中心", parent: "区服运营", icon: "merge" },
    players: { title: "玩家查询", parent: "玩家与交易", icon: "users" },
    orders: { title: "订单与发货", parent: "玩家与交易", icon: "receipt" },
    reconciliation: { title: "财务对账", parent: "玩家与交易", icon: "scale" },
    approvals: { title: "审批中心", parent: "权限与安全", icon: "check" },
    accounts: { title: "管理账号", parent: "权限与安全", icon: "user" },
    roles: { title: "角色权限", parent: "权限与安全", icon: "shield" },
    audit: { title: "审计日志", parent: "权限与安全", icon: "audit" },
    monitoring: { title: "运行监控", parent: "平台", icon: "activity" },
    jobs: { title: "任务中心", parent: "平台", icon: "queue" },
    licenses: { title: "许可与合规", parent: "平台", icon: "book" },
    settings: { title: "系统设置", parent: "平台", icon: "settings" },
    login: { title: "登录", parent: "MIR CONTROL", icon: "login" }
  };

  const STATUS = {
    published: ["已发布", "success"], draft: ["草稿", "neutral"], validating: ["校验中", "warning"],
    signed: ["已签名", "violet"], active: ["生效中", "success"], archived: ["已归档", "neutral"],
    rolled_back: ["已回滚", "danger"], failed: ["失败", "danger"], imported: ["已导入", "success"],
    running: ["执行中", "info"], waiting: ["等待中", "neutral"], preflight: ["预检中", "warning"],
    completed: ["已完成", "success"], scheduled: ["已排期", "info"], open: ["开放", "success"],
    maintenance: ["维护中", "warning"], warming: ["预热中", "info"], delivering: ["发货中", "info"],
    delivered: ["已发货", "success"], paid: ["已支付", "info"], refunded: ["已退款", "violet"],
    success: ["成功", "success"], pending: ["待处理", "warning"], denied: ["已拒绝", "danger"],
    locked: ["已锁定", "danger"], online: ["在线", "success"], away: ["暂离", "warning"],
    up: ["正常", "success"], warn: ["注意", "warning"], down: ["异常", "danger"],
    ready: ["就绪", "success"], warning: ["待确认", "warning"], danger: ["高风险", "danger"],
    low: ["低", "success"], medium: ["中", "warning"], high: ["高", "danger"]
  };

  function isLive() {
    return state.apiStatus === "live" && Boolean(state.user);
  }

  function currentUser() {
    return state.user || null;
  }

  function displayName() {
    return currentUser()?.displayName || "林策";
  }

  function displayRole() {
    const roles = currentUser()?.roles;
    return roles && roles.length ? roles.join(" · ") : "平台管理员";
  }

  function displayInitials() {
    const name = displayName();
    return name.slice(0, 2).toUpperCase();
  }

  function formatDateTime(value) {
    if (!value) return "—";
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return String(value);
    return new Intl.DateTimeFormat("zh-CN", {
      month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit", hour12: false
    }).format(date);
  }

  function formatNumber(value) {
    const number = Number(value);
    return Number.isFinite(number) ? number.toLocaleString("zh-CN") : "—";
  }

  function apiBadge() {
    const live = isLive();
    const tone = live ? "success" : state.apiStatus === "degraded" ? "warning" : "neutral";
    const label = live ? "实时 API" : state.apiStatus === "degraded" ? "API 降级" : "演示数据";
    return `<span class="api-badge ${tone}" title="${esc(API.baseUrl() || location.origin)}">${I("server", 11)}${esc(label)}</span>`;
  }

  function esc(value) {
    return String(value ?? "")
      .replaceAll("&", "&amp;")
      .replaceAll("<", "&lt;")
      .replaceAll(">", "&gt;")
      .replaceAll('"', "&quot;")
      .replaceAll("'", "&#039;");
  }

  function status(value, label) {
    const meta = STATUS[value] || [value || "未知", "neutral"];
    return `<span class="status ${meta[1]}">${esc(label || meta[0])}</span>`;
  }

  function routeFromHash() {
    return (location.hash.replace(/^#\/?/, "") || "dashboard").split("?")[0];
  }

  function navigate(route) {
    if (!route) return;
    closeOverlays();
    if (routeFromHash() === route) {
      render();
      return;
    }
    location.hash = `#/${route}`;
  }

  function pageMeta(route) {
    if (route.startsWith("content/")) {
      const id = route.split("/")[1];
      const type = CONTENT_BY_ID[id];
      if (type) return { title: type.label, parent: "内容中心", icon: type.icon };
    }
    if (route.startsWith("zone/")) {
      const zone = ZONE_BY_ID[route.split("/")[1]] || D.ZONES[0];
      return { title: zone.name, parent: "区服运营", icon: "server" };
    }
    return META[route] || { title: "页面未找到", parent: "MIR CONTROL", icon: "help" };
  }

  function pageIndex() {
    const pages = [];
    const seen = new Set();
    const add = (route, label, parent, icon) => {
      if (seen.has(route)) return;
      seen.add(route);
      pages.push({ route, label, parent, icon });
    };
    D.NAV.forEach(group => group.items.forEach(item => add(item.id, item.label, group.label, item.icon)));
    C.forEach(type => add(`content/${type.id}`, type.label, "内容中心", type.icon));
    add("login", "登录页", "认证", "login");
    return pages;
  }

  const PAGE_INDEX = pageIndex();

  function pageHeader({ title, description, eyebrow = "MIR CONTROL", actions = "" }) {
    return `
      <header class="page-header">
        <div class="page-heading">
          <div class="eyebrow">${esc(eyebrow)}</div>
          <h1 class="page-title">${esc(title)}</h1>
          ${description ? `<p class="page-description">${description}</p>` : ""}
        </div>
        ${actions ? `<div class="page-actions">${actions}</div>` : ""}
      </header>`;
  }

  function btn(label, iconName, cls = "btn-secondary", attrs = "") {
    return `<button class="btn ${cls}" ${attrs}>${iconName ? I(iconName, 14) : ""}<span>${esc(label)}</span></button>`;
  }

  function linkBtn(label, route, iconName, cls = "btn-secondary") {
    return `<a class="btn ${cls}" href="#/${route}">${iconName ? I(iconName, 14) : ""}<span>${esc(label)}</span></a>`;
  }

  function iconButton(iconName, label, attrs = "") {
    return `<button class="icon-btn" title="${esc(label)}" aria-label="${esc(label)}" ${attrs}>${I(iconName, 16)}</button>`;
  }

  function kpi({ label, value, unit = "", icon: iconName, tone = "", trend, meta }) {
    return `
      <article class="card kpi-card">
        <div class="kpi-top">
          <span class="kpi-label">${esc(label)}</span>
          <span class="kpi-icon ${tone}">${I(iconName, 15)}</span>
        </div>
        <div class="kpi-value">${value}${unit ? `<span class="unit">${esc(unit)}</span>` : ""}</div>
        <div class="kpi-meta">
          ${trend ? `<span class="trend ${trend.cls || "up"}">${I(trend.icon || "arrowUp", 9)}${esc(trend.text)}</span>` : ""}
          ${meta ? `<span>${esc(meta)}</span>` : ""}
        </div>
      </article>`;
  }

  function emptyState(title, desc, iconName = "box") {
    return `<div class="empty-state"><div><div class="empty-icon">${I(iconName, 21)}</div><strong>${esc(title)}</strong><span>${esc(desc)}</span></div></div>`;
  }

  function progress(value, tone = "") {
    return `<div class="progress ${tone}"><span style="width:${Math.max(0, Math.min(100, value))}%"></span></div>`;
  }

  function avatar(name, cls = "") {
    const initials = String(name || "?").slice(0, 2).toUpperCase();
    return `<span class="avatar ${cls}">${esc(initials)}</span>`;
  }

  function lineChart(values, options = {}) {
    const id = options.id || "chart";
    const width = 720;
    const height = 210;
    const pad = { l: 31, r: 12, t: 14, b: 26 };
    const max = Math.max(...values) * 1.15;
    const min = Math.min(...values) * .72;
    const span = Math.max(1, max - min);
    const x = i => pad.l + (i / (values.length - 1)) * (width - pad.l - pad.r);
    const y = v => pad.t + (1 - (v - min) / span) * (height - pad.t - pad.b);
    const points = values.map((value, index) => [x(index), y(value)]);
    const line = points.map((point, index) => `${index === 0 ? "M" : "L"}${point[0].toFixed(1)} ${point[1].toFixed(1)}`).join(" ");
    const area = `${line} L${points[points.length - 1][0].toFixed(1)} ${height - pad.b} L${points[0][0].toFixed(1)} ${height - pad.b} Z`;
    const grid = [0, 1, 2, 3].map(i => {
      const gy = pad.t + (i / 3) * (height - pad.t - pad.b);
      const label = Math.round(max - (i / 3) * span);
      return `<line class="viz-grid" x1="${pad.l}" x2="${width - pad.r}" y1="${gy}" y2="${gy}"/><text class="viz-label" x="${pad.l - 8}" y="${gy + 3}" text-anchor="end">${label}</text>`;
    }).join("");
    const xLabels = [0, 6, 12, 18, values.length - 1].map(index => `<text class="viz-label" x="${x(index)}" y="${height - 7}" text-anchor="middle">${options.labels?.[index] ?? index}</text>`).join("");
    const stroke = options.color === "blue" ? "viz-line blue" : "viz-line";
    const stops = options.color === "blue" ? ["#3977d5", "#3977d5"] : ["#c58b32", "#e1b361"];
    return `
      <svg class="line-chart" viewBox="0 0 ${width} ${height}" role="img" aria-label="${esc(options.label || "趋势图")}">
        <defs><linearGradient id="area-${id}" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="${stops[0]}" stop-opacity=".2"/><stop offset="1" stop-color="${stops[1]}" stop-opacity="0"/></linearGradient></defs>
        ${grid}
        <path d="${area}" fill="url(#area-${id})"/>
        <path class="${stroke}" d="${line}"/>
        ${points.filter((_, index) => index % 4 === 0).map(point => `<circle class="viz-dot" cx="${point[0]}" cy="${point[1]}" r="2.5"/>`).join("")}
        ${xLabels}
      </svg>`;
  }

  function barChart(values, labels = []) {
    const max = Math.max(...values);
    return `<div class="bar-chart">${values.map((value, index) => `<div class="bar-item"><span>${esc(labels[index] || "")}</span><div class="bar" style="height:${Math.max(3, (value / max) * 100)}%"></div></div>`).join("")}</div>`;
  }

  function sidebar() {
    const navGroups = D.NAV
      .map(group => ({ ...group, items: group.items.filter(item => canAccessRoute(item.id)) }))
      .filter(group => group.items.length > 0);
    return `
      <aside class="sidebar ${state.sidebarOpen ? "open" : ""}">
        <div class="sidebar-brand">
          <div class="brand-mark">M</div>
          <div class="brand-copy"><strong>MIR CONTROL</strong><span>CRYSTAL ADMIN CONSOLE</span></div>
        </div>
        <div class="environment-pill"><span><i class="env-dot"></i>${apiBadge()}</span><strong>PROD · CN</strong></div>
        <nav class="nav-scroll" aria-label="主导航">
          ${navGroups.map(group => {
            const collapsed = state.collapsedGroups.has(group.label);
            return `
              <section class="nav-group ${collapsed ? "collapsed" : ""}">
                <button class="nav-group-header" data-action="toggle-group" data-group="${esc(group.label)}">
                  <span>${esc(group.label)}</span><span class="nav-group-chevron">${I("chevronDown", 12)}</span>
                </button>
                <div class="nav-items">
                  ${group.items.map(item => `<button class="nav-item ${isActive(item.id) ? "active" : ""}" data-route="${esc(item.id)}" data-nav-item="${esc(item.id)}"><span class="nav-icon">${I(item.icon, 15)}</span><span class="nav-label">${esc(item.label)}</span>${item.badge ? `<span class="nav-badge ${item.badgeClass || ""}">${esc(item.badge)}</span>` : ""}</button>`).join("")}
                </div>
              </section>`;
          }).join("")}
        </nav>
        <div class="sidebar-foot">
          <div class="user-mini">
            ${avatar(displayInitials())}
            <div class="user-mini-copy"><strong>${esc(displayName())}</strong><span>${esc(displayRole())}${currentUser()?.mfaEnabled ? " · MFA" : ""}</span></div>
            <button class="icon-btn" style="width:28px;height:28px;color:#8093a1" data-action="logout" title="退出登录">${I("logout", 14)}</button>
          </div>
        </div>
      </aside>`;
  }

  function topbar(meta) {
    return `
      <header class="topbar">
        <div class="topbar-left">
          <button class="icon-btn mobile-menu" data-action="toggle-sidebar" aria-label="打开导航">${I("menu", 18)}</button>
          <div class="breadcrumb"><span class="crumb-parent">${esc(meta.parent)}</span><span class="crumb-sep">/</span><strong>${esc(meta.title)}</strong></div>
        </div>
        <button class="topbar-search" data-action="open-spotlight">${I("search", 14)}<span>搜索页面、内容、区服或命令...</span><kbd>⌘ K</kbd></button>
        <div class="topbar-actions">
          ${apiBadge()}
          <button class="icon-btn" data-action="show-notifications" title="通知">${I("bell", 17)}<span class="notification-dot"></span></button>
          <button class="icon-btn" data-action="show-help" title="帮助">${I("help", 17)}</button>
          <div class="topbar-user" data-action="user-menu">
            ${avatar(displayInitials())}
            <div class="topbar-user-copy"><strong>${esc(displayName())}</strong><span>${esc(displayRole())}</span></div>
            ${I("chevronDown", 12)}
          </div>
        </div>
      </header>`;
  }

  function shell(pageHtml, meta) {
    return `<div class="app-shell">${sidebar()}<div class="main-shell">${topbar(meta)}<main class="main-content">${pageHtml}</main></div></div><div class="sidebar-scrim ${state.sidebarOpen ? "show" : ""}" data-action="close-sidebar"></div>`;
  }

  function isActive(route) {
    if (route === "content/skills") return state.route === "content/skills" || state.route === "content/magic";
    return state.route === route;
  }

  function renderDashboard() {
    const actions = `
      ${btn("导入版本", "upload", "btn-secondary", 'data-action="open-modal" data-modal="import"')}
      ${btn("开新区", "server", "btn-secondary", 'data-action="open-modal" data-modal="new-zone"')}
      ${btn("发布内容", "rocket", "btn-primary", 'data-action="navigate" data-route="content-releases"')}`;
    const totalPlayers = D.ZONES.filter(zone => zone.status === "open").reduce((sum, zone) => sum + zone.players, 0);
    return `
      <div class="page">
        ${pageHeader({ title: "运营驾驶舱", description: "从内容生产、版本发布到区服健康和交易发货，在一个视图里掌握平台运行状态。", eyebrow: "PLATFORM OVERVIEW", actions })}
        <section class="kpi-grid">
          ${kpi({ label: "当前在线玩家", value: totalPlayers.toLocaleString(), icon: "users", trend: { text: "12.4%", cls: "up" }, meta: "较昨日同时段" })}
          ${kpi({ label: "运行区服", value: "12", unit: "/ 12", icon: "server", tone: "blue", trend: { text: "全部在线", cls: "up" }, meta: "1 个维护 · 1 个预热" })}
          ${kpi({ label: "今日流水", value: "¥ 186,420", icon: "coins", tone: "green", trend: { text: "8.7%", cls: "up" }, meta: "订单 1,284 笔" })}
          ${kpi({ label: "发布成功率", value: "98.7", unit: "%", icon: "rocket", tone: "violet", trend: { text: "1 次回滚", cls: "down" }, meta: "近 30 天 76 次发布" })}
        </section>
        <section class="grid grid-3 mt-16">
          <article class="card chart-card span-2">
            <div class="card-head">
              <div><h2 class="card-title">在线趋势与活跃峰值</h2><p class="card-subtitle">全部运行区服 · 每小时采样 · 演示数据</p></div>
              <div class="segmented"><button class="active">24 小时</button><button>7 天</button><button>30 天</button></div>
            </div>
            <div class="chart-wrap">${lineChart(D.chartOnline, { id: "dashboard-online", labels: ["00", "06", "12", "18", "23"], label: "在线玩家趋势" })}</div>
            <div class="chart-legend"><span><i class="legend-dot"></i>在线玩家</span><span><i class="legend-dot blue"></i>登录峰值 9,214</span><span><i class="legend-dot green"></i>平均在线 7,863</span></div>
          </article>
          <article class="card">
            <div class="card-head"><div><h2 class="card-title">内容发布流水线</h2><p class="card-subtitle">当前内容版本与待处理环节</p></div>${iconButton("more", "更多")}</div>
            <div class="card-body">
              <div class="pipeline">
                <div class="pipeline-step"><div class="pipeline-label">草稿</div><div class="pipeline-value">18</div></div>
                <div class="pipeline-step"><div class="pipeline-label">校验</div><div class="pipeline-value">6</div></div>
                <div class="pipeline-step"><div class="pipeline-label">已签名</div><div class="pipeline-value">3</div></div>
                <div class="pipeline-step active"><div class="pipeline-label">生效</div><div class="pipeline-value">4</div></div>
                <div class="pipeline-step"><div class="pipeline-label">归档</div><div class="pipeline-value">41</div></div>
              </div>
              <div class="divider"></div>
              <div class="data-list">
                <div class="data-row"><span>当前稳定版本</span><strong>2026.11.09.1</strong></div>
                <div class="data-row"><span>待审核发布</span><strong class="warning-text">1 个 · 四区灰度</strong></div>
                <div class="data-row"><span>最近内容校验</span><strong class="success-text">通过 · 4 分钟前</strong></div>
              </div>
              <a class="btn btn-secondary mt-16" style="width:100%" href="#/content-releases">${I("rocket", 14)}进入发布中心</a>
            </div>
          </article>
        </section>
        <section class="grid grid-3 mt-16">
          <article class="card span-2">
            <div class="card-head"><div><h2 class="card-title">区服健康</h2><p class="card-subtitle">按在线、Tick 延迟和内容版本汇总</p></div><a class="btn btn-ghost btn-sm" href="#/zones">查看全部 ${I("arrowRight", 12)}</a></div>
            <div class="zone-list">
              ${D.ZONES.slice(0, 5).map(zone => `
                <div class="zone-row" data-route="zone/${zone.id}" style="grid-template-columns:1.35fr .75fr .75fr .7fr auto">
                  <div class="zone-name"><span class="zone-mark">${esc(zone.short.slice(0, 1))}</span><div><div class="cell-main">${esc(zone.name)}</div><div class="cell-sub">${esc(zone.region)} · ${esc(zone.id)}</div></div></div>
                  <div class="zone-metric"><strong>${zone.players.toLocaleString()}</strong><span>在线 / ${zone.capacity.toLocaleString()}</span></div>
                  <div class="zone-metric"><strong>${zone.tick} ms</strong><span>Tick P95</span></div>
                  <div class="zone-metric"><strong>${esc(zone.release)}</strong><span>内容版本</span></div>
                  <div>${zone.status === "open" ? `<span class="pill-health">健康</span>` : status(zone.status)}</div>
                </div>`).join("")}
            </div>
          </article>
          <article class="card">
            <div class="card-head"><div><h2 class="card-title">告警与待办</h2><p class="card-subtitle">需要运营或管理员关注</p></div><span class="status danger">2 高优</span></div>
            <div class="card-body alert-list">
              ${D.ALERTS.map(alert => `<div class="alert-item ${alert.level === "danger" ? "danger" : alert.level === "warning" ? "warning" : ""}"><span class="alert-mark" style="color:${alert.level === "danger" ? "var(--red)" : alert.level === "warning" ? "var(--orange)" : "var(--blue)"};background:${alert.level === "danger" ? "var(--red-soft)" : alert.level === "warning" ? "var(--orange-soft)" : "var(--blue-soft)"}">${I(alert.level === "danger" ? "alert" : alert.level === "warning" ? "warning" : "info", 14)}</span><div class="alert-copy"><strong>${esc(alert.title)}</strong><span>${esc(alert.detail)}</span><div class="alert-time">${esc(alert.time)}</div></div></div>`).join("")}
            </div>
          </article>
        </section>
        <section class="grid grid-2 mt-16">
          <article class="card">
            <div class="card-head"><div><h2 class="card-title">待审批事项</h2><p class="card-subtitle">高风险操作需双人复核</p></div><a class="btn btn-ghost btn-sm" href="#/approvals">审批中心 ${I("arrowRight", 12)}</a></div>
            <div class="quick-list">
              ${D.APPROVALS.slice(0, 3).map(item => `<div class="quick-row"><span class="quick-icon" style="color:${item.risk === "high" ? "var(--red)" : "var(--orange)"};background:${item.risk === "high" ? "var(--red-soft)" : "var(--orange-soft)"}">${I(item.type.includes("发布") ? "rocket" : item.type.includes("GM") ? "gift" : item.type.includes("权限") ? "shield" : "scale", 14)}</span><div class="quick-copy"><strong>${esc(item.title)}</strong><span>${esc(item.requester)} · ${esc(item.time)} · ${esc(item.scope)}</span></div><span class="quick-meta">${item.risk === "high" ? status("high") : status("medium")}</span></div>`).join("")}
            </div>
          </article>
          <article class="card">
            <div class="card-head"><div><h2 class="card-title">最近动态</h2><p class="card-subtitle">控制面关键操作流</p></div><a class="btn btn-ghost btn-sm" href="#/audit">审计日志 ${I("arrowRight", 12)}</a></div>
            <div class="card-body timeline">
              ${D.ACTIVITY.map(item => `<div class="timeline-item"><span class="timeline-dot gold">${I(item.icon, 12)}</span><div class="timeline-copy"><strong>${esc(item.title)}</strong><span>${esc(item.detail)}</span><time>${esc(item.time)}</time></div></div>`).join("")}
            </div>
          </article>
        </section>
      </div>`;
  }

  function renderPrototypeMap() {
    const domainData = [
      { title: "总览与设计", icon: "home", desc: "入口、原型地图、平台驾驶舱", pages: D.NAV[0].items },
      { title: "内容中心", icon: "layers", desc: "内容生产、校验、发布、资源", pages: D.NAV[1].items.concat(C.filter(type => !D.NAV[1].items.some(item => item.id === `content/${type.id}`)).map(type => ({ id: `content/${type.id}`, label: type.label, icon: type.icon }))) },
      { title: "区服运营", icon: "server", desc: "区服、模板、GM、活动、合区", pages: D.NAV[2].items },
      { title: "玩家与交易", icon: "users", desc: "玩家、订单、发货、对账", pages: D.NAV[3].items },
      { title: "权限与安全", icon: "shield", desc: "审批、账号、角色、审计", pages: D.NAV[4].items },
      { title: "平台", icon: "settings", desc: "监控、任务、许可、设置", pages: D.NAV[5].items }
    ];
    return `
      <div class="page">
        ${pageHeader({ title: "后台原型地图", description: "完整信息架构、页面清单和关键业务闭环。每一项都可以直接打开对应高保真页面。", eyebrow: "INFORMATION ARCHITECTURE", actions: `${linkBtn("打开登录页", "login", "login")}${btn("查看设计说明", "book", "btn-primary", 'data-action="open-modal" data-modal="prototype-notes"')}` })}
        <section class="grid grid-4">
          ${kpi({ label: "一级功能域", value: "6", icon: "layers", tone: "blue", meta: "总览 / 内容 / 区服 / 玩家 / 安全 / 平台" })}
          ${kpi({ label: "可点击页面", value: String(PAGE_INDEX.length), icon: "monitor", tone: "green", meta: "含 14 个内容域与登录页" })}
          ${kpi({ label: "关键业务闭环", value: "7", icon: "flow", tone: "violet", meta: "编辑、发布、开区、GM、支付、审计、世界关系" })}
          ${kpi({ label: "高风险确认点", value: "9", icon: "shield", tone: "gold", meta: "发布、回滚、合区、补偿、退款" })}
        </section>
        <section class="card mt-16">
          <div class="card-head"><div><h2 class="card-title">功能域与二级页面</h2><p class="card-subtitle">按后台真实使用路径组织，不按数据库表组织</p></div><span class="tag gold">高保真可点击原型</span></div>
          <div class="card-body">
            <div class="ia-map">
              ${domainData.map(domain => `<section class="ia-domain"><div class="ia-domain-head"><span class="ia-domain-icon">${I(domain.icon, 16)}</span><div><strong>${esc(domain.title)}</strong><span>${esc(domain.desc)}</span></div></div><div class="ia-pages">${domain.pages.map(page => `<a class="ia-page" href="#/${esc(page.id)}"><span><i class="dot"></i> ${esc(page.label)}</span>${I("chevronRight", 11)}</a>`).join("")}</div></section>`).join("")}
            </div>
          </div>
        </section>
        <section class="grid grid-3 mt-16">
          <article class="card span-2">
            <div class="card-head"><div><h2 class="card-title">七个关键业务闭环</h2><p class="card-subtitle">原型覆盖的是流程，而不是孤立页面</p></div></div>
            <div class="card-body grid grid-2">
              ${[
                ["内容编辑 → 校验 → 发布", "内容库 → 导入批次 → 校验报告 → 发布中心 → 区服热重载", "layers"],
                ["开区 → 预热 → 开放", "开区模板 → 新区任务 → Agent 注册 → 健康门禁 → 开放登录", "server"],
                ["GM 操作 → 审批 → 审计", "GM 工作台 → 高风险确认 → 审批中心 → 审计日志", "command"],
                ["订单 → 发货 → 对账", "订单与发货 → Outbox 重试 → 财务对账 → 差异处理", "receipt"],
                ["合区 → 预检 → 切换", "合区中心 → 冲突报告 → 冻结备份 → ID 映射 → 回滚窗口", "merge"],
                ["账号 → 角色 → 会话", "管理账号 → 角色权限 → MFA / 会话 → 审计与告警", "shield"],
                ["地图 → 刷怪 → 掉落 → 装备", "地图拓扑 → 刷新点坐标 → 怪物 → 掉落组 → 装备反查 → 发布门禁", "map"]
              ].map(([title, desc, iconName]) => `<div class="quick-row" style="border:1px solid var(--line);border-radius:9px;padding:12px"><span class="quick-icon" style="color:var(--gold);background:var(--gold-soft)">${I(iconName, 15)}</span><div class="quick-copy"><strong>${esc(title)}</strong><span>${esc(desc)}</span></div></div>`).join("")}
            </div>
          </article>
          <article class="card">
            <div class="card-head"><div><h2 class="card-title">原型边界</h2><p class="card-subtitle">当前版本要验证什么</p></div></div>
            <div class="card-body data-list">
              <div class="data-row"><span>数据来源</span><strong>演示数据</strong></div>
              <div class="data-row"><span>目标分辨率</span><strong>1440 × 960+</strong></div>
              <div class="data-row"><span>适配范围</span><strong>桌面 / 平板 / 手机</strong></div>
              <div class="data-row"><span>认证交互</span><strong>原型模拟</strong></div>
              <div class="data-row"><span>真实 API</span><strong class="warning-text">下一阶段接入</strong></div>
            </div>
            <div class="card-foot"><a class="btn btn-secondary" style="width:100%" href="#/dashboard">${I("home", 14)}返回驾驶舱</a></div>
          </article>
        </section>
      </div>`;
  }

  function liveContentCounts() {
    const counts = Object.create(null);
    (state.live.counts?.counts || []).forEach(entry => {
      counts[entry.entityType] = Number(entry.count) || 0;
    });
    return counts;
  }

  function contentDomainCount(type, counts) {
    if (!isLive()) return type.total;
    if (!LIVE_ENTITY_TYPES.has(type.entity)) return type.total;
    return counts[type.entity] ?? 0;
  }

  function contentTypeStrip(activeId) {
    const counts = liveContentCounts();
    return `<div class="content-type-strip">${C.map(type => `<a class="content-type ${type.id === activeId ? "active" : ""}" href="#/content/${type.id}"><span class="content-type-icon">${I(type.icon, 14)}</span><span><strong>${esc(type.label)}</strong><span>${formatNumber(contentDomainCount(type, counts))} 条</span></span></a>`).join("")}</div>`;
  }

  function renderContentOverview() {
    const live = isLive();
    const counts = liveContentCounts();
    const run = state.live.run;
    const hasRun = live && Boolean(run);
    const total = hasRun ? run.entityCount : C.reduce((sum, type) => sum + type.total, 0);
    const drafts = C.reduce((sum, type) => sum + type.draft, 0);
    const issues = C.reduce((sum, type) => sum + type.issues, 0);
    const mirrored = C.filter(type => LIVE_ENTITY_TYPES.has(type.entity)).length;
    const pendingDomains = Math.max(0, C.length - mirrored);
    const totalValue = live ? (hasRun ? formatNumber(total) : "—") : total.toLocaleString();
    const totalMeta = live ? (hasRun ? `批次 #${run.id} · ${formatDateTime(run.startedAt)}` : "正在读取导入批次") : "黄金样本与新增模型";
    const batchLabel = hasRun ? `批次 #${run.id}` : live ? "等待批次" : "批次 #32";
    const hash = hasRun && run.sourceHash ? `${String(run.sourceHash).slice(0, 8)}...` : live ? "—" : "7e7a6dd1...";
    const importedLabel = hasRun ? `#${run.id} · ${formatNumber(run.entityCount)} 实体` : live ? "—" : "#32 · 9,734 实体";
    return `
      <div class="page">
        ${pageHeader({ title: "内容总览", description: "以“运行时内容”为中心组织内容域。现有 MirDB 导入内容与新增业务模型在同一套校验、发布和引用体系下管理。", eyebrow: "CONTENT COMMAND", actions: `${btn("导出内容清单", "download", "btn-secondary", 'data-action="toast" data-message="内容清单导出任务已创建"')}${btn("导入 MirDB", "upload", "btn-primary", 'data-action="open-modal" data-modal="import"')}` })}
        <section class="grid grid-4">
          ${kpi({ label: "内容实体总量", value: totalValue, icon: "package", tone: "blue", meta: totalMeta })}
          ${kpi({ label: "内容域", value: String(C.length), icon: "layers", tone: "green", meta: `${mirrored} 类来自只读批次 / ${pendingDomains} 类待建` })}
          ${kpi({ label: "草稿与待发布", value: live ? "—" : String(drafts), icon: "edit", tone: "gold", meta: live ? "内容写入 API 待接" : "需要版本作者继续处理" })}
          ${kpi({ label: "校验问题", value: live ? "—" : String(issues), icon: "alert", tone: "red", trend: live ? undefined : { text: "2 阻断", cls: "down" }, meta: live ? "校验结果 API 待接" : "发布前必须清零" })}
        </section>
        <section class="mt-16">${contentTypeStrip("")}</section>
        <section class="card mt-16 world-entry-card">
          <div class="card-head"><div><h2 class="card-title">世界关系工作台</h2><p class="card-subtitle">围绕“地图 → 刷怪点 → 怪物 → 掉落组 → 装备”组织内容，避免把地图、怪物和掉落拆成互不相干的数据表。</p></div><span class="tag gold">世界关系已建模</span></div>
          <div class="card-body">
            <div class="world-entry-grid">
              <a class="world-entry" href="#/world-map"><span class="world-entry-icon green">${I("mapPin", 16)}</span><span class="world-entry-copy"><strong>地图拓扑与坐标</strong><span>地图层级、入口/出口、NPC、怪物与人物坐标</span></span><span class="world-entry-meta">${W.MAPS.length} 节点 · ${W.PORTALS.length} 传送 · ${W.SPAWNS.length} 刷怪点</span>${I("arrowRight", 13)}</a>
              <a class="world-entry" href="#/content-relations"><span class="world-entry-icon violet">${I("flow", 16)}</span><span class="world-entry-copy"><strong>世界关系图谱</strong><span>从地图正向展开掉落链，也从装备反查来源与爆率</span></span><span class="world-entry-meta">${W.SCENARIOS.length} 条玩法链 · ${W.DROP_GROUPS.length} 个掉落组</span>${I("arrowRight", 13)}</a>
              <a class="world-entry" href="#/content/drops"><span class="world-entry-icon gold">${I("drop", 16)}</span><span class="world-entry-copy"><strong>掉落与爆率</strong><span>刷新时间、概率、数量、必掉组和期望产出</span></span><span class="world-entry-meta">${W.DROP_GROUPS.length} 组样本 · 3 项待处理</span>${I("arrowRight", 13)}</a>
            </div>
          </div>
        </section>
        <section class="grid grid-3 mt-16">
          <article class="card span-2">
            <div class="card-head"><div><h2 class="card-title">内容域全景</h2><p class="card-subtitle">点击进入对应内容库；批次域显示当前批次，其余域标注原型样本</p></div><span class="status ${live ? "info" : "info"}">${batchLabel}</span></div>
            <div class="card-body">
              <div class="content-domain-grid">
                ${C.map(type => {
                  const domainLive = live && LIVE_ENTITY_TYPES.has(type.entity);
                  return `<a class="domain-card" href="#/content/${type.id}"><div class="domain-head"><span class="domain-icon">${I(type.icon, 15)}</span>${domainLive ? `<span class="status success">已接入</span>` : live ? `<span class="status neutral">待接入</span>` : type.issues ? `<span class="status warning">${type.issues} 问题</span>` : `<span class="status success">健康</span>`}</div><strong>${esc(type.label)}</strong><p>${esc(type.description)}</p><div class="domain-foot"><span>${formatNumber(contentDomainCount(type, counts))} 条</span><span>${domainLive ? "批次快照" : esc(type.source)}</span></div></a>`;
                }).join("")}
              </div>
            </div>
          </article>
          <div class="stack">
            <article class="card">
              <div class="card-head"><div><h2 class="card-title">内容生命周期</h2><p class="card-subtitle">不可变快照与可回滚发布</p></div></div>
              <div class="card-body">
                <div class="pipeline" style="grid-template-columns:1fr 1fr">
                  <div class="pipeline-step active"><div class="pipeline-label">草稿</div><div class="pipeline-value">${live ? "—" : drafts}</div></div>
                  <div class="pipeline-step"><div class="pipeline-label">校验</div><div class="pipeline-value">${live ? "—" : 6}</div></div>
                  <div class="pipeline-step"><div class="pipeline-label">已签名</div><div class="pipeline-value">${live ? "—" : 3}</div></div>
                  <div class="pipeline-step"><div class="pipeline-label">已发布</div><div class="pipeline-value">${live ? "—" : 2}</div></div>
                </div>
                <div class="divider"></div>
                <div class="data-list"><div class="data-row"><span>最近导入</span><strong>${importedLabel}</strong></div><div class="data-row"><span>快照哈希</span><strong class="mono">${esc(hash)}</strong></div><div class="data-row"><span>最近发布</span><strong>${live ? "—" : "2026.11.09.1"}</strong></div></div>
              </div>
              <div class="card-foot"><a class="btn btn-secondary" style="width:100%" href="#/import-runs">${I("upload", 14)}查看导入批次</a></div>
            </article>
            <article class="card">
              <div class="card-head"><div><h2 class="card-title">数据来源缺口</h2><p class="card-subtitle">当前迁移边界</p></div></div>
              <div class="card-body alert-list">
                <div class="alert-item warning"><span class="alert-mark" style="color:var(--orange);background:var(--orange-soft)">${I("warning", 13)}</span><div class="alert-copy"><strong>脚本与资源管线</strong><span>NPC 脚本 AST/校验、资源下载服务和地图尺寸/阻挡统计已接入；图库、音效与客户端补丁资源待扩展。</span></div></div>
                <div class="alert-item warning"><span class="alert-mark" style="color:var(--orange);background:var(--orange-soft)">${I("warning", 13)}</span><div class="alert-copy"><strong>图库素材授权</strong><span>Items / Monsters / Objects 来源待确认。</span></div></div>
              </div>
            </article>
          </div>
        </section>
      </div>`;
  }

  function mapFor(key) {
    return MAP_BY_KEY[key] || null;
  }

  function contentRow(typeId, key) {
    const type = CONTENT_BY_ID[typeId];
    return type?.rows.find(row => String(row.key || row.index) === String(key)) || null;
  }

  function monsterFor(key) {
    return contentRow("monsters", key);
  }

  function itemFor(key) {
    return contentRow("items", key);
  }

  function spawnsForMap(mapKey) {
    return W.SPAWNS.filter(spawn => spawn.mapKey === mapKey);
  }

  function spawnSummaryForMap(mapKey) {
    const spawns = spawnsForMap(mapKey);
    const groups = new Map();
    spawns.forEach(spawn => {
      const key = String(spawn.monsterKey);
      const monster = monsterFor(key);
      let group = groups.get(key);
      if (!group) {
        group = {
          monsterKey: key,
          name: monster?.name || key,
          level: monster?.level ?? "—",
          tier: spawn.tier,
          count: 0,
          points: 0,
          minDelay: spawn.delay,
          maxDelay: spawn.delay,
          maxRandom: spawn.randomDelay,
          statuses: new Set(),
          spawns: []
        };
        groups.set(key, group);
      }
      group.count += spawn.count;
      group.points += 1;
      group.minDelay = Math.min(group.minDelay, spawn.delay);
      group.maxDelay = Math.max(group.maxDelay, spawn.delay);
      group.maxRandom = Math.max(group.maxRandom, spawn.randomDelay);
      group.statuses.add(spawn.status);
      group.spawns.push(spawn);
      if (spawn.tier === "Boss") group.tier = "Boss";
    });
    const totalMonsters = spawns.reduce((sum, spawn) => sum + spawn.count, 0);
    const monsterRows = Array.from(groups.values())
      .map(group => ({
        ...group,
        share: totalMonsters ? group.count / totalMonsters * 100 : 0,
        status: group.statuses.has("validating") ? "validating" : group.statuses.has("draft") ? "draft" : "published"
      }))
      .sort((a, b) => Number(b.tier === "Boss") - Number(a.tier === "Boss") || b.count - a.count);
    return {
      spawns,
      totalMonsters,
      monsterTypes: monsterRows.length,
      bossPoints: spawns.filter(spawn => spawn.tier === "Boss").length,
      rows: monsterRows
    };
  }

  function portalsForMap(mapKey) {
    return W.PORTALS.filter(portal => portal.from === mapKey || portal.to === mapKey);
  }

  function npcsForMap(mapKey) {
    return W.NPC_POINTS.filter(point => point.mapKey === mapKey);
  }

  function playersForMap(mapKey) {
    return D.ONLINE_PLAYERS.filter(player => W.ONLINE_COORDS[player.name]?.mapKey === mapKey);
  }

  function mapPointStyle(map, x, y) {
    return `left:${(x / map.size[0] * 100).toFixed(2)}%;top:${(y / map.size[1] * 100).toFixed(2)}%;`;
  }

  function mapToken(kind, map, x, y, label, iconName, attrs = "") {
    return `<button class="map-token ${kind}" style="${mapPointStyle(map, x, y)}" ${attrs} title="${esc(label)}"><span class="map-token-dot">${I(iconName, 11)}</span><span class="map-token-label">${esc(label)}</span></button>`;
  }

  function mapBoard(map) {
    const spawns = spawnsForMap(map.key);
    const npcs = npcsForMap(map.key);
    const portals = W.PORTALS.filter(portal => portal.from === map.key);
    const players = playersForMap(map.key);
    return `
      <div class="map-board ${map.tone || ""}" aria-label="${esc(map.name)} 坐标视图">
        <span class="map-corner map-corner-tl">(0, 0)</span>
        <span class="map-corner map-corner-br">(${map.size[0]}, ${map.size[1]})</span>
        <span class="map-axis map-axis-x">X 坐标 →</span>
        <span class="map-axis map-axis-y">Y 坐标 ↓</span>
        ${(map.safeZones || []).map(zone => mapToken("safe", map, zone.x, zone.y, zone.start ? "安全区 / 出生点" : "安全区", "shield")).join("")}
        ${spawns.map(spawn => {
          const monster = monsterFor(spawn.monsterKey);
          return mapToken(spawn.tier === "Boss" ? "boss" : "spawn", map, spawn.x, spawn.y, `${monster?.name || spawn.monsterKey} ×${spawn.count}`, spawn.tier === "Boss" ? "crown" : "spawn", `data-action="open-content" data-type="respawns" data-key="${esc(spawn.id)}"`);
        }).join("")}
        ${npcs.map(point => mapToken("npc", map, point.x, point.y, point.name, "message", `data-action="open-content" data-type="npcs" data-key="${esc(point.npcKey)}"`)).join("")}
        ${portals.map(portal => mapToken("portal", map, portal.fromPoint[0], portal.fromPoint[1], `→ ${portal.toName}`, "teleport", `data-action="toast" data-message="${esc(portal.kind)}：${esc(portal.fromName)} ${portal.fromPoint.join(",")} → ${esc(portal.toName)} ${portal.toPoint.join(",")}"`)).join("")}
        ${players.map(player => {
          const coord = W.ONLINE_COORDS[player.name];
          return mapToken("player", map, coord.x, coord.y, player.name, "users", `data-action="toast" data-message="${esc(player.name)}：${esc(map.name)} (${coord.x}, ${coord.y}) · ${esc(coord.state)}"`);
        }).join("")}
      </div>`;
  }

  function renderWorldMap() {
    const selected = mapFor(state.selectedWorldMap) || W.MAPS[0];
    const selectedPortals = portalsForMap(selected.key);
    const selectedNpcs = npcsForMap(selected.key);
    const selectedSpawns = spawnsForMap(selected.key);
    const selectedPlayers = playersForMap(selected.key);
    const spawnSummary = spawnSummaryForMap(selected.key);
    const worldMonsterTotal = W.SPAWNS.reduce((sum, spawn) => sum + spawn.count, 0);
    const sameGroup = W.MAPS.filter(map => map.group === selected.group);
    return `
      <div class="page">
        ${pageHeader({ title: "地图拓扑与坐标", description: "把地图层、入口/出口坐标、安全区、NPC 坐标、刷怪点、人物坐标和怪物坐标放在同一个空间视图里。一张地图可以配置多个刷怪点、多种怪物和多组数量，地图不是一张表，而是一组有向连接和可校验的坐标约束。", eyebrow: "WORLD TOPOLOGY", actions: `${btn("批量校验坐标", "play", "btn-secondary", 'data-action="toast" data-message="坐标校验已启动：检查入口/出口成对、边界、刷怪半径和 NPC 冲突"')}${btn("新建地图层", "plus", "btn-primary", 'data-action="open-modal" data-modal="create-map-layer"')}` })}
        <section class="grid grid-4">
          ${kpi({ label: "拓扑地图节点", value: String(W.MAPS.length), icon: "map", tone: "green", meta: "主城 / 野外 / 地下 / Boss / 攻城" })}
          ${kpi({ label: "传送与入口", value: String(W.PORTALS.length), icon: "teleport", tone: "blue", meta: "包含楼梯、洞口、NPC 传送" })}
          ${kpi({ label: "刷怪点样本", value: String(W.SPAWNS.length), icon: "spawn", tone: "gold", meta: `全库合计 ${worldMonsterTotal} 只 · 坐标 / 数量 / 半径 / 延迟` })}
          ${kpi({ label: "NPC 坐标样本", value: String(W.NPC_POINTS.length), icon: "message", tone: "violet", meta: "主城功能与传送节点" })}
        </section>
        ${liveRelationPanel()}
        <section class="map-selector mt-16">
          ${W.MAPS.map(map => { const summary = spawnSummaryForMap(map.key); return `<button class="content-type ${map.key === selected.key ? "active" : ""}" data-action="select-world-map" data-map-key="${esc(map.key)}"><span class="content-type-icon">${I(map.tone === "red" ? "crown" : map.tone === "gold" ? "crown" : map.tone === "blue" ? "compass" : "map", 14)}</span><span><strong>${esc(map.name)}</strong><span>${esc(map.layer)} · ${map.size[0]}×${map.size[1]} · ${summary.totalMonsters} 只</span></span></button>`; }).join("")}
        </section>
        <section class="map-workbench mt-16">
          <article class="card map-stage-card">
            <div class="card-head"><div><h2 class="card-title">${esc(selected.name)} · 坐标视图</h2><p class="card-subtitle">${esc(selected.file)} · ${esc(selected.terrain)} · 点击刷怪点/NPC 直接打开详情</p></div><div class="flex gap-6">${status(selected.status)}<span class="tag mono">${selected.size[0]} × ${selected.size[1]}</span></div></div>
            <div class="card-body">
              ${mapBoard(selected)}
              <div class="map-legend"><span><i class="legend-dot gold"></i>刷怪点</span><span><i class="legend-dot red"></i>Boss</span><span><i class="legend-dot blue"></i>NPC</span><span><i class="legend-dot violet"></i>传送</span><span><i class="legend-dot green"></i>在线人物</span><span><i class="legend-dot"></i>安全区</span></div>
            </div>
          </article>
          <aside class="split-side stack">
            <article class="card">
              <div class="card-head"><div><h2 class="card-title">地图层信息</h2><p class="card-subtitle">运行时约束与资源引用</p></div>${status(selected.status)}</div>
              <div class="card-body data-list">
                <div class="data-row"><span>地图组</span><strong>${esc(selected.group)}</strong></div>
                <div class="data-row"><span>层级</span><strong>${esc(selected.layer)}</strong></div>
                <div class="data-row"><span>坐标范围</span><strong class="mono">0,0 → ${selected.size[0]},${selected.size[1]}</strong></div>
                <div class="data-row"><span>安全区</span><strong>${(selected.safeZones || []).length} 个</strong></div>
                <div class="data-row"><span>NPC</span><strong>${selectedNpcs.length} 个</strong></div><div class="data-row"><span>刷怪点 / 怪物总量</span><strong>${selectedSpawns.length} 个 / ${spawnSummary.totalMonsters} 只</strong></div><div class="data-row"><span>怪物种类</span><strong>${spawnSummary.monsterTypes} 种</strong></div><div class="data-row"><span>Boss 刷新点</span><strong>${spawnSummary.bossPoints} 个</strong></div>
                <div class="data-row"><span>允许 PK</span><strong>${selected.group === "比奇省" || selected.group === "盟重省" ? "否" : "是"}</strong></div>
              </div>
              <div class="card-foot"><button class="btn btn-secondary" style="width:100%" data-action="open-content" data-type="maps" data-key="${esc(selected.key)}">${I("edit", 13)}打开地图内容详情</button></div>
            </article>
            <article class="card">
              <div class="card-head"><div><h2 class="card-title">同一地图组</h2><p class="card-subtitle">层级链路与上下楼关系</p></div><span class="tag">${sameGroup.length} 层</span></div>
              <div class="card-body map-chain">${sameGroup.map(map => { const summary = spawnSummaryForMap(map.key); return `<button class="map-chain-item ${map.key === selected.key ? "active" : ""}" data-action="select-world-map" data-map-key="${esc(map.key)}"><span class="map-chain-icon">${I(map.key === selected.key ? "mapPin" : "map", 13)}</span><span><strong>${esc(map.name)}</strong><small>${esc(map.layer)} · ${map.size[0]}×${map.size[1]} · ${summary.totalMonsters} 只 / ${summary.monsterTypes} 种</small></span>${I("chevronRight", 12)}</button>`; }).join("")}</div>
            </article>
            <article class="card">
              <div class="card-head"><div><h2 class="card-title">坐标校验</h2><p class="card-subtitle">发布前必须成对和边界检查</p></div><span class="status warning">1 待确认</span></div>
              <div class="card-body validation-list">
                <div class="validation-item ok">${I("checkCircle", 12)}<span>入口 / 出口坐标成对</span></div>
                <div class="validation-item ok">${I("checkCircle", 12)}<span>NPC 坐标在边界内</span></div>
                <div class="validation-item ok">${I("checkCircle", 12)}<span>刷怪半径未越界</span></div>
                <div class="validation-item warn">${I("warning", 12)}<span>D3003 传送点等待版本签名</span></div>
              </div>
            </article>
          </aside>
        </section>
        <section class="card mt-16">
          <div class="card-head">
            <div><h2 class="card-title">本图刷怪总览</h2><p class="card-subtitle">一张地图可配置多个刷怪点，同一怪物也可在多个坐标刷新；怪物总量按所有刷新点数量加总。</p></div>
            <div class="flex gap-6"><span class="tag">${spawnSummary.monsterTypes} 种怪物</span><span class="tag gold">${spawnSummary.totalMonsters} 只</span></div>
          </div>
          <div class="card-body">
            <div class="metric-mini-grid spawn-summary-grid">
              <div class="metric-mini"><span>刷怪点</span><strong>${selectedSpawns.length} 个</strong></div>
              <div class="metric-mini"><span>怪物种类</span><strong>${spawnSummary.monsterTypes} 种</strong></div>
              <div class="metric-mini"><span>怪物总量</span><strong>${spawnSummary.totalMonsters} 只</strong></div>
              <div class="metric-mini"><span>Boss 刷新点</span><strong>${spawnSummary.bossPoints} 个</strong></div>
            </div>
            <div class="table-wrap mt-12">
              <table>
                <thead><tr><th>怪物</th><th>等级 / 类型</th><th>数量</th><th>占比</th><th>刷新点</th><th>刷新间隔</th><th>掉落组</th><th>状态</th></tr></thead>
                <tbody>${spawnSummary.rows.map(group => {
                  const groupDrop = W.DROP_GROUPS.find(item => String(item.monsterKey) === group.monsterKey);
                  const delayText = group.minDelay === group.maxDelay ? `${group.maxDelay} 分钟` : `${group.minDelay}-${group.maxDelay} 分钟`;
                  return `<tr><td><button class="row-link" data-action="open-content" data-type="monsters" data-key="${esc(group.monsterKey)}">${esc(group.name)}</button><div class="cell-sub">${group.points} 个刷新点</div></td><td>Lv.${esc(group.level)}<div class="cell-sub">${esc(group.tier)}</div></td><td><span class="cell-main">${group.count} 只</span></td><td><div class="spawn-share">${progress(group.share)}<span class="mono small">${group.share.toFixed(1)}%</span></div></td><td>${group.points} 个</td><td>${delayText}<div class="cell-sub">±${group.maxRandom} 分钟</div></td><td>${groupDrop ? `<button class="row-link" data-action="open-content" data-type="drops" data-key="${esc(groupDrop.key)}">${esc(groupDrop.expected)}</button>` : `<span class="danger-text">未关联</span>`}</td><td>${status(group.status)}</td></tr>`;
                }).join("") || `<tr><td colspan="8"><div class="empty-inline">当前地图没有配置刷怪点，适合主城、安全区或活动地图。</div></td></tr>`}</tbody>
              </table>
            </div>
          </div>
        </section>
        <section class="grid grid-2 mt-16">
          <article class="card">
            <div class="card-head"><div><h2 class="card-title">入口 / 出口坐标</h2><p class="card-subtitle">含地图一层下二层、上下楼、洞口和 NPC 传送</p></div><a class="btn btn-ghost btn-sm" href="#/content/maps">地图库 ${I("arrowRight", 12)}</a></div>
            <div class="table-wrap"><table><thead><tr><th>类型</th><th>来源地图</th><th>入口坐标</th><th></th><th>目标地图</th><th>出口坐标</th><th>触发</th><th>状态</th></tr></thead><tbody>${selectedPortals.map(portal => `<tr><td>${esc(portal.kind)}</td><td><span class="cell-main">${esc(portal.fromName)}</span></td><td class="mono">${portal.fromPoint.join(", ")}</td><td class="muted">→</td><td><span class="cell-main">${esc(portal.toName)}</span></td><td class="mono">${portal.toPoint.join(", ")}</td><td>${esc(portal.trigger)}</td><td>${status(portal.status)}</td></tr>`).join("") || `<tr><td colspan="8"><div class="empty-inline">当前地图暂无出站传送，使用地图组切换查看其他层级。</div></td></tr>`}</tbody></table></div>
          </article>
          <article class="card">
            <div class="card-head"><div><h2 class="card-title">地图规则与玩法</h2><p class="card-subtitle">热血传奇的地图不是一个平面</p></div><span class="tag">Mir2</span></div>
            <div class="card-body map-rule-list">
              <div class="map-rule"><span class="rule-mark gold">01</span><div><strong>主城 / 安全区</strong><p>比奇、盟重、苍月等主城承载商店、仓库、任务和传送，出生点与安全区必须绑定坐标。</p></div></div>
              <div class="map-rule"><span class="rule-mark blue">02</span><div><strong>野外 / 练级区</strong><p>按等级段控制怪物密度与资源产出，野外地图需要关注跨地图入口和回城路径。</p></div></div>
              <div class="map-rule"><span class="rule-mark violet">03</span><div><strong>地下城 / 多层</strong><p>一层到二层不是复制地图，而是有方向的入口/出口坐标、阻挡和刷新点组合。</p></div></div>
              <div class="map-rule"><span class="rule-mark red">04</span><div><strong>Boss 房 / 高价值</strong><p>Boss 刷新时间、入口坐标、掉落组和装备产出必须一起审批和回滚。</p></div></div>
            </div>
          </article>
        </section>
        <section class="grid grid-3 mt-16">
          <article class="card"><div class="card-head"><div><h2 class="card-title">刷怪点明细</h2><p class="card-subtitle">每个点的怪物、坐标、数量和固定/随机刷新时间</p></div><span class="tag gold">${selectedSpawns.length} 点 / ${spawnSummary.totalMonsters} 只</span></div><div class="table-wrap"><table><thead><tr><th>怪物</th><th>坐标</th><th>数量</th><th>间隔</th><th>状态</th></tr></thead><tbody>${selectedSpawns.map(spawn => `<tr><td><button class="row-link" data-action="open-content" data-type="respawns" data-key="${spawn.id}">${esc(monsterFor(spawn.monsterKey)?.name || spawn.monsterKey)}</button></td><td class="mono">${spawn.x}, ${spawn.y}</td><td>${spawn.count}</td><td>${spawn.delay} 分钟 <span class="cell-sub">±${spawn.randomDelay}</span></td><td>${status(spawn.status)}</td></tr>`).join("") || `<tr><td colspan="5"><div class="empty-inline">当前地图暂无刷怪点。</div></td></tr>`}</tbody></table></div></article>
          <article class="card"><div class="card-head"><div><h2 class="card-title">NPC 坐标</h2><p class="card-subtitle">主城功能点与脚本入口</p></div><span class="tag blue">${selectedNpcs.length} 个</span></div><div class="table-wrap"><table><thead><tr><th>NPC</th><th>坐标</th><th>职能</th><th>状态</th></tr></thead><tbody>${selectedNpcs.map(point => `<tr><td><button class="row-link" data-action="open-content" data-type="npcs" data-key="${point.npcKey}">${esc(point.name)}</button></td><td class="mono">${point.x}, ${point.y}</td><td>${esc(point.role)}</td><td>${status(point.status)}</td></tr>`).join("") || `<tr><td colspan="4"><div class="empty-inline">当前地图暂无 NPC 坐标。</div></td></tr>`}</tbody></table></div></article>
          <article class="card"><div class="card-head"><div><h2 class="card-title">在线人物坐标</h2><p class="card-subtitle">GM 定位与异常行为排查</p></div><span class="tag green">${selectedPlayers.length} 人</span></div><div class="table-wrap"><table><thead><tr><th>角色</th><th>坐标</th><th>方向</th><th>状态</th></tr></thead><tbody>${selectedPlayers.map(player => { const coord = W.ONLINE_COORDS[player.name]; return `<tr><td><span class="cell-main">${esc(player.name)}</span><div class="cell-sub">${esc(player.zone)}</div></td><td class="mono">${coord.x}, ${coord.y}</td><td>${esc(coord.direction)}</td><td>${esc(coord.state)}</td></tr>`; }).join("") || `<tr><td colspan="4"><div class="empty-inline">当前地图暂无在线人物样本。</div></td></tr>`}</tbody></table></div></article>
        </section>
      </div>`;
  }

  function relationNode(typeId, key, label, sub, iconName) {
    return `<button class="relation-node" data-action="open-content" data-type="${typeId}" data-key="${esc(key)}"><span class="relation-node-icon">${I(iconName, 16)}</span><strong>${esc(label)}</strong><span>${esc(sub)}</span><i class="relation-node-arrow">${I("chevronRight", 11)}</i></button>`;
  }

  function relationWorldNode(map) {
    const summary = spawnSummaryForMap(map.key);
    return `<button class="relation-node relation-node-map" data-action="open-world-map" data-map-key="${esc(map.key)}"><span class="relation-node-icon">${I("map", 16)}</span><strong>${esc(map.name)}</strong><span>${esc(map.layer)} · ${map.size[0]}×${map.size[1]} · ${summary.totalMonsters} 只 / ${summary.monsterTypes} 种</span><i class="relation-node-arrow">${I("chevronRight", 11)}</i></button>`;
  }

  function renderRelationsPage() {
    const scenario = W.SCENARIOS.find(item => item.key === state.relationScenario) || W.SCENARIOS[0];
    const map = mapFor(scenario.mapKey);
    const spawn = SPAWN_BY_ID[scenario.spawnId];
    const monster = monsterFor(scenario.monsterKey);
    const drop = DROP_BY_KEY[scenario.dropKey];
    const worldMonsterTotal = W.SPAWNS.reduce((sum, item) => sum + item.count, 0);
    const reverseRows = W.DROP_GROUPS.flatMap(group => group.entries.filter(entry => entry.itemKey).map(entry => {
      const spawnRow = W.SPAWNS.find(item => item.monsterKey === group.monsterKey);
      return { ...entry, group, spawnRow, map: spawnRow ? mapFor(spawnRow.mapKey) : null };
    })).filter(row => row.category.includes("武器") || row.category.includes("衣服") || row.category.includes("首饰"));
    return `
      <div class="page">
        ${pageHeader({ title: "世界关系图谱", description: "从地图和刷怪点开始，沿着怪物、掉落组、装备正向展开，也可以从装备反查怪物、地图、坐标和刷新时间。关系不完整时，发布门禁必须阻断。", eyebrow: "WORLD RELATION GRAPH", actions: `${btn("关系校验", "play", "btn-secondary", 'data-action="toast" data-message="关系校验已启动：检查孤立怪物、空掉落组、无效地图坐标和装备无来源"')}${btn("导出关系清单", "download", "btn-primary", 'data-action="toast" data-message="关系清单导出任务已创建"')}` })}
        <section class="grid grid-4">
          ${kpi({ label: "关系链样本", value: String(W.SCENARIOS.length), icon: "flow", tone: "violet", meta: `全库 ${W.SPAWNS.length} 点 / ${worldMonsterTotal} 只 · 地图 → 刷怪 → 怪物 → 掉落` })}
          ${kpi({ label: "掉落组", value: String(W.DROP_GROUPS.length), icon: "drop", tone: "orange", meta: "独立概率 / 必掉组 / 活动池" })}
          ${kpi({ label: "装备产出", value: String(reverseRows.length), icon: "sword", tone: "gold", meta: "武器 / 衣服 / 首饰可反查" })}
          ${kpi({ label: "待处理引用", value: "3", icon: "alert", tone: "red", meta: "未签名地图 / 失效物品 / 阻挡哈希" })}
        </section>
        ${liveRelationPanel()}
        <section class="card mt-16">
          <div class="card-head"><div><h2 class="card-title">选择一条典型玩法链</h2><p class="card-subtitle">切换 Boss / 普通怪 / 任务道具，观察关联页面如何联动</p></div><span class="tag gold">可点击关系节点</span></div>
          <div class="card-body">
            <div class="relation-switcher">${W.SCENARIOS.map(item => `<button class="chip ${item.key === scenario.key ? "active" : ""}" data-action="select-relation" data-scenario="${esc(item.key)}">${esc(item.label)}</button>`).join("")}</div>
            <div class="relation-flow">
              ${relationWorldNode(map)}
              <span class="relation-arrow">${I("arrowRight", 16)}</span>
              ${relationNode("respawns", spawn.id, `刷新点 ${spawn.id}`, `${spawn.count} 只 · (${spawn.x}, ${spawn.y}) · ${spawn.delay} 分钟 ±${spawn.randomDelay}`, "spawn")}
              <span class="relation-arrow">${I("arrowRight", 16)}</span>
              ${relationNode("monsters", String(monster.index), monster.name, `Lv.${monster.level} · ${monster.metric} HP`, "skull")}
              <span class="relation-arrow">${I("arrowRight", 16)}</span>
              ${relationNode("drops", drop.key, drop.key, `${drop.entries.length} 个条目 · ${drop.expected}`, "drop")}
              <span class="relation-arrow">${I("arrowRight", 16)}</span>
              <div class="relation-node relation-node-items"><span class="relation-node-icon">${I("sword", 16)}</span><strong>装备 / 物品产出</strong><div class="relation-item-list">${drop.entries.slice(0, 4).map(entry => `<button data-action="open-content" data-type="items" data-key="${entry.itemKey}">${esc(entry.name)} <span>${esc(entry.chanceText)}</span></button>`).join("")}</div></div>
            </div>
            <div class="relation-note">${I("info", 14)}<span>${esc(scenario.note)}</span></div>
          </div>
        </section>
        <section class="grid grid-2 mt-16">
          <article class="card">
            <div class="card-head"><div><h2 class="card-title">刷怪与掉落链路</h2><p class="card-subtitle">每条链路都必须能回答“在哪刷、刷几只、刷多久、掉什么、掉率多少”</p></div><span class="tag">${W.SPAWNS.length} 个样本</span></div>
            <div class="table-wrap"><table><thead><tr><th>地图 / 坐标</th><th>怪物</th><th>数量 / 刷新</th><th>掉落组</th><th>期望产出</th><th>状态</th></tr></thead><tbody>${W.SPAWNS.map(spawnRow => { const monsterRow = monsterFor(spawnRow.monsterKey); const group = W.DROP_GROUPS.find(item => item.monsterKey === spawnRow.monsterKey); return `<tr><td><span class="cell-main">${esc(mapFor(spawnRow.mapKey)?.name || spawnRow.mapKey)}</span><div class="cell-sub mono">(${spawnRow.x}, ${spawnRow.y}) · 半径 ${spawnRow.spread}</div></td><td>${esc(monsterRow?.name || spawnRow.monsterKey)}</td><td><span class="cell-main">${spawnRow.count} 只</span> <div class="cell-sub">${spawnRow.delay} 分钟 ±${spawnRow.randomDelay}</div></td><td>${group ? `<button class="row-link" data-action="open-content" data-type="drops" data-key="${esc(group.key)}">${esc(group.key)}</button>` : `<span class="danger-text">未关联</span>`}</td><td>${group ? esc(group.expected) : "—"}</td><td>${status(spawnRow.status)}</td></tr>`; }).join("")}</tbody></table></div>
          </article>
          <article class="card">
            <div class="card-head"><div><h2 class="card-title">装备产出反查</h2><p class="card-subtitle">从装备看到来源怪物、地图、坐标、概率和期望击杀</p></div><span class="tag gold">${reverseRows.length} 条</span></div>
            <div class="table-wrap"><table><thead><tr><th>装备</th><th>来源怪物</th><th>地图 / 坐标</th><th>基础概率</th><th>期望击杀</th></tr></thead><tbody>${reverseRows.map(row => `<tr><td><button class="row-link" data-action="open-content" data-type="items" data-key="${row.itemKey}">${esc(row.name)}</button><div class="cell-sub">${esc(row.category)}</div></td><td>${esc(monsterFor(row.group.monsterKey)?.name || row.group.monsterKey)}</td><td><span class="cell-main">${esc(row.map?.name || "—")}</span><div class="cell-sub mono">(${row.spawnRow?.x}, ${row.spawnRow?.y})</div></td><td class="mono">${esc(row.chanceText)}</td><td>≈ ${Math.round(1 / row.chance).toLocaleString()} 次</td></tr>`).join("")}</tbody></table></div>
          </article>
        </section>
        <section class="grid grid-2 mt-16">
          <article class="card"><div class="card-head"><div><h2 class="card-title">关系完整性检查</h2><p class="card-subtitle">内容发布前的图谱门禁</p></div><span class="status warning">3 项待处理</span></div><div class="card-body validation-list"><div class="validation-item ok">${I("checkCircle", 12)}<span>4 条样本链路的地图、刷新点、怪物、掉落组均存在</span></div><div class="validation-item ok">${I("checkCircle", 12)}<span>入口 / 出口坐标成对，且均在边界内</span></div><div class="validation-item warn">${I("warning", 12)}<span>D3003 传送点关联地图尚未签名</span></div><div class="validation-item warn">${I("warning", 12)}<span>drop.stone.tomb 含已删除物品 1308</span></div><div class="validation-item warn">${I("warning", 12)}<span>0150.map 阻挡文件哈希不一致</span></div></div></article>
          <article class="card"><div class="card-head"><div><h2 class="card-title">传奇玩法里的关系</h2><p class="card-subtitle">后台应按运行时语义组织，而不是按数据库表组织</p></div><span class="tag">MIR2</span></div><div class="card-body relation-principles"><div><strong>地图层</strong><span>主城、野外、地下城、Boss 房、攻城地图；每层有尺寸、阻挡、安全区和进入条件。</span></div><div><strong>刷怪点</strong><span>由地图 + 坐标 + 怪物 + 数量 + 范围 + 固定/随机延迟组成，决定经验、材料与装备产出节奏。</span></div><div><strong>怪物与掉落</strong><span>怪物关联掉落组；掉落组再关联物品/装备、概率、数量和必掉组，不能只维护一张怪物表。</span></div><div><strong>装备与产出</strong><span>装备必须可反查来源，支持经济模拟、稀有度、活动倍率和发布影响分析。</span></div><div><strong>人物坐标</strong><span>在线人物坐标用于 GM 定位、异常行为、卡点排查和地图热点分析。</span></div><div><strong>NPC 坐标</strong><span>主城功能、商店、仓库、传送、任务和脚本入口都依赖地图坐标。</span></div></div></article>
        </section>
      </div>`;
  }

  function contentCell(row, column, type) {
    if (column === "name") {
      return `<div class="cell-stack"><span class="cell-icon ${type.tone === "blue" ? "blue" : type.tone === "green" ? "green" : type.tone === "violet" ? "violet" : type.tone === "red" ? "red" : ""}">${I(row.icon || type.icon, 14)}</span><div><div class="cell-main">${esc(row.name)}</div><div class="cell-sub">${esc(row.subtitle || row.key || "")}</div></div></div>`;
    }
    if (column === "status") return status(row.status);
    if (column === "index") return `<span class="mono">${esc(row.index)}</span>`;
    if (column === "key") return `<span class="mono small">${esc(row.keyDisplay ?? row.key)}</span>`;
    if (column === "updated") return `<span class="muted">${esc(row.updated)}</span>`;
    if (column === "metric") return `<span class="strong">${esc(row.metric)}</span>`;
    if (column === "coord") return `<span class="mono small">${esc(row.coord ?? "—")}</span>`;
    return `<span class="${column === "level" ? "mono" : ""}">${esc(row[column] ?? "—")}</span>`;
  }

  function hasPermission(permission) {
    const permissions = currentUser()?.permissions || [];
    return permissions.includes("*") || permissions.includes(permission);
  }

  function routePermission(route) {
    if (route.startsWith("content/")) return "content:read";
    if (route.startsWith("zone/")) return "zone:read";
    return ROUTE_PERMISSIONS[route] || null;
  }

  function canAccessRoute(route) {
    if (!isLive()) return true;
    const permission = routePermission(route);
    return !permission || hasPermission(permission);
  }

  function roleLabel(code) {
    return ROLE_LABELS[code] || code || "—";
  }

  function roleLabelList(roles) {
    return (Array.isArray(roles) ? roles : []).map(roleLabel).join(" · ") || "—";
  }

  function textValue(value, fallback = "—") {
    if (value === null || value === undefined || value === "") return fallback;
    if (typeof value === "object") return fallback;
    return String(value);
  }

  function pointText(point, fallback = "—") {
    if (!point || typeof point !== "object") return fallback;
    const x = point.X ?? point.x;
    const y = point.Y ?? point.y;
    return x === undefined || y === undefined ? fallback : `${x}, ${y}`;
  }

  function arrayCount(value) {
    return Array.isArray(value) ? value.length : 0;
  }

  function entityLabel(entityType) {
    return C.find(type => type.entity === entityType)?.label || entityType;
  }

  function contentQueryState(type) {
    if (!state.contentQueries[type.id]) {
      state.contentQueries[type.id] = { q: "", page: 1, sort: "index", direction: "asc" };
    }
    return state.contentQueries[type.id];
  }

  function contentCacheKey(type) {
    const query = contentQueryState(type);
    const runId = state.live.run?.id || "latest";
    return `${type.entity}:${runId}:${query.page}:${query.q}:${query.sort}:${query.direction}`;
  }

  function attributesOf(entries) {
    return entries
      .filter(([, value]) => value !== null && value !== undefined && value !== "")
      .slice(0, 12)
      .map(([label, value]) => [label, textValue(value)]);
  }

  function mapLiveContent(type, entity) {
    const data = entity.data || {};
    const row = {
      index: entity.localIndex,
      key: entity.contentKey,
      keyDisplay: entity.contentKey,
      name: entity.contentKey,
      subtitle: type.entity,
      category: "—",
      level: "—",
      metric: "—",
      status: "imported",
      updated: formatDateTime(state.live.run?.startedAt),
      icon: type.icon,
      tags: ["MirDB"],
      attributes: []
    };

    if (type.entity === "item") {
      row.name = textValue(data.Name, entity.contentKey);
      row.subtitle = [data.Type, data.RequiredClass].filter(Boolean).join(" · ");
      row.category = textValue(data.Type);
      row.level = textValue(data.RequiredAmount);
      row.metric = data.Price === undefined ? "—" : `${formatNumber(data.Price)} 金币`;
      row.icon = "sword";
      row.attributes = attributesOf([
        ["旧索引", entity.localIndex], ["类型", data.Type], ["需求等级", data.RequiredAmount],
        ["需求职业", data.RequiredClass], ["重量", data.Weight], ["持久", data.Durability],
        ["叠加", data.StackSize], ["售价", data.Price === undefined ? null : `${formatNumber(data.Price)} 金币`],
        ["绑定", data.Bind], ["内容键", entity.contentKey]
      ]);
    } else if (type.entity === "monster") {
      row.name = textValue(data.Name, entity.contentKey);
      row.subtitle = data.IsBoss ? "Boss · 运行时怪物" : "普通 · 运行时怪物";
      row.category = data.IsBoss ? "Boss" : `AI ${textValue(data.AI)}`;
      row.level = textValue(data.Level);
      row.metric = data.Experience === undefined ? "—" : `${formatNumber(data.Experience)} 经验`;
      row.icon = data.IsBoss ? "crown" : "monster";
      row.tags = data.IsBoss ? ["Boss", "MirDB"] : ["怪物", "MirDB"];
      row.attributes = attributesOf([
        ["旧索引", entity.localIndex], ["等级", data.Level], ["经验", data.Experience],
        ["AI", data.AI], ["攻击速度", data.AttackSpeed], ["移动速度", data.MoveSpeed],
        ["Boss", data.IsBoss ? "是" : "否"], ["掉落条目", arrayCount(data.Drops)], ["内容键", entity.contentKey]
      ]);
    } else if (type.entity === "map") {
      row.name = textValue(data.Title || data.FileName, entity.contentKey);
      row.keyDisplay = textValue(data.FileName, entity.contentKey);
      row.subtitle = textValue(data.FileName, entity.contentKey);
      row.category = `${arrayCount(data.SafeZones)} 个安全区`;
      row.level = arrayCount(data.Respawns);
      row.metric = textValue(data.FileName);
      row.icon = "map";
      row.attributes = attributesOf([
        ["旧索引", entity.localIndex], ["文件名", data.FileName], ["标题", data.Title],
        ["安全区", arrayCount(data.SafeZones)], ["传送点", arrayCount(data.Movements)],
        ["刷怪点", arrayCount(data.Respawns)], ["NPC", arrayCount(data.NPCs)], ["内容键", entity.contentKey]
      ]);
    } else if (type.entity === "npc") {
      row.name = textValue(data.Name, entity.contentKey);
      row.subtitle = textValue(data.FileName, entity.contentKey);
      row.category = data.MapIndex === undefined ? "—" : `地图 ${data.MapIndex}`;
      row.coord = pointText(data.Location);
      row.metric = textValue(data.FileName);
      row.icon = "message";
      row.attributes = attributesOf([
        ["旧索引", entity.localIndex], ["脚本", data.FileName], ["地图索引", data.MapIndex],
        ["坐标", pointText(data.Location)], ["刷新率", data.Rate], ["图像", data.Image], ["内容键", entity.contentKey]
      ]);
    } else if (type.entity === "quest") {
      row.name = textValue(data.Name, entity.contentKey);
      row.subtitle = textValue(data.Group || data.Type, entity.contentKey);
      row.category = textValue(data.Group || data.Type);
      row.level = textValue(data.RequiredMinLevel);
      row.metric = `${arrayCount(data.CarryItems) + arrayCount(data.KillTasks) + arrayCount(data.ItemTasks) + arrayCount(data.FlagTasks)} 个目标`;
      row.icon = "file";
      row.attributes = attributesOf([
        ["旧索引", entity.localIndex], ["分组", data.Group], ["类型", data.Type],
        ["最低等级", data.RequiredMinLevel], ["最高等级", data.RequiredMaxLevel], ["职业", data.RequiredClass],
        ["NPC 索引", data.NpcIndex], ["完成 NPC", data.FinishNpcIndex], ["内容键", entity.contentKey]
      ]);
    } else if (type.entity === "magic") {
      row.name = textValue(data.Name, entity.contentKey);
      row.subtitle = `Spell ${textValue(data.Spell)}`;
      row.category = textValue(data.Spell);
      row.level = textValue(data.Level1);
      row.metric = data.DelayBase === undefined ? "—" : `${(Number(data.DelayBase) / 1000).toFixed(1)} 秒`;
      row.icon = "wand";
      row.attributes = attributesOf([
        ["旧索引", entity.localIndex], ["Spell", data.Spell], ["基础消耗", data.BaseCost],
        ["等级消耗", data.LevelCost], ["学习等级", data.Level1], ["范围", data.Range],
        ["基础延迟", data.DelayBase], ["内容键", entity.contentKey]
      ]);
    } else if (type.entity === "gameshop") {
      row.name = textValue(data.Info?.Name || data.Info?.name || `Item ${data.ItemIndex}`, entity.contentKey);
      row.subtitle = textValue(data.Category, "商城条目");
      row.category = textValue(data.Category);
      row.level = textValue(data.Count);
      row.metric = data.GoldPrice === undefined ? "—" : `${formatNumber(data.GoldPrice)} 金币`;
      row.icon = "bag";
      row.attributes = attributesOf([
        ["旧索引", entity.localIndex], ["商品索引", data.ItemIndex], ["分类", data.Category],
        ["金币价", data.GoldPrice], ["元宝价", data.CreditPrice], ["数量", data.Count],
        ["库存", data.Stock], ["内容键", entity.contentKey]
      ]);
    } else if (type.entity === "conquest") {
      row.name = textValue(data.Name, entity.contentKey);
      row.subtitle = textValue(data.Type, "攻城配置");
      row.category = textValue(data.Type);
      row.icon = "crown";
      row.attributes = attributesOf([
        ["旧索引", entity.localIndex], ["名称", data.Name], ["类型", data.Type], ["内容键", entity.contentKey]
      ]);
    } else if (type.entity === "dragon") {
      row.name = "龙族配置";
      row.subtitle = "全局运行时配置";
      row.category = "Dragon";
      row.level = textValue(data.Level);
      row.icon = "sparkles";
      row.attributes = attributesOf([
        ["旧索引", entity.localIndex], ["等级", data.Level], ["内容键", entity.contentKey]
      ]);
    } else if (type.entity === "respawn") {
      row.name = `怪物 ${textValue(data.MonsterIndex)}`;
      row.subtitle = textValue(data.mapFileName || data.mapIndex, entity.contentKey);
      row.category = textValue(data.mapFileName || `地图 ${textValue(data.mapIndex)}`);
      row.coord = pointText(data.Location);
      row.level = textValue(data.Count);
      row.metric = data.Delay === undefined ? "—" : `${data.Delay} 分钟 ±${textValue(data.RandomDelay, "0")}`;
      row.icon = "spawn";
      row.attributes = attributesOf([
        ["旧索引", entity.localIndex], ["怪物索引", data.MonsterIndex], ["地图", data.mapFileName || data.mapIndex],
        ["坐标", pointText(data.Location)], ["数量", data.Count], ["半径", data.Spread],
        ["固定延迟", data.Delay], ["随机延迟", data.RandomDelay], ["内容键", entity.contentKey]
      ]);
    } else if (type.entity === "drop") {
      const dropCount = arrayCount(data.drops);
      row.name = `${textValue(data.monsterName, `怪物 ${data.monsterIndex}`)} · 掉落`;
      row.subtitle = textValue(data.dropPath || data.fileName, entity.contentKey);
      row.category = textValue(data.monsterName || data.monsterIndex);
      row.level = dropCount;
      row.metric = `${dropCount} 条`;
      row.icon = "drop";
      row.tags = ["掉落", "文件内容"];
      row.attributes = attributesOf([
        ["旧索引", entity.localIndex], ["怪物索引", data.monsterIndex], ["怪物", data.monsterName],
        ["掉落路径", data.dropPath], ["文件", data.fileName], ["条目", dropCount], ["内容键", entity.contentKey]
      ]);
    } else if (type.entity === "recipe") {
      const output = data.output || {};
      const ingredients = Array.isArray(data.ingredients) ? data.ingredients : [];
      const tools = Array.isArray(data.tools) ? data.tools : [];
      row.name = textValue(data.name, entity.contentKey);
      row.subtitle = textValue(data.fileName, "外部配方文本");
      row.category = `产出 ${textValue(output.itemName, output.itemIndex)}`;
      row.level = ingredients.length;
      row.metric = `${textValue(output.itemName, "—")} × ${textValue(output.count, "1")}`;
      row.icon = "hammer";
      row.tags = ["配方", "文件内容"];
      row.attributes = attributesOf([
        ["旧索引", entity.localIndex], ["文件", data.fileName],
        ["产出", `${textValue(output.itemName, "—")} × ${textValue(output.count, "—")}`],
        ["材料", ingredients.length], ["工具", tools.length],
        ["成功率", data.chance === undefined ? null : `${data.chance}%`],
        ["金币", data.gold === undefined ? null : formatNumber(data.gold)], ["内容键", entity.contentKey]
      ]);
    } else if (type.entity === "script") {
      const pages = Array.isArray(data.pages) ? data.pages : [];
      const includes = Array.isArray(data.includes) ? data.includes : [];
      row.name = textValue(data.fileName, entity.contentKey);
      row.subtitle = textValue(data.relativePath, entity.contentKey);
      row.category = textValue(data.language, "CrystalNpc");
      row.level = textValue(data.lineCount);
      row.metric = `${pages.length} 个页面`;
      row.icon = "code";
      row.tags = ["脚本", "文件内容"];
      const validation = data.validation || {};
      row.attributes = attributesOf([
        ["本地索引", entity.localIndex], ["文件", data.relativePath], ["语言", data.language],
        ["行数", data.lineCount], ["字节", data.sizeBytes], ["页面", pages.join(" / ")],
        ["包含", includes.length], ["调用", arrayCount(data.calls)],
        ["校验", validation.valid ? "通过" : `${validation.errorCount || 0} 个错误`],
        ["AST SHA256", validation.compiledSha256],
        ["SHA256", data.sha256], ["内容键", entity.contentKey]
      ]);
    } else if (type.entity === "asset") {
      const map = data.map || null;
      row.name = textValue(data.relativePath || data.fileName, entity.contentKey);
      row.subtitle = textValue(data.assetType, "资源文件");
      row.category = textValue(data.assetType);
      row.level = map ? `${map.width} × ${map.height}` : "—";
      row.metric = data.sizeBytes === undefined ? "—" : formatBytes(data.sizeBytes);
      row.icon = data.assetType === "map" ? "map" : "image";
      row.tags = [textValue(data.assetType), map?.valid ? "地图已解析" : "资源元数据"];
      row.attributes = attributesOf([
        ["本地索引", entity.localIndex], ["类型", data.assetType], ["文件", data.relativePath],
        ["大小", data.sizeBytes === undefined ? null : formatBytes(data.sizeBytes)],
        ["地图格式", map?.formatName], ["尺寸", map ? `${map.width} × ${map.height}` : null],
        ["格子", map?.cellCount], ["可走", map?.walkableCells], ["阻挡", map?.blockedCells],
        ["门", map?.doorCells], ["SHA256", data.sha256], ["内容键", entity.contentKey]
      ]);
    } else if (type.entity === "buff") {
      row.name = textValue(data.Name || data.Type, entity.contentKey);
      row.subtitle = [data.Properties, data.StackType].filter(Boolean).join(" · ");
      row.category = textValue(data.StackType);
      row.level = textValue(data.Icon);
      row.metric = data.Visible ? "可见" : "隐藏";
      row.icon = "sparkles";
      row.tags = ["Buff", "运行时"];
      row.attributes = attributesOf([
        ["本地索引", entity.localIndex], ["类型", data.Type], ["叠加", data.StackType],
        ["属性", data.Properties], ["图标", data.Icon], ["可见", data.Visible ? "是" : "否"],
        ["内容键", entity.contentKey]
      ]);
    }

    return row;
  }

  function formatBytes(value) {
    const bytes = Number(value);
    if (!Number.isFinite(bytes) || bytes < 0) return "—";
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }

  function contentView(type) {
    if (!isLive() || !LIVE_ENTITY_TYPES.has(type.entity)) {
      return { rows: type.rows, total: type.total, live: false, loading: false };
    }
    const key = contentCacheKey(type);
    const result = state.live.content[key];
    if (!result) {
      return { rows: [], total: 0, live: true, loading: !state.failed.has(key) };
    }
    if (result.error) {
      return { rows: type.rows, total: type.total, live: false, loading: false, error: result.error };
    }
    return {
      rows: result.items.map(entity => mapLiveContent(type, entity)),
      total: result.total,
      live: true,
      loading: false
    };
  }

  function renderContentPager(type, page, total) {
    const pageSize = 50;
    const pages = Math.max(1, Math.ceil(total / pageSize));
    const current = Math.min(Math.max(1, page), pages);
    const start = total === 0 ? 0 : (current - 1) * pageSize + 1;
    const end = Math.min(current * pageSize, total);
    const buttons = [];
    const first = Math.max(1, Math.min(current - 2, pages - 4));
    const last = Math.min(pages, first + 4);
    for (let index = first; index <= last; index++) {
      buttons.push(`<button class="page-btn ${index === current ? "active" : ""}" data-action="content-page" data-type="${type.id}" data-page="${index}">${index}</button>`);
    }
    return `<span>显示 ${start}–${end}，共 ${formatNumber(total)} 条</span><div class="page-buttons"><button class="page-btn" data-action="content-page" data-type="${type.id}" data-page="${current - 1}" ${current <= 1 ? "disabled" : ""}>${I("chevronLeft", 11)}</button>${buttons.join("")}<button class="page-btn" data-action="content-page" data-type="${type.id}" data-page="${current + 1}" ${current >= pages ? "disabled" : ""}>${I("chevronRight", 11)}</button></div>`;
  }

  async function ensureLiveContext() {
    if (!isLive()) return null;
    if (state.live.runs) return state.live;
    if (!state.contextPromise) {
      state.contextPromise = (async () => {
        const runs = await API.listImportRuns(50);
        state.live.runs = runs;
        state.live.run = runs.find(run => run.status === "imported") || runs[0] || null;
        state.live.counts = state.live.run ? await API.getContentTypes(state.live.run.id) : null;
        return state.live;
      })().finally(() => { state.contextPromise = null; });
    }
    return state.contextPromise;
  }

  function liveRelations() {
    return state.live.relations;
  }

  function liveDropKey(monsterIndex) {
    return `${state.live.run?.id || "latest"}:${monsterIndex}`;
  }

  function liveMapLabel(entity) {
    const data = entity?.data || {};
    const fileName = entity?.fileName ?? data.FileName;
    const title = entity?.title ?? data.Title;
    return `#${entity?.localIndex ?? "—"} · ${textValue(fileName, "unknown")} · ${textValue(title, "未命名")}`;
  }

  function filteredLiveMapCatalog() {
    const catalog = liveRelations().catalog || [];
    const query = liveRelations().catalogQuery.trim().toLowerCase();
    if (!query) return catalog;
    return catalog.filter(item => liveMapLabel(item).toLowerCase().includes(query));
  }

  function liveMapOptions(catalog, selectedMapIndex) {
    if (!catalog.length) return `<option value="" selected disabled>没有匹配的地图</option>`;
    return catalog.map(item => {
      const selected = Number(item.localIndex) === Number(selectedMapIndex) ? " selected" : "";
      return `<option value="${esc(item.localIndex)}"${selected}>${esc(liveMapLabel(item))}</option>`;
    }).join("");
  }

  async function ensureLiveMapCatalog() {
    const relations = liveRelations();
    if (relations.catalog) return relations.catalog;
    if (!relations.catalogPromise) {
      relations.catalogLoading = true;
      relations.error = null;
      relations.catalogPromise = (async () => {
        const items = [];
        let page = 1;
        let total = 0;
        while (true) {
          const payload = await API.listMapCatalog({
            importRunId: state.live.run?.id,
            page,
            pageSize: 500
          });
          const batch = Array.isArray(payload?.items) ? payload.items : [];
          total = Number(payload?.total) || 0;
          items.push(...batch);
          if (!batch.length || items.length >= total || page >= 100) break;
          page += 1;
        }

        relations.catalogTotal = total;
        relations.catalog = items;
        const selectedExists = items.some(item => Number(item.localIndex) === Number(relations.selectedMapIndex));
        if (!selectedExists) {
          const preferred = items.find(item => Number(item.localIndex) === 1) || items[0];
          relations.selectedMapIndex = preferred ? Number(preferred.localIndex) : null;
        }
        return items;
      })().catch(error => {
        relations.error = error;
        throw error;
      }).finally(() => {
        relations.catalogLoading = false;
        relations.catalogPromise = null;
      });
    }
    return relations.catalogPromise;
  }

  async function ensureMapRelations(page = 1) {
    const relations = liveRelations();
    if (relations.selectedMapIndex === null || relations.selectedMapIndex === undefined) {
      await ensureLiveMapCatalog();
    }
    if (relations.selectedMapIndex === null || relations.selectedMapIndex === undefined) return null;

    const mapIndex = Number(relations.selectedMapIndex);
    const key = `${state.live.run?.id || "latest"}:${mapIndex}:${page}`;
    if (relations.loadedKey === key) return relations.response;
    if (relations.inflightKey === key) return null;

    const runId = state.live.run?.id;
    relations.inflightKey = key;
    relations.loading = true;
    relations.error = null;
    if (state.route === "world-map" || state.route === "content-relations") render();

    try {
      const response = await API.getMapRelations(mapIndex, {
        importRunId: runId,
        respawnPage: page,
        respawnPageSize: 50
      });
      if (relations.inflightKey !== key || Number(relations.selectedMapIndex) !== mapIndex) {
        return null;
      }
      relations.response = response;
      relations.loadedKey = key;
      return response;
    } catch (error) {
      if (error?.status === 401) throw error;
      if (relations.inflightKey !== key || Number(relations.selectedMapIndex) !== mapIndex) {
        return null;
      }
      relations.response = null;
      relations.loadedKey = key;
      relations.error = error;
      return null;
    } finally {
      if (relations.inflightKey === key) relations.inflightKey = null;
      if (Number(relations.selectedMapIndex) === mapIndex) {
        relations.loading = false;
        if (state.route === "world-map" || state.route === "content-relations") render();
      }
    }
  }

  function selectLiveMap(mapIndex) {
    const relations = liveRelations();
    relations.selectedMapIndex = Number(mapIndex);
    relations.response = null;
    relations.loadedKey = null;
    relations.error = null;
    relations.drops = Object.create(null);
    void ensureMapRelations(1).catch(handleApiFailure);
  }

  function dropChanceText(chance) {
    const value = Number(chance) || 0;
    return value > 0 ? `1 / ${formatNumber(value)}` : "—";
  }

  function flattenDropRows(drops, depth = 0) {
    const rows = [];
    (Array.isArray(drops) ? drops : []).forEach(drop => {
      const isGroup = Boolean(drop.group);
      rows.push({
        depth,
        name: isGroup
          ? `分组${drop.group.random ? " · 随机" : ""}${drop.group.first ? " · 优先" : ""}`
          : drop.item?.name || (Number(drop.gold) > 0 ? `金币 ${formatNumber(drop.gold)}` : "未解析物品"),
        contentKey: drop.item?.contentKey || "",
        chance: dropChanceText(drop.chance),
        gold: Number(drop.gold) > 0 ? formatNumber(drop.gold) : "—",
        quest: drop.questRequired ? "任务物品" : "—",
        group: isGroup ? `${arrayCount(drop.group.items)} 项` : "—"
      });
      if (isGroup) rows.push(...flattenDropRows(drop.group.items, depth + 1));
    });
    return rows;
  }

  function dropTableHtml(drops) {
    const rows = flattenDropRows(drops);
    if (!rows.length) return `<div class="empty-inline">该怪物没有已导入的掉落文本。</div>`;
    return `<div class="table-wrap"><table><thead><tr><th>物品 / 分组</th><th>概率</th><th>金币</th><th>分组</th><th>约束</th></tr></thead><tbody>${rows.map(row => `<tr><td style="padding-left:${8 + row.depth * 14}px">${row.depth ? `<span class="muted">↳ </span>` : ""}${esc(row.name)}${row.contentKey ? `<div class="cell-sub mono">${esc(row.contentKey)}</div>` : ""}</td><td class="mono">${esc(row.chance)}</td><td class="mono">${esc(row.gold)}</td><td>${esc(row.group)}</td><td>${esc(row.quest)}</td></tr>`).join("")}</tbody></table></div>`;
  }

  function renderLiveDropModal(payload, error) {
    const body = document.getElementById("live-drop-body");
    if (!body) return;
    if (error) {
      body.innerHTML = `<div class="alert-item danger"><span class="alert-mark" style="color:var(--red);background:var(--red-soft)">${I("alert", 13)}</span><div class="alert-copy"><strong>掉落读取失败</strong><span>${esc(error.message || "Control API 请求失败")}</span></div></div>`;
      return;
    }
    body.innerHTML = `<div class="data-list"><div class="data-row"><span>怪物</span><strong>${esc(payload.name || payload.contentKey)}</strong></div><div class="data-row"><span>掉落路径</span><strong class="mono">${esc(payload.dropPath || "—")}</strong></div><div class="data-row"><span>掉落内容键</span><strong class="mono">${esc(payload.dropContentKey || "—")}</strong></div></div><div class="divider"></div>${dropTableHtml(payload.drops)}`;
  }

  async function openLiveDrops(monsterIndex, monsterName) {
    const relations = liveRelations();
    const key = liveDropKey(monsterIndex);
    const cached = relations.drops[key];
    if (cached) {
      modalShell({ title: `${textValue(monsterName, `怪物 ${monsterIndex}`)} · 掉落`, desc: `真实批次 #${state.live.run?.id ?? "—"} · monster/${monsterIndex}`, iconName: "drop", body: `<div id="live-drop-body"></div>`, foot: `${btn("打开怪物内容", "skull", "btn-secondary", `data-action="open-content" data-type="monsters" data-key="${esc(cached.contentKey)}"`)}${btn("关闭", "", "btn-primary", 'data-action="close-modal"')}` });
      renderLiveDropModal(cached);
      return;
    }

    modalShell({ title: `${textValue(monsterName, `怪物 ${monsterIndex}`)} · 掉落`, desc: `正在读取真实批次 #${state.live.run?.id ?? "—"} 的掉落文件`, iconName: "drop", body: `<div id="live-drop-body"><div class="empty-inline">${I("refresh", 14)} 正在读取掉落关系...</div></div>`, foot: `${btn("关闭", "", "btn-primary", 'data-action="close-modal"')}` });
    try {
      const payload = await API.getMonsterDrops(monsterIndex, state.live.run?.id);
      relations.drops[key] = payload;
      renderLiveDropModal(payload);
    } catch (error) {
      if (error?.status === 401) {
        handleApiFailure(error);
        return;
      }
      renderLiveDropModal(null, error);
    }
  }

  function liveRelationPanel() {
    if (!isLive()) return "";
    const relations = liveRelations();
    const runId = state.live.run?.id;
    const catalog = relations.catalog || [];
    const visibleCatalog = filteredLiveMapCatalog();
    const response = relations.response;
    const map = response?.map;

    if (!catalog.length && (relations.catalogLoading || !relations.error)) {
      return `<section class="card mt-16"><div class="card-body"><div class="empty-inline">${I("refresh", 14)} 正在读取批次 #${esc(runId ?? "—")} 的地图关系...</div></div></section>`;
    }
    if (relations.error && !response) {
      return `<section class="card mt-16"><div class="card-body"><div class="alert-item danger"><span class="alert-mark" style="color:var(--red);background:var(--red-soft)">${I("alert", 13)}</span><div class="alert-copy"><strong>真实关系读取失败</strong><span>${esc(relations.error.message || "Control API 请求失败")}</span></div><button class="btn btn-secondary btn-sm" data-action="reload-live-map">${I("refresh", 11)}重试</button></div></div></section>`;
    }
    if (!catalog.length) {
      return `<section class="card mt-16"><div class="card-body"><div class="empty-inline">当前批次没有地图实体。</div></div></section>`;
    }

    const respawns = response?.respawns;
    const pageSize = respawns?.pageSize || 50;
    const total = respawns?.total || 0;
    const page = respawns?.page || 1;
    const pages = Math.max(1, Math.ceil(total / Math.max(1, pageSize)));
    const movements = response?.movements || [];
    const npcs = response?.npcs || [];
    const safeZones = response?.safeZones || [];
    const respawnRows = respawns?.items || [];

    return `
      <section class="card mt-16" id="live-relation-panel">
        <div class="card-head"><div><h2 class="card-title">真实批次关系</h2><p class="card-subtitle">来自 Control API 的地图、传送、安全区、NPC、刷新点和掉落文件，不混用演示数据。</p></div><div class="flex gap-6"><span class="status success">批次 #${esc(runId ?? "—")}</span>${relations.loading ? `<span class="status info">读取中</span>` : ""}</div></div>
        <div class="card-body">
          <div class="grid grid-2">
            <div class="field" style="margin:0"><label>检索地图</label><label class="search-field">${I("search", 13)}<input id="live-map-search" data-live-map-filter value="${esc(relations.catalogQuery)}" placeholder="名称、文件名、内容键或索引"></label></div>
            <div class="field" style="margin:0"><label>选择导入地图（${formatNumber(catalog.length)} / ${formatNumber(relations.catalogTotal)}）</label><select id="live-map-select" data-live-map-select>${liveMapOptions(visibleCatalog, relations.selectedMapIndex)}</select></div>
          </div>
          ${map ? `<div class="data-list mt-12"><div class="data-row"><span>地图</span><strong>${esc(map.title || map.fileName || map.contentKey)}</strong></div><div class="data-row"><span>内容键 / 本地索引</span><strong class="mono">${esc(map.contentKey)} · ${esc(map.localIndex)}</strong></div><div class="data-row"><span>文件名</span><strong class="mono">${esc(map.fileName || "—")}</strong></div><div class="data-row"><span>传送 / 安全区 / NPC / 刷新点</span><strong>${movements.length} / ${safeZones.length} / ${npcs.length} / ${formatNumber(total)}</strong></div></div>` : `<div class="empty-inline">正在读取地图 ${esc(relations.selectedMapIndex ?? "—")} 的关系...</div>`}
        </div>
      </section>
      ${map ? `<section class="grid grid-2 mt-16" id="live-relation-data">
        <article class="card"><div class="card-head"><div><h2 class="card-title">入口、出口与 NPC</h2><p class="card-subtitle">按地图本地索引关联 MovementInfo 和 NPCInfo.MapIndex</p></div><span class="tag blue">${movements.length} 传送 · ${npcs.length} NPC</span></div><div class="table-wrap"><table><thead><tr><th>目标地图</th><th>源坐标</th><th>目标坐标</th><th>规则</th></tr></thead><tbody>${movements.map(move => `<tr><td><span class="cell-main">${esc(move.targetMap?.title || move.targetMap?.fileName || `地图 ${move.targetMapIndex}`)}</span><div class="cell-sub mono">${esc(move.targetMap?.contentKey || `map.${move.targetMapIndex}`)}</div></td><td class="mono">${esc(pointText(move.source))}</td><td class="mono">${esc(pointText(move.destination))}</td><td>${move.needHole ? `<span class="tag">洞口</span>` : ""}${move.needMove ? `<span class="tag">踩点</span>` : ""}${move.showOnBigMap ? `<span class="tag">大地图</span>` : ""}${!move.needHole && !move.needMove && !move.showOnBigMap ? "—" : ""}</td></tr>`).join("") || `<tr><td colspan="4"><div class="empty-inline">该地图没有传送关系。</div></td></tr>`}</tbody></table></div><div class="card-body"><div class="table-wrap"><table><thead><tr><th>NPC</th><th>脚本</th><th>坐标</th><th>图像</th></tr></thead><tbody>${npcs.map(npc => `<tr><td><button class="row-link" data-action="open-content" data-type="npcs" data-key="${esc(npc.contentKey)}">${esc(npc.name || npc.contentKey)}</button><div class="cell-sub mono">${esc(npc.contentKey)}</div></td><td>${npc.script ? `<button class="row-link mono" data-action="open-content" data-type="npc-scripts" data-key="${esc(npc.script.contentKey)}">${esc(npc.script.contentKey)}</button><div class="cell-sub mono">${esc(npc.fileName || "—")} · ${esc(npc.script.lineCount ?? "—")} 行</div>` : `<span class="warning-text">脚本未导入</span><div class="cell-sub mono">${esc(npc.fileName || "—")}</div>`}</td><td class="mono">${esc(pointText(npc.location))}</td><td>${esc(npc.image ?? "—")}</td></tr>`).join("") || `<tr><td colspan="4"><div class="empty-inline">该地图没有 NPC 实体。</div></td></tr>`}</tbody></table></div></div></article>
        <article class="card"><div class="card-head"><div><h2 class="card-title">刷新点与掉落</h2><p class="card-subtitle">刷新点按 mapIndex 分页；怪物掉落按 monster localIndex 单独查询</p></div><span class="tag gold">${formatNumber(total)} 个刷新点</span></div><div class="table-wrap"><table><thead><tr><th>怪物</th><th>坐标</th><th>数量 / 半径</th><th>刷新</th><th>掉落</th></tr></thead><tbody>${respawnRows.map(respawn => `<tr><td><span class="cell-main">${esc(respawn.monsterName || `怪物 ${respawn.monsterIndex}`)}</span><div class="cell-sub mono">${esc(respawn.monsterContentKey || `monster.${respawn.monsterIndex}`)}</div></td><td class="mono">${esc(pointText(respawn.location))}</td><td>${esc(respawn.count)} 只<div class="cell-sub">半径 ${esc(respawn.spread)}</div></td><td>${esc(respawn.delay)} 分钟<div class="cell-sub">±${esc(respawn.randomDelay)}</div></td><td><button class="row-link" data-action="open-live-drops" data-index="${esc(respawn.monsterIndex)}" data-name="${esc(respawn.monsterName || "")}">查看掉落</button></td></tr>`).join("") || `<tr><td colspan="5"><div class="empty-inline">该地图没有刷新点。</div></td></tr>`}</tbody></table></div><div class="card-foot"><span class="muted">第 ${page} / ${pages} 页</span><div class="page-buttons"><button class="page-btn" data-action="live-respawn-page" data-page="${page - 1}" ${page <= 1 ? "disabled" : ""}>${I("chevronLeft", 11)}</button><button class="page-btn" data-action="live-respawn-page" data-page="${page + 1}" ${page >= pages ? "disabled" : ""}>${I("chevronRight", 11)}</button></div></div></article>
      </section>` : ""}`;
  }

  function handleApiFailure(error) {
    if (error?.status === 401) {
      API.clearSession();
      state.user = null;
      state.apiStatus = "demo";
      state.live = emptyLiveState();
      state.failed.clear();
      showToast("登录会话已失效，请重新登录。", "danger");
      if (state.route !== "login") navigate("login");
      return;
    }
    if (!error?.status) {
      state.apiStatus = "degraded";
    }
    state.apiError = error?.message || "Control API 请求失败";
    showToast(`Control API：${state.apiError}`, "danger");
  }

  async function hydrateRoute(route) {
    if (!isLive() || !canAccessRoute(route)) return;
    try {
      if (route.startsWith("content/")) {
        const typeId = route.split("/")[1];
        const type = CONTENT_BY_ID[typeId];
        if (!type || !LIVE_ENTITY_TYPES.has(type.entity)) return;
        await ensureLiveContext();
        const key = contentCacheKey(type);
        if (state.live.content[key] || state.inflight.has(key) || state.failed.has(key)) return;
        state.inflight.add(key);
        const query = contentQueryState(type);
        try {
          state.live.content[key] = await API.queryContent(type.entity, {
            importRunId: state.live.run?.id,
            page: query.page,
            pageSize: 50,
            q: query.q,
            sort: query.sort,
            direction: query.direction
          });
          if (state.route === route) render();
        } catch (error) {
          if (error?.status === 401) throw error;
          state.failed.add(key);
          state.live.content[key] = { error };
          if (!error?.status) state.apiStatus = "degraded";
          if (state.route === route) render();
        } finally {
          state.inflight.delete(key);
        }
        return;
      }

      if (route === "world-map" || route === "content-relations") {
        await ensureLiveContext();
        if (!state.live.run) return;
        if (state.live.relations.error && !state.live.relations.response) return;
        try {
          await ensureLiveMapCatalog();
          await ensureMapRelations(1);
        } catch (error) {
          if (error?.status === 401) throw error;
          state.live.relations.error = error;
          if (state.route === route) render();
        }
        return;
      }

      if (route === "content-overview" || route === "import-runs") {
        const hadContext = Boolean(state.live.runs);
        await ensureLiveContext();
        if (!hadContext && state.route === route) render();
        return;
      }

      if (route === "audit") {
        if (state.live.audit || state.inflight.has("audit") || state.failed.has("audit")) return;
        state.inflight.add("audit");
        try {
          state.live.audit = await API.listAuditEvents(100);
          if (state.route === route) render();
        } catch (error) {
          if (error?.status === 401) throw error;
          state.failed.add("audit");
          state.live.audit = { items: [], error };
          if (!error?.status) state.apiStatus = "degraded";
          if (state.route === route) render();
        } finally {
          state.inflight.delete("audit");
        }
        return;
      }

      if (route === "accounts") {
        if (state.live.accounts || state.inflight.has("accounts") || state.failed.has("accounts")) return;
        state.inflight.add("accounts");
        try {
          const payload = await API.listAccounts(200);
          state.live.accounts = { ...payload, loadedAt: new Date().toISOString() };
          if (hasPermission("role:read") && !state.live.roles && !state.inflight.has("roles") && !state.failed.has("roles")) {
            API.listRoles()
              .then(roles => {
                state.live.roles = { ...roles, loadedAt: new Date().toISOString() };
                if (state.route === route) render();
              })
              .catch(error => {
                if (error?.status !== 401) state.failed.add("roles");
              });
          }
          if (state.route === route) render();
        } catch (error) {
          if (error?.status === 401) throw error;
          state.failed.add("accounts");
          state.live.accounts = { items: [], error };
          if (!error?.status) state.apiStatus = "degraded";
          if (state.route === route) render();
        } finally {
          state.inflight.delete("accounts");
        }
        return;
      }

      if (route === "roles") {
        if (state.live.roles || state.inflight.has("roles") || state.failed.has("roles")) return;
        state.inflight.add("roles");
        try {
          const payload = await API.listRoles();
          state.live.roles = { ...payload, loadedAt: new Date().toISOString() };
          if (state.route === route) render();
        } catch (error) {
          if (error?.status === 401) throw error;
          state.failed.add("roles");
          state.live.roles = { items: [], error };
          if (!error?.status) state.apiStatus = "degraded";
          if (state.route === route) render();
        } finally {
          state.inflight.delete("roles");
        }
      }
    } catch (error) {
      handleApiFailure(error);
    }
  }

  function csvCell(value) {
    return `"${String(value ?? "").replaceAll('"', '""')}"`;
  }

  function exportAuditCsv() {
    if (isLive() && !state.live.audit?.items) {
      showToast("审计数据尚未加载，请稍后重试。", "danger");
      return;
    }
    if (isLive() && state.live.audit?.error) {
      showToast("审计数据读取失败，不能导出。", "danger");
      return;
    }
    const rows = auditRows();
    const header = ["time", "actor", "action", "resource", "ip", "result", "detail", "request_id"];
    const lines = [header, ...rows.map(event => [event.time, event.actor, event.action, event.resource, event.ip, event.result, event.detail, event.requestId])]
      .map(columns => columns.map(csvCell).join(","))
      .join("\r\n");
    const blob = new Blob([`\ufeff${lines}`], { type: "text/csv;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = `crystal-audit-${new Date().toISOString().slice(0, 19).replaceAll(":", "-")}.csv`;
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    window.setTimeout(() => URL.revokeObjectURL(url), 1000);
    showToast(`已导出当前查询结果，共 ${rows.length} 条。`);
  }

  function saveBlob(blob, fileName) {
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = fileName || "download";
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    window.setTimeout(() => URL.revokeObjectURL(url), 1000);
  }

  async function sha256Hex(blob) {
    if (!globalThis.crypto?.subtle) return null;
    const digest = await globalThis.crypto.subtle.digest("SHA-256", await blob.arrayBuffer());
    return Array.from(new Uint8Array(digest), byte => byte.toString(16).padStart(2, "0")).join("");
  }

  async function downloadAssetFromDrawer(actionEl) {
    const localIndex = Number(actionEl.dataset.index);
    if (!Number.isFinite(localIndex)) {
      showToast("资源索引无效，无法下载。", "danger");
      return;
    }
    actionEl.disabled = true;
    try {
      const result = await API.downloadAsset(localIndex, state.live.run?.id);
      const expectedSha256 = String(actionEl.dataset.sha256 || "").toLowerCase();
      const actualSha256 = expectedSha256 ? await sha256Hex(result.blob) : null;
      if (expectedSha256 && actualSha256 && actualSha256 !== expectedSha256) {
        showToast(`资源哈希校验失败：期望 ${expectedSha256.slice(0, 12)}…，实际 ${actualSha256.slice(0, 12)}…。`, "danger");
        return;
      }
      saveBlob(result.blob, actionEl.dataset.fileName || result.fileName);
      showToast(expectedSha256 && actualSha256 ? "资源已下载，SHA256 校验通过。" : "资源已下载。");
    } catch (error) {
      if (error?.status === 401) {
        handleApiFailure(error);
        return;
      }
      showToast(`资源下载失败：${error?.message || "Control API 请求失败"}`, "danger");
    } finally {
      actionEl.disabled = false;
    }
  }

  function auditRows() {
    if (isLive() && state.live.audit?.items) {
      return state.live.audit.items.map((event, index) => ({
        id: event.id,
        index,
        time: formatDateTime(event.occurredAt),
        actor: event.actorLabel || (event.actorAccountId ? `账号 #${event.actorAccountId}` : "system"),
        action: event.action,
        resource: [event.resourceType, event.resourceId].filter(Boolean).join("/") || "—",
        ip: event.actorIp || "—",
        result: event.result === "failure" ? "failed" : event.result,
        detail: event.reason || (event.metadata ? JSON.stringify(event.metadata) : "—"),
        requestId: event.requestId || "—"
      }));
    }
    return D.AUDIT_EVENTS.map((event, index) => ({ ...event, index }));
  }

  function importRunRows() {
    if (isLive() && state.live.runs) {
      return state.live.runs.map(run => {
        const started = new Date(run.startedAt);
        const finished = run.finishedAt ? new Date(run.finishedAt) : null;
        const seconds = finished && Number.isFinite(started.getTime())
          ? `${Math.max(0, (finished.getTime() - started.getTime()) / 1000).toFixed(1)}s`
          : "—";
        return {
          id: run.id,
          version: run.legacyVersion ?? run.schemaVersion,
          source: String(run.source || "").split(/[\\/]/).pop() || run.source,
          status: run.status,
          count: run.entityCount,
          started: formatDateTime(run.startedAt),
          duration: seconds,
          actor: "导入 Worker",
          hash: run.sourceHash || ""
        };
      });
    }
    return D.IMPORT_RUNS;
  }

  function selectedImportRun() {
    const rows = importRunRows();
    if (!rows.length) return null;
    if (isLive() && state.live.run) {
      return rows.find(row => String(row.id) === String(state.live.run.id)) || rows[0];
    }
    return rows[0];
  }

  function importRunSummary() {
    if (isLive() && state.live.counts?.counts) {
      return state.live.counts.counts.map(entry => [entityLabel(entry.entityType), entry.count]);
    }
    return Object.entries(D.IMPORT_RUNS[0].summary);
  }

  function renderContentLibrary(type) {
    const actions = `${btn("导出", "download", "btn-secondary", 'data-action="toast" data-message="已创建内容导出任务"')}${btn(`新建${type.singular}`, "plus", "btn-primary", 'data-action="open-modal" data-modal="create-content" data-type="' + type.id + '"')}`;
    const query = contentQueryState(type);
    const view = contentView(type);
    const rows = view.rows;
    const total = view.total;
    const issueCount = view.live ? 0 : type.issues;
    const sample = rows[0] || type.rows[0] || { name: type.label, index: "—", key: "—", status: "draft" };
    const run = selectedImportRun();
    const batchLabel = view.live ? `批次 #${run?.id ?? "—"}` : "批次 #32";
    const sourceLabel = view.live ? (String(state.live.run?.source || "").split(/[\\/]/).pop() || type.source) : type.source;
    const loadingRow = `<tr><td colspan="${type.columns.length + 2}"><div class="empty-inline">${I("refresh", 14)} 正在从 Control API 读取 ${esc(type.label)}...</div></td></tr>`;
    return `
      <div class="page">
        ${pageHeader({ title: type.label, description: esc(type.description), eyebrow: "CONTENT DOMAIN", actions })}
        <section>${contentTypeStrip(type.id)}</section>
        ${!LIVE_ENTITY_TYPES.has(type.entity) && isLive() ? `<div class="api-notice warning">${I("info", 14)}<span>${esc(type.label)} 尚未进入 MirDB 只读快照，当前展示原型样本；正式导入后可切换为批次数据。</span></div>` : ""}
        ${view.error ? `<div class="api-notice warning">${I("alert", 14)}<span>读取批次内容失败，已回退原型样本：${esc(view.error.message || "未知错误")}</span></div>` : ""}
        <section class="split mt-16">
          <div class="split-main stack">
            <article class="card">
              <div class="toolbar">
                <div class="toolbar-left">
                  <label class="search-field">${I("search", 13)}<input id="content-search-${type.id}" value="${esc(query.q)}" data-table-filter="#content-table" data-live-content-search="${type.id}" placeholder="搜索名称、索引、内容键..."></label>
                  <select class="select"><option>全部状态</option><option>已导入</option><option>已发布</option><option>草稿</option></select>
                  <select class="select"><option>全部来源</option><option>MirDB 导入</option><option>手工创建</option><option>外部文本</option></select>
                </div>
                <div class="toolbar-right"><span class="tag">${esc(batchLabel)}</span><span class="tag">${view.live ? "实时只读" : "按旧索引"}</span>${dividerButton()}</div>
              </div>
              <div class="table-wrap">
                <table id="content-table">
                  <thead><tr><th style="width:35px"><input class="checkbox" type="checkbox" aria-label="全选"></th>${type.columns.map(column => `<th>${esc(type.columnLabels[column])}</th>`).join("")}<th></th></tr></thead>
                  <tbody>
                    ${view.loading ? loadingRow : rows.map(row => `<tr class="content-row" data-action="open-content" data-type="${type.id}" data-key="${esc(row.key || row.index)}"><td><input class="checkbox" type="checkbox" aria-label="选择 ${esc(row.name)}"></td>${type.columns.map(column => `<td>${contentCell(row, column, type)}</td>`).join("")}<td><div class="table-actions">${iconButton("more", "更多操作")}</div></td></tr>`).join("")}
                    ${!view.loading && rows.length === 0 ? `<tr><td colspan="${type.columns.length + 2}"><div class="empty-inline">没有匹配的内容记录。</div></td></tr>` : ""}
                  </tbody>
                </table>
              </div>
              <div class="paginator">${view.live ? renderContentPager(type, query.page, total) : `<span>显示 1–${type.rows.length}，共 ${type.total.toLocaleString()} 条</span><div class="page-buttons"><button class="page-btn">${I("chevronLeft", 11)}</button><button class="page-btn active">1</button><button class="page-btn">2</button><button class="page-btn">3</button><span class="muted">…</span><button class="page-btn">${Math.ceil(type.total / 50)}</button><button class="page-btn">${I("chevronRight", 11)}</button></div>`}</div>
            </article>
            <article class="card">
              <div class="card-head"><div><h2 class="card-title">最近变更</h2><p class="card-subtitle">当前内容域的操作链与校验结果</p></div><a class="btn btn-ghost btn-sm" href="#/audit">查看审计 ${I("arrowRight", 12)}</a></div>
              <div class="card-body timeline">
                <div class="timeline-item"><span class="timeline-dot gold">${I("edit", 11)}</span><div class="timeline-copy"><strong>${view.live ? "批次导入只读同步" : "周遥更新"} ${esc(sample.name)}</strong><span>${view.live ? "从 PostgreSQL 读取导入快照，未做任何写入" : "基础字段、引用关系与资源绑定"}</span><time>${view.live ? esc(formatDateTime(state.live.run?.startedAt)) : "今天 17:58"} · ${esc(sample.key || sample.index)}</time></div></div>
                <div class="timeline-item"><span class="timeline-dot">${I("checkCircle", 11)}</span><div class="timeline-copy"><strong>${view.live ? "快照哈希与批次校验通过" : "校验通过 · 1 条 warning"}</strong><span>${view.live ? "Manifest 计数、内容哈希与导入批次一致" : "引用完整、资源存在、协议索引在范围内"}</span><time>${view.live ? esc(formatDateTime(state.live.run?.finishedAt || state.live.run?.startedAt)) : "今天 17:59"}</time></div></div>
                <div class="timeline-item"><span class="timeline-dot">${I("upload", 11)}</span><div class="timeline-copy"><strong>${view.live ? `批次 #${run?.id} 导入 ${formatNumber(run?.count)} 个实体` : "批次 #32 导入 9,734 个实体"}</strong><span>${view.live ? esc(sourceLabel) : "哈希与黄金样本一致"}</span><time>${view.live ? esc(run?.started || "—") : "今天 10:14"}</time></div></div>
              </div>
            </article>
          </div>
          <aside class="split-side stack">
            <article class="card">
              <div class="card-head"><div><h2 class="card-title">当前版本上下文</h2><p class="card-subtitle">避免在错误批次上编辑</p></div><span class="status info">只读批次</span></div>
              <div class="card-body data-list">
                <div class="data-row"><span>导入批次</span><strong>${view.live ? `#${run?.id} · ${esc(run?.status)}` : "#32 · imported"}</strong></div>
                <div class="data-row"><span>来源</span><strong>${esc(sourceLabel)}</strong></div>
                <div class="data-row"><span>实体数量</span><strong>${formatNumber(total)}</strong></div>
                <div class="data-row"><span>草稿</span><strong>${view.live ? 0 : type.draft}</strong></div>
                <div class="data-row"><span>校验问题</span><strong class="${issueCount ? "warning-text" : "success-text"}">${issueCount}</strong></div>
                <div class="data-row"><span>内容键</span><strong class="mono">${esc(type.entity)}.*</strong></div>
              </div>
            </article>
            <article class="card">
              <div class="card-head"><div><h2 class="card-title">发布前检查</h2><p class="card-subtitle">内容域级门禁</p></div></div>
              <div class="card-body validation-list">
                <div class="validation-item ok">${I("checkCircle", 13)}<span>Schema 与必填字段通过</span></div>
                <div class="validation-item ${issueCount ? "warn" : "ok"}">${I(issueCount ? "warning" : "checkCircle", 13)}<span>${issueCount} 个引用或资源问题需要处理</span></div>
                <div class="validation-item ok">${I("checkCircle", 13)}<span>协议索引范围通过</span></div>
                <div class="validation-item ok">${I("checkCircle", 13)}<span>内容哈希可复现</span></div>
              </div>
              <div class="card-foot"><button class="btn btn-secondary" style="width:100%" data-action="open-modal" data-modal="validate-content">${I("play", 13)}运行完整校验</button></div>
            </article>
            <article class="card">
              <div class="card-head"><div><h2 class="card-title">快捷操作</h2></div></div>
              <div class="card-body stack-sm">
                ${linkBtn("查看导入批次", "import-runs", "upload", "btn-secondary")}
                ${linkBtn("进入发布中心", "content-releases", "rocket", "btn-secondary")}
                ${linkBtn("浏览资源库", "assets", "image", "btn-secondary")}
              </div>
            </article>
          </aside>
        </section>
      </div>`;
  }

  function dividerButton() {
    return `<button class="icon-btn" data-action="toast" data-message="视图设置已打开" title="视图设置">${I("filter", 15)}</button>`;
  }

  function renderImportRuns() {
    const rows = importRunRows();
    const run = selectedImportRun();
    const summary = importRunSummary();
    const live = isLive() && state.live.runs;
    return `
      <div class="page">
        ${pageHeader({ title: "导入批次", description: "每次旧数据库导入都是不可变批次；批次、哈希、实体计数和校验摘要用于追溯与回滚。", eyebrow: "LEGACY INGESTION", actions: `${btn("查看映射规则", "book", "btn-secondary", 'data-action="open-modal" data-modal="mapping"')}${btn("导入 MirDB", "upload", "btn-primary", 'data-action="open-modal" data-modal="import"')}` })}
        <section class="split">
          <div class="split-main card">
            <div class="toolbar"><div class="toolbar-left"><label class="search-field">${I("search", 13)}<input data-table-filter="#import-table" placeholder="搜索批次、来源、哈希..."></label><span class="tag">${live ? "实时批次" : "最近 30 天"}</span></div><div class="toolbar-right">${iconButton("refresh", "刷新")}</div></div>
            <div class="table-wrap">
              <table id="import-table"><thead><tr><th>批次</th><th>来源</th><th>状态</th><th>实体</th><th>开始时间</th><th>耗时</th><th>操作者</th><th></th></tr></thead><tbody>
                ${rows.map((item, index) => `<tr class="${run && String(item.id) === String(run.id) ? "selected" : ""}"><td><span class="cell-main mono">#${esc(item.id)}</span><div class="cell-sub">v${esc(item.version)} · ${esc(String(item.hash || "—").slice(0, 8))}</div></td><td>${esc(item.source)}</td><td>${status(item.status)}</td><td><span class="cell-main">${formatNumber(item.count)}</span></td><td class="muted">${esc(item.started)}</td><td>${esc(item.duration)}</td><td>${esc(item.actor)}</td><td><div class="table-actions">${iconButton("eye", "查看批次", `data-action="open-import-run" data-id="${esc(item.id)}"`)}</div></td></tr>`).join("")}
                ${rows.length === 0 ? `<tr><td colspan="8"><div class="empty-inline">尚未读取到导入批次。</div></td></tr>` : ""}
              </tbody></table>
            </div>
            <div class="paginator"><span>共 ${rows.length} 个批次</span><div class="page-buttons"><button class="page-btn active">1</button><button class="page-btn" ${live ? "disabled" : ""}>2</button><button class="page-btn" ${live ? "disabled" : ""}>${I("chevronRight", 11)}</button></div></div>
          </div>
          <aside class="split-side stack">
            <article class="detail-hero"><div class="eyebrow">BATCH #${esc(run?.id ?? "—")}</div><h3>${run?.status === "imported" ? "导入完成" : run?.status === "failed" ? "导入失败" : "批次处理中"}</h3><p>${esc(run?.source || "Server.MirDB")} · v${esc(run?.version ?? "—")} · ${esc(run?.started || "—")}</p><div class="hero-meta"><span class="hero-tag">${formatNumber(run?.count)} 实体</span><span class="hero-tag">${esc(run?.duration || "—")}</span><span class="hero-tag">${live ? "实时只读" : "哈希校验通过"}</span></div></article>
            <article class="card">
              <div class="detail-section"><div class="detail-section-title"><span>批次摘要</span>${status(run?.status || "imported")}</div><div class="metric-mini-grid"><div class="metric-mini"><span>实体总量</span><strong>${formatNumber(run?.count)}</strong></div><div class="metric-mini"><span>来源哈希</span><strong class="mono" style="font-size:9px">${esc(String(run?.hash || "—").slice(0, 8))}</strong></div><div class="metric-mini"><span>来源文件</span><strong class="mono" style="font-size:9px">${esc(run?.source || "—")}</strong></div><div class="metric-mini"><span>耗时</span><strong>${esc(run?.duration || "—")}</strong></div></div></div>
              <div class="detail-section"><div class="detail-section-title"><span>实体计数</span><span>${summary.length} 类</span></div><div class="data-list">${summary.map(([key, value]) => `<div class="data-row"><span>${esc(key)}</span><strong>${formatNumber(value)}</strong></div>`).join("")}</div></div>
              <div class="detail-section"><div class="detail-section-title"><span>校验</span></div><div class="validation-list" style="padding:0"><div class="validation-item ok">${I("checkCircle", 12)}<span>计数与快照 manifest 一致</span></div><div class="validation-item ok">${I("checkCircle", 12)}<span>内容哈希可复现</span></div><div class="validation-item warn">${I("warning", 12)}<span>掉落/配方/脚本需独立导入</span></div></div></div>
              <div class="card-foot"><a class="btn btn-secondary" style="width:100%" href="#/content/items">${I("layers", 13)}打开内容库</a></div>
            </article>
          </aside>
        </section>
      </div>`;
  }

  function renderReleases() {
    return `
      <div class="page">
        ${pageHeader({ title: "发布中心", description: "内容从草稿、校验、签名到发布和回滚的完整流水线。发布包不可变，区服只消费已签名版本。", eyebrow: "RELEASE PIPELINE", actions: `${btn("版本对比", "gitBranch", "btn-secondary", 'data-action="open-modal" data-modal="release-diff"')}${btn("创建版本", "plus", "btn-primary", 'data-action="open-modal" data-modal="create-release"')}` })}
        <section class="grid grid-4">
          ${kpi({ label: "当前生效版本", value: "2026.11.09.1", icon: "rocket", tone: "green", meta: "5 个区服运行中" })}
          ${kpi({ label: "待审核", value: "1", icon: "clock", tone: "gold", meta: "四区 · 10% 灰度" })}
          ${kpi({ label: "已签名待发布", value: "1", icon: "shield", tone: "violet", meta: "签名算法 Ed25519" })}
          ${kpi({ label: "近 30 天回滚", value: "1", icon: "rotate", tone: "red", meta: "回滚耗时 2 分 18 秒" })}
        </section>
        <section class="grid grid-3 mt-16">
          <article class="card span-2">
            <div class="card-head"><div><h2 class="card-title">当前发布链路</h2><p class="card-subtitle">2026.11.10.0-rc2 · 四区灰度候选版本</p></div><span class="status warning">等待审批</span></div>
            <div class="card-body">
              <div class="pipeline">
                <div class="pipeline-step active"><div class="pipeline-label">草稿</div><div class="pipeline-value">✓</div><div class="small muted">18:42 完成</div></div>
                <div class="pipeline-step active"><div class="pipeline-label">校验</div><div class="pipeline-value">✓</div><div class="small muted">0 error · 3 warning</div></div>
                <div class="pipeline-step active"><div class="pipeline-label">编译</div><div class="pipeline-value">✓</div><div class="small muted">9,741 实体</div></div>
                <div class="pipeline-step active"><div class="pipeline-label">签名</div><div class="pipeline-value">✓</div><div class="small muted">Ed25519</div></div>
                <div class="pipeline-step"><div class="pipeline-label">发布</div><div class="pipeline-value">→</div><div class="small muted">等待审批</div></div>
              </div>
              <div class="grid grid-3 mt-16">
                <div class="metric-mini"><span>目标区服</span><strong style="font-size:10px">传奇四区</strong></div><div class="metric-mini"><span>灰度比例</span><strong style="font-size:10px">10%</strong></div><div class="metric-mini"><span>协议版本</span><strong style="font-size:10px">1 · 兼容</strong></div>
              </div>
            </div>
            <div class="card-foot flex justify-between items-center"><span class="small muted">审批人：陈屿 · 发布审核</span><div class="flex gap-6">${btn("查看校验", "eye", "btn-secondary btn-sm", 'data-action="open-modal" data-modal="validate-content"')}${btn("提交发布", "rocket", "btn-primary btn-sm", 'data-action="open-modal" data-modal="publish-release"')}</div></div>
          </article>
          <article class="card">
            <div class="card-head"><div><h2 class="card-title">发布通道</h2><p class="card-subtitle">渠道与灰度策略</p></div></div>
            <div class="card-body stack-sm">
              <div class="quick-row" style="padding:10px;border:1px solid var(--line);border-radius:8px"><span class="quick-icon" style="color:var(--green);background:var(--green-soft)">${I("checkCircle", 14)}</span><div class="quick-copy"><strong>stable</strong><span>5 个区服 · 2026.11.09.1</span></div><span class="status success">稳定</span></div>
              <div class="quick-row" style="padding:10px;border:1px solid var(--line);border-radius:8px"><span class="quick-icon" style="color:var(--orange);background:var(--orange-soft)">${I("play", 14)}</span><div class="quick-copy"><strong>staging</strong><span>1 个区服 · rc2 候选</span></div><span class="status warning">灰度</span></div>
              <div class="quick-row" style="padding:10px;border:1px solid var(--line);border-radius:8px"><span class="quick-icon" style="color:var(--blue);background:var(--blue-soft)">${I("users", 14)}</span><div class="quick-copy"><strong>canary</strong><span>按账号白名单</span></div><span class="status info">备用</span></div>
            </div>
            <div class="card-foot"><button class="btn btn-secondary" style="width:100%" data-action="toast" data-message="通道策略编辑器已打开">${I("settings", 13)}管理通道策略</button></div>
          </article>
        </section>
        <section class="card mt-16">
          <div class="card-head"><div><h2 class="card-title">版本历史</h2><p class="card-subtitle">不可变发布记录与回滚入口</p></div><div class="filter-pills"><span class="chip active">全部</span><span class="chip">stable</span><span class="chip">staging</span><span class="chip">已回滚</span></div></div>
          <div class="table-wrap"><table><thead><tr><th>版本</th><th>状态</th><th>通道</th><th>区服</th><th>实体</th><th>内容哈希</th><th>操作者</th><th>发布时间</th><th></th></tr></thead><tbody>
            ${D.RELEASES.map(release => `<tr><td><span class="cell-main mono">${esc(release.version)}</span><div class="cell-sub">${esc(release.notes)}</div></td><td>${status(release.status)}</td><td><span class="tag">${esc(release.channel)}</span></td><td>${release.zones}</td><td>${release.entities.toLocaleString()}</td><td class="mono small">${esc(release.hash)}</td><td>${esc(release.author)}</td><td class="muted">${esc(release.published)}</td><td><div class="table-actions"><button class="icon-btn" title="查看" data-action="open-modal" data-modal="release-detail">${I("eye", 14)}</button><button class="icon-btn" title="回滚" data-action="open-modal" data-modal="rollback-release">${I("rotate", 14)}</button></div></td></tr>`).join("")}
          </tbody></table></div>
        </section>
      </div>`;
  }

  function renderAssets() {
    return `
      <div class="page">
        ${pageHeader({ title: "资源库", description: "图库、地图、音效、特效和补丁包的哈希、引用、许可证与发布状态。所有资源按内容寻址存储。", eyebrow: "ASSET VAULT", actions: `${btn("许可证清单", "book", "btn-secondary", 'data-action="navigate" data-route="licenses"')}${btn("上传资源", "uploadCloud", "btn-primary", 'data-action="open-modal" data-modal="upload-asset"')}` })}
        <section class="grid grid-4">
          ${kpi({ label: "资源总量", value: "806", icon: "image", tone: "blue", meta: "图库 / 地图 / 音效 / 特效" })}
          ${kpi({ label: "存储占用", value: "3.42", unit: "TB", icon: "database", tone: "violet", meta: "对象存储 · 多副本" })}
          ${kpi({ label: "未确认授权", value: "4", icon: "alert", tone: "red", meta: "阻断商业发布" })}
          ${kpi({ label: "未被引用", value: "37", icon: "link", tone: "gold", meta: "可进入归档候选" })}
        </section>
        <section class="card mt-16">
          <div class="toolbar"><div class="toolbar-left"><label class="search-field">${I("search", 13)}<input data-table-filter="#asset-grid" placeholder="搜索文件名、哈希、引用..."></label><div class="filter-pills"><span class="chip active">全部</span><span class="chip">图库</span><span class="chip">地图</span><span class="chip">音效</span><span class="chip">特效</span><span class="chip">补丁包</span></div></div><div class="toolbar-right"><span class="tag danger">3 个授权告警</span>${iconButton("filter", "筛选")}</div></div>
          <div class="card-body"><div class="asset-grid" id="asset-grid">
            ${D.ASSETS.map(asset => `<article class="asset-card"><div class="asset-preview"><span class="asset-glyph">${I(asset.icon, 24)}</span><span class="asset-ref">${esc(asset.hash)}</span></div><div class="asset-copy"><div class="flex justify-between items-center gap-6"><strong>${esc(asset.name)}</strong>${asset.license === "需确认" ? status("warning", "授权待确认") : status("success", asset.license)}</div><span>${esc(asset.type)} · ${esc(asset.size)} · ${asset.refs.toLocaleString()} 引用</span><div class="asset-meta"><span class="muted">${esc(asset.hash)}</span><button class="icon-btn" style="width:24px;height:24px" data-action="toast" data-message="资源详情已打开">${I("more", 13)}</button></div></div></article>`).join("")}
          </div></div>
          <div class="paginator"><span>显示 8 / 806 个资源</span><div class="page-buttons"><button class="page-btn active">1</button><button class="page-btn">2</button><button class="page-btn">${I("chevronRight", 11)}</button></div></div>
        </section>
        <section class="grid grid-2 mt-16">
          <article class="card"><div class="card-head"><div><h2 class="card-title">资源管线</h2><p class="card-subtitle">上传、解析、哈希、引用和发布</p></div></div><div class="card-body pipeline"><div class="pipeline-step active"><div class="pipeline-label">上传</div><div class="pipeline-value">✓</div></div><div class="pipeline-step active"><div class="pipeline-label">解析</div><div class="pipeline-value">✓</div></div><div class="pipeline-step active"><div class="pipeline-label">哈希</div><div class="pipeline-value">✓</div></div><div class="pipeline-step"><div class="pipeline-label">引用</div><div class="pipeline-value">37</div></div><div class="pipeline-step"><div class="pipeline-label">发布</div><div class="pipeline-value">806</div></div></div></article>
          <article class="card"><div class="card-head"><div><h2 class="card-title">授权阻断项</h2><p class="card-subtitle">未确认授权的素材不得进入发布包</p></div><span class="status danger">4 项</span></div><div class="card-body alert-list"><div class="alert-item danger"><span class="alert-mark" style="color:var(--red);background:var(--red-soft)">${I("alert", 13)}</span><div class="alert-copy"><strong>Items.lib / Monsters.lib / Objects.lib</strong><span>来源与分发授权记录缺失。</span></div></div><div class="alert-item warning"><span class="alert-mark" style="color:var(--orange);background:var(--orange-soft)">${I("warning", 13)}</span><div class="alert-copy"><strong>Crystal.MapEditor</strong><span>未发现明确许可证，不能随发布包分发。</span></div></div></div><div class="card-foot"><a class="btn btn-secondary" style="width:100%" href="#/licenses">${I("book", 13)}进入许可与合规</a></div></article>
        </section>
      </div>`;
  }

  function codeLine(n, text) {
    return `<div class="code-line"><span class="line-no">${n}</span><span class="code">${text}</span></div>`;
  }

  function renderScripts() {
    const lines = [
      '<span class="cm">// merchant_weapon_01.npc</span>',
      '<span class="kw">NPC</span> <span class="str">"比奇武器商"</span> {',
      '  <span class="kw">MAP</span> <span class="str">"比奇城"</span>',
      '  <span class="kw">ICON</span> <span class="num">12</span>',
      '  <span class="kw">ON</span> <span class="fn">OPEN</span> {',
      '    <span class="kw">SAY</span> <span class="str">"欢迎光临，勇士。需要一把趁手的武器吗？"</span>',
      '    <span class="kw">MENU</span> {',
      '      <span class="kw">OPTION</span> <span class="str">"购买武器"</span> -> <span class="fn">SHOP</span>(<span class="str">weapon_list</span>)',
      '      <span class="kw">OPTION</span> <span class="str">"武器修理"</span> -> <span class="fn">REPAIR</span>()',
      '      <span class="kw">OPTION</span> <span class="str">"离开"</span> -> <span class="fn">CLOSE</span>()',
      '    }',
      '  }',
      '',
      '  <span class="kw">SHOP</span> <span class="str">weapon_list</span> {',
      '    <span class="kw">ITEM</span> <span class="num">1001</span> <span class="kw">PRICE</span> <span class="num">128000</span>',
      '    <span class="kw">ITEM</span> <span class="num">1002</span> <span class="kw">PRICE</span> <span class="num">96500</span>',
      '    <span class="kw">ITEM</span> <span class="num">1005</span> <span class="kw">PRICE</span> <span class="num">46000</span>',
      '  }',
      '}'
    ];
    return `
      <div class="page">
        ${pageHeader({ title: "脚本工作台", description: "兼容现有 NPC 文本 DSL，同时为后续 Lua 沙箱预留编辑、校验、引用检查和版本发布能力。", eyebrow: "SCRIPT STUDIO", actions: `${btn("格式化", "wand", "btn-secondary", 'data-action="toast" data-message="脚本已格式化"')}${btn("运行校验", "play", "btn-secondary", 'data-action="open-modal" data-modal="validate-script"')}${btn("保存草稿", "save", "btn-primary", 'data-action="toast" data-message="脚本草稿已保存"')}` })}
        <div class="script-studio">
          <aside class="script-tree">
            <div class="tree-title"><span>脚本目录</span>${iconButton("plus", "新建脚本")}</div>
            <div class="tree-group"><div class="tree-group-label">NPC · 比奇城</div><div class="tree-item active">${I("message", 13)}merchant_weapon_01.npc</div><div class="tree-item">${I("message", 13)}storage_keeper.npc</div><div class="tree-item">${I("message", 13)}quest_ambassador.npc</div></div>
            <div class="tree-group"><div class="tree-group-label">NPC · 盟重省</div><div class="tree-item">${I("message", 13)}teleport_mentor.npc</div><div class="tree-item">${I("message", 13)}merchant_potion.npc</div></div>
            <div class="tree-group"><div class="tree-group-label">掉落</div><div class="tree-item">${I("drop", 13)}drops_zuma.txt</div><div class="tree-item">${I("drop", 13)}drops_redmoon.txt</div></div>
            <div class="tree-group"><div class="tree-group-label">扩展脚本</div><div class="tree-item">${I("code", 13)}event_moon_festival.lua</div></div>
          </aside>
          <section class="script-editor">
            <div class="editor-bar"><span class="editor-tab">${I("file", 12)}merchant_weapon_01.npc <span class="tag gold" style="min-height:16px;padding:0 4px">已修改</span></span><span class="mono small" style="margin-left:auto;color:#72838f">NPC DSL · UTF-8</span></div>
            <div class="editor-body">${lines.map((line, index) => codeLine(index + 1, line)).join("")}</div>
          </section>
          <aside class="script-side">
            <div class="validation-score"><strong style="color:var(--green)">92</strong><span>脚本健康分</span></div>
            <div>
              <div class="validation-list">
                <div class="validation-item ok">${I("checkCircle", 12)}<span>语法解析通过</span></div>
                <div class="validation-item ok">${I("checkCircle", 12)}<span>3 个物品引用存在</span></div>
                <div class="validation-item ok">${I("checkCircle", 12)}<span>地图与 NPC 坐标有效</span></div>
                <div class="validation-item warn">${I("warning", 12)}<span>价格 128,000 高于经济建议值 120,000</span></div>
                <div class="validation-item ok">${I("checkCircle", 12)}<span>API 白名单通过</span></div>
              </div>
              <div class="detail-section"><div class="detail-section-title"><span>引用</span></div><div class="data-list"><div class="data-row"><span>物品</span><strong>item.1001 / 1002 / 1005</strong></div><div class="data-row"><span>地图</span><strong>D2001.map</strong></div><div class="data-row"><span>对话段</span><strong>18</strong></div><div class="data-row"><span>最后修改</span><strong>周遥 · 今天 17:58</strong></div></div></div>
              <div class="card-foot"><button class="btn btn-secondary" style="width:100%" data-action="open-modal" data-modal="script-diff">${I("gitBranch", 13)}查看变更</button></div>
            </div>
          </aside>
        </div>
      </div>`;
  }

  function renderZones() {
    const openZones = D.ZONES.filter(zone => zone.status === "open");
    const players = openZones.reduce((sum, zone) => sum + zone.players, 0);
    return `
      <div class="page">
        ${pageHeader({ title: "区服总览", description: "区服生命周期、在线容量、内容版本、Tick 延迟和 Agent 健康状态。区服是有状态服务，按区独立隔离。", eyebrow: "ZONE FLEET", actions: `${linkBtn("管理开区模板", "zone-templates", "copy")}${btn("开新区", "plus", "btn-primary", 'data-action="open-modal" data-modal="new-zone"')}` })}
        <section class="grid grid-4">
          ${kpi({ label: "在线玩家", value: players.toLocaleString(), icon: "users", tone: "blue", trend: { text: "6.2%", cls: "up" }, meta: "开放区服合计" })}
          ${kpi({ label: "区服健康", value: "10", unit: "/ 12", icon: "server", tone: "green", meta: "1 维护 · 1 预热" })}
          ${kpi({ label: "平均 Tick P95", value: "24", unit: "ms", icon: "gauge", tone: "gold", trend: { text: "3ms", cls: "down" }, meta: "阈值 50ms" })}
          ${kpi({ label: "稳定版覆盖率", value: "42", unit: "%", icon: "rocket", tone: "violet", meta: "5 / 12 区服" })}
        </section>
        <section class="split mt-16">
          <div class="split-main stack">
            <article class="card">
              <div class="toolbar"><div class="toolbar-left"><label class="search-field">${I("search", 13)}<input data-table-filter="#zone-table" placeholder="搜索区服、Host、版本..."></label><div class="filter-pills"><span class="chip active">全部 12</span><span class="chip">开放 10</span><span class="chip">维护 1</span><span class="chip">预热 1</span></div></div><div class="toolbar-right">${iconButton("refresh", "刷新区服状态")}</div></div>
              <div class="table-wrap"><table id="zone-table"><thead><tr><th>区服</th><th>状态</th><th>在线</th><th>Tick</th><th>内容版本</th><th>可用性</th><th></th></tr></thead><tbody>
                ${D.ZONES.map(zone => `<tr class="content-row" data-route="zone/${zone.id}"><td><div class="cell-stack"><span class="zone-mark">${esc(zone.short.slice(0, 1))}</span><div><div class="cell-main">${esc(zone.name)}</div><div class="cell-sub">${esc(zone.id)} · ${esc(zone.host)}:${zone.port} · ${esc(zone.region)}</div></div></div></td><td>${status(zone.status)}</td><td><span class="cell-main">${zone.players.toLocaleString()}</span><div class="cell-sub">容量 ${zone.capacity.toLocaleString()}</div></td><td class="${zone.tick > 40 ? "warning-text" : ""}">${zone.tick ? `${zone.tick} ms` : "—"}</td><td class="mono small">${esc(zone.release)}</td><td><div class="progress-row"><div class="progress ${zone.health < 99.8 ? "red" : "green"}"><span style="width:${zone.health}%"></span></div><span class="progress-value">${zone.health}%</span></div></td><td><div class="table-actions">${iconButton("chevronRight", "查看区服")}</div></td></tr>`).join("")}
              </tbody></table></div>
              <div class="paginator"><span>共 12 个区服</span><div class="page-buttons"><button class="page-btn active">1</button><button class="page-btn">2</button><button class="page-btn">${I("chevronRight", 11)}</button></div></div>
            </article>
            <article class="card">
              <div class="card-head"><div><h2 class="card-title">内容版本分布</h2><p class="card-subtitle">稳定版与灰度候选版本</p></div><a class="btn btn-ghost btn-sm" href="#/content-releases">发布中心 ${I("arrowRight", 12)}</a></div>
              <div class="card-body grid grid-2">
                <div><div class="flex justify-between items-center"><span class="small muted">stable · 2026.11.09.1</span><strong class="small">5 区</strong></div><div class="progress mt-8"><span style="width:42%"></span></div></div>
                <div><div class="flex justify-between items-center"><span class="small muted">stable · 2026.11.08.2</span><strong class="small">2 区</strong></div><div class="progress blue mt-8"><span style="width:17%"></span></div></div>
                <div><div class="flex justify-between items-center"><span class="small muted">staging · rc2</span><strong class="small">1 区</strong></div><div class="progress red mt-8"><span style="width:8%"></span></div></div>
                <div><div class="flex justify-between items-center"><span class="small muted">未分配 / 维护</span><strong class="small">4 区</strong></div><div class="progress mt-8" style="background:#edf0f2"><span style="width:33%;background:#c9d0d7"></span></div></div>
              </div>
            </article>
          </div>
          <aside class="split-side stack">
            <article class="card"><div class="card-head"><div><h2 class="card-title">区服生命周期</h2><p class="card-subtitle">从创建到归档</p></div></div><div class="card-body data-list"><div class="data-row"><span>created</span><strong>2 个</strong></div><div class="data-row"><span>warming</span><strong>1 个</strong></div><div class="data-row"><span>open</span><strong>10 个</strong></div><div class="data-row"><span>maintenance</span><strong>1 个</strong></div><div class="data-row"><span>merging</span><strong>0 个</strong></div></div></article>
            <article class="card"><div class="card-head"><div><h2 class="card-title">健康门禁</h2><p class="card-subtitle">开放登录前必须通过</p></div></div><div class="card-body validation-list"><div class="validation-item ok">${I("checkCircle", 12)}<span>Agent 心跳正常</span></div><div class="validation-item ok">${I("checkCircle", 12)}<span>内容包签名与哈希通过</span></div><div class="validation-item ok">${I("checkCircle", 12)}<span>地图与关键 NPC 加载完成</span></div><div class="validation-item warn">${I("warning", 12)}<span>六区登录冒烟尚未执行</span></div></div></article>
            <article class="card"><div class="card-head"><div><h2 class="card-title">快速操作</h2></div></div><div class="card-body stack-sm">${linkBtn("进入 GM 工作台", "gm", "command")}${linkBtn("合区中心", "merges", "merge")}${linkBtn("运行监控", "monitoring", "activity")}</div></article>
          </aside>
        </section>
      </div>`;
  }

  function renderZoneDetail(zone) {
    const actions = `${btn("进入维护", "pause", "btn-secondary", 'data-action="open-modal" data-modal="zone-maintenance"')}${btn("GM 操作", "command", "btn-secondary", 'data-action="navigate" data-route="gm"')}${btn("切换内容版本", "rocket", "btn-primary", 'data-action="open-modal" data-modal="publish-release"')}`;
    const zonePlayers = D.ONLINE_PLAYERS.filter(player => player.zone.includes(zone.short));
    const playerRows = zonePlayers.length ? zonePlayers : D.ONLINE_PLAYERS.slice(0, 3);
    return `
      <div class="page">
        <div class="page-actions" style="justify-content:flex-start;padding:0 0 16px"><button class="btn btn-ghost btn-sm" data-action="navigate" data-route="zones">${I("chevronLeft", 12)}返回区服总览</button></div>
        <section class="zone-hero">
          <div class="zone-hero-main"><div class="zone-hero-mark">${esc(zone.short.slice(0, 1))}</div><div><div class="eyebrow" style="color:#e4bb70">ZONE · ${esc(zone.id)}</div><h2>${esc(zone.name)}</h2><p>${esc(zone.region)} · ${esc(zone.host)}:${zone.port} · Gateway ${esc(zone.gateway)} · 已运行 ${esc(zone.uptime)}</p><div class="hero-meta">${status(zone.status)}<span class="hero-tag">${esc(zone.release)}</span><span class="hero-tag">${zone.players.toLocaleString()} / ${zone.capacity.toLocaleString()} 在线</span></div></div></div>
          <div class="zone-hero-meta">${actions}</div>
        </section>
        <section class="grid grid-4 mt-16">
          ${kpi({ label: "在线玩家", value: zone.players.toLocaleString(), icon: "users", tone: "blue", trend: { text: "4.8%", cls: "up" }, meta: `容量 ${zone.capacity.toLocaleString()}` })}
          ${kpi({ label: "Tick P95", value: String(zone.tick || "—"), unit: zone.tick ? "ms" : "", icon: "gauge", tone: "green", meta: "阈值 50ms" })}
          ${kpi({ label: "内容版本", value: zone.release, icon: "rocket", tone: "violet", meta: zone.status === "maintenance" ? "灰度候选" : "稳定通道" })}
          ${kpi({ label: "今日流水", value: zone.revenue, icon: "coins", tone: "gold", meta: `${zone.guilds} 个行会 · ${zone.maps} 张地图` })}
        </section>
        <section class="grid grid-3 mt-16">
          <article class="card chart-card span-2"><div class="card-head"><div><h2 class="card-title">在线与负载</h2><p class="card-subtitle">最近 24 小时 · 玩家与 Tick P95</p></div><div class="segmented"><button class="active">24 小时</button><button>7 天</button></div></div><div class="chart-wrap">${lineChart(D.chartOnline.map((v, i) => Math.max(18, Math.round(v * (zone.players / 8400) + (i % 5) * 2))), { id: `zone-${zone.id}`, labels: ["00", "06", "12", "18", "23"], label: "区服在线趋势" })}</div><div class="chart-legend"><span><i class="legend-dot"></i>在线玩家</span><span><i class="legend-dot blue"></i>Tick P95 阈值 50ms</span></div></article>
          <article class="card"><div class="card-head"><div><h2 class="card-title">运行信息</h2><p class="card-subtitle">Agent 最近上报 12 秒前</p></div><span class="status success">健康</span></div><div class="card-body data-list"><div class="data-row"><span>运行时长</span><strong>${esc(zone.uptime)}</strong></div><div class="data-row"><span>可用性</span><strong class="success-text">${zone.health}%</strong></div><div class="data-row"><span>Host / Port</span><strong class="mono">${esc(zone.host)}:${zone.port}</strong></div><div class="data-row"><span>Gateway</span><strong class="mono">${esc(zone.gateway)}</strong></div><div class="data-row"><span>地图</span><strong>${zone.maps}</strong></div><div class="data-row"><span>行会</span><strong>${zone.guilds}</strong></div></div><div class="card-foot"><button class="btn btn-secondary" style="width:100%" data-action="toast" data-message="区服日志已打开">${I("terminal", 13)}查看 Agent 日志</button></div></article>
        </section>
        <section class="split mt-16">
          <div class="split-main stack">
            <article class="card"><div class="card-head"><div><h2 class="card-title">在线玩家</h2><p class="card-subtitle">仅显示当前开放区服样本</p></div><a class="btn btn-ghost btn-sm" href="#/players">玩家查询 ${I("arrowRight", 12)}</a></div><div class="table-wrap"><table><thead><tr><th>角色</th><th>等级 / 职业</th><th>当前地图</th><th>登录时间</th><th>状态</th><th></th></tr></thead><tbody>${playerRows.map(player => `<tr><td><div class="cell-stack">${avatar(player.name, "sm gold")}<div><div class="cell-main">${esc(player.name)}</div><div class="cell-sub">${esc(player.account)}</div></div></div></td><td>Lv.${player.level} · ${esc(player.class)}</td><td>${esc(player.map)}</td><td class="muted">${esc(player.login)}</td><td>${status(player.status)}</td><td><div class="table-actions">${iconButton("command", "GM 操作")}</div></td></tr>`).join("")}</tbody></table></div></article>
            <article class="card"><div class="card-head"><div><h2 class="card-title">区服命令历史</h2><p class="card-subtitle">最近 Agent 命令与内容切换</p></div></div><div class="card-body timeline"><div class="timeline-item"><span class="timeline-dot gold">${I("rocket", 11)}</span><div class="timeline-copy"><strong>加载内容快照 2026.11.09.1</strong><span>staging 预加载通过，原子切换成功</span><time>11-09 22:18 · 命令 cmd-91ac</time></div></div><div class="timeline-item"><span class="timeline-dot">${I("refresh", 11)}</span><div class="timeline-copy"><strong>NPC 脚本热重载</strong><span>18 个脚本通过校验，0 error</span><time>11-09 18:42 · 命令 cmd-88f2</time></div></div><div class="timeline-item"><span class="timeline-dot">${I("shield", 11)}</span><div class="timeline-copy"><strong>健康门禁通过</strong><span>Agent、地图、数据库、登录冒烟全部通过</span><time>11-09 22:02 · 系统</time></div></div></div></article>
          </div>
          <aside class="split-side stack">
            <article class="card"><div class="card-head"><div><h2 class="card-title">内容与协议</h2><p class="card-subtitle">版本兼容性</p></div></div><div class="card-body data-list"><div class="data-row"><span>内容版本</span><strong class="mono">${esc(zone.release)}</strong></div><div class="data-row"><span>内容哈希</span><strong class="mono">${zone.status === "maintenance" ? "b41e8a69" : "7e7a6dd1"}</strong></div><div class="data-row"><span>协议版本</span><strong>v1 · 兼容</strong></div><div class="data-row"><span>Schema</span><strong>v1</strong></div><div class="data-row"><span>最低客户端</span><strong>0.1.0</strong></div><div class="data-row"><span>热重载</span><strong class="success-text">可用</strong></div></div></article>
            <article class="card"><div class="card-head"><div><h2 class="card-title">地图热点</h2><p class="card-subtitle">在线分布</p></div></div><div class="card-body"><div class="data-list"><div class="data-row"><span>比奇城</span><strong>38%</strong></div><div class="progress"><span style="width:38%"></span></div><div class="data-row"><span>祖玛寺庙</span><strong>24%</strong></div><div class="progress"><span style="width:24%"></span></div><div class="data-row"><span>苍月岛</span><strong>18%</strong></div><div class="progress"><span style="width:18%"></span></div><div class="data-row"><span>其他地图</span><strong>20%</strong></div><div class="progress"><span style="width:20%"></span></div></div></div></article>
            <article class="card"><div class="card-head"><div><h2 class="card-title">高危操作</h2><p class="card-subtitle">均需要审批与审计</p></div></div><div class="card-body stack-sm"><button class="btn btn-secondary" data-action="open-modal" data-modal="zone-maintenance">${I("pause", 13)}进入维护模式</button><button class="btn btn-secondary" data-action="open-modal" data-modal="rollback-release">${I("rotate", 13)}回滚内容版本</button><button class="btn btn-danger" data-action="open-modal" data-modal="zone-maintenance">${I("archive", 13)}关闭区服</button></div></article>
          </aside>
        </section>
      </div>`;
  }

  function renderZoneTemplates() {
    return `
      <div class="page">
        ${pageHeader({ title: "开区模板", description: "用版本化模板定义内容版本、倍率、地图组、网络端口、数据库连接和监控策略。模板变更不影响已创建区服。", eyebrow: "ZONE TEMPLATES", actions: `${btn("对比版本", "gitBranch", "btn-secondary", 'data-action="toast" data-message="模板版本对比已打开"')}${btn("新建模板", "plus", "btn-primary", 'data-action="open-modal" data-modal="new-template"')}` })}
        <section class="grid grid-4">
          ${kpi({ label: "模板数量", value: "4", icon: "copy", tone: "blue", meta: "3 个使用中" })}
          ${kpi({ label: "已开区服", value: "12", icon: "server", tone: "green", meta: "来自 4 个模板" })}
          ${kpi({ label: "最新模板版本", value: "v12", icon: "gitBranch", tone: "violet", meta: "经典 1.76 · 复古" })}
          ${kpi({ label: "模板健康", value: "3", unit: "/ 4", icon: "shield", tone: "gold", meta: "沙盒模板未绑定稳定版" })}
        </section>
        <section class="grid grid-2 mt-16">
          ${D.TEMPLATES.map(template => `<article class="card"><div class="card-head"><div class="flex gap-8 items-center"><span class="cell-icon ${template.tone === "blue" ? "blue" : template.tone === "green" ? "green" : template.tone === "red" ? "red" : ""}" style="width:36px;height:36px">${I(template.icon, 17)}</span><div><h2 class="card-title">${esc(template.name)}</h2><p class="card-subtitle">${esc(template.version)} · 已被 ${template.zones} 个区服使用</p></div></div><span class="tag gold">${esc(template.release)}</span></div><div class="card-body"><p class="small muted" style="margin:0 0 12px">${esc(template.desc)}</p><div class="grid grid-3"><div class="metric-mini"><span>经验倍率</span><strong>${esc(template.exp)}</strong></div><div class="metric-mini"><span>掉落倍率</span><strong>${esc(template.drop)}</strong></div><div class="metric-mini"><span>地图数</span><strong>${template.maps}</strong></div></div></div><div class="card-foot flex justify-between items-center"><span class="small muted">数据库 / 端口 / 监控策略已绑定</span><div class="flex gap-6">${btn("查看", "eye", "btn-secondary btn-sm", 'data-action="toast" data-message="模板详情已打开"')}${btn("用此模板开区", "plus", "btn-primary btn-sm", 'data-action="open-modal" data-modal="new-zone"')}</div></div></article>`).join("")}
        </section>
        <section class="grid grid-2 mt-16">
          <article class="card"><div class="card-head"><div><h2 class="card-title">模板变更策略</h2><p class="card-subtitle">不可变模板版本与显式升级</p></div></div><div class="card-body timeline"><div class="timeline-item"><span class="timeline-dot gold">${I("edit", 11)}</span><div class="timeline-copy"><strong>创建新模板版本</strong><span>不修改正在被区服引用的旧版本</span></div></div><div class="timeline-item"><span class="timeline-dot">${I("checkCircle", 11)}</span><div class="timeline-copy"><strong>校验模板引用的内容版本</strong><span>内容必须已签名并可被区服拉取</span></div></div><div class="timeline-item"><span class="timeline-dot">${I("play", 11)}</span><div class="timeline-copy"><strong>新区使用新版本</strong><span>老区通过显式升级任务切换</span></div></div></div></article>
          <article class="card"><div class="card-head"><div><h2 class="card-title">开区检查清单</h2><p class="card-subtitle">从模板创建到开放登录</p></div></div><div class="card-body validation-list"><div class="validation-item ok">${I("checkCircle", 12)}<span>端口范围未冲突</span></div><div class="validation-item ok">${I("checkCircle", 12)}<span>内容版本已签名</span></div><div class="validation-item ok">${I("checkCircle", 12)}<span>数据库连接模板可用</span></div><div class="validation-item warn">${I("warning", 12)}<span>目标区域容量需要重新评估</span></div><div class="validation-item ok">${I("checkCircle", 12)}<span>监控策略与告警规则已绑定</span></div></div><div class="card-foot"><button class="btn btn-primary" style="width:100%" data-action="open-modal" data-modal="new-zone">${I("rocket", 13)}从模板创建新区</button></div></article>
        </section>
      </div>`;
  }

  function renderGm() {
    return `
      <div class="page">
        ${pageHeader({ title: "GM 工作台", description: "在线玩家、公告、补偿、封禁、邮件、传送和内容重载。所有操作必须选择原因、记录审计，高风险操作进入审批。", eyebrow: "GAME MASTER CONSOLE", actions: `${btn("查看 GM 审计", "audit", "btn-secondary", 'data-action="navigate" data-route="audit"')}${btn("高风险审批", "shield", "btn-primary", 'data-action="navigate" data-route="approvals"')}` })}
        <section class="grid grid-4">
          ${kpi({ label: "当前在线", value: "8,642", icon: "users", tone: "blue", meta: "跨 10 个开放区服" })}
          ${kpi({ label: "今日 GM 操作", value: "46", icon: "command", tone: "green", meta: "全部已审计" })}
          ${kpi({ label: "待审批", value: "2", icon: "clock", tone: "gold", meta: "补偿 / 内容重载" })}
          ${kpi({ label: "异常账号", value: "7", icon: "ban", tone: "red", meta: "风控命中 · 待复核" })}
        </section>
        <section class="split mt-16">
          <div class="split-main stack">
            <article class="card">
              <div class="toolbar"><div class="toolbar-left"><label class="search-field">${I("search", 13)}<input data-table-filter="#gm-player-table" placeholder="搜索角色、账号、地图..."></label><select class="select"><option>全部区服</option>${D.ZONES.filter(z => z.status === "open").map(z => `<option>${esc(z.name)}</option>`).join("")}</select></div><div class="toolbar-right"><span class="tag">在线 5 个样本</span>${iconButton("refresh", "刷新在线列表")}</div></div>
              <div class="table-wrap"><table id="gm-player-table"><thead><tr><th>角色</th><th>等级</th><th>区服</th><th>当前地图 / 坐标</th><th>登录</th><th>状态</th><th></th></tr></thead><tbody>${D.ONLINE_PLAYERS.map(player => { const coord = W.ONLINE_COORDS[player.name] || { mapKey: "", x: "—", y: "—", direction: "—" }; return `<tr><td><div class="cell-stack">${avatar(player.name, "sm gold")}<div><div class="cell-main">${esc(player.name)}</div><div class="cell-sub">${esc(player.account)}</div></div></div></td><td>Lv.${player.level} · ${esc(player.class)}</td><td>${esc(player.zone)}</td><td><span class="cell-main">${esc(player.map)}</span><div class="cell-sub mono">(${coord.x}, ${coord.y}) · ${esc(coord.direction)}</div></td><td class="muted">${esc(player.login)}</td><td>${status(player.status)}</td><td><div class="table-actions"><button class="icon-btn" title="在地图定位" data-action="open-world-map" data-map-key="${esc(coord.mapKey)}">${I("mapPin", 14)}</button><button class="icon-btn" title="踢下线" data-action="confirm-high-risk" data-message="已提交踢下线操作">${I("logout", 14)}</button></div></td></tr>`; }).join("")}</tbody></table></div>
            </article>
            <article class="card"><div class="card-head"><div><h2 class="card-title">最近 GM 操作</h2><p class="card-subtitle">所有命令均带操作者、目标、参数和结果</p></div><a class="btn btn-ghost btn-sm" href="#/audit">完整审计 ${I("arrowRight", 12)}</a></div><div class="table-wrap"><table><thead><tr><th>时间</th><th>操作者</th><th>动作</th><th>目标</th><th>结果</th></tr></thead><tbody>${D.AUDIT_EVENTS.filter(event => event.action.startsWith("gm.") || event.action.includes("compensation")).map(event => `<tr><td class="muted">${esc(event.time)}</td><td>${esc(event.actor)}</td><td class="mono small">${esc(event.action)}</td><td class="mono small">${esc(event.resource)}</td><td>${status(event.result)}</td></tr>`).join("")}</tbody></table></div></article>
          </div>
          <aside class="split-side stack">
            <article class="card">
              <div class="card-head"><div><h2 class="card-title">执行 GM 命令</h2><p class="card-subtitle">高风险操作提交审批</p></div><span class="status warning">需审计</span></div>
              <div class="card-body">
                <div class="filter-pills" style="margin-bottom:12px"><span class="chip active">角色</span><span class="chip">资产</span><span class="chip">世界</span><span class="chip">内容</span></div>
                <form class="js-form">
                  <div class="field"><label>目标区服</label><select><option>传奇四区 · 沙城</option>${D.ZONES.filter(z => z.status === "open").map(z => `<option>${esc(z.name)}</option>`).join("")}</select></div>
                  <div class="field"><label>目标角色 / 账号</label><input placeholder="输入角色名或账号 ID" aria-label="目标角色"><div class="field-hint">支持单个角色、账号或按条件筛选。</div></div>
                  <div class="field-row"><div class="field"><label>命令</label><select><option>发放金币</option><option>发放物品</option><option>发送邮件</option><option>封禁账号</option><option>传送角色</option><option>全服公告</option></select></div><div class="field"><label>数量 / 参数</label><input value="10,000" aria-label="数量"></div></div>
                  <div class="field"><label>操作原因（必填）</label><textarea placeholder="说明运营依据、补偿方案或工单编号"></textarea></div>
                  <div class="risk-box">${I("alert", 15)}<span>批量发放金币属于高风险操作，达到阈值后需要发布审核或财务复核。</span></div>
                  <button class="btn btn-primary" style="width:100%;margin-top:12px" type="button" data-action="submit-gm">${I("command", 13)}提交 GM 命令</button>
                </form>
              </div>
            </article>
            <article class="card"><div class="card-head"><div><h2 class="card-title">安全限额</h2><p class="card-subtitle">按角色和账号实时校验</p></div></div><div class="card-body data-list"><div class="data-row"><span>单次金币</span><strong>≤ 20,000</strong></div><div class="data-row"><span>单日金币</span><strong>≤ 200,000</strong></div><div class="data-row"><span>批量邮件</span><strong>≤ 50,000 角色</strong></div><div class="data-row"><span>高价值物品</span><strong class="warning-text">需二次审批</strong></div><div class="data-row"><span>封禁 7 天以上</span><strong class="warning-text">需双人复核</strong></div></div><div class="card-foot"><a class="btn btn-secondary" style="width:100%" href="#/settings">${I("settings", 13)}调整安全策略</a></div></article>
          </aside>
        </section>
      </div>`;
  }

  function renderAnnouncements() {
    return `
      <div class="page">
        ${pageHeader({ title: "公告与活动", description: "公告编辑、区服定向、定时发布和活动日历。所有对外内容先预览，再按区服和渠道发布。", eyebrow: "CAMPAIGN DESK", actions: `${btn("活动日历", "calendar", "btn-secondary", 'data-action="toast" data-message="活动日历已打开"')}${btn("新建公告", "plus", "btn-primary", 'data-action="open-modal" data-modal="new-announcement"')}` })}
        <section class="grid grid-4">
          ${kpi({ label: "排期公告", value: "3", icon: "calendar", tone: "blue", meta: "最近一条 11-12 01:30" })}
          ${kpi({ label: "草稿", value: "5", icon: "edit", tone: "gold", meta: "需要运营确认" })}
          ${kpi({ label: "进行中活动", value: "2", icon: "sparkles", tone: "green", meta: "竞技场 S3 / 沙巴克" })}
          ${kpi({ label: "活动待校验", value: "1", icon: "alert", tone: "red", meta: "月华庆典奖励引用" })}
        </section>
        <section class="event-composer mt-16">
          <article class="card">
            <div class="card-head"><div><h2 class="card-title">公告编辑器</h2><p class="card-subtitle">按区服、渠道和优先级定向发送</p></div><span class="status neutral">草稿</span></div>
            <div class="card-body">
              <div class="field-row"><div class="field"><label>公告标题</label><input value="月华庆典活动开启" aria-label="公告标题"></div><div class="field"><label>发布渠道</label><select><option>游戏内 + 官网 + 社群</option><option>仅游戏内</option><option>仅官网</option></select></div></div>
              <div class="field-row"><div class="field"><label>目标区服</label><select><option>全部开放区服</option><option>传奇一区至五区</option><option>指定区服</option></select></div><div class="field"><label>发布时间</label><input value="2026-11-12 20:00" aria-label="发布时间"></div></div>
              <div class="field"><label>公告正文</label><textarea style="min-height:126px">月华升起，沙城不眠。\n\n活动时间：11 月 12 日 20:00 — 11 月 19 日 23:59\n参与条件：角色等级 ≥ 20\n活动内容：收集月华碎片，兑换限时称号、Buff 与月华宝箱。</textarea></div>
              <div class="flex justify-between items-center"><span class="small muted">支持 Markdown · 游戏内自动转纯文本</span><div class="flex gap-6">${btn("保存草稿", "save", "btn-secondary btn-sm", 'data-action="toast" data-message="公告草稿已保存"')}${btn("预览", "eye", "btn-secondary btn-sm", 'data-action="toast" data-message="游戏内预览已打开"')}${btn("排期发布", "calendar", "btn-primary btn-sm", 'data-action="open-modal" data-modal="schedule-announcement"')}</div></div>
            </div>
          </article>
          <article class="preview-poster"><div class="eyebrow">GAME ANNOUNCEMENT</div><h3>月华升起<br>沙城不眠</h3><p>收集月华碎片，兑换限时称号、Buff 与月华宝箱。活动期间全服经验 +20%，沙巴克攻城奖励同步升级。</p><div class="poster-meta"><span class="hero-tag">11.12 — 11.19</span><span class="hero-tag">等级 ≥ 20</span><span class="hero-tag">全服</span></div></article>
        </section>
        <section class="card mt-16">
          <div class="card-head"><div><h2 class="card-title">公告排期</h2><p class="card-subtitle">已发布、已排期与草稿</p></div><div class="filter-pills"><span class="chip active">全部</span><span class="chip">已发布</span><span class="chip">已排期</span><span class="chip">草稿</span></div></div>
          <div class="table-wrap"><table><thead><tr><th>公告</th><th>渠道</th><th>范围</th><th>状态</th><th>发布时间</th><th>作者</th><th></th></tr></thead><tbody>${D.ANNOUNCEMENTS.map(item => `<tr><td><span class="cell-main">${esc(item.title)}</span></td><td><span class="tag">${esc(item.channel)}</span></td><td>${esc(item.scope)}</td><td>${status(item.status)}</td><td class="muted">${esc(item.time)}</td><td>${esc(item.author)}</td><td><div class="table-actions">${iconButton("eye", "预览")}${iconButton("edit", "编辑")}</div></td></tr>`).join("")}</tbody></table></div>
        </section>
        <section class="grid grid-2 mt-16">
          <article class="card"><div class="card-head"><div><h2 class="card-title">本周活动日历</h2><p class="card-subtitle">2026-11-09 → 2026-11-15</p></div></div><div class="card-body"><div class="grid" style="grid-template-columns:repeat(7,1fr);gap:6px">${["一 09","二 10","三 11","四 12","五 13","六 14","日 15"].map((day, index) => `<div style="min-height:102px;padding:7px;background:${index === 3 ? "var(--gold-soft)" : "#fafbfc"};border:1px solid ${index === 3 ? "#ecd6ae" : "var(--line)"};border-radius:7px"><div class="small muted">${day}</div>${index === 1 ? `<span class="tag gold" style="margin-top:7px">竞技场 S3</span>` : ""}${index === 3 ? `<span class="tag gold" style="margin-top:7px">月华庆典</span>` : ""}${index === 5 ? `<span class="tag gold" style="margin-top:7px">沙巴克</span><span class="tag" style="margin-top:5px">双倍经验</span>` : ""}</div>`).join("")}</div></div></article>
          <article class="card"><div class="card-head"><div><h2 class="card-title">渠道与发布规则</h2><p class="card-subtitle">公告按渠道模板输出</p></div></div><div class="card-body data-list"><div class="data-row"><span>游戏内</span><strong>纯文本 · 最长 800 字</strong></div><div class="data-row"><span>官网</span><strong>Markdown / HTML</strong></div><div class="data-row"><span>社群</span><strong>短文本 + 图片</strong></div><div class="data-row"><span>大区维护</span><strong class="warning-text">必须提前 24 小时</strong></div><div class="data-row"><span>审批</span><strong>全服公告需发布审核</strong></div></div></article>
        </section>
      </div>`;
  }

  function mergeSteps() {
    return ["预检", "冻结", "备份", "映射", "迁移", "校验", "切换", "解冻", "回滚窗口"];
  }

  function renderMerges() {
    return `
      <div class="page">
        ${pageHeader({ title: "合区中心", description: "合区是高风险有状态操作。预检、冻结、备份、ID 映射、迁移、校验、切换和回滚窗口必须全程可追踪。", eyebrow: "MERGE OPERATIONS", actions: `${btn("冲突策略", "book", "btn-secondary", 'data-action="open-modal" data-modal="merge-policy"')}${btn("新建合区任务", "plus", "btn-primary", 'data-action="open-modal" data-modal="create-merge"')}` })}
        <section class="risk-box" style="margin-bottom:14px">${I("alert", 17)}<span><strong>当前有 1 个合区任务处于预检阶段。</strong> 三区 → 一区检测到 3 个名称冲突和 1 个拍卖引用问题，任何 error 都会阻止切换。</span></div>
        <section class="grid grid-4">
          ${kpi({ label: "进行中合区", value: "1", icon: "merge", tone: "gold", meta: "三区 → 一区 · 预检 38%" })}
          ${kpi({ label: "计划窗口", value: "11-12", unit: "02:00", icon: "calendar", tone: "blue", meta: "预计停机 45 分钟" })}
          ${kpi({ label: "冲突项", value: "3", icon: "alert", tone: "red", meta: "名称 2 · 拍卖 1" })}
          ${kpi({ label: "历史成功率", value: "1", unit: "/ 2", icon: "shield", tone: "violet", meta: "1 次回滚 · 已归档" })}
        </section>
        <section class="card mt-16">
          <div class="card-head"><div><h2 class="card-title">合区执行阶段</h2><p class="card-subtitle">当前任务 merge-202611-01 · 三区 → 一区</p></div>${status("preflight")}</div>
          <div class="card-body"><div class="pipeline" style="grid-template-columns:repeat(9,minmax(0,1fr))">${mergeSteps().map((step, index) => `<div class="pipeline-step ${index < 1 ? "active" : ""}"><div class="pipeline-label">0${index + 1}</div><div class="pipeline-value" style="font-size:15px">${index === 0 ? "38%" : index === 1 ? "→" : "·"}</div><div class="small muted">${esc(step)}</div></div>`).join("")}</div></div>
        </section>
        <section class="split mt-16">
          <div class="split-main stack">
            <article class="card"><div class="card-head"><div><h2 class="card-title">合区任务</h2><p class="card-subtitle">当前与历史任务</p></div><div class="filter-pills"><span class="chip active">全部</span><span class="chip">进行中</span><span class="chip">已完成</span><span class="chip">已回滚</span></div></div><div class="table-wrap"><table><thead><tr><th>任务</th><th>源区 → 目标区</th><th>状态</th><th>进度</th><th>窗口</th><th>问题</th><th>负责人</th><th></th></tr></thead><tbody>${D.MERGES.map(merge => `<tr><td><span class="cell-main mono">${esc(merge.id)}</span><div class="cell-sub">${esc(merge.scope)}</div></td><td><span class="strong">${esc(merge.source)}</span><div class="cell-sub">→ ${esc(merge.target)}</div></td><td>${status(merge.status)}</td><td><div class="progress-row"><div class="progress ${merge.status === "rolled_back" ? "red" : ""}"><span style="width:${merge.progress}%"></span></div><span class="progress-value">${merge.progress}%</span></div></td><td class="muted">${esc(merge.windows)}</td><td class="${merge.issues ? "warning-text" : "success-text"}">${merge.issues}</td><td>${esc(merge.owner)}</td><td><div class="table-actions">${iconButton("eye", "查看合区任务")}${iconButton("more", "更多")}</div></td></tr>`).join("")}</tbody></table></div></article>
            <article class="card"><div class="card-head"><div><h2 class="card-title">预检报告</h2><p class="card-subtitle">三区 → 一区 · 最近更新今天 14:04</p></div><span class="status warning">3 个问题</span></div><div class="table-wrap"><table><thead><tr><th>检查项</th><th>结果</th><th>详情</th><th>处理建议</th></tr></thead><tbody><tr><td>Schema 兼容</td><td>${status("success", "通过")}</td><td class="muted">v1 → v1</td><td>—</td></tr><tr><td>内容版本</td><td>${status("success", "通过")}</td><td class="muted">2026.11.08.2 → 2026.11.09.1</td><td>合区后统一升级</td></tr><tr><td>角色名冲突</td><td>${status("warning", "2 项")}</td><td class="muted">目标区保留原名</td><td>源区角色发放改名券</td></tr><tr><td>拍卖引用</td><td>${status("danger", "1 项")}</td><td class="muted">拍卖 8841 引用已删除物品</td><td>冻结前人工处理</td></tr><tr><td>资产哈希</td><td>${status("success", "通过")}</td><td class="muted">抽样 10,000 角色</td><td>—</td></tr></tbody></table></div></article>
          </div>
          <aside class="split-side stack">
            <article class="card"><div class="card-head"><div><h2 class="card-title">任务摘要</h2><p class="card-subtitle">merge-202611-01</p></div></div><div class="card-body data-list"><div class="data-row"><span>源区</span><strong>传奇三区 · 逐月</strong></div><div class="data-row"><span>目标区</span><strong>传奇一区 · 龙腾</strong></div><div class="data-row"><span>冻结窗口</span><strong>02:00 — 02:45</strong></div><div class="data-row"><span>迁移范围</span><strong>账号 / 角色 / 行会 / 资产</strong></div><div class="data-row"><span>备份</span><strong class="success-text">已完成</strong></div><div class="data-row"><span>回滚窗口</span><strong>72 小时</strong></div></div><div class="card-foot"><button class="btn btn-danger" style="width:100%" data-action="confirm-merge">${I("merge", 13)}进入冻结与切换</button></div></article>
            <article class="card"><div class="card-head"><div><h2 class="card-title">冲突策略</h2><p class="card-subtitle">默认策略可在任务中覆盖</p></div></div><div class="card-body data-list"><div class="data-row"><span>账号重复</span><strong>按身份合并</strong></div><div class="data-row"><span>角色名</span><strong>目标区保留</strong></div><div class="data-row"><span>行会名</span><strong>目标区保留</strong></div><div class="data-row"><span>物品 ID</span><strong>重新分配全局 ID</strong></div><div class="data-row"><span>攻城状态</span><strong>保留目标区</strong></div><div class="data-row"><span>排行榜</span><strong>合区后重算</strong></div></div></article>
            <article class="card"><div class="card-head"><div><h2 class="card-title">回滚条件</h2></div></div><div class="card-body validation-list"><div class="validation-item error">${I("xCircle", 12)}<span>资产总额差异 &gt; 0.01%</span></div><div class="validation-item error">${I("xCircle", 12)}<span>引用完整性 error</span></div><div class="validation-item error">${I("xCircle", 12)}<span>切换后登录失败率 &gt; 2%</span></div><div class="validation-item warn">${I("warning", 12)}<span>Tick P95 &gt; 80ms 持续 5 分钟</span></div></div></article>
          </aside>
        </section>
      </div>`;
  }

  function renderPlayers() {
    return `
      <div class="page">
        ${pageHeader({ title: "玩家查询", description: "按账号、角色、区服和状态检索玩家；查看角色资产、登录会话、封禁和订单记录。所有敏感字段按权限脱敏。", eyebrow: "PLAYER LOOKUP", actions: `${btn("导出查询结果", "download", "btn-secondary", 'data-action="toast" data-message="查询结果导出任务已创建"')}${btn("高级筛选", "filter", "btn-primary", 'data-action="toast" data-message="高级筛选面板已打开"')}` })}
        <section class="card"><div class="card-body"><div class="grid" style="grid-template-columns:1.2fr .8fr .8fr .7fr auto;align-items:end"><div class="field" style="margin:0"><label>账号 / 角色名</label><div class="search-field">${I("search", 13)}<input placeholder="例如 mir_88421 或 风起云涌"></div></div><div class="field" style="margin:0"><label>区服</label><select><option>全部区服</option>${D.ZONES.map(z => `<option>${esc(z.name)}</option>`).join("")}</select></div><div class="field" style="margin:0"><label>状态</label><select><option>全部状态</option><option>在线</option><option>离线</option><option>封禁</option><option>风险命中</option></select></div><div class="field" style="margin:0"><label>等级</label><input placeholder="≥ 1" aria-label="等级"></div><button class="btn btn-primary" data-action="toast" data-message="已执行玩家查询">${I("search", 13)}查询</button></div></div></section>
        <section class="split mt-16">
          <div class="split-main card">
            <div class="toolbar"><div class="toolbar-left"><span class="tag">查询结果 · 5 条</span><span class="tag">数据已脱敏</span></div><div class="toolbar-right"><button class="icon-btn" data-action="toast" data-message="已刷新">${I("refresh", 15)}</button></div></div>
            <div class="table-wrap"><table><thead><tr><th>角色</th><th>账号</th><th>区服</th><th>等级 / 职业</th><th>状态</th><th>最近登录</th><th></th></tr></thead><tbody>${D.ONLINE_PLAYERS.map((player, index) => `<tr class="${index === 0 ? "selected" : ""}"><td><div class="cell-stack">${avatar(player.name, index % 2 ? "sm blue" : "sm gold")}<div><div class="cell-main">${esc(player.name)}</div><div class="cell-sub">${esc(player.map)}</div></div></div></td><td class="mono small">${esc(player.account)}</td><td>${esc(player.zone)}</td><td>Lv.${player.level} · ${esc(player.class)}</td><td>${status(player.status)}</td><td class="muted">${esc(player.login)}</td><td><div class="table-actions">${iconButton("eye", "查看玩家")}</div></td></tr>`).join("")}</tbody></table></div>
            <div class="paginator"><span>显示 1–5，共 28,491 个角色</span><div class="page-buttons"><button class="page-btn active">1</button><button class="page-btn">2</button><button class="page-btn">3</button><button class="page-btn">${I("chevronRight", 11)}</button></div></div>
          </div>
          <aside class="split-side stack">
            <article class="detail-hero"><div class="eyebrow">PLAYER PROFILE</div><h3>风起云涌</h3><p>mir_88421 · 传奇一区 · 龙腾</p><div class="hero-meta"><span class="hero-tag">Lv.48 战士</span><span class="hero-tag">在线</span><span class="hero-tag">账号健康</span></div></article>
            <article class="card"><div class="detail-section"><div class="detail-section-title"><span>角色概览</span><span class="status success">正常</span></div><div class="metric-mini-grid"><div class="metric-mini"><span>等级</span><strong>48</strong></div><div class="metric-mini"><span>战力</span><strong>28,640</strong></div><div class="metric-mini"><span>行会</span><strong style="font-size:11px">龙腾天下</strong></div><div class="metric-mini"><span>今日在线</span><strong>3.4h</strong></div></div></div><div class="detail-section"><div class="detail-section-title"><span>账号与安全</span></div><div class="data-list"><div class="data-row"><span>当前地图</span><strong>祖玛寺庙三层</strong></div><div class="data-row"><span>当前坐标</span><strong class="mono">(152, 112) · 东</strong></div><div class="data-row"><span>最后登录 IP</span><strong class="mono">112.81.•.•</strong></div><div class="data-row"><span>登录设备</span><strong>Windows Client</strong></div><div class="data-row"><span>封禁记录</span><strong class="success-text">无</strong></div><div class="data-row"><span>风险标签</span><strong>无</strong></div></div></div><div class="card-foot flex gap-6"><button class="btn btn-secondary" style="flex:1" data-action="toast" data-message="已打开角色资产">${I("bag", 12)}资产</button><button class="btn btn-secondary" style="flex:1" data-action="toast" data-message="已打开登录历史">${I("clock", 12)}登录历史</button></div></article>
            <article class="card"><div class="card-head"><div><h2 class="card-title">高风险操作</h2><p class="card-subtitle">需要原因和审批</p></div></div><div class="card-body stack-sm"><button class="btn btn-secondary" data-action="open-modal" data-modal="gm-command">${I("mail", 13)}发送补偿邮件</button><button class="btn btn-secondary" data-action="open-modal" data-modal="gm-command">${I("gift", 13)}发放物品</button><button class="btn btn-danger" data-action="confirm-high-risk" data-message="已提交封禁账号操作">${I("ban", 13)}封禁 / 解封</button></div></article>
          </aside>
        </section>
      </div>`;
  }

  function renderOrders() {
    return `
      <div class="page">
        ${pageHeader({ title: "订单与发货", description: "支付订单、支付流水、发货 Outbox、重试和退款。发货按 order_id 幂等，重复回调不会重复发放。", eyebrow: "PAYMENT & DELIVERY", actions: `${btn("导出订单", "download", "btn-secondary", 'data-action="toast" data-message="订单导出任务已创建"')}${btn("进入对账", "scale", "btn-primary", 'data-action="navigate" data-route="reconciliation"')}` })}
        <section class="grid grid-4">
          ${kpi({ label: "今日订单", value: "1,284", icon: "receipt", tone: "blue", trend: { text: "8.7%", cls: "up" }, meta: "支付成功率 99.97%" })}
          ${kpi({ label: "今日流水", value: "¥ 186,420", icon: "coins", tone: "green", meta: "含退款 ¥ 712" })}
          ${kpi({ label: "待发货", value: "7", icon: "clock", tone: "gold", meta: "最久 3 分 12 秒" })}
          ${kpi({ label: "发货失败", value: "1", icon: "alert", tone: "red", meta: "重试 2 / 5" })}
        </section>
        <section class="split mt-16">
          <div class="split-main card">
            <div class="toolbar"><div class="toolbar-left"><label class="search-field">${I("search", 13)}<input data-table-filter="#order-table" placeholder="搜索订单号、玩家、商品..."></label><select class="select"><option>全部状态</option><option>已支付</option><option>发货中</option><option>已发货</option><option>失败</option></select><select class="select"><option>全部渠道</option><option>微信支付</option><option>支付宝</option><option>Apple Pay</option></select></div><div class="toolbar-right"><span class="tag">最近 1 小时</span>${iconButton("filter", "筛选")}</div></div>
            <div class="table-wrap"><table id="order-table"><thead><tr><th>订单</th><th>玩家</th><th>商品</th><th>金额</th><th>渠道</th><th>订单状态</th><th>发货</th><th>风险</th><th></th></tr></thead><tbody>${D.ORDERS.map(order => `<tr><td><span class="mono small">${esc(order.id)}</span><div class="cell-sub">${esc(order.time)}</div></td><td>${esc(order.player)}<div class="cell-sub">${esc(order.zone)}</div></td><td>${esc(order.product)}</td><td class="strong">${esc(order.amount)}</td><td><span class="tag">${esc(order.channel)}</span></td><td>${status(order.status)}</td><td>${order.delivery.includes("重试") ? `<span class="warning-text">${esc(order.delivery)}</span>` : esc(order.delivery)}</td><td>${status(order.risk === "中" ? "medium" : "low")}</td><td><div class="table-actions">${iconButton("eye", "查看订单")}${order.status === "failed" ? `<button class="icon-btn" title="重试发货" data-action="retry-job">${I("refresh", 14)}</button>` : ""}</div></td></tr>`).join("")}</tbody></table></div>
            <div class="paginator"><span>显示 1–6，共 1,284 笔</span><div class="page-buttons"><button class="page-btn active">1</button><button class="page-btn">2</button><button class="page-btn">3</button><button class="page-btn">${I("chevronRight", 11)}</button></div></div>
          </div>
          <aside class="split-side stack">
            <article class="card"><div class="card-head"><div><h2 class="card-title">订单状态机</h2><p class="card-subtitle">服务端权威 · 幂等发货</p></div></div><div class="card-body data-list"><div class="data-row"><span>created</span><strong>创建订单</strong></div><div class="data-row"><span>paid</span><strong class="success-text">验签通过</strong></div><div class="data-row"><span>delivering</span><strong>Outbox 投递</strong></div><div class="data-row"><span>delivered</span><strong class="success-text">区服确认</strong></div><div class="data-row"><span>failed</span><strong class="danger-text">重试 / 人工</strong></div><div class="data-row"><span>refunded</span><strong>反向分录</strong></div></div></article>
            <article class="card"><div class="card-head"><div><h2 class="card-title">发货异常</h2><p class="card-subtitle">需要运营或财务处理</p></div><span class="status danger">1</span></div><div class="card-body alert-list"><div class="alert-item danger"><span class="alert-mark" style="color:var(--red);background:var(--red-soft)">${I("alert", 13)}</span><div class="alert-copy"><strong>MIR-20261110-0435 发货失败</strong><span>逐月区 Agent 超时，已重试 2 次；订单已支付，禁止用户重复购买。</span><div class="alert-time">6 分钟前</div></div></div></div><div class="card-foot flex gap-6"><button class="btn btn-secondary" style="flex:1" data-action="retry-job">${I("refresh", 12)}重试</button><button class="btn btn-primary" style="flex:1" data-action="toast" data-message="已创建人工发货任务">人工处理</button></div></article>
            <article class="card"><div class="card-head"><div><h2 class="card-title">支付渠道</h2></div></div><div class="card-body data-list"><div class="data-row"><span>微信支付</span><strong class="success-text">正常 · 72%</strong></div><div class="data-row"><span>支付宝</span><strong class="success-text">正常 · 24%</strong></div><div class="data-row"><span>Apple Pay</span><strong class="warning-text">回调延迟 1.8s</strong></div></div></article>
          </aside>
        </section>
      </div>`;
  }

  function renderReconciliation() {
    return `
      <div class="page">
        ${pageHeader({ title: "财务对账", description: "订单、支付流水、账本和区服发货的每日对账。差异进入人工处理任务，账本只追加不修改。", eyebrow: "FINANCE CONTROL", actions: `${btn("导出日报", "download", "btn-secondary", 'data-action="toast" data-message="对账日报导出任务已创建"')}${btn("发起对账", "refresh", "btn-primary", 'data-action="toast" data-message="2026-11-10 对账任务已启动"')}` })}
        <section class="card"><div class="card-body"><div class="grid" style="grid-template-columns:1fr 1fr 1fr auto;align-items:end"><div class="field" style="margin:0"><label>开始日期</label><input value="2026-11-01" aria-label="开始日期"></div><div class="field" style="margin:0"><label>结束日期</label><input value="2026-11-10" aria-label="结束日期"></div><div class="field" style="margin:0"><label>区服</label><select><option>全部区服</option>${D.ZONES.map(z => `<option>${esc(z.name)}</option>`).join("")}</select></div><button class="btn btn-secondary" data-action="toast" data-message="筛选已应用">${I("filter", 13)}应用筛选</button></div></div></section>
        <section class="grid grid-4 mt-16">
          ${kpi({ label: "对账周期", value: "11-01 → 11-10", icon: "calendar", tone: "blue", meta: "最近 10 天" })}
          ${kpi({ label: "账本总额", value: "¥ 1,284,960", icon: "scale", tone: "green", meta: "订单 / 退款 / 补偿" })}
          ${kpi({ label: "差异金额", value: "¥ 64.80", icon: "alert", tone: "red", meta: "1 笔待处理" })}
          ${kpi({ label: "对账状态", value: "99.99", unit: "%", icon: "checkCircle", tone: "violet", meta: "11,284 笔已匹配" })}
        </section>
        <section class="grid grid-3 mt-16">
          <article class="card chart-card span-2"><div class="card-head"><div><h2 class="card-title">每日收入与发货</h2><p class="card-subtitle">订单金额与已确认发货金额</p></div><div class="chart-legend" style="padding:0"><span><i class="legend-dot"></i>订单</span><span><i class="legend-dot blue"></i>发货</span></div></div><div class="card-body">${barChart([118, 142, 136, 154, 168, 162, 176, 184, 179, 186], ["01","02","03","04","05","06","07","08","09","10"])}</div></article>
          <article class="card"><div class="card-head"><div><h2 class="card-title">对账构成</h2><p class="card-subtitle">今日账本分类</p></div></div><div class="card-body donut-row"><div class="donut"><div class="donut-center"><strong>¥186k</strong><span>今日总额</span></div></div><div class="donut-legend"><div class="donut-legend-row"><span>游戏充值</span><strong>82%</strong></div><div class="donut-legend-row"><span>月卡订阅</span><strong>12%</strong></div><div class="donut-legend-row"><span>活动礼包</span><strong>5%</strong></div><div class="donut-legend-row"><span>其他</span><strong>1%</strong></div></div></div></article>
        </section>
        <section class="grid grid-2 mt-16">
          <article class="card"><div class="card-head"><div><h2 class="card-title">差异处理</h2><p class="card-subtitle">只有 error 差异需要人工介入</p></div><span class="status danger">1 笔</span></div><div class="table-wrap"><table><thead><tr><th>类型</th><th>订单</th><th>金额</th><th>状态</th><th>处理</th></tr></thead><tbody><tr><td>发货金额差异</td><td class="mono small">MIR-20261110-0434</td><td>¥ 64.80</td><td>${status("pending")}</td><td><button class="btn btn-secondary btn-sm" data-action="toast" data-message="差异处理任务已打开">处理</button></td></tr><tr><td>支付渠道延迟</td><td class="mono small">MIR-20261110-0435</td><td>¥ 648.00</td><td>${status("warning", "自动重试")}</td><td><button class="btn btn-ghost btn-sm" data-action="toast" data-message="已查看重试详情">查看</button></td></tr></tbody></table></div></article>
          <article class="card"><div class="card-head"><div><h2 class="card-title">账本摘要</h2><p class="card-subtitle">只追加分录 · 退款使用反向分录</p></div><a class="btn btn-ghost btn-sm" href="#/orders">查看订单 ${I("arrowRight", 12)}</a></div><div class="card-body data-list"><div class="data-row"><span>充值入账</span><strong class="success-text">+ ¥ 187,132.00</strong></div><div class="data-row"><span>退款出账</span><strong class="danger-text">− ¥ 712.00</strong></div><div class="data-row"><span>补偿金币折算</span><strong>¥ 0.00</strong></div><div class="data-row"><span>未匹配发货</span><strong>1 笔</strong></div><div class="data-row"><span>Outbox 重试</span><strong>2 次</strong></div></div></article>
        </section>
      </div>`;
  }

  function renderApprovals() {
    return `
      <div class="page">
        ${pageHeader({ title: "审批中心", description: "发布、回滚、合区、批量补偿、退款和权限变更进入统一审批流。审批人与申请人分离，结果写入审计。", eyebrow: "APPROVAL WORKFLOW", actions: `${btn("审批规则", "settings", "btn-secondary", 'data-action="toast" data-message="审批规则已打开"')}${btn("我的待办", "check", "btn-primary", 'data-action="toast" data-message="已筛选我的待办"')}` })}
        <section class="grid grid-4">
          ${kpi({ label: "待我审批", value: "4", icon: "clock", tone: "gold", meta: "2 高风险 · 2 中风险" })}
          ${kpi({ label: "今日已处理", value: "12", icon: "checkCircle", tone: "green", meta: "平均处理 6 分 42 秒" })}
          ${kpi({ label: "审批通过率", value: "91.7", unit: "%", icon: "shield", tone: "blue", meta: "近 30 天" })}
          ${kpi({ label: "超时待办", value: "0", icon: "timer", tone: "violet", meta: "SLA 4 小时" })}
        </section>
        <section class="split mt-16">
          <div class="split-main">
            <div class="card"><div class="toolbar"><div class="toolbar-left"><div class="filter-pills"><span class="chip active">全部待办 4</span><span class="chip">高风险 2</span><span class="chip">内容发布</span><span class="chip">GM / 财务</span></div></div><div class="toolbar-right"><span class="tag">按风险排序</span></div></div><div class="card-body"><div class="approval-list">
              ${D.APPROVALS.map(item => `<article class="approval-card"><span class="approval-risk ${item.risk}">${I(item.risk === "high" ? "alert" : item.risk === "medium" ? "warning" : "info", 16)}</span><div class="approval-copy"><div class="flex justify-between items-center gap-8"><strong>${esc(item.title)}</strong>${status(item.risk)}</div><p>${esc(item.reason)}</p><div class="approval-meta"><span>${esc(item.id)}</span><span>${esc(item.requester)} · ${esc(item.role)}</span><span>${esc(item.time)}</span><span>${esc(item.scope)}</span></div><div class="mt-8 small muted">${esc(item.checks)}</div></div><div class="approval-actions"><button class="btn btn-secondary btn-sm" data-action="open-modal" data-modal="reject">拒绝</button><button class="btn btn-primary btn-sm" data-action="open-modal" data-modal="approve">批准</button></div></article>`).join("")}
            </div></div></div>
          </div>
          <aside class="split-side stack">
            <article class="card"><div class="card-head"><div><h2 class="card-title">审批规则</h2><p class="card-subtitle">按操作类型与风险自动路由</p></div></div><div class="card-body data-list"><div class="data-row"><span>内容发布</span><strong>发布审核</strong></div><div class="data-row"><span>内容回滚</span><strong>发布审核 + 管理员</strong></div><div class="data-row"><span>批量补偿</span><strong>GM + 财务复核</strong></div><div class="data-row"><span>合区切换</span><strong>管理员 + 发布审核</strong></div><div class="data-row"><span>权限变更</span><strong>平台管理员</strong></div><div class="data-row"><span>退款</span><strong>财务 + 管理员</strong></div></div></article>
            <article class="card"><div class="card-head"><div><h2 class="card-title">审批 SLA</h2><p class="card-subtitle">超时自动提醒与升级</p></div></div><div class="card-body"><div class="data-list"><div class="data-row"><span>高风险管理</span><strong>30 分钟提醒</strong></div><div class="data-row"><span>中风险管理</span><strong>2 小时提醒</strong></div><div class="data-row"><span>普通管理</span><strong>4 小时提醒</strong></div><div class="data-row"><span>升级路径</span><strong>平台管理员</strong></div></div><div class="divider"></div><div class="validation-list" style="padding:0"><div class="validation-item ok">${I("checkCircle", 12)}<span>审批人与申请人分离</span></div><div class="validation-item ok">${I("checkCircle", 12)}<span>高风险操作重新校验 MFA</span></div><div class="validation-item ok">${I("checkCircle", 12)}<span>审批结果不可编辑</span></div></div></div></article>
            <article class="card"><div class="card-head"><div><h2 class="card-title">最近处理</h2></div><a class="btn btn-ghost btn-sm" href="#/audit">审计 ${I("arrowRight", 12)}</a></div><div class="card-body timeline"><div class="timeline-item"><span class="timeline-dot gold">${I("check", 10)}</span><div class="timeline-copy"><strong>批准订单退款 ¥648</strong><span>林策 · 12 分钟前</span></div></div><div class="timeline-item"><span class="timeline-dot">${I("xCircle", 10)}</span><div class="timeline-copy"><strong>拒绝首次 GM 补偿申请</strong><span>陈屿 · 今天 14:58</span></div></div></div></article>
          </aside>
        </section>
      </div>`;
  }

  function accountsView() {
    if (!isLive()) {
      return {
        rows: D.ACCOUNTS.map((account, index) => ({ ...account, id: index + 1, roles: account.roles || [] })),
        live: false,
        loading: false
      };
    }
    const payload = state.live.accounts;
    if (!payload) return { rows: [], live: true, loading: !state.failed.has("accounts") };
    if (payload.error) return { rows: [], live: true, loading: false, error: payload.error };
    return {
      rows: (payload.items || []).map(account => ({
        id: account.id,
        name: account.displayName || account.username,
        username: account.username,
        email: account.email || "—",
        roles: Array.isArray(account.roles) ? account.roles : [],
        status: account.status,
        mfa: Boolean(account.mfaEnabled),
        last: formatDateTime(account.lastLoginAt),
        created: formatDateTime(account.createdAt),
        ip: "—",
        avatar: account.displayName || account.username
      })),
      live: true,
      loading: false,
      loadedAt: payload.loadedAt
    };
  }

  function accountRowActions(account, live, canWrite) {
    if (!live || !canWrite) return `${iconButton("eye", "查看账号")}${iconButton("more", "更多")}`;
    const isSelf = account.username === currentUser()?.username;
    const statusAction = isSelf ? "" : iconButton(
      account.status === "active" ? "ban" : "checkCircle",
      account.status === "active" ? "停用账号" : "启用账号",
      `data-action="account-toggle-status" data-id="${esc(account.id)}" data-status="${esc(account.status)}" data-username="${esc(account.username)}"`
    );
    const resetAction = isSelf ? "" : iconButton(
      "key",
      "重置密码",
      `data-action="open-modal" data-modal="reset-account-password" data-id="${esc(account.id)}" data-username="${esc(account.username)}"`
    );
    const rolesAction = iconButton(
      "userCheck",
      "调整角色",
      `data-action="open-modal" data-modal="edit-account-roles" data-id="${esc(account.id)}" data-username="${esc(account.username)}" data-roles="${esc(account.roles.join(","))}"`
    );
    return `${rolesAction}${statusAction}${resetAction}`;
  }

  function renderAccounts() {
    const view = accountsView();
    const rows = view.rows;
    const live = view.live;
    const hasData = !live || (Array.isArray(state.live.accounts?.items) && !view.error);
    const mfaCount = rows.filter(account => account.mfa).length;
    const activeCount = rows.filter(account => account.status === "active").length;
    const disabledCount = rows.filter(account => account.status === "disabled").length;
    const lockedCount = rows.filter(account => account.status === "locked").length;
    const mfaCoverage = rows.length ? Math.round(mfaCount / rows.length * 100) : null;
    const roleOptions = Array.from(new Set(rows.flatMap(account => account.roles || [])));
    const canCreate = !live || hasPermission("account:write");
    const canWrite = !live || hasPermission("account:write");
    const actions = `${btn("导出账号", "download", "btn-secondary", 'data-action="toast" data-message="账号清单导出任务已创建"')}${canCreate ? btn("创建账号", "plus", "btn-primary", 'data-action="open-modal" data-modal="new-account"') : ""}`;
    const loadingRow = `<tr><td colspan="7"><div class="empty-inline">${I("refresh", 14)} 正在从 Control API 读取管理账号...</div></td></tr>`;
    return `
      <div class="page">
        ${pageHeader({ title: "管理账号", description: "控制面账号与玩家账号完全分离。密码使用 Argon2id，支持短时 JWT、刷新令牌轮换和会话撤销。", eyebrow: "ADMIN IDENTITY", actions })}
        ${view.error ? `<div class="api-notice warning">${I("alert", 14)}<span>读取账号失败：${esc(view.error.message || "未知错误")}</span></div>` : ""}
        <section class="grid grid-4">
          ${kpi({ label: "管理账号", value: hasData ? formatNumber(rows.length) : "—", icon: "users", tone: "blue", meta: live ? "来自 control_account" : "2 管理员 · 5 版本作者" })}
          ${kpi({ label: "MFA 覆盖", value: mfaCoverage === null ? "—" : String(mfaCoverage), unit: mfaCoverage === null ? "" : "%", icon: "shield", tone: "green", meta: hasData ? `${mfaCount} / ${rows.length} 已启用` : "等待账号数据" })}
          ${kpi({ label: "启用账号", value: hasData ? formatNumber(activeCount) : "—", icon: "checkCircle", tone: "violet", meta: live ? "status = active" : "可正常登录控制面" })}
          ${kpi({ label: "停用 / 锁定", value: hasData ? formatNumber(disabledCount + lockedCount) : "—", icon: "lock", tone: "red", meta: live ? "status = disabled" : "1 个锁定账号" })}
        </section>
        <section class="split mt-16">
          <div class="split-main card">
            <div class="toolbar"><div class="toolbar-left"><label class="search-field">${I("search", 13)}<input data-table-filter="#account-table" placeholder="搜索姓名、用户名、角色..."></label><select class="select"><option>全部角色</option>${roleOptions.map(role => `<option>${esc(roleLabel(role))}</option>`).join("")}</select><select class="select"><option>全部状态</option><option>启用</option><option>停用</option></select></div><div class="toolbar-right"><span class="tag">${live ? "实时 API" : "演示数据"}</span>${iconButton("refresh", "刷新", 'data-action="reload-accounts"')}</div></div>
            <div class="table-wrap"><table id="account-table"><thead><tr><th>账号</th><th>角色</th><th>MFA</th><th>状态</th><th>最近登录</th><th>${live ? "账号 ID" : "来源 IP"}</th><th></th></tr></thead><tbody>${view.loading ? loadingRow : rows.map(account => `<tr><td><div class="cell-stack">${avatar(account.avatar, account.status === "locked" || account.status === "disabled" ? "sm red" : "sm gold")}<div><div class="cell-main">${esc(account.name)}</div><div class="cell-sub">${esc(account.username)} · ${esc(account.email)}</div></div></div></td><td><div class="cell-main">${esc(roleLabelList(account.roles))}</div><div class="cell-sub mono">${esc(account.roles.join(" · ") || "—")}</div></td><td>${account.mfa ? status("success", "已启用") : status("warning", "未启用")}</td><td>${account.status === "active" ? status("success", "启用") : account.status === "locked" ? status("locked") : status("danger", "停用")}</td><td class="muted">${esc(account.last)}</td><td class="mono small">${live ? `#${esc(account.id)}` : esc(account.ip)}</td><td><div class="table-actions">${accountRowActions(account, live, canWrite)}</div></td></tr>`).join("")}${!view.loading && rows.length === 0 ? `<tr><td colspan="7"><div class="empty-inline">当前账号列表为空。</div></td></tr>` : ""}</tbody></table></div>
            <div class="paginator"><span>${hasData ? `共 ${rows.length} 个管理账号` : "等待账号数据"}</span><div class="page-buttons"><button class="page-btn active">1</button></div></div>
          </div>
          <aside class="split-side stack">
            <article class="card"><div class="card-head"><div><h2 class="card-title">MFA 合规</h2><p class="card-subtitle">管理员与发布审核强制 MFA</p></div><span class="status ${mfaCount === rows.length && rows.length ? "success" : "warning"}">${hasData ? `${rows.length - mfaCount} 个缺失` : "—"}</span></div><div class="card-body alert-list">${rows.filter(account => !account.mfa).slice(0, 3).map(account => `<div class="alert-item warning"><span class="alert-mark" style="color:var(--orange);background:var(--orange-soft)">${I("warning", 13)}</span><div class="alert-copy"><strong>${esc(account.name)} · ${esc(roleLabelList(account.roles))}</strong><span>账号尚未启用 MFA，建议在下一次登录前完成绑定。</span></div></div>`).join("")}${disabledCount + lockedCount > 0 ? `<div class="alert-item danger"><span class="alert-mark" style="color:var(--red);background:var(--red-soft)">${I("lock", 13)}</span><div class="alert-copy"><strong>${disabledCount + lockedCount} 个账号不可登录</strong><span>停用或锁定账号保留在审计与身份记录中。</span></div></div>` : ""}${hasData && mfaCount === rows.length && disabledCount + lockedCount === 0 ? `<div class="empty-inline">全部账号已启用 MFA，且没有停用或锁定账号。</div>` : ""}${!hasData || view.loading ? `<div class="empty-inline">等待账号数据。</div>` : ""}</div></article>
            <article class="card"><div class="card-head"><div><h2 class="card-title">${live ? "数据来源" : "当前会话"}</h2><p class="card-subtitle">${live ? "Control API 只读账号接口" : "可即时撤销"}</p></div></div>${live ? `<div class="card-body data-list"><div class="data-row"><span>接口</span><strong class="mono">GET /api/v1/accounts</strong></div><div class="data-row"><span>读取上限</span><strong>200</strong></div><div class="data-row"><span>已加载</span><strong>${esc(formatDateTime(state.live.accounts?.loadedAt))}</strong></div><div class="data-row"><span>IP 字段</span><strong>接口未暴露</strong></div></div>` : `<div class="card-body data-list"><div class="data-row"><span>林策</span><strong>09:42 后到期</strong></div><div class="data-row"><span>周遥</span><strong>11:18 后到期</strong></div><div class="data-row"><span>陈屿</span><strong>06:22 后到期</strong></div><div class="data-row"><span>苏禾</span><strong>14:08 后到期</strong></div></div><div class="card-foot"><button class="btn btn-secondary" style="width:100%" data-action="confirm-high-risk" data-message="已撤销全部非当前会话">${I("logout", 13)}撤销其他会话</button></div>`}</article>
            <article class="card"><div class="card-head"><div><h2 class="card-title">账号策略</h2></div></div><div class="card-body data-list"><div class="data-row"><span>密码算法</span><strong>Argon2id</strong></div><div class="data-row"><span>访问令牌</span><strong>15 分钟</strong></div><div class="data-row"><span>刷新令牌</span><strong>30 天 · 轮换</strong></div><div class="data-row"><span>失败锁定</span><strong>5 次 / 15 分钟</strong></div><div class="data-row"><span>会话校验</span><strong>每请求</strong></div></div></article>
          </aside>
        </section>
      </div>`;
  }

  function rolesView() {
    if (!isLive()) {
      return { roles: D.ROLES, live: false, loading: false };
    }
    const payload = state.live.roles;
    if (!payload) return { roles: [], live: true, loading: !state.failed.has("roles") };
    if (payload.error) return { roles: [], live: true, loading: false, error: payload.error };
    return {
      roles: (payload.items || []).map(role => ({
        code: role.code,
        name: roleLabel(role.code) || role.name,
        apiName: role.name,
        desc: role.description || "—",
        permissions: Array.isArray(role.permissions) ? role.permissions : [],
        members: null,
        system: true
      })),
      live: true,
      loading: false,
      loadedAt: payload.loadedAt
    };
  }

  function renderRoles() {
    const view = rolesView();
    const roles = view.roles;
    const live = view.live;
    const hasData = !live || (Array.isArray(state.live.roles?.items) && !view.error);
    const selectedCode = roles.some(role => role.code === state.selectedRoleCode)
      ? state.selectedRoleCode
      : live ? (roles[0]?.code || "") : "release_approver";
    const selected = roles.find(role => role.code === selectedCode) || roles[0] || null;
    const allPermissions = Array.from(new Set(roles.flatMap(role => role.permissions || [])));
    const highRiskPermissions = allPermissions.filter(permission => /:(publish|rollback|merge|refund|operate|configure)$/.test(permission));
    const writableRoles = roles.filter(role => (role.permissions || []).some(permission => /:(write|publish|rollback|merge|refund|operate|configure)$/.test(permission))).length;
    const permissionSet = new Set(selected?.permissions || []);
    const canWrite = !live || hasPermission("role:write");
    const canAudit = !live || hasPermission("audit:read");
    const actions = `${canAudit ? btn("权限审计", "audit", "btn-secondary", 'data-action="navigate" data-route="audit"') : ""}${canWrite ? btn("保存权限", "save", "btn-primary", 'data-action="toast" data-message="角色权限已保存并写入审计"') : ""}`;
    const permissions = [
      ["内容", true, true, true, false, false], ["发布", true, false, false, true, true], ["区服", true, true, false, false, false],
      ["GM", true, true, false, false, false], ["支付", true, false, false, false, false], ["审计", true, true, false, false, false],
      ["账号", true, false, false, false, false], ["系统", true, false, false, false, false]
    ];
    const demoActions = ["查看", "新建/编辑", "校验", "发布/执行", "审批", "导出"];
    const matrixHead = live
      ? `<tr><th>资源域</th>${PERMISSION_ACTIONS.map(([, label]) => `<th>${esc(label)}</th>`).join("")}</tr>`
      : `<tr><th>资源域</th>${demoActions.map(action => `<th>${esc(action)}</th>`).join("")}</tr>`;
    const matrixBody = live
      ? Object.entries(PERMISSION_RESOURCES).map(([resource, label]) => `<tr><td><span class="strong">${esc(label)}</span><span class="cell-sub mono">${esc(resource)}</span></td>${PERMISSION_ACTIONS.map(([action, , highRisk]) => {
          const granted = permissionSet.has(`${resource}:${action}`);
          return `<td>${granted ? `<span class="${highRisk ? "lock" : "check"}">${I(highRisk ? "lock" : "check", 13)}</span>` : `<span class="minus">—</span>`}</td>`;
        }).join("")}</tr>`).join("")
      : permissions.map(([resource, permissionRead, edit, validate, publish, approve]) => `<tr><td><span class="strong">${esc(resource)}</span></td><td>${permissionRead ? `<span class="check">${I("check", 13)}</span>` : `<span class="minus">—</span>`}</td><td>${edit ? `<span class="check">${I("check", 13)}</span>` : `<span class="minus">—</span>`}</td><td>${validate ? `<span class="check">${I("check", 13)}</span>` : `<span class="minus">—</span>`}</td><td>${publish ? `<span class="lock">${I("lock", 13)}</span>` : `<span class="minus">—</span>`}</td><td>${approve ? `<span class="lock">${I("lock", 13)}</span>` : `<span class="minus">—</span>`}</td><td><span class="check">${I("check", 13)}</span></td></tr>`).join("");
    const loading = `<tr><td colspan="9"><div class="empty-inline">${I("refresh", 14)} 正在从 Control API 读取角色和权限...</div></td></tr>`;
    return `
      <div class="page">
        ${pageHeader({ title: "角色权限", description: live ? "角色与权限来自 Control API 的只读 RBAC 定义。导航和直达路由使用相同权限点判定，写操作仍待后续接入。" : "按资源与动作授权，内置六类角色。高风险权限单独标记并要求 MFA、审批和审计。", eyebrow: "RBAC & PERMISSIONS", actions })}
        ${view.error ? `<div class="api-notice warning">${I("alert", 14)}<span>读取角色失败：${esc(view.error.message || "未知错误")}</span></div>` : ""}
        <section class="grid grid-4">
          ${kpi({ label: "内置角色", value: hasData ? String(roles.length) : "—", icon: "shield", tone: "blue", meta: live ? "control_role" : "不可删除的 system 角色" })}
          ${kpi({ label: "权限点", value: live ? (hasData ? String(allPermissions.length) : "—") : "84", icon: "key", tone: "green", meta: live ? "所有角色权限去重" : "resource:action 模型" })}
          ${kpi({ label: "高风险权限", value: live ? (hasData ? String(highRiskPermissions.length) : "—") : "12", icon: "alert", tone: "red", meta: "发布 / 合区 / 退款 / GM" })}
          ${kpi({ label: live ? "可写角色" : "角色成员", value: live ? (hasData ? String(writableRoles) : "—") : "23", icon: "users", tone: "violet", meta: live ? "成员数需账号聚合" : "6 个管理账号" })}
        </section>
        <section class="split mt-16">
          <div class="split-main card"><div class="card-head"><div><h2 class="card-title">权限矩阵</h2><p class="card-subtitle">当前角色：${esc(selected?.name || "—")} · <span class="mono">${esc(selected?.code || "—")}</span></p></div><span class="tag gold">${live ? `${permissionSet.size} 个权限点` : "system role"}</span></div><div class="card-body"><div class="table-wrap" style="border:1px solid var(--line);border-radius:9px"><table class="matrix"><thead>${matrixHead}</thead><tbody>${view.loading ? loading : matrixBody}${!view.loading && !matrixBody ? `<tr><td colspan="9"><div class="empty-inline">当前角色没有权限定义。</div></td></tr>` : ""}</tbody></table></div><div class="flex gap-12 mt-12 small muted"><span>${I("check", 12)} 已授权</span><span>${I("lock", 12)} 高风险权限</span><span>— 未授权</span></div></div></div>
          <aside class="split-side stack">
            <article class="card"><div class="card-head"><div><h2 class="card-title">角色</h2><p class="card-subtitle">选择角色查看权限</p></div><div class="flex gap-6">${canWrite ? `<button class="icon-btn" data-action="toast" data-message="新建角色面板已打开">${I("plus", 14)}</button>` : ""}${iconButton("refresh", "刷新角色", 'data-action="reload-roles"')}</div></div><div class="card-body role-list" id="role-list">${view.loading ? `<div class="empty-inline">正在读取角色...</div>` : roles.map(role => `<button class="role-item ${role.code === selectedCode ? "active" : ""}" type="button" data-action="select-role" data-code="${esc(role.code)}"><span class="cell-icon ${role.code === "platform_admin" ? "" : role.code === "finance" ? "green" : role.code === "auditor" ? "blue" : "violet"}">${I(role.code === "platform_admin" ? "crown" : role.code === "gm" ? "command" : role.code === "finance" ? "scale" : role.code === "auditor" ? "audit" : role.code === "release_approver" ? "rocket" : "edit", 13)}</span><span style="flex:1;min-width:0"><strong>${esc(role.name)}</strong><span>${esc(role.desc)}</span><span class="mono">${esc(role.code)}</span></span><span class="tag">${live ? role.permissions.length : role.members}</span></button>`).join("")}</div></article>
            <article class="card"><div class="card-head"><div><h2 class="card-title">权限边界</h2><p class="card-subtitle">控制面安全护栏</p></div></div><div class="card-body validation-list"><div class="validation-item ok">${I("checkCircle", 12)}<span>发布者不能审批自己的发布</span></div><div class="validation-item ok">${I("checkCircle", 12)}<span>审计角色只读导出</span></div><div class="validation-item ok">${I("checkCircle", 12)}<span>财务不能修改内容</span></div><div class="validation-item ok">${I("checkCircle", 12)}<span>GM 不能修改角色权限</span></div><div class="validation-item warn">${I("warning", 12)}<span>临时活动权限需设置到期时间</span></div></div></article>
            <article class="card"><div class="card-head"><div><h2 class="card-title">${live ? "当前权限" : "高风险权限"}</h2><p class="card-subtitle">${live ? "选中角色的 permission claim" : "需要 MFA 或审批"}</p></div></div>${live ? `<div class="card-body"><div class="flex gap-6" style="flex-wrap:wrap">${Array.from(permissionSet).sort().map(permission => `<span class="tag mono">${esc(permission)}</span>`).join("") || `<span class="empty-inline">该角色没有权限点。</span>`}</div><div class="data-list mt-12"><div class="data-row"><span>已加载</span><strong>${esc(formatDateTime(state.live.roles?.loadedAt))}</strong></div><div class="data-row"><span>API 名称</span><strong>${esc(selected?.apiName || "—")}</strong></div></div></div>` : `<div class="card-body data-list"><div class="data-row"><span>release:publish</span><strong>发布审核</strong></div><div class="data-row"><span>release:rollback</span><strong>发布审核 + 管理员</strong></div><div class="data-row"><span>zone:merge</span><strong>平台管理员</strong></div><div class="data-row"><span>payment:refund</span><strong>财务 + 管理员</strong></div><div class="data-row"><span>gm:bulk_grant</span><strong>GM + 审批</strong></div></div>`}</article>
          </aside>
        </section>
      </div>`;
  }

  function renderAudit() {
    const rows = auditRows();
    const live = isLive() && Boolean(state.live.audit?.items);
    const denied = rows.filter(event => event.result === "denied").length;
    const failed = rows.filter(event => event.result === "failed" || event.result === "failure").length;
    const highRisk = rows.filter(event => /delete|revoke|rollback|publish|gm|merge/i.test(event.action)).length;
    return `
      <div class="page">
        ${pageHeader({ title: "审计日志", description: "登录、内容变更、发布、GM、支付、权限与审批的只追加审计。记录操作者、IP、请求 ID、前后哈希、结果和原因。", eyebrow: "AUDIT TRAIL", actions: `${btn(live ? "导出当前查询结果" : "导出审计", "download", "btn-secondary", 'data-action="export-audit"')}${btn("查看保留策略", "clock", "btn-primary", 'data-action="toast" data-message="审计保留策略已打开"')}` })}
        <section class="grid grid-4">
          ${kpi({ label: live ? "最近事件" : "今日事件", value: formatNumber(rows.length), icon: "audit", tone: "blue", meta: live ? "来自 audit_event 只追加表" : "成功 1,827 · 拒绝 15" })}
          ${kpi({ label: "权限拒绝", value: formatNumber(denied), icon: "shield", tone: "red", meta: live ? "当前查询窗口" : "集中在内容写入" })}
          ${kpi({ label: "失败事件", value: formatNumber(failed), icon: "alert", tone: "gold", meta: live ? "登录失败与请求错误" : "高风险操作全部已审批" })}
          ${kpi({ label: "高风险动作", value: formatNumber(highRisk), icon: "lock", tone: "green", meta: live ? "关键字匹配，仅供筛查" : "上一归档 11-09 23:00" })}
        </section>
        <section class="card mt-16">
          <div class="toolbar"><div class="toolbar-left"><label class="search-field">${I("search", 13)}<input data-table-filter="#audit-table" placeholder="搜索操作者、动作、资源、IP..."></label><select class="select"><option>全部结果</option><option>成功</option><option>待处理</option><option>拒绝</option></select><select class="select"><option>全部动作</option><option>认证</option><option>内容</option><option>发布</option><option>GM</option><option>支付</option><option>权限</option></select><select class="select"><option>最近 100 条</option><option>今天</option><option>最近 7 天</option><option>最近 30 天</option></select></div><div class="toolbar-right"><span class="tag">只追加</span>${iconButton("refresh", "刷新", 'data-action="reload-audit"')}</div></div>
          <div class="table-wrap"><table id="audit-table"><thead><tr><th>时间</th><th>操作者</th><th>动作</th><th>资源</th><th>IP</th><th>结果</th><th>详情</th><th></th></tr></thead><tbody>${rows.map((event, index) => `<tr class="${index === 1 ? "selected" : ""}"><td class="mono small">${esc(event.time)}</td><td>${esc(event.actor)}</td><td class="mono small">${esc(event.action)}</td><td class="mono small">${esc(event.resource)}</td><td class="mono small">${esc(event.ip)}</td><td>${status(event.result)}</td><td class="muted" style="max-width:280px">${esc(event.detail)}</td><td><div class="table-actions">${iconButton("eye", "查看事件", `data-action="open-audit" data-index="${index}"`)}</div></td></tr>`).join("")}</tbody></table></div>
          <div class="paginator"><span>显示 1–${rows.length}，共 ${formatNumber(rows.length)} 条</span><div class="page-buttons"><button class="page-btn active">1</button><button class="page-btn" ${live ? "disabled" : ""}>2</button><button class="page-btn" ${live ? "disabled" : ""}>3</button><button class="page-btn" ${live ? "disabled" : ""}>${I("chevronRight", 11)}</button></div></div>
        </section>
        <section class="grid grid-2 mt-16">
          <article class="card"><div class="card-head"><div><h2 class="card-title">审计完整性</h2><p class="card-subtitle">防篡改与归档</p></div></div><div class="card-body validation-list"><div class="validation-item ok">${I("checkCircle", 12)}<span>数据库触发器禁止 UPDATE / DELETE</span></div><div class="validation-item ok">${I("checkCircle", 12)}<span>请求 ID 贯穿控制面与 Agent</span></div><div class="validation-item ok">${I("checkCircle", 12)}<span>归档存储只读</span></div><div class="validation-item warn">${I("warning", 12)}<span>外部 WORM 归档尚未接入</span></div></div></article>
          <article class="card"><div class="card-head"><div><h2 class="card-title">安全事件</h2><p class="card-subtitle">登录失败、权限拒绝与异常操作</p></div><span class="status ${denied + failed > 0 ? "danger" : "success"}">${denied + failed} 条异常</span></div><div class="card-body alert-list">${rows.filter(event => event.result === "denied" || event.result === "failed" || event.result === "failure").slice(0, 3).map(event => `<div class="alert-item ${event.result === "denied" ? "danger" : "warning"}"><span class="alert-mark" style="color:var(--${event.result === "denied" ? "red" : "orange"});background:var(--${event.result === "denied" ? "red" : "orange"}-soft)">${I(event.result === "denied" ? "ban" : "warning", 13)}</span><div class="alert-copy"><strong>${esc(event.action)} · ${esc(event.actor)}</strong><span>${esc(event.detail)}</span><div class="alert-time">${esc(event.time)} · ${esc(event.ip)}</div></div></div>`).join("") || `<div class="empty-inline">当前窗口没有拒绝或失败事件。</div>`}</div></article>
        </section>
      </div>`;
  }

  function renderMonitoring() {
    return `
      <div class="page">
        ${pageHeader({ title: "运行监控", description: "控制面、数据面、发布链路和支付回调的日志、指标、追踪与告警。Trace 贯穿后台操作到区服结果。", eyebrow: "OBSERVABILITY", actions: `${btn("打开 Grafana", "external", "btn-secondary", 'data-action="toast" data-message="Grafana 外部链接已打开"')}${btn("刷新指标", "refresh", "btn-primary", 'data-action="toast" data-message="监控指标已刷新"')}` })}
        <section class="grid grid-4">
          ${kpi({ label: "平台可用性", value: "99.98", unit: "%", icon: "activity", tone: "green", meta: "近 30 天" })}
          ${kpi({ label: "区服 Tick P95", value: "24", unit: "ms", icon: "gauge", tone: "blue", meta: "阈值 50ms" })}
          ${kpi({ label: "API 错误率", value: "0.03", unit: "%", icon: "alert", tone: "gold", meta: "5xx / 全部请求" })}
          ${kpi({ label: "活跃告警", value: "4", icon: "bell", tone: "red", meta: "2 高优 · 2 警告" })}
        </section>
        <section class="card mt-16"><div class="card-head"><div><h2 class="card-title">服务健康</h2><p class="card-subtitle">最近探针与依赖状态</p></div><span class="status success">全部关键服务在线</span></div><div class="card-body"><div class="service-grid">${D.SERVICES.map(service => `<div class="service-item"><span class="service-dot ${service.status === "warn" ? "warn" : service.status === "down" ? "down" : ""}"></span><div style="flex:1"><strong>${esc(service.name)}</strong><span>${esc(service.note)}</span></div><span class="small muted">${esc(service.latency)}</span></div>`).join("")}</div></div></section>
        <section class="grid grid-3 mt-16">
          <article class="card chart-card span-2"><div class="card-head"><div><h2 class="card-title">全平台在线与请求</h2><p class="card-subtitle">最近 24 小时聚合</p></div><div class="segmented"><button class="active">24 小时</button><button>7 天</button><button>30 天</button></div></div><div class="chart-wrap">${lineChart(D.chartOnline, { id: "monitor-online", labels: ["00", "06", "12", "18", "23"], label: "在线玩家趋势" })}</div><div class="chart-legend"><span><i class="legend-dot"></i>在线玩家</span><span><i class="legend-dot blue"></i>Control API QPS</span><span><i class="legend-dot green"></i>登录成功率 99.6%</span></div></article>
          <article class="card"><div class="card-head"><div><h2 class="card-title">资源水位</h2><p class="card-subtitle">控制面与数据库</p></div></div><div class="card-body"><div class="donut-row"><div class="donut" style="background:conic-gradient(#3977d5 0 54%, #198a68 54% 76%, #c58b32 76% 91%, #7357c8 91% 100%)"><div class="donut-center"><strong>61%</strong><span>总体水位</span></div></div><div class="donut-legend"><div class="donut-legend-row"><span>PostgreSQL</span><strong>54%</strong></div><div class="donut-legend-row"><span>Redis</span><strong>22%</strong></div><div class="donut-legend-row"><span>对象存储</span><strong>15%</strong></div><div class="donut-legend-row"><span>Worker</span><strong>9%</strong></div></div></div></div></article>
        </section>
        <section class="grid grid-2 mt-16">
          <article class="card"><div class="card-head"><div><h2 class="card-title">活跃告警</h2><p class="card-subtitle">按优先级与影响范围</p></div><a class="btn btn-ghost btn-sm" href="#/jobs">任务中心 ${I("arrowRight", 12)}</a></div><div class="card-body alert-list">${D.ALERTS.map(alert => `<div class="alert-item ${alert.level === "danger" ? "danger" : alert.level === "warning" ? "warning" : ""}"><span class="alert-mark" style="color:var(--${alert.level === "danger" ? "red" : alert.level === "warning" ? "orange" : "blue"});background:var(--${alert.level === "danger" ? "red" : alert.level === "warning" ? "orange" : "blue"}-soft)">${I(alert.level === "danger" ? "alert" : alert.level === "warning" ? "warning" : "info", 13)}</span><div class="alert-copy"><strong>${esc(alert.title)}</strong><span>${esc(alert.detail)}</span><div class="alert-time">${esc(alert.time)}</div></div></div>`).join("")}</div></article>
          <article class="card"><div class="card-head"><div><h2 class="card-title">最近追踪</h2><p class="card-subtitle">OpenTelemetry Trace 样本</p></div><span class="tag">trace_id</span></div><div class="table-wrap"><table><thead><tr><th>Trace</th><th>入口</th><th>耗时</th><th>结果</th></tr></thead><tbody><tr><td class="mono small">4c1e8b…</td><td>发布 2026.11.10.0-rc2</td><td>2.4s</td><td>${status("success")}</td></tr><tr><td class="mono small">8af2d0…</td><td>内容导入 #32</td><td>18.4s</td><td>${status("success")}</td></tr><tr><td class="mono small">91ac33…</td><td>四区热重载</td><td>4.8s</td><td>${status("failed")}</td></tr><tr><td class="mono small">72be41…</td><td>订单发货回调</td><td>842ms</td><td>${status("success")}</td></tr></tbody></table></div></article>
        </section>
      </div>`;
  }

  function renderJobs() {
    return `
      <div class="page">
        ${pageHeader({ title: "任务中心", description: "导入、编译、签名、发布、热重载、开区、合区和资源扫描的异步任务。任务幂等、可重试、可追踪。", eyebrow: "JOB ORCHESTRATOR", actions: `${btn("清理已完成", "trash", "btn-secondary", 'data-action="confirm-high-risk" data-message="已完成任务清理任务已提交"')}${btn("刷新队列", "refresh", "btn-primary", 'data-action="toast" data-message="任务队列已刷新"')}` })}
        <section class="grid grid-4">
          ${kpi({ label: "运行中", value: "2", icon: "play", tone: "blue", meta: "预热 / 资源扫描" })}
          ${kpi({ label: "等待中", value: "1", icon: "clock", tone: "gold", meta: "等待审批" })}
          ${kpi({ label: "今日完成", value: "38", icon: "checkCircle", tone: "green", meta: "成功率 97.4%" })}
          ${kpi({ label: "失败", value: "1", icon: "xCircle", tone: "red", meta: "合区预检" })}
        </section>
        <section class="card mt-16">
          <div class="toolbar"><div class="toolbar-left"><div class="filter-pills"><span class="chip active">全部 6</span><span class="chip">运行中 2</span><span class="chip">等待 1</span><span class="chip">失败 1</span><span class="chip">完成</span></div></div><div class="toolbar-right"><select class="select"><option>全部任务类型</option><option>导入</option><option>发布</option><option>热重载</option><option>开区</option><option>合区</option></select></div></div>
          <div class="table-wrap"><table><thead><tr><th>任务</th><th>类型</th><th>状态</th><th>进度</th><th>执行者</th><th>开始</th><th>耗时</th><th></th></tr></thead><tbody>${D.JOBS.map(job => `<tr><td><span class="cell-main">${esc(job.title)}</span><div class="cell-sub">${esc(job.id)}</div></td><td><span class="tag">${esc(job.type)}</span></td><td>${status(job.status)}</td><td style="min-width:120px"><div class="progress-row"><div class="progress ${job.status === "failed" ? "red" : job.status === "success" ? "green" : "blue"}"><span style="width:${job.progress}%"></span></div><span class="progress-value">${job.progress}%</span></div></td><td>${esc(job.owner)}</td><td class="muted">${esc(job.started)}</td><td>${esc(job.duration)}</td><td><div class="table-actions">${iconButton("eye", "查看任务")}${job.status === "failed" ? `<button class="icon-btn" title="重试" data-action="retry-job">${I("refresh", 14)}</button>` : job.status === "running" ? `<button class="icon-btn" title="取消" data-action="cancel-job">${I("pause", 14)}</button>` : ""}</div></td></tr>`).join("")}</tbody></table></div>
          <div class="paginator"><span>显示 6 / 38 个任务</span><div class="page-buttons"><button class="page-btn active">1</button><button class="page-btn">2</button><button class="page-btn">${I("chevronRight", 11)}</button></div></div>
        </section>
        <section class="grid grid-2 mt-16">
          <article class="card"><div class="card-head"><div><h2 class="card-title">失败任务详情</h2><p class="card-subtitle">job-2e10aa · 三区 → 一区 合区预检</p></div><span class="status danger">失败</span></div><div class="card-body"><div class="risk-box" style="background:var(--red-soft);border-color:#f0cccc;color:#7b2929">${I("alert", 15)}<span>预检在第 5 步失败：拍卖 8841 引用已删除物品，无法生成完整 ID 映射。</span></div><div class="data-list mt-16"><div class="data-row"><span>失败步骤</span><strong>引用完整性检查</strong></div><div class="data-row"><span>错误码</span><strong class="mono">MERGE_REFERENCE_MISSING</strong></div><div class="data-row"><span>可重试</span><strong class="success-text">是 · 修复数据后</strong></div><div class="data-row"><span>Trace</span><strong class="mono">91ac33…</strong></div></div></div><div class="card-foot flex gap-6"><button class="btn btn-secondary" style="flex:1" data-action="toast" data-message="任务日志已打开">${I("terminal", 12)}查看日志</button><button class="btn btn-primary" style="flex:1" data-action="retry-job">${I("refresh", 12)}修复后重试</button></div></article>
          <article class="card"><div class="card-head"><div><h2 class="card-title">任务队列</h2><p class="card-subtitle">Worker 并发与等待时间</p></div></div><div class="card-body data-list"><div class="data-row"><span>导入 Worker</span><strong>空闲</strong></div><div class="data-row"><span>发布 Worker</span><strong class="warning-text">等待 3</strong></div><div class="data-row"><span>合区 Worker</span><strong>空闲</strong></div><div class="data-row"><span>资源 Worker</span><strong>1 运行中</strong></div><div class="data-row"><span>最长等待</span><strong>06:18</strong></div></div><div class="card-foot"><button class="btn btn-secondary" style="width:100%" data-action="toast" data-message="队列策略已打开">${I("settings", 13)}查看队列策略</button></div></article>
        </section>
      </div>`;
  }

  function renderLicenses() {
    const licenseRows = [
      ["Client/", "MIT", "可闭源分发", "success"],
      ["Shared/", "MIT", "保留版权许可", "success"],
      ["Server/", "GPL-3.0-only", "分发二进制需提供完整源码", "warning"],
      ["Server.MirForms/", "GPL-3.0-only", "分发二进制需提供完整源码", "warning"],
      ["Tests/", "GPL-3.0-only", "与 GPL 服务端同边界", "warning"],
      ["Crystal.Database", "Unlicense", "外部资源 · 已记录 commit", "success"],
      ["Crystal.MapEditor", "未发现明确许可证", "禁止随发布包分发", "danger"],
      ["Items/Monsters/Objects 图库", "来源待确认", "阻断商业发布", "danger"]
    ];
    return `
      <div class="page">
        ${pageHeader({ title: "许可与合规", description: "代码、第三方依赖、外部资源和素材的许可证边界。控制面不得链接 GPL 服务端代码，未授权素材不得进入发布包。", eyebrow: "LICENSE & COMPLIANCE", actions: `${btn("导出清单", "download", "btn-secondary", 'data-action="toast" data-message="许可证清单导出任务已创建"')}${btn("新增审查记录", "plus", "btn-primary", 'data-action="toast" data-message="许可证审查记录已创建"')}` })}
        <section class="grid grid-4">
          ${kpi({ label: "已审查组件", value: "42", icon: "book", tone: "blue", meta: "代码 / 依赖 / 资源" })}
          ${kpi({ label: "GPL 边界项", value: "3", icon: "shield", tone: "gold", meta: "Server / MirForms / Tests" })}
          ${kpi({ label: "阻断级问题", value: "2", icon: "alert", tone: "red", meta: "MapEditor / 图库素材" })}
          ${kpi({ label: "发布合规", value: "部分", icon: "checkCircle", tone: "violet", meta: "技术预览可，商业发布待确认" })}
        </section>
        <section class="grid grid-3 mt-16">
          <article class="card span-2"><div class="card-head"><div><h2 class="card-title">许可证清单</h2><p class="card-subtitle">代码和外部资源的事实来源</p></div><span class="status warning">待处理 2</span></div><div class="table-wrap"><table><thead><tr><th>对象</th><th>许可证</th><th>约束</th><th>状态</th></tr></thead><tbody>${licenseRows.map(([name, license, note, tone]) => `<tr><td><span class="cell-main">${esc(name)}</span></td><td><span class="tag ${tone === "danger" ? "danger" : tone === "warning" ? "gold" : ""}">${esc(license)}</span></td><td class="muted">${esc(note)}</td><td>${status(tone === "success" ? "success" : tone === "warning" ? "warning" : "danger", tone === "success" ? "已确认" : tone === "warning" ? "需保留义务" : "阻断")}</td></tr>`).join("")}</tbody></table></div></article>
          <aside class="stack">
            <article class="card"><div class="card-head"><div><h2 class="card-title">合规阻断</h2><p class="card-subtitle">商业发布前必须清零</p></div></div><div class="card-body alert-list"><div class="alert-item danger"><span class="alert-mark" style="color:var(--red);background:var(--red-soft)">${I("alert", 13)}</span><div class="alert-copy"><strong>Crystal.MapEditor</strong><span>未发现明确许可证，不能随发布包分发。需联系维护者或替换工具。</span></div></div><div class="alert-item danger"><span class="alert-mark" style="color:var(--red);background:var(--red-soft)">${I("image", 13)}</span><div class="alert-copy"><strong>客户端图库素材</strong><span>Items / Monsters / Objects 授权来源缺失。</span></div></div></div></article>
            <article class="card"><div class="card-head"><div><h2 class="card-title">GPL 归档流程</h2></div></div><div class="card-body validation-list"><div class="validation-item ok">${I("checkCircle", 12)}<span>标记 GPL 覆盖的程序集</span></div><div class="validation-item ok">${I("checkCircle", 12)}<span>生成对应源码归档</span></div><div class="validation-item ok">${I("checkCircle", 12)}<span>发布包附带获取方式</span></div><div class="validation-item warn">${I("warning", 12)}<span>商业分发前由合规复核</span></div></div></article>
          </aside>
        </section>
        <section class="grid grid-2 mt-16">
          <article class="card"><div class="card-head"><div><h2 class="card-title">控制面许可证边界</h2></div></div><div class="card-body"><div class="pipeline"><div class="pipeline-step active"><div class="pipeline-label">Control.Web</div><div class="pipeline-value" style="font-size:12px">独立</div></div><div class="pipeline-step active"><div class="pipeline-label">Control.Api</div><div class="pipeline-value" style="font-size:12px">MIT</div></div><div class="pipeline-step active"><div class="pipeline-label">Content</div><div class="pipeline-value" style="font-size:12px">MIT</div></div><div class="pipeline-step active"><div class="pipeline-label">Contracts</div><div class="pipeline-value" style="font-size:12px">MIT</div></div><div class="pipeline-step"><div class="pipeline-label">Server</div><div class="pipeline-value" style="font-size:12px">GPL</div></div></div><p class="small muted mt-12">控制面只通过 MIT 契约和 gRPC/HTTP 跨边界调用，不链接 GPL 服务端代码。</p></div></article>
          <article class="card"><div class="card-head"><div><h2 class="card-title">发布前合规门禁</h2></div></div><div class="card-body validation-list"><div class="validation-item ok">${I("checkCircle", 12)}<span>第三方依赖清单已生成</span></div><div class="validation-item ok">${I("checkCircle", 12)}<span>GPL 源码归档可重建</span></div><div class="validation-item error">${I("xCircle", 12)}<span>图库素材授权未确认</span></div><div class="validation-item error">${I("xCircle", 12)}<span>MapEditor 未取得分发许可</span></div></div></article>
        </section>
      </div>`;
  }

  function renderSettings() {
    return `
      <div class="page">
        ${pageHeader({ title: "系统设置", description: "环境、密钥、认证、内容、热更新和通知配置。敏感配置由密钥管理系统注入，变更需要审计和回滚。", eyebrow: "PLATFORM SETTINGS", actions: `${btn("查看变更历史", "clock", "btn-secondary", 'data-action="toast" data-message="配置变更历史已打开"')}${btn("保存设置", "save", "btn-primary", 'data-action="submit-settings"')}` })}
        <section class="grid grid-3">
          <aside class="card">
            <div class="card-head"><div><h2 class="card-title">设置导航</h2><p class="card-subtitle">按配置域管理</p></div></div>
            <div class="card-body role-list">
              <div class="role-item active"><span class="cell-icon">${I("globe", 13)}</span><div><strong>环境与区域</strong><span>Production · CN</span></div></div>
              <div class="role-item"><span class="cell-icon blue">${I("key", 13)}</span><div><strong>密钥与集成</strong><span>JWT / S3 / 支付</span></div></div>
              <div class="role-item"><span class="cell-icon green">${I("shield", 13)}</span><div><strong>认证与安全</strong><span>MFA / 会话 / 限额</span></div></div>
              <div class="role-item"><span class="cell-icon violet">${I("layers", 13)}</span><div><strong>内容与发布</strong><span>快照 / 签名 / 通道</span></div></div>
              <div class="role-item"><span class="cell-icon red">${I("rocket", 13)}</span><div><strong>热更新</strong><span>Manifest / CDN / 回滚</span></div></div>
              <div class="role-item"><span class="cell-icon">${I("bell", 13)}</span><div><strong>通知与告警</strong><span>Webhook / 邮件</span></div></div>
            </div>
          </aside>
          <div class="span-2 stack">
            <article class="card"><div class="card-head"><div><h2 class="card-title">环境与区域</h2><p class="card-subtitle">不同环境的网络、数据库、密钥和对象存储隔离</p></div><span class="status success">Production</span></div><div class="card-body"><div class="field-row"><div class="field"><label>环境名称</label><input value="prod-cn" aria-label="环境名称"></div><div class="field"><label>区域</label><select><option>华东 · cn-east-1</option><option>华南 · cn-south-1</option></select></div></div><div class="field-row"><div class="field"><label>控制面实例</label><input value="2 / 2" disabled aria-label="控制面实例"></div><div class="field"><label>时区</label><input value="Asia/Shanghai" disabled aria-label="时区"></div></div><div class="field"><label>公开管理端域名</label><input value="https://control.example.com" aria-label="管理端域名"></div></div></article>
            <article class="card"><div class="card-head"><div><h2 class="card-title">密钥与集成</h2><p class="card-subtitle">敏感值只显示状态，不返回明文</p></div></div><div class="card-body"><div class="data-list"><div class="data-row"><span>JWT 签名密钥</span><strong class="success-text">已配置 · 轮换于 11-01</strong></div><div class="data-row"><span>PostgreSQL</span><strong class="success-text">已连接 · TLS</strong></div><div class="data-row"><span>Redis</span><strong class="success-text">已连接 · 3 节点</strong></div><div class="data-row"><span>MinIO / S3</span><strong class="success-text">已连接 · 版本化</strong></div><div class="data-row"><span>支付回调密钥</span><strong class="success-text">已配置</strong></div><div class="data-row"><span>Ed25519 发布私钥</span><strong class="warning-text">KMS 托管 · 待迁移 HSM</strong></div></div><div class="card-foot flex gap-6">${btn("测试连接", "zap", "btn-secondary", 'data-action="toast" data-message="所有集成连接正常"')}${btn("轮换密钥", "refresh", "btn-secondary", 'data-action="confirm-high-risk" data-message="密钥轮换任务已提交审批"')}</div></article>
            <article class="card"><div class="card-head"><div><h2 class="card-title">安全策略</h2><p class="card-subtitle">高风险操作与账号保护</p></div></div><div class="card-body"><div class="field-row"><div class="field"><label>MFA</label><select><option>管理员和发布审核强制</option><option>全部管理账号强制</option></select></div><div class="field"><label>访问令牌有效期</label><select><option>15 分钟</option><option>30 分钟</option></select></div></div><div class="field-row"><div class="field"><label>登录失败锁定</label><input value="5 次 / 15 分钟" aria-label="登录失败锁定"></div><div class="field"><label>高风险再认证</label><select><option>10 分钟</option><option>每次操作</option></select></div></div><div class="risk-box">${I("shield", 15)}<span>生产环境缺失 JWT 签名密钥会拒绝启动。真实密钥不进入仓库和日志。</span></div></div></article>
            <article class="card"><div class="card-head"><div><h2 class="card-title">危险操作</h2><p class="card-subtitle">需要双人审批和完整备份</p></div></div><div class="card-body flex gap-8" style="flex-wrap:wrap"><button class="btn btn-danger" data-action="confirm-high-risk" data-message="已提交数据库迁移演练申请">${I("database", 13)}数据库迁移演练</button><button class="btn btn-danger" data-action="confirm-high-risk" data-message="已提交区服全量备份申请">${I("archive", 13)}全量备份</button><button class="btn btn-danger" data-action="confirm-high-risk" data-message="已提交清理旧审计归档申请">${I("trash", 13)}清理旧归档</button></div></article>
          </div>
        </section>
      </div>`;
  }

  // PAGE_RENDERERS_CONTINUE

  function renderPermissionDenied(meta, permission) {
    return `
      <div class="page">
        ${pageHeader({ title: meta.title, description: "当前管理账号没有访问该页面所需的权限。导航与直达链接使用相同的服务端权限点判定。", eyebrow: "ACCESS DENIED", actions: `${linkBtn("返回驾驶舱", "dashboard", "home", "btn-primary")}` })}
        <section class="card">
          ${emptyState("403 · 无权访问", `缺少权限 ${permission || "未知"}。如需访问，请联系平台管理员调整角色；本次拒绝不会返回样本或真实业务数据。`, "shield")}
          <div class="card-body">
            <div class="grid grid-2">
              <div class="data-list">
                <div class="data-row"><span>当前账号</span><strong>${esc(displayName())}</strong></div>
                <div class="data-row"><span>登录名</span><strong class="mono">${esc(currentUser()?.username || "—")}</strong></div>
                <div class="data-row"><span>所需权限</span><strong class="mono">${esc(permission || "—")}</strong></div>
              </div>
              <div class="data-list">
                <div class="data-row"><span>当前角色</span><strong>${esc(roleLabelList(currentUser()?.roles))}</strong></div>
                <div class="data-row"><span>可用权限</span><strong>${formatNumber(currentUser()?.permissions?.length || 0)} 个</strong></div>
                <div class="data-row"><span>页面路由</span><strong class="mono">#/${esc(state.route)}</strong></div>
              </div>
            </div>
          </div>
        </section>
      </div>`;
  }

  function renderPage() {
    const route = state.route;
    if (!canAccessRoute(route)) return renderPermissionDenied(pageMeta(route), routePermission(route));
    if (route === "dashboard") return renderDashboard();
    if (route === "prototype-map") return renderPrototypeMap();
    if (route === "content-overview") return renderContentOverview();
    if (route === "world-map") return renderWorldMap();
    if (route === "content-relations") return renderRelationsPage();
    if (route === "content/scripts") return renderScripts();
    if (route === "import-runs") return renderImportRuns();
    if (route === "content-releases") return renderReleases();
    if (route === "assets") return renderAssets();
    if (route.startsWith("content/")) {
      const id = route.split("/")[1];
      if (CONTENT_BY_ID[id]) return renderContentLibrary(CONTENT_BY_ID[id]);
    }
    if (route === "zones") return renderZones();
    if (route.startsWith("zone/")) return renderZoneDetail(ZONE_BY_ID[route.split("/")[1]] || D.ZONES[0]);
    if (route === "zone-templates") return renderZoneTemplates();
    if (route === "gm") return renderGm();
    if (route === "announcements") return renderAnnouncements();
    if (route === "merges") return renderMerges();
    if (route === "players") return renderPlayers();
    if (route === "orders") return renderOrders();
    if (route === "reconciliation") return renderReconciliation();
    if (route === "approvals") return renderApprovals();
    if (route === "accounts") return renderAccounts();
    if (route === "roles") return renderRoles();
    if (route === "audit") return renderAudit();
    if (route === "monitoring") return renderMonitoring();
    if (route === "jobs") return renderJobs();
    if (route === "licenses") return renderLicenses();
    if (route === "settings") return renderSettings();
    return `<div class="page">${pageHeader({ title: "页面未找到", description: "这个原型页面尚未注册。", actions: linkBtn("返回驾驶舱", "dashboard", "home") })}<div class="card">${emptyState("404", "请检查路由或从原型地图进入。", "search")}</div></div>`;
  }

  function loginPage() {
    return `
      <div class="login-page">
        <section class="login-visual">
          <div class="login-brand"><div class="brand-mark" style="width:38px;height:38px;font-size:21px">M</div><div><strong>MIR CONTROL</strong><span>GENERAL LEGEND ENGINE PLATFORM</span></div></div>
          <div class="login-hero"><div class="eyebrow">CONTROL PLANE</div><h1>让每一次发布<br>都<em>可控</em>。</h1><p>内容、区服、玩家、交易与审计的统一运营后台。用版本化内容包连接创作与运行，用审批和审计守住高风险操作。</p><div class="login-stats"><div class="login-stat"><strong>9,734</strong><span>内容实体</span></div><div class="login-stat"><strong>12</strong><span>运行区服</span></div><div class="login-stat"><strong>99.98%</strong><span>控制面可用性</span></div></div></div>
          <div class="login-foot"><span>GPL boundary protected</span><span>Signed releases</span><span>Audit by default</span></div>
        </section>
        <section class="login-panel">
          <div class="login-card">
            <div class="mobile-brand"><div class="brand-mark">M</div><div class="brand-copy" style="color:var(--ink)"><strong>MIR CONTROL</strong><span style="color:var(--muted)">传奇运营控制台</span></div></div>
            <h2>登录控制台</h2>
            <p>使用管理端账号进入。玩家账号不能登录后台。</p>
            <div class="login-tabs"><button class="active">账号登录</button><button data-action="toast" data-message="OIDC 登录将在 M1 后续接入">企业 SSO</button></div>
            <form class="login-form js-login">
              <div id="login-error" class="login-error" role="alert" hidden></div>
              <div class="field"><label>用户名</label><div class="input-icon">${I("user", 14)}<input name="username" value="admin" autocomplete="username" aria-label="用户名"></div></div>
              <div class="field"><label>密码</label><div class="input-icon">${I("lock", 14)}<input name="password" type="password" value="" autocomplete="current-password" aria-label="密码" placeholder="输入控制面账号密码"></div></div>
              <div class="login-options"><label class="check-line"><input class="checkbox" type="checkbox" checked> 本浏览器会话内保持登录</label><span>忘记密码？</span></div>
              <button class="btn btn-primary login-submit" type="submit">${I("login", 15)}进入控制台</button>
              <button class="login-demo" type="button" data-action="login">${I("play", 13)}直接进入高保真原型</button>
            </form>
            <div class="login-api-hint">Control API：<span class="mono">${esc(API.baseUrl() || location.origin)}</span></div>
            <div class="login-security">${I("shield", 15)}<span>管理端账号与游戏玩家账号完全分离。高风险操作需要 MFA、二次确认和审批；所有操作写入只追加审计日志。</span></div>
          </div>
        </section>
      </div>`;
  }

  function render() {
    state.route = routeFromHash();
    if (state.route === "login") {
      app.innerHTML = loginPage();
      document.title = "登录 · MIR Control";
      window.scrollTo(0, 0);
      return;
    }
    const meta = pageMeta(state.route);
    const activeElement = document.activeElement;
    if (activeElement?.matches?.("[data-live-content-search]")) {
      state.focusContentSearch = activeElement.id;
    }
    app.innerHTML = shell(renderPage(), meta);
    document.title = `${meta.title} · MIR Control`;
    if (state.focusContentSearch) {
      const input = document.getElementById(state.focusContentSearch);
      if (input) {
        input.focus();
        input.setSelectionRange(input.value.length, input.value.length);
      }
      state.focusContentSearch = null;
    }
    window.scrollTo(0, 0);
    requestAnimationFrame(() => document.querySelector(".nav-item.active")?.scrollIntoView({ block: "nearest" }));
    void hydrateRoute(state.route);
  }

  function showToast(message, tone = "success") {
    const node = document.createElement("div");
    node.className = "toast";
    node.innerHTML = `<span class="toast-icon" style="${tone === "danger" ? "color:#ffb8b8;background:rgba(215,77,77,.16)" : ""}">${I(tone === "danger" ? "alert" : "checkCircle", 15)}</span><span>${esc(message)}</span>`;
    toastRoot.appendChild(node);
    setTimeout(() => {
      node.style.opacity = "0";
      node.style.transform = "translateY(6px)";
      setTimeout(() => node.remove(), 220);
    }, 2800);
  }

  function modalForm() {
    return document.querySelector(".modal form");
  }

  function formValue(form, name) {
    return String(form?.elements?.[name]?.value || "").trim();
  }

  function selectedRoles(form) {
    return Array.from(form?.querySelectorAll('input[name="roles"]:checked') || []).map(input => input.value);
  }

  function accountActionError(error) {
    if (error?.status === 401) {
      handleApiFailure(error);
      return;
    }
    const details = error?.problem?.errors;
    const message = details
      ? Object.values(details).flat().join(" ")
      : error?.status === 403
        ? "当前账号没有执行该操作的权限。"
        : error?.message || "账号操作失败。";
    showToast(message, "danger");
  }

  function refreshAccounts(message) {
    state.live.accounts = null;
    state.failed.delete("accounts");
    closeOverlays();
    if (message) showToast(message);
    render();
  }

  async function createAccountFromModal() {
    if (!isLive()) {
      closeOverlays();
      showToast("演示模式：账号创建未写入服务端");
      return;
    }
    const form = modalForm();
    const username = formValue(form, "username");
    const displayName = formValue(form, "displayName");
    const email = formValue(form, "email");
    const password = String(form?.elements?.password?.value || "");
    const roles = selectedRoles(form);
    if (!username || !password || roles.length === 0) {
      showToast("请填写用户名、密码并至少选择一个角色。", "danger");
      return;
    }
    if (password.length < 12 || password.length > 128) {
      showToast("密码长度必须为 12 到 128 个字符。", "danger");
      return;
    }
    try {
      await API.createAccount({ username, displayName: displayName || username, email: email || null, password, roles });
      refreshAccounts("管理账号已创建");
    } catch (error) {
      accountActionError(error);
    }
  }

  async function resetAccountPasswordFromModal() {
    const form = modalForm();
    const password = String(form?.elements?.newPassword?.value || "");
    if (password.length < 12 || password.length > 128) {
      showToast("新密码长度必须为 12 到 128 个字符。", "danger");
      return;
    }
    try {
      const id = document.querySelector('.modal [data-action="submit-account-password"]')?.dataset.id;
      await API.resetAccountPassword(id, password);
      refreshAccounts("密码已重置，目标账号会话已撤销");
    } catch (error) {
      accountActionError(error);
    }
  }

  async function updateAccountRolesFromModal() {
    const form = modalForm();
    const roles = selectedRoles(form);
    if (roles.length === 0) {
      showToast("至少选择一个角色。", "danger");
      return;
    }
    try {
      const id = document.querySelector('.modal [data-action="submit-account-roles"]')?.dataset.id;
      await API.updateAccountRoles(id, roles);
      refreshAccounts("账号角色已更新，目标会话已撤销");
    } catch (error) {
      accountActionError(error);
    }
  }

  async function setAccountStatusFromModal(actionEl) {
    try {
      await API.setAccountStatus(actionEl.dataset.id, actionEl.dataset.status);
      refreshAccounts(actionEl.dataset.status === "active" ? "账号已启用" : "账号已停用，现有会话已撤销");
    } catch (error) {
      accountActionError(error);
    }
  }

  async function changeOwnPasswordFromModal() {
    const form = modalForm();
    const currentPassword = String(form?.elements?.currentPassword?.value || "");
    const newPassword = String(form?.elements?.newPassword?.value || "");
    if (!currentPassword || newPassword.length < 12 || newPassword.length > 128) {
      showToast("请填写当前密码，新密码长度必须为 12 到 128 个字符。", "danger");
      return;
    }
    try {
      await API.changePassword(currentPassword, newPassword);
      closeOverlays();
      showToast("密码已修改，其他会话已撤销");
    } catch (error) {
      accountActionError(error);
    }
  }

  function closeOverlays() {
    modalRoot.innerHTML = "";
    state.spotlightOpen = false;
  }

  function openSpotlight() {
    state.spotlightOpen = true;
    modalRoot.innerHTML = `
      <div class="spotlight-backdrop" data-overlay="spotlight">
        <div class="spotlight">
          <div class="spotlight-search">${I("search", 18)}<input id="spotlight-input" placeholder="搜索页面、内容域、区服或命令..."><span class="kbd">ESC</span></div>
          <div class="spotlight-results" id="spotlight-results">${spotlightResults("")}</div>
        </div>
      </div>`;
    setTimeout(() => document.getElementById("spotlight-input")?.focus(), 0);
  }

  function spotlightResults(query) {
    const q = query.trim().toLowerCase();
    const pages = PAGE_INDEX
      .filter(page => canAccessRoute(page.route))
      .filter(page => !q || `${page.label} ${page.parent} ${page.route}`.toLowerCase().includes(q))
      .slice(0, 10);
    const zones = canAccessRoute("zones")
      ? D.ZONES.filter(zone => !q || `${zone.name} ${zone.id}`.toLowerCase().includes(q)).slice(0, 4)
      : [];
    return `${pages.length ? `<div class="spotlight-group">页面与功能</div>${pages.map(page => `<button class="spotlight-item" data-route="${esc(page.route)}"><span class="nav-icon">${I(page.icon, 14)}</span><span>${esc(page.label)}</span><small>${esc(page.parent)}</small></button>`).join("")}` : ""}${zones.length ? `<div class="spotlight-group">区服</div>${zones.map(zone => `<button class="spotlight-item" data-route="zone/${esc(zone.id)}"><span class="nav-icon">${I("server", 14)}</span><span>${esc(zone.name)}</span><small>${zone.players.toLocaleString()} 在线</small></button>`).join("")}` : ""}${!pages.length && !zones.length ? emptyState("没有匹配结果", "尝试搜索“物品”、“发布”或“龙腾”。", "searchX") : ""}`;
  }

  function drawerLinks(typeId, key, label, sub, iconName = "link") {
    return `<button class="relation-mini" data-action="open-content" data-type="${typeId}" data-key="${esc(key)}"><span class="relation-mini-icon">${I(iconName, 13)}</span><span><strong>${esc(label)}</strong><small>${esc(sub)}</small></span>${I("chevronRight", 11)}</button>`;
  }

  function drawerRelations(typeId, row) {
    let body = "";
    let caption = "关联数据";
    if (typeId === "items") {
      const sources = W.DROP_GROUPS.flatMap(group => group.entries.filter(entry => String(entry.itemKey) === String(row.index)).map(entry => ({ group, entry })));
      caption = `${sources.length} 个掉落来源`;
      body = sources.length ? `<div class="relation-mini-list">${sources.map(({ group, entry }) => {
        const spawn = W.SPAWNS.find(item => item.monsterKey === group.monsterKey);
        const map = spawn ? mapFor(spawn.mapKey) : null;
        const monster = monsterFor(group.monsterKey);
        return drawerLinks("drops", group.key, `${monster?.name || group.monsterKey} · ${entry.chanceText}`, `${map?.name || "未关联地图"} (${spawn?.x ?? "—"}, ${spawn?.y ?? "—"})`, "drop");
      }).join("")}</div>` : `<div class="empty-inline">暂无怪物掉落来源，可能来自商店、任务或活动奖励。</div>`;
    }
    if (typeId === "monsters") {
      const spawns = W.SPAWNS.filter(spawn => String(spawn.monsterKey) === String(row.index));
      const group = W.DROP_GROUPS.find(item => String(item.monsterKey) === String(row.index));
      const totalMonsters = spawns.reduce((sum, spawn) => sum + spawn.count, 0);
      caption = `${spawns.length} 个刷新点 · 合计 ${totalMonsters} 只 · ${group ? "已关联掉落组" : "无掉落组"}`;
      body = `<div class="relation-mini-list">${spawns.map(spawn => {
        const map = mapFor(spawn.mapKey);
        return `<button class="relation-mini" data-action="open-content" data-type="respawns" data-key="${spawn.id}"><span class="relation-mini-icon">${I("spawn", 13)}</span><span><strong>${esc(map?.name || spawn.mapKey)} · ${spawn.x}, ${spawn.y}</strong><small>${spawn.count} 只 · ${spawn.delay} 分钟 ±${spawn.randomDelay} · 半径 ${spawn.spread}</small></span>${I("chevronRight", 11)}</button>`;
      }).join("")}${group ? drawerLinks("drops", group.key, group.key, `${group.entries.length} 个物品 · ${group.expected}`, "drop") : ""}</div>`;
    }
    if (typeId === "maps") {
      const map = mapFor(row.key);
      const portals = map ? portalsForMap(map.key) : [];
      const npcs = map ? npcsForMap(map.key) : [];
      const spawns = map ? spawnsForMap(map.key) : [];
      const spawnSummary = map ? spawnSummaryForMap(map.key) : { totalMonsters: 0, monsterTypes: 0 };
      caption = `${portals.length} 个传送 · ${spawns.length} 个刷怪点 · ${spawnSummary.totalMonsters} 只 / ${spawnSummary.monsterTypes} 种 · ${npcs.length} 个 NPC`;
      body = `<div class="relation-mini-list">${portals.slice(0, 4).map(portal => `<button class="relation-mini" data-action="open-world-map" data-map-key="${esc(portal.to)}"><span class="relation-mini-icon">${I("teleport", 13)}</span><span><strong>${esc(portal.fromName)} ${portal.fromPoint.join(",")} → ${esc(portal.toName)} ${portal.toPoint.join(",")}</strong><small>${esc(portal.kind)} · ${esc(portal.trigger)}</small></span>${I("chevronRight", 11)}</button>`).join("")}${spawns.slice(0, 3).map(spawn => `<button class="relation-mini" data-action="open-content" data-type="respawns" data-key="${spawn.id}"><span class="relation-mini-icon">${I("spawn", 13)}</span><span><strong>${esc(monsterFor(spawn.monsterKey)?.name || spawn.monsterKey)} · (${spawn.x}, ${spawn.y})</strong><small>${spawn.count} 只 · ${spawn.delay} 分钟 ±${spawn.randomDelay}</small></span>${I("chevronRight", 11)}</button>`).join("")}${npcs.slice(0, 3).map(point => drawerLinks("npcs", point.npcKey, `${point.name} · ${point.x}, ${point.y}`, point.role, "message")).join("")}</div>`;
    }
    if (typeId === "npcs") {
      const point = NPC_POINT_BY_KEY[String(row.index)];
      if (point) {
        const map = mapFor(point.mapKey);
        caption = "地图坐标与脚本入口";
        body = `<div class="relation-mini-list"><button class="relation-mini" data-action="open-world-map" data-map-key="${esc(point.mapKey)}"><span class="relation-mini-icon">${I("mapPin", 13)}</span><span><strong>${esc(map?.name || point.mapKey)} · (${point.x}, ${point.y})</strong><small>${esc(point.role)} · ${esc(point.script)}</small></span>${I("chevronRight", 11)}</button></div>`;
      }
    }
    if (typeId === "drops") {
      const group = DROP_BY_KEY[row.key];
      if (group) {
        const monster = monsterFor(group.monsterKey);
        const spawn = W.SPAWNS.find(item => item.monsterKey === group.monsterKey);
        const map = spawn ? mapFor(spawn.mapKey) : null;
        caption = `${group.entries.length} 个物品条目`;
        body = `<div class="relation-mini-list">${drawerLinks("monsters", String(monster?.index || group.monsterKey), monster?.name || group.monsterKey, `${map?.name || "未关联地图"} · ${spawn?.delay ?? "—"} 分钟刷新`, "skull")}${group.entries.map(entry => drawerLinks("items", entry.itemKey, `${entry.name} · ${entry.chanceText}`, `${entry.category} · ${entry.group}`, "sword")).join("")}</div>`;
      }
    }
    if (typeId === "respawns") {
      const spawn = SPAWN_BY_ID[String(row.index)] || W.SPAWNS.find(item => String(item.id) === String(row.index));
      if (spawn) {
        const map = mapFor(spawn.mapKey);
        const monster = monsterFor(spawn.monsterKey);
        const group = W.DROP_GROUPS.find(item => item.monsterKey === spawn.monsterKey);
        caption = "地图、怪物与刷新规则";
        body = `<div class="relation-mini-list"><button class="relation-mini" data-action="open-world-map" data-map-key="${esc(spawn.mapKey)}"><span class="relation-mini-icon">${I("mapPin", 13)}</span><span><strong>${esc(map?.name || spawn.mapKey)} · (${spawn.x}, ${spawn.y})</strong><small>${spawn.count} 只 · 半径 ${spawn.spread} · ${spawn.delay} 分钟 ±${spawn.randomDelay}</small></span>${I("chevronRight", 11)}</button>${drawerLinks("monsters", spawn.monsterKey, monster?.name || spawn.monsterKey, `Lv.${monster?.level ?? "—"} · ${monster?.metric ?? "—"} HP`, "skull")}${group ? drawerLinks("drops", group.key, group.key, `${group.entries.length} 个条目 · ${group.expected}`, "drop") : ""}</div>`;
      }
    }
    if (!body) return "";
    return `<div class="detail-section"><div class="detail-section-title"><span>世界关系</span><span class="tag gold">${esc(caption)}</span></div>${body}</div>`;
  }

  async function hydrateLiveDrawerRelations(entityType, row) {
    const host = document.getElementById("live-drawer-relations");
    if (!host) return;
    const runId = state.live.run?.id;
    try {
      if (entityType === "map") {
        const cached = liveRelations().response;
        const payload = cached?.map && Number(cached.map.localIndex) === Number(row.index)
          ? cached
          : await API.getMapRelations(row.index, { importRunId: runId, respawnPage: 1, respawnPageSize: 10 });
        if (!document.getElementById("live-drawer-relations")) return;
        const movements = payload.movements || [];
        const npcs = payload.npcs || [];
        const respawns = payload.respawns?.items || [];
        const mapAsset = payload.asset?.map;
        host.innerHTML = `<div class="detail-section"><div class="detail-section-title"><span>真实世界关系</span><span class="tag gold">批次 #${esc(runId ?? "—")}</span></div><div class="data-list"><div class="data-row"><span>内容键 / 本地索引</span><strong class="mono">${esc(payload.map.contentKey)} · ${esc(payload.map.localIndex)}</strong></div><div class="data-row"><span>地图资源</span><strong class="mono">${esc(payload.asset?.relativePath || "未导入")}</strong></div><div class="data-row"><span>尺寸 / 阻挡 / 门</span><strong>${mapAsset?.valid ? `${esc(mapAsset.width)} × ${esc(mapAsset.height)} · ${esc(mapAsset.blockedCells)} · ${esc(mapAsset.doorCells)}` : "未解析"}</strong></div><div class="data-row"><span>传送 / 安全区 / NPC</span><strong>${movements.length} / ${(payload.safeZones || []).length} / ${npcs.length}</strong></div><div class="data-row"><span>刷新点</span><strong>${formatNumber(payload.respawns?.total || 0)} 个</strong></div></div><div class="divider"></div><div class="relation-mini-list">${movements.slice(0, 3).map(move => `<div class="relation-mini"><span class="relation-mini-icon">${I("teleport", 13)}</span><span><strong>${esc(move.targetMap?.title || move.targetMap?.fileName || `地图 ${move.targetMapIndex}`)}</strong><small>${esc(pointText(move.source))} → ${esc(pointText(move.destination))}</small></span></div>`).join("")}${npcs.slice(0, 2).map(npc => `<div class="relation-mini"><span class="relation-mini-icon">${I("message", 13)}</span><span><strong>${esc(npc.name || npc.contentKey)}</strong><small>${esc(npc.script?.contentKey || "脚本未导入")} · ${esc(pointText(npc.location))}</small></span></div>`).join("")}${respawns.slice(0, 2).map(respawn => `<div class="relation-mini"><span class="relation-mini-icon">${I("spawn", 13)}</span><span><strong>${esc(respawn.monsterName || `怪物 ${respawn.monsterIndex}`)}</strong><small>${esc(pointText(respawn.location))} · ${esc(respawn.count)} 只 · ${esc(respawn.delay)} 分钟</small></span></div>`).join("")}</div></div>`;
        return;
      }

      if (entityType === "monster") {
        const key = liveDropKey(row.index);
        const payload = liveRelations().drops[key] || await API.getMonsterDrops(row.index, runId);
        liveRelations().drops[key] = payload;
        if (!document.getElementById("live-drawer-relations")) return;
        host.innerHTML = `<div class="detail-section"><div class="detail-section-title"><span>真实掉落关系</span><span class="tag gold">${esc(payload.dropContentKey || "无掉落文件")}</span></div><div class="data-list"><div class="data-row"><span>掉落路径</span><strong class="mono">${esc(payload.dropPath || "—")}</strong></div><div class="data-row"><span>顶层条目</span><strong>${arrayCount(payload.drops)}</strong></div></div><div class="divider"></div>${dropTableHtml(payload.drops)}</div>`;
        return;
      }

      if (entityType === "recipe") {
        const payload = await API.getRecipeRelations(row.index, runId);
        if (!document.getElementById("live-drawer-relations")) return;
        const recipeItem = item => item
          ? `<div class="relation-mini"><span class="relation-mini-icon">${I("sword", 13)}</span><span><strong>${esc(item.name || `物品 ${item.itemIndex}`)} × ${esc(item.count)}</strong><small>${esc(item.contentKey || "未解析物品引用")} · 持久 ${esc(item.currentDura)} / ${esc(item.maxDura)}</small></span></div>`
          : "";
        const ingredients = (payload.ingredients || []).map(recipeItem).join("");
        const tools = (payload.tools || []).map(recipeItem).join("");
        const classes = (payload.requiredClasses || []).join(" / ") || "不限";
        host.innerHTML = `<div class="detail-section"><div class="detail-section-title"><span>配方产出与材料</span><span class="tag gold">${esc(payload.chance)}% · ${formatNumber(payload.gold)} 金币</span></div><div class="data-list"><div class="data-row"><span>产出</span><strong>${esc(payload.output?.name || payload.output?.contentKey || "—")} × ${esc(payload.output?.count ?? "—")}</strong></div><div class="data-row"><span>条件</span><strong>等级 ${esc(payload.requiredLevel ?? "不限")} · ${esc(classes)} · ${esc(payload.requiredGender || "不限性别")}</strong></div><div class="data-row"><span>文件</span><strong class="mono">${esc(payload.fileName || "—")}</strong></div></div><div class="divider"></div><div class="relation-mini-list">${recipeItem(payload.output)}${ingredients}${tools}</div></div>`;
      }

      if (entityType === "script") {
        const payload = await API.getScriptValidation(row.index, runId);
        if (!document.getElementById("live-drawer-relations")) return;
        const validation = payload.validation || {};
        const issues = payload.issues || [];
        const astPages = payload.ast?.pages || [];
        host.innerHTML = `<div class="detail-section"><div class="detail-section-title"><span>AST 与编译校验</span><span class="${validation.valid ? "success-text" : "warning-text"}">${validation.valid ? "通过" : "未通过"}</span></div><div class="data-list"><div class="data-row"><span>页面 / 命令</span><strong>${esc(astPages.length)} / ${esc(payload.ast?.statementCount ?? "—")}</strong></div><div class="data-row"><span>错误 / 警告</span><strong>${esc(validation.errorCount ?? 0)} / ${esc(validation.warningCount ?? 0)}</strong></div><div class="data-row"><span>编译哈希</span><strong class="mono">${esc(validation.compiledSha256 || "—")}</strong></div></div><div class="divider"></div><div class="validation-list">${issues.length ? issues.map(issue => `<div class="validation-item ${issue.severity === "error" ? "warn" : "ok"}">${I(issue.severity === "error" ? "warning" : "checkCircle", 12)}<span>${esc(issue.line || "—")} 行 · ${esc(issue.message)}</span></div>`).join("") : `<div class="validation-item ok">${I("checkCircle", 12)}<span>没有校验问题。</span></div>`}</div></div>`;
        return;
      }

      if (entityType === "asset") {
        const payload = await API.getContentByKey("asset", row.key, runId);
        if (!document.getElementById("live-drawer-relations")) return;
        const map = payload.data?.map;
        host.innerHTML = `<div class="detail-section"><div class="detail-section-title"><span>资源文件</span><span class="tag gold">${esc(formatBytes(payload.data?.sizeBytes))}</span></div><div class="data-list"><div class="data-row"><span>路径</span><strong class="mono">${esc(payload.data?.relativePath || "—")}</strong></div><div class="data-row"><span>哈希</span><strong class="mono">${esc(payload.data?.sha256 || "—")}</strong></div><div class="data-row"><span>地图格式</span><strong>${esc(map?.formatName || "非地图资源")}</strong></div><div class="data-row"><span>尺寸 / 可走 / 阻挡</span><strong>${map?.valid ? `${esc(map.width)} × ${esc(map.height)} / ${esc(map.walkableCells)} / ${esc(map.blockedCells)}` : "—"}</strong></div></div><div class="divider"></div><button class="btn btn-primary" style="width:100%" data-action="download-asset" data-index="${esc(row.index)}" data-file-name="${esc(payload.data?.fileName || "")}" data-sha256="${esc(payload.data?.sha256 || "")}">${I("download", 14)}下载并校验资源</button></div>`;
      }
    } catch (error) {
      if (error?.status === 401) {
        handleApiFailure(error);
        return;
      }
      if (document.getElementById("live-drawer-relations")) {
        host.innerHTML = `<div class="detail-section"><div class="detail-section-title"><span>真实世界关系</span></div><div class="alert-item danger"><span class="alert-mark" style="color:var(--red);background:var(--red-soft)">${I("alert", 13)}</span><div class="alert-copy"><strong>关系读取失败</strong><span>${esc(error?.message || "Control API 请求失败")}</span></div></div></div>`;
      }
    }
  }

  function openContentDrawer(typeId, key) {
    const type = CONTENT_BY_ID[typeId];
    if (!type) return;
    const rows = isLive() && LIVE_ENTITY_TYPES.has(type.entity) ? contentView(type).rows : type.rows;
    const row = rows.find(item => String(item.key || item.index) === String(key)) || rows[0] || type.rows[0];
    if (!row) return;
    modalRoot.innerHTML = `
      <div class="drawer-backdrop">
        <aside class="drawer">
          <div class="drawer-head"><span class="cell-icon ${type.tone === "blue" ? "blue" : type.tone === "green" ? "green" : type.tone === "violet" ? "violet" : ""}" style="width:38px;height:38px">${I(row.icon || type.icon, 18)}</span><div class="drawer-head-copy"><div class="eyebrow">${esc(type.entity.toUpperCase())} · ${esc(row.key || row.index)}</div><h2>${esc(row.name)}</h2><p>${esc(row.subtitle || type.label)} · ${isLive() && LIVE_ENTITY_TYPES.has(type.entity) ? `批次 #${state.live.run?.id ?? "—"}` : "批次 #32"}</p></div>${iconButton("close", "关闭", 'data-action="close-drawer"')}</div>
          <div class="drawer-body">
            <div class="detail-section"><div class="flex gap-8 items-center">${status(row.status)}${row.tags.map(tag => `<span class="tag gold">${esc(tag)}</span>`).join("")}</div></div>
            <div class="detail-section"><div class="detail-section-title"><span>基础字段</span><button class="btn btn-ghost btn-sm" data-action="toast" data-message="已切换到字段编辑模式">${I("edit", 11)}编辑</button></div><div class="data-list">${row.attributes.map(([label, value]) => `<div class="data-row"><span>${esc(label)}</span><strong>${esc(value)}</strong></div>`).join("")}</div></div>
            ${isLive() && LIVE_ENTITY_TYPES.has(type.entity)
              ? (type.entity === "map" || type.entity === "monster" || type.entity === "recipe" || type.entity === "script" || type.entity === "asset" ? `<div id="live-drawer-relations"><div class="detail-section"><div class="empty-inline">${I("refresh", 14)} 正在读取真实关系...</div></div></div>` : "")
              : drawerRelations(typeId, row)}
            <div class="detail-section"><div class="detail-section-title"><span>校验与引用</span><span class="success-text">通过</span></div><div class="validation-list" style="padding:0"><div class="validation-item ok">${I("checkCircle", 12)}<span>Schema 校验通过</span></div><div class="validation-item ok">${I("checkCircle", 12)}<span>资源引用存在且哈希一致</span></div><div class="validation-item ${type.issues ? "warn" : "ok"}">${I(type.issues ? "warning" : "checkCircle", 12)}<span>${type.issues} 个内容域级提示</span></div><div class="validation-item ok">${I("checkCircle", 12)}<span>协议本地索引 ${esc(row.index || row.key)} 在允许范围内</span></div></div></div>
            <div class="detail-section"><div class="detail-section-title"><span>发布影响</span></div><div class="grid grid-2"><div class="metric-mini"><span>影响区服</span><strong style="font-size:11px">5 个</strong></div><div class="metric-mini"><span>引用内容</span><strong style="font-size:11px">${row.tags.length + 2} 处</strong></div><div class="metric-mini"><span>热重载</span><strong style="font-size:11px">支持</strong></div><div class="metric-mini"><span>回滚</span><strong style="font-size:11px">支持</strong></div></div></div>
            <div class="detail-section"><div class="detail-section-title"><span>最近变更</span></div><div class="timeline"><div class="timeline-item"><span class="timeline-dot gold">${I("edit", 10)}</span><div class="timeline-copy"><strong>周遥更新内容</strong><span>字段与资源引用变更</span><time>今天 17:58 · 请求 4c1e…</time></div></div><div class="timeline-item"><span class="timeline-dot">${I("checkCircle", 10)}</span><div class="timeline-copy"><strong>自动校验通过</strong><span>0 error · 1 warning</span><time>今天 17:59</time></div></div></div></div>
          </div>
          <div class="drawer-foot"><button class="btn btn-secondary" data-action="toast" data-message="内容键已复制">${I("copy", 13)}复制内容键</button><button class="btn btn-secondary" data-action="toast" data-message="变更历史已打开">${I("clock", 13)}变更历史</button><button class="btn btn-primary" data-action="toast" data-message="已进入内容编辑草稿">${I("edit", 13)}编辑内容</button></div>
        </aside>
      </div>`;
    if (isLive() && (type.entity === "map" || type.entity === "monster" || type.entity === "recipe" || type.entity === "script" || type.entity === "asset")) {
      void hydrateLiveDrawerRelations(type.entity, row);
    }
  }

  function modalShell({ title, desc, iconName = "info", body, foot }) {
    modalRoot.innerHTML = `<div class="modal-backdrop"><section class="modal"><div class="modal-head"><span class="cell-icon" style="width:36px;height:36px;color:var(--gold);background:var(--gold-soft)">${I(iconName, 17)}</span><div class="modal-head-copy"><h2>${esc(title)}</h2><p>${esc(desc)}</p></div>${iconButton("close", "关闭", 'data-action="close-modal"')}</div><div class="modal-body">${body}</div><div class="modal-foot">${foot}</div></section></div>`;
  }

  function roleCheckboxes(selected = []) {
    const selectedSet = new Set(selected);
    const roles = state.live.roles?.items?.length ? state.live.roles.items : D.ROLES;
    return roles.map(role => {
      const code = String(role.code || "");
      return `<label style="display:flex;align-items:center;gap:8px;padding:5px 0;font-size:10px;color:var(--text)"><input class="checkbox" type="checkbox" name="roles" value="${esc(code)}"${selectedSet.has(code) ? " checked" : ""}><span>${esc(roleLabel(code) || role.name || code)}</span><span class="mono muted" style="font-size:8px">${esc(code)}</span></label>`;
    }).join("");
  }

  function openModal(name, dataset = {}) {
    if (name === "import") {
      return modalShell({ title: "导入旧版 MirDB", desc: "上传 Server.MirDB 并生成不可变导入批次。", iconName: "upload", body: `<form class="js-form"><div class="field"><label>数据库文件</label><input type="file" aria-label="数据库文件"><div class="field-hint">支持标准 Server.MirDB；导入前自动校验头部版本与哈希。</div></div><div class="field-row"><div class="field"><label>目标环境</label><select><option>staging</option><option>dev</option><option>prod</option></select></div><div class="field"><label>导入后动作</label><select><option>仅生成快照</option><option>生成快照并写入 PostgreSQL</option><option>生成快照并运行完整校验</option></select></div></div><div class="risk-box">${I("info", 15)}<span>导入不会直接修改运行中的区服；内容需要经过校验、签名和发布流程。</span></div></form>`, foot: `${btn("取消", "", "btn-secondary", 'data-action="close-modal"')}${btn("开始导入", "upload", "btn-primary", 'data-action="submit-import"')}` });
    }
    if (name === "new-zone") {
      return modalShell({ title: "开启新区服", desc: "从模板创建区服，进入预热和健康门禁流程。", iconName: "server", body: `<form class="js-form"><div class="field"><label>区服名称</label><input value="传奇七区 · 星海" aria-label="区服名称"></div><div class="field-row"><div class="field"><label>区服模板</label><select>${D.TEMPLATES.map(t => `<option>${esc(t.name)} · ${esc(t.version)}</option>`).join("")}</select></div><div class="field"><label>内容版本</label><select><option>2026.11.09.1（stable）</option><option>2026.11.10.0-rc2（staging）</option></select></div></div><div class="field-row"><div class="field"><label>区域</label><select><option>华东</option><option>华南</option><option>华北</option></select></div><div class="field"><label>容量上限</label><input value="3,000" aria-label="容量上限"></div></div><div class="field"><label>开区时间</label><input value="2026-11-12 20:00" aria-label="开区时间"></div><div class="risk-box">${I("shield", 15)}<span>开放前必须通过 Agent 心跳、内容签名、地图加载、数据库迁移和登录冒烟检查。</span></div></form>`, foot: `${btn("取消", "", "btn-secondary", 'data-action="close-modal"')}${btn("创建并预热", "rocket", "btn-primary", 'data-action="submit-zone"')}` });
    }
    if (name === "create-map-layer") {
      return modalShell({ title: "新建地图层", desc: "建立地图尺寸、层级、入口/出口坐标和资源引用；保存后进入坐标与资源校验。", iconName: "map", body: `<form class="js-form"><div class="field-row"><div class="field"><label>地图组</label><select><option>祖玛寺庙</option><option>沃玛寺庙</option><option>赤月峡谷</option><option>石墓阵</option><option>主城</option></select></div><div class="field"><label>层级</label><select><option>1F</option><option>2F</option><option>3F</option><option>Boss</option><option>野外</option></select></div></div><div class="field-row"><div class="field"><label>地图名称</label><input value="祖玛寺庙四层" aria-label="地图名称"></div><div class="field"><label>地图文件</label><input value="4.map" aria-label="地图文件"></div></div><div class="field-row"><div class="field"><label>地图尺寸 X</label><input value="180" aria-label="地图尺寸 X"></div><div class="field"><label>地图尺寸 Y</label><input value="180" aria-label="地图尺寸 Y"></div></div><div class="field-row"><div class="field"><label>入口坐标</label><input value="42, 168" aria-label="入口坐标"></div><div class="field"><label>出口坐标</label><input value="42, 22" aria-label="出口坐标"></div></div><div class="field"><label>连接目标</label><select><option>祖玛寺庙三层 · (126, 20)</option><option>祖玛教主大厅 · (60, 108)</option><option>不连接，稍后配置</option></select></div><div class="risk-box">${I("info", 15)}<span>地图层必须同时绑定阻挡文件、MiniMap、安全区、NPC 和刷怪点；入口与出口坐标必须成对。</span></div></form>`, foot: `${btn("取消", "", "btn-secondary", 'data-action="close-modal"')}${btn("创建草稿", "plus", "btn-primary", 'data-action="toast" data-message="地图层草稿已创建，正在执行坐标和资源校验"')}` });
    }

    if (name === "create-content") {
      const type = CONTENT_BY_ID[dataset.type] || C[0];
      return modalShell({ title: `新建${type.singular}`, desc: `创建 ${type.label} 草稿，保存后进入校验流程。`, iconName: type.icon, body: `<form class="js-form"><div class="field-row"><div class="field"><label>名称</label><input placeholder="请输入名称" aria-label="名称"></div><div class="field"><label>本地索引</label><input value="自动分配" aria-label="本地索引" disabled></div></div><div class="field-row"><div class="field"><label>内容键</label><input value="${esc(type.entity)}.auto" aria-label="内容键"></div><div class="field"><label>数据来源</label><select><option>手工创建</option><option>从模板复制</option><option>JSON 导入</option></select></div></div><div class="field"><label>摘要</label><textarea placeholder="描述这个内容项的用途、来源和变更原因"></textarea></div><div class="risk-box">${I("info", 15)}<span>新内容默认保存为草稿，不会直接影响运行中的区服。</span></div></form>`, foot: `${btn("取消", "", "btn-secondary", 'data-action="close-modal"')}${btn("创建草稿", "plus", "btn-primary", 'data-action="create-draft"')}` });
    }
    if (name === "validate-content" || name === "validate-script") {
      return modalShell({ title: name === "validate-script" ? "运行脚本校验" : "运行内容校验", desc: "执行 Schema、引用、资源、协议和经济规则检查。", iconName: "play", body: `<div class="validation-list" style="padding:0"><div class="validation-item ok">${I("checkCircle", 12)}<span>Schema 与必填字段：通过</span></div><div class="validation-item ok">${I("checkCircle", 12)}<span>引用完整性：通过</span></div><div class="validation-item ok">${I("checkCircle", 12)}<span>资源哈希：通过</span></div><div class="validation-item warn">${I("warning", 12)}<span>经济规则：1 个 warning · 价格高于建议值</span></div><div class="validation-item ok">${I("checkCircle", 12)}<span>协议兼容：通过 · protocol v1</span></div></div><div class="divider"></div><div class="data-list"><div class="data-row"><span>校验对象</span><strong>${name === "validate-script" ? "merchant_weapon_01.npc" : "2026.11.10.0-rc2"}</strong></div><div class="data-row"><span>实体数量</span><strong>9,741</strong></div><div class="data-row"><span>结论</span><strong class="success-text">允许进入签名阶段</strong></div></div>`, foot: `${btn("关闭", "", "btn-secondary", 'data-action="close-modal"')}${btn("生成报告", "download", "btn-primary", 'data-action="toast" data-message="校验报告已生成"')}` });
    }
    if (name === "publish-release" || name === "rollback-release") {
      const rollback = name === "rollback-release";
      return modalShell({ title: rollback ? "回滚内容版本" : "发布内容版本", desc: rollback ? "将目标区服切回上一个稳定版本。" : "发布高风险操作，将写入审计并通知区服 Agent。", iconName: rollback ? "rotate" : "rocket", body: `<div class="risk-box" style="background:${rollback ? "var(--red-soft)" : "var(--orange-soft)"};border-color:${rollback ? "#f0cccc" : "#f2d9c4"};color:${rollback ? "#7b2929" : "#7b3f21"}">${I("alert", 15)}<span>${rollback ? "回滚会触发区服 staging 快照加载和原子切换。失败时保留当前版本，不影响在线玩家。" : "发布将影响所选区服；建议先 10% 灰度并观察至少 15 分钟。"}</span></div><div class="mt-16"><div class="field"><label>目标区服</label><select><option>传奇四区 · 沙城（灰度）</option><option>全部区服</option></select></div><div class="field"><label>版本</label><select><option>2026.11.10.0-rc2</option><option>2026.11.09.1</option></select></div><div class="field"><label>操作原因（必填）</label><textarea placeholder="说明发布或回滚原因，至少 12 个字符"></textarea></div></div>`, foot: `${btn("取消", "", "btn-secondary", 'data-action="close-modal"')}${btn(rollback ? "确认回滚" : "确认发布", rollback ? "rotate" : "rocket", "btn-danger", 'data-action="confirm-high-risk" data-message="高风险操作已提交审批并写入审计"')}` });
    }
    if (name === "approve" || name === "reject") {
      const approve = name === "approve";
      return modalShell({ title: approve ? "批准审批事项" : "拒绝审批事项", desc: "审批结果会通知申请人并写入审计日志。", iconName: approve ? "checkCircle" : "xCircle", body: `<div class="field"><label>审批意见</label><textarea placeholder="填写审批依据和备注">${approve ? "已核对校验报告与灰度范围，同意执行。" : "请补充影响范围与回滚方案。"}</textarea></div><div class="risk-box">${I("shield", 15)}<span>审批人与申请人不能是同一账号。高风险操作会再次校验 MFA 会话。</span></div>`, foot: `${btn("取消", "", "btn-secondary", 'data-action="close-modal"')}${btn(approve ? "确认批准" : "确认拒绝", approve ? "check" : "xCircle", approve ? "btn-primary" : "btn-danger", `data-action="confirm-high-risk" data-message="${approve ? "审批已通过" : "审批已拒绝"}"`)}` });
    }
    if (name === "upload-asset") {
      return modalShell({ title: "上传资源", desc: "资源会按 SHA-256 内容寻址，并记录来源与许可证。", iconName: "uploadCloud", body: `<form class="js-form"><div class="field"><label>资源文件</label><input type="file" aria-label="资源文件"></div><div class="field-row"><div class="field"><label>资源类型</label><select><option>图库 .lib</option><option>地图 .map</option><option>音效</option><option>特效</option><option>补丁包</option></select></div><div class="field"><label>许可证</label><select><option>需确认</option><option>内部制作</option><option>MIT</option><option>CC BY 4.0</option></select></div></div><div class="field"><label>来源说明</label><input placeholder="文件来源、授权方或制作记录" aria-label="来源说明"></div></form>`, foot: `${btn("取消", "", "btn-secondary", 'data-action="close-modal"')}${btn("上传并解析", "upload", "btn-primary", 'data-action="toast" data-message="资源上传任务已创建"')}` });
    }
    if (name === "show-help" || name === "prototype-notes") {
      return modalShell({ title: "原型说明", desc: "这是一个高保真、可点击的后台原型。", iconName: "book", body: `<div class="data-list"><div class="data-row"><span>原型范围</span><strong>M1/M2 控制面 + 运营骨架</strong></div><div class="data-row"><span>数据</span><strong>演示数据，不调用 API</strong></div><div class="data-row"><span>导航</span><strong>左侧按业务域分组</strong></div><div class="data-row"><span>快捷键</span><strong>⌘/Ctrl + K 全局搜索</strong></div><div class="data-row"><span>高风险操作</span><strong>发布 / 回滚 / GM / 合区 / 退款</strong></div></div><div class="risk-box mt-16">${I("info", 15)}<span>原型用于确认结构、版面和功能范围，不包含真实鉴权、真实数据写入或生产级前端实现。</span></div>`, foot: `${btn("关闭", "", "btn-primary", 'data-action="close-modal"')}` });
    }
    if (name === "show-notifications") {
      return modalShell({ title: "通知中心", desc: "2 条高优先级告警，3 条待办。", iconName: "bell", body: `<div class="alert-list">${D.ALERTS.map(alert => `<div class="alert-item ${alert.level === "danger" ? "danger" : alert.level === "warning" ? "warning" : ""}"><span class="alert-mark" style="color:var(--${alert.level === "danger" ? "red" : alert.level === "warning" ? "orange" : "blue"});background:var(--${alert.level === "danger" ? "red" : alert.level === "warning" ? "orange" : "blue"}-soft)">${I("alert", 13)}</span><div class="alert-copy"><strong>${esc(alert.title)}</strong><span>${esc(alert.detail)}</span><div class="alert-time">${esc(alert.time)}</div></div></div>`).join("")}</div>`, foot: `${btn("全部标为已读", "check", "btn-secondary", 'data-action="toast" data-message="通知已全部标为已读"')}${btn("关闭", "", "btn-primary", 'data-action="close-modal"')}` });
    }
    if (name === "user-menu") {
      const user = currentUser();
      return modalShell({ title: displayName(), desc: `${displayRole()} · ${user ? "控制面账号" : "原型会话"}${user?.mfaEnabled ? " · MFA 已验证" : ""}`, iconName: "user", body: `<div class="data-list"><div class="data-row"><span>账号</span><strong>${esc(user?.username || "lin.ce")}</strong></div><div class="data-row"><span>角色</span><strong>${esc((user?.roles || ["platform_admin"]).join(", "))}</strong></div><div class="data-row"><span>会话模式</span><strong>${user ? "JWT · 数据库会话" : "高保真原型"}</strong></div><div class="data-row"><span>Control API</span><strong class="mono">${esc(API.baseUrl() || location.origin)}</strong></div></div><div class="divider"></div><div class="stack-sm">${user?.principalType === "account" ? btn("修改密码", "key", "btn-secondary", 'data-action="open-modal" data-modal="change-password"') : ""}${linkBtn("管理账号", "accounts", "users", "btn-secondary")}${linkBtn("审计日志", "audit", "audit", "btn-secondary")}</div>`, foot: `${btn("退出登录", "logout", "btn-danger", 'data-action="logout"')}${btn("关闭", "", "btn-primary", 'data-action="close-modal"')}` });
    }
    if (name === "gm-command") {
      return modalShell({ title: "执行 GM 操作", desc: "高风险命令需要选择原因、目标并进入审批。", iconName: "command", body: `<form class="js-form"><div class="field-row"><div class="field"><label>目标区服</label><select><option>传奇一区 · 龙腾</option>${D.ZONES.filter(z => z.status === "open").map(z => `<option>${esc(z.name)}</option>`).join("")}</select></div><div class="field"><label>目标角色</label><input value="风起云涌" aria-label="目标角色"></div></div><div class="field-row"><div class="field"><label>命令</label><select><option>发放物品</option><option>发放金币</option><option>发送补偿邮件</option><option>传送角色</option><option>封禁账号</option></select></div><div class="field"><label>参数</label><input value="item.1001 × 1" aria-label="参数"></div></div><div class="field"><label>操作原因</label><textarea placeholder="填写工单号、补偿依据或运营方案">活动补偿，工单 OPS-20261110-042。</textarea></div><div class="risk-box">${I("alert", 15)}<span>该操作会进入审批中心，并记录操作者、目标、参数、IP 和请求 ID。</span></div></form>`, foot: `${btn("取消", "", "btn-secondary", 'data-action="close-modal"')}${btn("提交审批", "shield", "btn-primary", 'data-action="confirm-high-risk" data-message="GM 操作已提交审批"')}` });
    }
    if (name === "new-account") {
      return modalShell({ title: "创建管理账号", desc: "控制面账号与玩家账号分离，创建后会话与权限立即生效。", iconName: "user", body: `<form class="js-form js-account-create"><div class="field-row"><div class="field"><label>显示名称</label><input name="displayName" placeholder="例如 张策" aria-label="显示名称"></div><div class="field"><label>用户名</label><input name="username" placeholder="zhang.ce" aria-label="用户名"></div></div><div class="field"><label>邮箱</label><input name="email" type="email" placeholder="name@example.com" aria-label="邮箱"></div><div class="field"><label>角色</label><div style="display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:0 14px">${roleCheckboxes(["content_author"])}</div></div><div class="field"><label>初始密码</label><input name="password" type="password" placeholder="至少 12 个字符" aria-label="初始密码"><div class="field-hint">密码使用 Argon2id 存储；首次登录后建议立即修改。</div></div></form>`, foot: `${btn("取消", "", "btn-secondary", 'data-action="close-modal"')}${btn("创建账号", "plus", "btn-primary", 'data-action="submit-account"')}` });
    }
    if (name === "reset-account-password") {
      return modalShell({ title: "重置账号密码", desc: "管理员重置会撤销目标账号的全部会话。", iconName: "key", body: `<form class="js-form"><div class="field"><label>账号</label><input value="${esc(dataset.username || "")}" disabled aria-label="账号"></div><div class="field"><label>新密码</label><input name="newPassword" type="password" placeholder="至少 12 个字符" aria-label="新密码"><div class="field-hint">重置后目标账号必须使用新密码重新登录。</div></div></form>`, foot: `${btn("取消", "", "btn-secondary", 'data-action="close-modal"')}${btn("重置密码", "key", "btn-primary", `data-action="submit-account-password" data-id="${esc(dataset.id || "")}"`)}` });
    }
    if (name === "edit-account-roles") {
      const selected = String(dataset.roles || "").split(",").filter(Boolean);
      return modalShell({ title: "调整账号角色", desc: "保存后会撤销目标账号的现有会话，确保权限立即生效。", iconName: "userCheck", body: `<form class="js-form"><div class="field"><label>账号</label><input value="${esc(dataset.username || "")}" disabled aria-label="账号"></div><div class="field"><label>角色</label><div style="display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:0 14px">${roleCheckboxes(selected)}</div><div class="field-hint">至少选择一个角色；最后一个启用的平台管理员不能移除 platform_admin。</div></div></form>`, foot: `${btn("取消", "", "btn-secondary", 'data-action="close-modal"')}${btn("保存角色", "save", "btn-primary", `data-action="submit-account-roles" data-id="${esc(dataset.id || "")}"`)}` });
    }
    if (name === "account-status") {
      const currentStatus = dataset.status === "active" ? "active" : "disabled";
      const nextStatus = currentStatus === "active" ? "disabled" : "active";
      const disabling = nextStatus === "disabled";
      return modalShell({ title: disabling ? "停用账号" : "启用账号", desc: disabling ? "停用后该账号的现有会话会立即失效。" : "启用后账号可重新登录，并清除失败锁定。", iconName: disabling ? "ban" : "checkCircle", body: `<div class="data-list"><div class="data-row"><span>账号</span><strong>${esc(dataset.username || "")}</strong></div><div class="data-row"><span>当前状态</span><strong>${currentStatus === "active" ? "启用" : "停用"}</strong></div><div class="data-row"><span>目标状态</span><strong>${nextStatus === "active" ? "启用" : "停用"}</strong></div></div>${disabling ? `<div class="risk-box mt-16">${I("alert", 15)}<span>停用会撤销该账号的全部会话，但不会删除审计记录。</span></div>` : ""}`, foot: `${btn("取消", "", "btn-secondary", 'data-action="close-modal"')}${btn(disabling ? "确认停用" : "确认启用", disabling ? "ban" : "checkCircle", disabling ? "btn-danger" : "btn-primary", `data-action="submit-account-status" data-id="${esc(dataset.id || "")}" data-status="${nextStatus}"`)}` });
    }
    if (name === "change-password") {
      return modalShell({ title: "修改当前密码", desc: "修改成功后保留当前会话，撤销其他设备上的会话。", iconName: "key", body: `<form class="js-form"><div class="field"><label>当前密码</label><input name="currentPassword" type="password" aria-label="当前密码"></div><div class="field"><label>新密码</label><input name="newPassword" type="password" placeholder="至少 12 个字符" aria-label="新密码"><div class="field-hint">新密码必须与当前密码不同。</div></div></form>`, foot: `${btn("取消", "", "btn-secondary", 'data-action="close-modal"')}${btn("修改密码", "key", "btn-primary", 'data-action="submit-change-password"')}` });
    }
    if (name === "zone-maintenance") {
      return modalShell({ title: "切换区服维护状态", desc: "维护会阻止新登录，可选择保留在线玩家或强制下线。", iconName: "pause", body: `<div class="risk-box">${I("alert", 15)}<span>维护操作会影响玩家登录与在线会话，必须提前公告并写入审计。</span></div><div class="field mt-16"><label>维护模式</label><select><option>禁止新登录，保留在线玩家</option><option>踢下线并禁止登录</option><option>只读模式</option></select></div><div class="field"><label>预计结束时间</label><input value="2026-11-12 02:45" aria-label="预计结束时间"></div><div class="field"><label>公告 / 原因</label><textarea>四区内容版本升级与数据库迁移演练，预计停机 45 分钟。</textarea></div>`, foot: `${btn("取消", "", "btn-secondary", 'data-action="close-modal"')}${btn("确认维护", "pause", "btn-danger", 'data-action="confirm-high-risk" data-message="区服维护状态已切换并记录审计"')}` });
    }
    if (name === "new-template") {
      return modalShell({ title: "新建开区模板", desc: "模板版本创建后不可原地修改，用于新区服创建。", iconName: "copy", body: `<form class="js-form"><div class="field"><label>模板名称</label><input placeholder="例如 经典 1.80 · 合击版" aria-label="模板名称"></div><div class="field-row"><div class="field"><label>内容版本</label><select><option>2026.11.09.1（stable）</option><option>2026.11.10.0-rc2（staging）</option></select></div><div class="field"><label>经验 / 掉落</label><input value="1× / 1×" aria-label="经验掉落"></div></div><div class="field-row"><div class="field"><label>端口范围</label><input value="7100 - 7199" aria-label="端口范围"></div><div class="field"><label>容量上限</label><input value="3,000" aria-label="容量上限"></div></div><div class="field"><label>模板说明</label><textarea placeholder="模板定位、差异和适用运营场景"></textarea></div></form>`, foot: `${btn("取消", "", "btn-secondary", 'data-action="close-modal"')}${btn("创建模板", "plus", "btn-primary", 'data-action="toast" data-message="开区模板已创建为草稿"')}` });
    }
    if (name === "new-announcement" || name === "schedule-announcement") {
      return modalShell({ title: name === "new-announcement" ? "新建公告" : "确认公告排期", desc: "公告将按目标区服和渠道发布。", iconName: "megaphone", body: `<div class="field"><label>标题</label><input value="月华庆典活动开启" aria-label="标题"></div><div class="field-row"><div class="field"><label>目标区服</label><select><option>全部开放区服</option><option>传奇一区至五区</option></select></div><div class="field"><label>发布时间</label><input value="2026-11-12 20:00" aria-label="发布时间"></div></div><div class="risk-box">${I("info", 15)}<span>全服公告需要发布审核；游戏内公告会自动转换为纯文本并限制长度。</span></div>`, foot: `${btn("取消", "", "btn-secondary", 'data-action="close-modal"')}${btn(name === "new-announcement" ? "保存草稿" : "确认排期", "calendar", "btn-primary", 'data-action="confirm-high-risk" data-message="公告排期已保存"')}` });
    }
    if (name === "create-merge" || name === "merge-policy") {
      return modalShell({ title: name === "create-merge" ? "新建合区任务" : "合区冲突策略", desc: name === "create-merge" ? "创建任务后先执行预检，不会立即冻结区服。" : "默认策略可按任务覆盖，所有覆盖需要审批。", iconName: "merge", body: name === "create-merge" ? `<form class="js-form"><div class="field-row"><div class="field"><label>源区服</label><select>${D.ZONES.map(z => `<option>${esc(z.name)}</option>`).join("")}</select></div><div class="field"><label>目标区服</label><select>${D.ZONES.map(z => `<option>${esc(z.name)}</option>`).join("")}</select></div></div><div class="field-row"><div class="field"><label>计划窗口</label><input value="2026-11-15 02:00" aria-label="计划窗口"></div><div class="field"><label>回滚窗口</label><select><option>72 小时</option><option>48 小时</option><option>7 天</option></select></div></div><div class="field"><label>迁移范围</label><input value="账号 / 角色 / 行会 / 资产 / 邮件 / 拍卖" aria-label="迁移范围"></div><div class="risk-box">${I("alert", 15)}<span>任何 error 都会阻止切换。源区数据在回滚窗口内必须保留。</span></div></form>` : `<div class="data-list"><div class="data-row"><span>账号重复</span><strong>按身份合并</strong></div><div class="data-row"><span>角色名重复</span><strong>目标区保留，源区改名</strong></div><div class="data-row"><span>行会名重复</span><strong>目标区保留</strong></div><div class="data-row"><span>物品 ID 重复</span><strong>重新分配全局 ID</strong></div><div class="data-row"><span>邮件 / 拍卖</span><strong>迁移并重算过期</strong></div><div class="data-row"><span>攻城状态</span><strong>保留目标区</strong></div></div>`, foot: `${btn("取消", "", "btn-secondary", 'data-action="close-modal"')}${btn(name === "create-merge" ? "创建并预检" : "保存策略", name === "create-merge" ? "merge" : "save", "btn-primary", 'data-action="toast" data-message="操作已保存并记录审计"')}` });
    }
    if (name === "release-diff" || name === "release-detail" || name === "create-release" || name === "mapping" || name === "script-diff") {
      const titles = { "release-diff": "版本对比", "release-detail": "发布详情", "create-release": "创建内容版本", mapping: "旧数据库映射规则", "script-diff": "脚本变更对比" };
      return modalShell({ title: titles[name], desc: "原型中的版本、差异与映射摘要。", iconName: name === "mapping" ? "database" : "gitBranch", body: `<div class="grid grid-2"><div class="metric-mini"><span>当前稳定版</span><strong style="font-size:11px">2026.11.09.1</strong></div><div class="metric-mini"><span>候选版本</span><strong style="font-size:11px">2026.11.10.0-rc2</strong></div><div class="metric-mini"><span>新增实体</span><strong style="font-size:11px">+7</strong></div><div class="metric-mini"><span>修改实体</span><strong style="font-size:11px">18</strong></div></div><div class="divider"></div><div class="data-list"><div class="data-row"><span>物品</span><strong>修改 6 · 新增 2</strong></div><div class="data-row"><span>怪物</span><strong>修改 3</strong></div><div class="data-row"><span>地图</span><strong>修改 1 · 新增 1</strong></div><div class="data-row"><span>脚本</span><strong>修改 4</strong></div><div class="data-row"><span>资源引用</span><strong class="warning-text">1 个哈希变化</strong></div></div>`, foot: `${btn("关闭", "", "btn-secondary", 'data-action="close-modal"')}${btn(name === "create-release" ? "创建版本" : "导出报告", name === "create-release" ? "plus" : "download", "btn-primary", 'data-action="toast" data-message="操作已提交"')}` });
    }
    modalShell({ title: "功能预览", desc: "该入口属于完整后台结构，本原型已预留页面与交互位置。", iconName: "sparkles", body: `<div class="risk-box">${I("info", 15)}<span>这个操作在后续接入真实 API 时实现。当前原型用于确认信息架构、版面与权限边界。</span></div>`, foot: `${btn("关闭", "", "btn-primary", 'data-action="close-modal"')}` });
  }

  function genericConfirm(message) {
    modalShell({ title: "请确认操作", desc: "高风险操作需要二次确认，并写入只追加审计日志。", iconName: "alert", body: `<div class="risk-box">${I("alert", 15)}<span>${esc(message)}</span></div><div class="field mt-16"><label>操作原因</label><textarea placeholder="请填写操作原因，至少 12 个字符"></textarea></div>`, foot: `${btn("取消", "", "btn-secondary", 'data-action="close-modal"')}${btn("确认执行", "check", "btn-danger", `data-action="confirm-high-risk" data-message="${esc(message)}"`)}` });
  }

  window.addEventListener("hashchange", () => { closeOverlays(); render(); });
  document.addEventListener("change", event => {
    if (event.target.matches("[data-live-map-select]")) {
      selectLiveMap(event.target.value);
    }
  });
  document.addEventListener("click", event => {
    const routeEl = event.target.closest("[data-route]");
    if (routeEl && !event.target.closest("[data-action]")) {
      event.preventDefault();
      navigate(routeEl.dataset.route);
      return;
    }
    const actionEl = event.target.closest("[data-action]");
    if (actionEl) {
      const action = actionEl.dataset.action;
      if (action === "navigate") {
        event.preventDefault();
        navigate(actionEl.dataset.route);
        return;
      }
      if (action === "toggle-group") {
        const group = actionEl.dataset.group;
        state.collapsedGroups.has(group) ? state.collapsedGroups.delete(group) : state.collapsedGroups.add(group);
        render();
        return;
      }
      if (action === "toggle-sidebar") { state.sidebarOpen = !state.sidebarOpen; render(); return; }
      if (action === "close-sidebar") { state.sidebarOpen = false; render(); return; }
      if (action === "open-spotlight") { openSpotlight(); return; }
      if (action === "select-world-map") { state.selectedWorldMap = actionEl.dataset.mapKey; render(); return; }
      if (action === "open-world-map") { state.selectedWorldMap = actionEl.dataset.mapKey; navigate("world-map"); return; }
      if (action === "live-respawn-page") {
        const page = Number(actionEl.dataset.page);
        if (Number.isFinite(page) && page > 0) void ensureMapRelations(page).catch(handleApiFailure);
        return;
      }
      if (action === "reload-live-map") {
        const relations = liveRelations();
        relations.catalog = null;
        relations.catalogTotal = 0;
        relations.catalogQuery = "";
        relations.catalogPromise = null;
        relations.selectedMapIndex = null;
        relations.response = null;
        relations.loadedKey = null;
        relations.inflightKey = null;
        relations.error = null;
        void ensureLiveMapCatalog()
          .then(() => ensureMapRelations(1))
          .catch(handleApiFailure);
        return;
      }
      if (action === "open-live-drops") {
        void openLiveDrops(Number(actionEl.dataset.index), actionEl.dataset.name || "");
        return;
      }
      if (action === "select-relation") { state.relationScenario = actionEl.dataset.scenario; render(); return; }
      if (action === "open-content") { openContentDrawer(actionEl.dataset.type, actionEl.dataset.key); return; }
      if (action === "open-audit") {
        const events = auditRows();
        const event = events[Number(actionEl.dataset.index)] || events[0];
        if (!event) return;
        modalShell({ title: "审计事件详情", desc: `${event.action} · ${event.time}`, iconName: "audit", body: `<div class="data-list"><div class="data-row"><span>事件 ID</span><strong class="mono">${esc(event.id ? `event-${event.id}` : `evt-${esc(actionEl.dataset.index)}-4c1e`)}</strong></div><div class="data-row"><span>操作者</span><strong>${esc(event.actor)}</strong></div><div class="data-row"><span>动作</span><strong class="mono">${esc(event.action)}</strong></div><div class="data-row"><span>资源</span><strong class="mono">${esc(event.resource)}</strong></div><div class="data-row"><span>来源 IP</span><strong class="mono">${esc(event.ip)}</strong></div><div class="data-row"><span>结果</span><strong>${status(event.result)}</strong></div></div><div class="divider"></div><div class="field"><label>详情</label><textarea readonly>${esc(event.detail)}</textarea></div><div class="data-list"><div class="data-row"><span>请求 ID</span><strong class="mono">${esc(event.requestId || "req-8e2f…")}</strong></div><div class="data-row"><span>Trace</span><strong class="mono">${esc(event.requestId || "4c1e8b…")}</strong></div></div>`, foot: `${btn("复制请求 ID", "copy", "btn-secondary", 'data-action="toast" data-message="请求 ID 已复制"')}${btn("关闭", "", "btn-primary", 'data-action="close-modal"')}` });
        return;
      }
      if (action === "open-import-run") {
        const id = Number(actionEl.dataset.id);
        if (!isLive()) { showToast("当前为原型模式，批次详情使用演示数据"); return; }
        const run = state.live.runs?.find(item => Number(item.id) === id);
        if (!run) return;
        state.live.run = run;
        state.live.counts = null;
        state.live.relations = emptyLiveState().relations;
        render();
        API.getContentTypes(id).then(counts => {
          state.live.counts = counts;
          if (state.route === "import-runs") render();
        }).catch(error => {
          if (error?.status === 401) handleApiFailure(error);
          else showToast(`读取批次计数失败：${error.message}`, "danger");
        });
        return;
      }
      if (action === "content-page") {
        const type = CONTENT_BY_ID[actionEl.dataset.type];
        const page = Math.max(1, Number(actionEl.dataset.page) || 1);
        if (!type) return;
        contentQueryState(type).page = page;
        render();
        return;
      }
      if (action === "reload-audit") {
        state.live.audit = null;
        state.failed.delete("audit");
        render();
        return;
      }
      if (action === "reload-accounts") {
        state.live.accounts = null;
        state.failed.delete("accounts");
        render();
        return;
      }
      if (action === "reload-roles") {
        state.live.roles = null;
        state.failed.delete("roles");
        render();
        return;
      }
      if (action === "select-role") {
        state.selectedRoleCode = actionEl.dataset.code || null;
        render();
        return;
      }
      if (action === "export-audit") { exportAuditCsv(); return; }
      if (action === "download-asset") { void downloadAssetFromDrawer(actionEl); return; }
      if (action === "open-modal") { openModal(actionEl.dataset.modal, actionEl.dataset); return; }
      if (action === "close-modal" || action === "close-drawer" || action === "close-overlays") { closeOverlays(); return; }
      if (action === "login") { state.apiStatus = "demo"; state.user = null; navigate("dashboard"); setTimeout(() => showToast("已进入 MIR Control 高保真原型"), 80); return; }
      if (action === "logout") {
        state.user = null;
        state.apiStatus = "demo";
        state.selectedRoleCode = null;
        state.live = emptyLiveState();
        state.failed.clear();
        closeOverlays();
        navigate("login");
        API.logout().catch(() => {});
        return;
      }
      if (action === "toast") { showToast(actionEl.dataset.message || "操作已提交"); return; }
      if (action === "show-notifications") { openModal("show-notifications"); return; }
      if (action === "show-help") { openModal("show-help"); return; }
      if (action === "user-menu") { openModal("user-menu"); return; }
      if (action === "confirm-high-risk") { closeOverlays(); showToast(actionEl.dataset.message || "操作已确认并写入审计"); return; }
      if (action === "submit-import") { closeOverlays(); showToast("导入任务已创建，批次 #33 正在执行"); return; }
      if (action === "submit-zone") { closeOverlays(); showToast("新区服已创建，正在进入预热"); return; }
      if (action === "create-draft") { closeOverlays(); showToast("内容草稿已创建"); return; }
      if (action === "submit-gm") { closeOverlays(); showToast("GM 命令已提交，等待高风险审批"); return; }
      if (action === "account-toggle-status") { openModal("account-status", actionEl.dataset); return; }
      if (action === "submit-account") { void createAccountFromModal(); return; }
      if (action === "submit-account-password") { void resetAccountPasswordFromModal(); return; }
      if (action === "submit-account-roles") { void updateAccountRolesFromModal(); return; }
      if (action === "submit-account-status") { void setAccountStatusFromModal(actionEl); return; }
      if (action === "submit-change-password") { void changeOwnPasswordFromModal(); return; }
      if (action === "submit-settings") { showToast("设置已保存，配置变更已写入审计"); return; }
      if (action === "confirm-merge") { genericConfirm("确认切换合区入口并解冻目标区服？当前操作不可自动撤销。"); return; }
      if (action === "retry-job") { showToast("任务已重新加入队列"); return; }
      if (action === "cancel-job") { genericConfirm("确认取消正在执行的任务？部分已完成步骤可能保留。"); return; }
      if (action === "refresh" || action === "export" || action === "save") { showToast(actionEl.dataset.message || "操作已完成"); return; }
    }
    if (event.target.classList.contains("drawer-backdrop") || event.target.classList.contains("modal-backdrop") || event.target.classList.contains("spotlight-backdrop")) {
      closeOverlays();
    }
  });

  document.addEventListener("input", event => {
    if (event.target.id === "spotlight-input") {
      const results = document.getElementById("spotlight-results");
      if (results) results.innerHTML = spotlightResults(event.target.value);
      return;
    }
    if (event.target.matches("[data-live-content-search]") && isLive()) {
      const type = CONTENT_BY_ID[event.target.dataset.liveContentSearch];
      if (!type) return;
      const value = event.target.value;
      window.clearTimeout(state.searchTimer);
      state.searchTimer = window.setTimeout(() => {
        const query = contentQueryState(type);
        query.q = value.trim();
        query.page = 1;
        state.focusContentSearch = `content-search-${type.id}`;
        render();
      }, 300);
      return;
    }
    if (event.target.matches("[data-live-map-filter]") && isLive()) {
      const relations = liveRelations();
      relations.catalogQuery = event.target.value;
      const select = document.getElementById("live-map-select");
      if (select) {
        const visible = filteredLiveMapCatalog();
        select.innerHTML = liveMapOptions(visible, relations.selectedMapIndex);
        const selectedVisible = visible.some(item => Number(item.localIndex) === Number(relations.selectedMapIndex));
        select.value = selectedVisible ? String(relations.selectedMapIndex) : "";
      }
      return;
    }
    if (event.target.matches("[data-table-filter]")) {
      const selector = event.target.dataset.tableFilter;
      const root = document.querySelector(selector);
      if (!root) return;
      const q = event.target.value.trim().toLowerCase();
      root.querySelectorAll("tbody tr, .asset-card, .domain-card").forEach(row => {
        row.style.display = !q || row.innerText.toLowerCase().includes(q) ? "" : "none";
      });
    }
  });

  document.addEventListener("submit", event => {
    if (event.target.matches(".js-login")) {
      event.preventDefault();
      const form = event.target;
      const errorBox = document.getElementById("login-error");
      const submit = form.querySelector(".login-submit");
      const username = String(form.elements.username?.value || "").trim();
      const password = String(form.elements.password?.value || "");
      const showError = message => {
        if (!errorBox) return;
        errorBox.textContent = message;
        errorBox.hidden = false;
      };
      if (errorBox) errorBox.hidden = true;
      if (!username || !password) {
        showError("请输入管理端用户名和密码。");
        return;
      }
      if (submit) {
        submit.disabled = true;
        submit.dataset.label = submit.innerHTML;
        submit.textContent = "正在验证...";
      }
      (async () => {
        try {
          const session = await API.login(username, password);
          state.user = session.user;
          state.apiStatus = "live";
          state.apiError = "";
          state.failed.clear();
          showToast(`登录成功，已进入运营驾驶舱 · ${displayName()}`);
          navigate("dashboard");
        } catch (error) {
          if (error?.status === 401) {
            showError("用户名或密码错误，或账号已被锁定。");
          } else if (error?.status === 429) {
            showError("登录尝试过于频繁，请稍后再试。");
          } else if (!error?.status) {
            showError(`无法连接 Control API（${API.baseUrl() || location.origin}）。可点击下方进入高保真原型。`);
          } else {
            showError(error.message || "登录失败，请稍后重试。");
          }
        } finally {
          if (submit) {
            submit.disabled = false;
            if (submit.dataset.label) submit.innerHTML = submit.dataset.label;
          }
        }
      })();
      return;
    }
    if (event.target.matches(".js-form")) {
      event.preventDefault();
      showToast("表单已提交");
    }
  });

  document.addEventListener("keydown", event => {
    if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === "k") {
      event.preventDefault();
      openSpotlight();
      return;
    }
    if (event.key === "Escape") {
      closeOverlays();
      if (state.sidebarOpen) { state.sidebarOpen = false; render(); }
    }
  });

  const restored = API.restoreSession();
  if (restored) {
    state.user = restored.user || null;
    state.apiStatus = state.user ? "live" : "demo";
  }
  render();
  if (restored) {
    API.me().then(user => {
      if (user?.principalType === "account") {
        state.user = Object.assign({}, state.user, user);
        state.apiStatus = "live";
        render();
      }
    }).catch(error => handleApiFailure(error));
  }
})();
