(function () {
  const SESSION_KEY = "mir.control.session";
  const BASE_KEY = "mir.control.apiBase";
  const config = window.MirControlConfig || {};

  class ApiError extends Error {
    constructor(message, status, problem) {
      super(message);
      this.name = "ApiError";
      this.status = status || 0;
      this.problem = problem || null;
    }
  }

  let session = readSession();
  let refreshPromise = null;

  function readSession() {
    try {
      const raw = window.sessionStorage.getItem(SESSION_KEY);
      return raw ? JSON.parse(raw) : null;
    } catch {
      return null;
    }
  }

  function writeSession(tokens) {
    session = {
      accessToken: tokens.accessToken,
      refreshToken: tokens.refreshToken,
      accessTokenExpiresAt: tokens.accessTokenExpiresAt,
      refreshTokenExpiresAt: tokens.refreshTokenExpiresAt,
      user: tokens.user
    };
    window.sessionStorage.setItem(SESSION_KEY, JSON.stringify(session));
    return session;
  }

  function clearSession() {
    session = null;
    window.sessionStorage.removeItem(SESSION_KEY);
  }

  function baseUrl() {
    return String(config.apiBaseUrl || window.sessionStorage.getItem(BASE_KEY) || "").replace(/\/+$/, "");
  }

  function setBaseUrl(value) {
    const normalized = String(value || "").replace(/\/+$/, "");
    config.apiBaseUrl = normalized;
    if (normalized) {
      window.sessionStorage.setItem(BASE_KEY, normalized);
    } else {
      window.sessionStorage.removeItem(BASE_KEY);
    }
    return normalized;
  }

  function isAuthenticated() {
    return Boolean(session && session.accessToken);
  }

  function getUser() {
    return session ? session.user : null;
  }

  function tokenNeedsRefresh() {
    if (!session || !session.accessTokenExpiresAt || !session.refreshToken) return false;
    const expiresAt = Date.parse(session.accessTokenExpiresAt);
    return Number.isFinite(expiresAt) && expiresAt - Date.now() < 30000;
  }

  async function fetchJson(path, options) {
    const controller = new AbortController();
    const timeout = window.setTimeout(() => controller.abort(), config.requestTimeoutMs || 12000);
    const headers = Object.assign({ Accept: "application/json" }, options.headers || {});
    if (options.body !== undefined && !headers["Content-Type"]) {
      headers["Content-Type"] = "application/json";
    }
    try {
      const response = await window.fetch(`${baseUrl()}${path}`, {
        method: options.method || "GET",
        headers,
        body: options.body === undefined ? undefined : JSON.stringify(options.body),
        signal: controller.signal
      });
      const text = response.status === 204 ? "" : await response.text();
      let payload = null;
      if (text) {
        try { payload = JSON.parse(text); } catch { payload = { detail: text }; }
      }
      if (!response.ok) {
        const detail = payload && (payload.detail || payload.title || payload.message);
        throw new ApiError(detail || `Request failed with HTTP ${response.status}.`, response.status, payload);
      }
      return payload;
    } catch (error) {
      if (error.name === "AbortError") {
        throw new ApiError("Control API request timed out.", 0, null);
      }
      throw error;
    } finally {
      window.clearTimeout(timeout);
    }
  }

  async function refresh() {
    if (!session || !session.refreshToken) {
      throw new ApiError("No refresh token is available.", 401, null);
    }
    if (!refreshPromise) {
      refreshPromise = fetchJson("/api/v1/auth/refresh", {
        method: "POST",
        body: { refreshToken: session.refreshToken }
      }).then(tokens => writeSession(tokens)).finally(() => { refreshPromise = null; });
    }
    return refreshPromise;
  }

  async function request(path, options = {}) {
    const useAuth = options.auth !== false;
    const retry = options.retry !== false;
    if (useAuth && tokenNeedsRefresh()) {
      await refresh();
    }
    const headers = Object.assign({}, options.headers || {});
    if (useAuth && session && session.accessToken) {
      headers.Authorization = `Bearer ${session.accessToken}`;
    }
    try {
      return await fetchJson(path, {
        method: options.method,
        body: options.body,
        headers
      });
    } catch (error) {
      if (error instanceof ApiError && error.status === 401 && useAuth && retry && session && session.refreshToken) {
        await refresh();
        return request(path, Object.assign({}, options, { retry: false }));
      }
      throw error;
    }
  }

  async function login(username, password) {
    const tokens = await request("/api/v1/auth/login", {
      method: "POST",
      auth: false,
      body: { username, password }
    });
    return writeSession(tokens);
  }

  async function logout() {
    try {
      if (isAuthenticated()) {
        await request("/api/v1/auth/logout", { method: "POST", retry: false });
      }
    } finally {
      clearSession();
    }
  }

  async function me() {
    const user = await request("/api/v1/auth/me");
    if (session && user && user.principalType === "account") {
      session.user = Object.assign({}, session.user, user);
      window.sessionStorage.setItem(SESSION_KEY, JSON.stringify(session));
    }
    return user;
  }

  function listImportRuns(limit = 20) {
    return request(`/api/v1/import-runs?limit=${encodeURIComponent(limit)}`);
  }

  function getImportRun(id) {
    return request(`/api/v1/import-runs/${encodeURIComponent(id)}`);
  }

  function getContentTypes(importRunId) {
    return request(`/api/v1/import-runs/${encodeURIComponent(importRunId)}/content-types`);
  }

  function queryContent(entityType, options = {}) {
    const params = new URLSearchParams();
    if (options.importRunId) params.set("importRunId", options.importRunId);
    params.set("page", options.page || 1);
    params.set("pageSize", options.pageSize || 50);
    if (options.q) params.set("q", options.q);
    params.set("sort", options.sort || "index");
    params.set("direction", options.direction || "asc");
    return request(`/api/v1/content/${encodeURIComponent(entityType)}?${params}`);
  }

  function getContentByKey(entityType, contentKey, importRunId) {
    const params = importRunId ? `?importRunId=${encodeURIComponent(importRunId)}` : "";
    return request(`/api/v1/content/${encodeURIComponent(entityType)}/key/${encodeURIComponent(contentKey)}${params}`);
  }

  function getMapRelations(localIndex, options = {}) {
    const params = new URLSearchParams();
    if (options.importRunId) params.set("importRunId", options.importRunId);
    params.set("respawnPage", options.respawnPage || 1);
    params.set("respawnPageSize", options.respawnPageSize || 50);
    return request(`/api/v1/content/map/${encodeURIComponent(localIndex)}/relations?${params}`);
  }

  function listMapCatalog(options = {}) {
    const params = new URLSearchParams();
    if (options.importRunId) params.set("importRunId", options.importRunId);
    if (options.q) params.set("q", options.q);
    params.set("page", options.page || 1);
    params.set("pageSize", options.pageSize || 500);
    return request(`/api/v1/content/map/catalog?${params}`);
  }

  function getMonsterDrops(localIndex, importRunId) {
    const params = importRunId ? `?importRunId=${encodeURIComponent(importRunId)}` : "";
    return request(`/api/v1/content/monster/${encodeURIComponent(localIndex)}/drops${params}`);
  }

  function getRecipeRelations(localIndex, importRunId) {
    const params = importRunId ? `?importRunId=${encodeURIComponent(importRunId)}` : "";
    return request(`/api/v1/content/recipe/${encodeURIComponent(localIndex)}/relations${params}`);
  }

  function getScriptValidation(localIndex, importRunId) {
    const params = importRunId ? `?importRunId=${encodeURIComponent(importRunId)}` : "";
    return request(`/api/v1/content/script/${encodeURIComponent(localIndex)}/validation${params}`);
  }

  async function downloadAsset(localIndex, importRunId) {
    if (tokenNeedsRefresh()) await refresh();
    const params = importRunId ? `?importRunId=${encodeURIComponent(importRunId)}` : "";
    const path = `/api/v1/content/asset/${encodeURIComponent(localIndex)}/file${params}`;
    let response = await window.fetch(`${baseUrl()}${path}`, {
      headers: { Authorization: `Bearer ${session?.accessToken || ""}` }
    });
    if (response.status === 401 && session?.refreshToken) {
      await refresh();
      response = await window.fetch(`${baseUrl()}${path}`, {
        headers: { Authorization: `Bearer ${session?.accessToken || ""}` }
      });
    }
    if (!response.ok) {
      const text = await response.text();
      let payload = null;
      try { payload = JSON.parse(text); } catch { payload = { detail: text }; }
      throw new ApiError(payload?.detail || payload?.title || `HTTP ${response.status}`, response.status, payload);
    }
    const disposition = response.headers.get("content-disposition") || "";
    const match = disposition.match(/filename="?([^";]+)"?/i);
    return {
      blob: await response.blob(),
      fileName: match ? match[1] : `asset-${localIndex}`
    };
  }

  function listAuditEvents(limit = 100, filters = {}) {
    const params = new URLSearchParams({ limit });
    if (filters.action) params.set("action", filters.action);
    if (filters.result) params.set("result", filters.result);
    return request(`/api/v1/audit/events?${params}`);
  }

  function listAccounts(limit = 50) {
    return request(`/api/v1/accounts?limit=${encodeURIComponent(limit)}`);
  }

  function createAccount(account) {
    return request("/api/v1/accounts", { method: "POST", body: account });
  }

  function setAccountStatus(id, status) {
    return request(`/api/v1/accounts/${encodeURIComponent(id)}/status`, {
      method: "POST",
      body: { status }
    });
  }

  function resetAccountPassword(id, newPassword) {
    return request(`/api/v1/accounts/${encodeURIComponent(id)}/password-reset`, {
      method: "POST",
      body: { newPassword }
    });
  }

  function updateAccountRoles(id, roles) {
    return request(`/api/v1/accounts/${encodeURIComponent(id)}/roles`, {
      method: "PUT",
      body: { roles }
    });
  }

  function changePassword(currentPassword, newPassword) {
    return request("/api/v1/auth/change-password", {
      method: "POST",
      body: { currentPassword, newPassword }
    });
  }

  function listRoles() {
    return request("/api/v1/roles");
  }

  function restoreSession() {
    session = readSession();
    return session;
  }

  window.MirControlApi = {
    ApiError,
    baseUrl,
    changePassword,
    clearSession,
    createAccount,
    getContentByKey,
    getContentTypes,
    getImportRun,
    getMapRelations,
    getMonsterDrops,
    getRecipeRelations,
    getScriptValidation,
    getUser,
    isAuthenticated,
    listAccounts,
    listAuditEvents,
    listImportRuns,
    listMapCatalog,
    listRoles,
    login,
    logout,
    me,
    queryContent,
    downloadAsset,
    resetAccountPassword,
    restoreSession,
    setAccountStatus,
    updateAccountRoles,
    setBaseUrl
  };
})();
