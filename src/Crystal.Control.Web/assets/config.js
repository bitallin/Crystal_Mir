(function () {
  const params = new URLSearchParams(window.location.search);
  const configured = params.get("api");
  if (configured) {
    window.sessionStorage.setItem("mir.control.apiBase", configured);
  }
  const stored = window.sessionStorage.getItem("mir.control.apiBase") || "";
  window.MirControlConfig = Object.assign({
    apiBaseUrl: stored,
    requestTimeoutMs: 12000
  }, window.MirControlConfig || {});
})();
