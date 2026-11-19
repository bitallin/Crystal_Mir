import { useEffect, useRef, useState } from "preact/hooks";
import { InputController } from "../input/InputController";
import { GameRenderer } from "../render/GameRenderer";
import type { GameSession, SessionState } from "../session/GameSession";
import { BrandMark } from "./BrandMark";
import { GameHud, type ControlHandle } from "./GameHud";

export function GameScreen({ session, state }: { session: GameSession; state: SessionState }) {
  const controls: ControlHandle = useRef<InputController | null>(null);
  const [fps, setFps] = useState(0);
  const [status, setStatus] = useState<string | null>(null);
  const [targetId, setTargetId] = useState<number | null>(null);
  return (
    <main class="game-shell">
      <WorldViewport
        session={session}
        mapAsset={state.mapAsset}
        controls={controls}
        onFps={setFps}
        onStatus={setStatus}
        onTarget={setTargetId}
      />
      <GameHud
        session={session}
        state={state}
        controls={controls}
        targetId={targetId}
        fps={fps}
        status={status}
        onCloseNotice={() => setStatus(null)}
      />
      {state.mapLoading && (
        <div class="map-loading" role="status">
          <BrandMark compact />
          <div><strong>{state.notice ?? "正在载入地图"}</strong><span>地图与图库按需加载</span></div>
        </div>
      )}
      <div class="rotate-gate" role="status">
        <div class="rotate-gate__device"><span /></div>
        <strong>请横屏游玩</strong>
        <p>旋转设备后，世界与操作区会自动适配。</p>
      </div>
    </main>
  );
}

function WorldViewport({
  session,
  mapAsset,
  controls,
  onFps,
  onStatus,
  onTarget
}: {
  session: GameSession;
  mapAsset: SessionState["mapAsset"];
  controls: ControlHandle;
  onFps(value: number): void;
  onStatus(value: string | null): void;
  onTarget(value: number | null): void;
}) {
  const host = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!mapAsset || !host.current) return;
    const element = host.current;
    const renderer = new GameRenderer({
      onFps,
      onStatus,
      onError: message => onStatus(`渲染器错误：${message}`)
    });
    let input: InputController | null = null;
    let cancelled = false;
    void renderer.init(element, session).then(() => {
      if (cancelled) return;
      input = new InputController(session, renderer, {
        onTargetChanged: onTarget,
        onNotice: onStatus
      });
      input.attach(element);
      controls.current = input;
    }).catch(exception => {
      if (cancelled) return;
      onStatus(exception instanceof Error ? exception.message : "WebGL 场景初始化失败。");
    });
    return () => {
      cancelled = true;
      input?.detach();
      if (controls.current === input) controls.current = null;
      renderer.destroy();
    };
  }, [session, mapAsset, controls, onFps, onStatus, onTarget]);
  return <div ref={host} class="world-host" />;
}
