export interface WebGL2Report {
  supported: boolean;
  renderer: string | null;
  reason: string | null;
}

export function probeWebGL2(): WebGL2Report {
  if (typeof document === "undefined") {
    return { supported: false, renderer: null, reason: "当前环境没有可用的 Canvas。" };
  }
  const canvas = document.createElement("canvas");
  try {
    const context = canvas.getContext("webgl2", { antialias: false, powerPreference: "high-performance" });
    if (!context) {
      return {
        supported: false,
        renderer: null,
        reason: "浏览器未启用 WebGL2。请更新浏览器、开启硬件加速，或换用最新版 Chrome、Edge、Firefox、Safari。"
      };
    }
    const debug = context.getExtension("WEBGL_debug_renderer_info");
    const renderer = debug
      ? String(context.getParameter(debug.UNMASKED_RENDERER_WEBGL))
      : context.getParameter(context.RENDERER);
    context.getExtension("WEBGL_lose_context")?.loseContext();
    return { supported: true, renderer, reason: null };
  } catch (exception) {
    return {
      supported: false,
      renderer: null,
      reason: exception instanceof Error ? exception.message : "无法初始化 WebGL2。"
    };
  }
}
