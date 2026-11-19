import { render } from "preact";
import { App } from "./app/App";
import { probeWebGL2 } from "./render/capabilities";
import { GameSession } from "./session/GameSession";
import "./styles/game.css";

const root = document.getElementById("app");
if (!root) throw new Error("Missing #app root.");

const capabilities = probeWebGL2();
const unsupportedReason = !capabilities.supported
  ? capabilities.reason ?? "当前浏览器不支持 WebGL2。"
  : typeof DecompressionStream === "undefined"
    ? "浏览器缺少 DecompressionStream，无法解码区服的压缩数据。请升级到 Safari 16.4+ 或最新版主流浏览器。"
    : null;
if (unsupportedReason) {
  render(<UnsupportedBrowser reason={unsupportedReason} />, root);
} else {
  const session = new GameSession();
  render(<App session={session} />, root);
}

function UnsupportedBrowser({ reason }: { reason: string }) {
  return (
    <main class="entry entry--blocked">
      <section class="error-card">
        <div class="error-card__sigil">GL</div>
        <p class="eyebrow">WEBGL2 REQUIRED</p>
        <h1>浏览器无法绘制世界</h1>
        <p class="error-card__detail">{reason}</p>
        <ul class="support-list">
          <li>升级到最新两个版本的 Chrome、Edge、Firefox 或 Safari。</li>
          <li>在浏览器设置中开启硬件加速，并重新启动浏览器。</li>
          <li>iOS 请使用 Safari 16.4 及以上版本，Android 请使用 Chrome 110 及以上版本。</li>
        </ul>
        <button type="button" class="button button--primary" onClick={() => window.location.reload()}>重新检测</button>
      </section>
    </main>
  );
}
