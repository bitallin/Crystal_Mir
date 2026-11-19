export interface BootstrapInfo {
  protocol: string;
  zoneId: string;
  wsUrl: string;
  assetVersion: string;
  assetManifestUrl: string;
  clientBuildHash: string;
}

export class BootstrapError extends Error {
  constructor(message: string, readonly detail?: unknown) {
    super(message);
    this.name = "BootstrapError";
  }
}

export async function loadBootstrap(signal?: AbortSignal): Promise<BootstrapInfo> {
  const path = import.meta.env.VITE_BOOTSTRAP_URL ?? "/api/web/bootstrap";
  const url = new URL(path, window.location.origin).toString();
  let response: Response;
  try {
    const init: RequestInit = {
      credentials: "same-origin",
      headers: { Accept: "application/json" },
      cache: "no-store"
    };
    if (signal) init.signal = signal;
    response = await fetch(url, init);
  } catch (exception) {
    throw new BootstrapError("无法连接游戏网关，请稍后重试。", exception);
  }
  if (!response.ok) throw new BootstrapError(`网关启动配置不可用（HTTP ${response.status}）。`);
  const value = (await response.json()) as Partial<BootstrapInfo>;
  if (value.protocol !== "crystal-v1") throw new BootstrapError(`不支持的 Web 协议：${value.protocol ?? "missing"}`);
  if (!value.zoneId || !value.wsUrl || !value.assetManifestUrl || !value.clientBuildHash) {
    throw new BootstrapError("网关启动配置不完整。");
  }
  return value as BootstrapInfo;
}

export function resolveWebSocketUrl(bootstrap: BootstrapInfo): string {
  const url = new URL(bootstrap.wsUrl, window.location.origin);
  url.protocol = window.location.protocol === "https:" ? "wss:" : "ws:";
  return url.toString();
}

export function versionHashBytes(clientBuildHash: string): Uint8Array {
  return new TextEncoder().encode(clientBuildHash);
}
