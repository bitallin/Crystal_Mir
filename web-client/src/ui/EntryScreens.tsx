import { useEffect, useState } from "preact/hooks";
import type { SessionState } from "../session/GameSession";
import type { GameSession } from "../session/GameSession";
import { BrandMark } from "./BrandMark";
import { CLASS_NAMES, characterSubtitle, classGlyph } from "./format";

export function BootScreen({ state }: { state: SessionState }) {
  const steps = [
    { key: "bootstrap", label: "区服配置", done: state.bootstrap !== null },
    { key: "assets", label: "资源指纹", done: state.assetManifest !== null },
    { key: "gateway", label: "安全网关", done: state.link === "open" }
  ];
  return (
    <main class="entry entry--boot">
      <div class="boot-art">
        <BrandMark />
        <p class="eyebrow">CRYSTAL · BROWSER EDITION</p>
        <h1>石门正在开启</h1>
        <p class="entry__lede">{state.notice ?? "正在建立与玛法大陆的连接。"}</p>
        <div class="boot-steps" aria-label="启动进度">
          {steps.map(step => (
            <span key={step.key} class={`boot-step${step.done ? " is-done" : ""}`}>
              <i />
              {step.label}
            </span>
          ))}
        </div>
      </div>
      <div class="boot-loading" role="status">
        <span /> <span /> <span />
      </div>
    </main>
  );
}

const BLOCKING_COPY: Readonly<Record<string, { title: string; detail: string }>> = {
  blocked: { title: "资源门禁阻止进入", detail: "当前区服内容与 Web 资源包不是兼容组合。" },
  error: { title: "无法进入玛法大陆", detail: "连接或资源加载失败，请检查网络与服务端状态。" }
};

export function BlockingScreen({
  state,
  onRetry,
  onReconnectAssets
}: {
  state: SessionState;
  onRetry(): void;
  onReconnectAssets?(): void;
}) {
  const copy = BLOCKING_COPY[state.phase] ?? BLOCKING_COPY.error!;
  return (
    <main class="entry entry--blocked">
      <section class="error-card">
        <div class="error-card__sigil">!</div>
        <p class="eyebrow">CONNECTION INTERRUPTED</p>
        <h1>{copy.title}</h1>
        <p class="error-card__detail">{state.error || copy.detail}</p>
        {state.bootstrap && (
          <dl class="error-meta">
            <div><dt>区服</dt><dd>{state.bootstrap.zoneId}</dd></div>
            <div><dt>资源</dt><dd>{state.bootstrap.assetVersion}</dd></div>
            <div><dt>网关</dt><dd>{state.link === "open" ? "已连接" : "未连接"}</dd></div>
          </dl>
        )}
        <div class="error-card__actions">
          <button type="button" class="button button--primary" onClick={onRetry}>重新连接</button>
          {onReconnectAssets && <button type="button" class="button button--ghost" onClick={onReconnectAssets}>重试地图资源</button>}
        </div>
        <p class="error-card__hint">旧版 Windows 客户端仍可通过 TCP 连接同一区服。</p>
      </section>
    </main>
  );
}

export function LoginScreen({ state, onLogin }: { state: SessionState; onLogin(account: string, password: string): void }) {
  const [account, setAccount] = useState(() => localStorage.getItem("crystal.web.account") ?? "");
  const [password, setPassword] = useState("");
  const [visible, setVisible] = useState(false);
  const canSubmit = state.link === "open" && account.trim().length > 0 && password.length > 0;

  return (
    <main class="entry entry--login">
      <section class="login-story">
        <BrandMark />
        <p class="eyebrow">THE LEGEND RETURNS</p>
        <h1>从浏览器，重返<br /><em>玛法大陆</em></h1>
        <p class="entry__lede">无需安装客户端。账号、角色与区服状态沿用现有 Crystal 服务端。</p>
        <div class="story-runes" aria-hidden="true">
          <span>战</span><span>法</span><span>道</span><span>刺</span><span>弓</span>
        </div>
        <div class="story-meta">
          <span class="signal-dot" /> 区服 {state.bootstrap?.zoneId ?? "连接中"}
          <span>协议 {state.bootstrap?.protocol ?? "crystal-v1"}</span>
        </div>
      </section>

      <section class="login-panel">
        <div class="login-panel__head">
          <p class="eyebrow">EXISTING ACCOUNT</p>
          <h2>登录你的账号</h2>
          <p>首版使用现有游戏账号，注册与改密请暂时使用旧客户端。</p>
        </div>
        <form
          class="stack-form"
          onSubmit={event => {
            event.preventDefault();
            if (!canSubmit) return;
            localStorage.setItem("crystal.web.account", account.trim());
            onLogin(account, password);
          }}
        >
          <label class="field">
            <span>账号</span>
            <input
              name="account"
              autoComplete="username"
              autocapitalize="off"
              spellcheck={false}
              value={account}
              onInput={event => setAccount((event.currentTarget as HTMLInputElement).value)}
              placeholder="输入现有游戏账号"
            />
          </label>
          <label class="field">
            <span>密码</span>
            <div class="field__secret">
              <input
                name="password"
                type={visible ? "text" : "password"}
                autoComplete="current-password"
                value={password}
                onInput={event => setPassword((event.currentTarget as HTMLInputElement).value)}
                placeholder="输入密码"
              />
              <button type="button" class="field__reveal" onClick={() => setVisible(value => !value)} aria-label={visible ? "隐藏密码" : "显示密码"}>
                {visible ? "隐" : "显"}
              </button>
            </div>
          </label>
          {state.error && <p class="form-error" role="alert">{state.error}</p>}
          <button type="submit" class="button button--primary button--large" disabled={!canSubmit}>
            {state.notice ?? "进入游戏"}
            <span aria-hidden="true">→</span>
          </button>
        </form>
        <div class="login-panel__foot">
          <span>WebGL2 已校验</span>
          <span>WSS 加密传输</span>
          <span>旧客户端可共存</span>
        </div>
      </section>
    </main>
  );
}

export function CharacterSelectScreen({
  state,
  onSelect,
  onCreate,
  onDelete
}: {
  state: SessionState;
  onSelect(index: number): void;
  onCreate(name: string, gender: number, classId: number): void;
  onDelete(index: number): void;
}) {
  const [selected, setSelected] = useState<number | null>(state.selectedCharacterIndex ?? state.characters[0]?.index ?? null);
  const [createOpen, setCreateOpen] = useState(false);
  const [deleteTarget, setDeleteTarget] = useState<number | null>(null);

  useEffect(() => {
    if (selected !== null && state.characters.some(value => value.index === selected)) return;
    setSelected(state.characters[0]?.index ?? null);
  }, [state.characters, selected]);

  const current = state.characters.find(value => value.index === selected) ?? null;

  return (
    <main class="entry entry--select">
      <header class="select-head">
        <div class="select-head__brand"><BrandMark compact /><span>CRYSTAL</span></div>
        <div class="select-head__meta">
          <span class="signal-dot" /> {state.bootstrap?.zoneId}
          <span>{state.pingMs !== null ? `${state.pingMs} ms` : "已连接"}</span>
        </div>
      </header>

      <section class="select-stage">
        <div class="select-intro">
          <p class="eyebrow">CHOOSE YOUR LEGEND</p>
          <h1>谁将踏入石门？</h1>
          <p>角色数据直接来自当前区服。进入世界后，浏览器只发送操作请求，位置与战斗仍由服务端裁定。</p>
          <button type="button" class="button button--ghost" onClick={() => setCreateOpen(true)}>＋ 创建新角色</button>
        </div>

        <div class="character-grid" role="listbox" aria-label="角色列表">
          {state.characters.map(character => (
            <article
              key={character.index}
              class={`character-card${selected === character.index ? " is-selected" : ""}`}
              role="option"
              aria-selected={selected === character.index}
              onClick={() => setSelected(character.index)}
            >
              <button
                type="button"
                class="character-card__delete"
                aria-label={`删除角色 ${character.name}`}
                onClick={event => {
                  event.stopPropagation();
                  setDeleteTarget(character.index);
                }}
              >×</button>
              <div class={`character-card__sigil class-${character.classId}`}>{classGlyph(character.classId)}</div>
              <div class="character-card__info">
                <h2>{character.name}</h2>
                <p>{characterSubtitle(character)}</p>
                <span class="character-card__stamp">{CLASS_NAMES[character.classId] ?? "冒险者"}</span>
              </div>
            </article>
          ))}
          {state.characters.length === 0 && (
            <button type="button" class="character-empty" onClick={() => setCreateOpen(true)}>
              <strong>尚未创建角色</strong>
              <span>创建第一个角色，推开石门。</span>
            </button>
          )}
        </div>
      </section>

      <footer class="select-actions">
        <div class="select-summary">
          {current ? (
            <>
              <span>已选择</span>
              <strong>{current.name}</strong>
              <span>Lv.{current.level}</span>
            </>
          ) : <span>选择一个角色开始</span>}
        </div>
        <button type="button" class="button button--primary button--large" disabled={!current} onClick={() => current && onSelect(current.index)}>
          {state.notice ?? "进入世界"}
          <span aria-hidden="true">→</span>
        </button>
      </footer>

      {createOpen && <CreateCharacterDialog onClose={() => setCreateOpen(false)} onCreate={onCreate} />}
      {deleteTarget !== null && (
        <div class="modal-shade" role="presentation" onClick={() => setDeleteTarget(null)}>
          <section class="modal modal--small" role="dialog" aria-modal="true" aria-labelledby="delete-title" onClick={event => event.stopPropagation()}>
            <p class="eyebrow">DANGER ZONE</p>
            <h2 id="delete-title">确认删除角色？</h2>
            <p class="modal__copy">角色删除后由服务端执行，无法从浏览器撤销。</p>
            <div class="modal__actions">
              <button type="button" class="button button--ghost" onClick={() => setDeleteTarget(null)}>取消</button>
              <button type="button" class="button button--danger" onClick={() => { onDelete(deleteTarget); setDeleteTarget(null); }}>删除</button>
            </div>
          </section>
        </div>
      )}
    </main>
  );
}

function CreateCharacterDialog({ onClose, onCreate }: { onClose(): void; onCreate(name: string, gender: number, classId: number): void }) {
  const [name, setName] = useState("");
  const [classId, setClassId] = useState(0);
  const [gender, setGender] = useState(0);
  return (
    <div class="modal-shade" role="presentation" onClick={onClose}>
      <section class="modal" role="dialog" aria-modal="true" aria-labelledby="create-title" onClick={event => event.stopPropagation()}>
        <button type="button" class="modal__close" onClick={onClose} aria-label="关闭">×</button>
        <p class="eyebrow">NEW LEGEND</p>
        <h2 id="create-title">创建角色</h2>
        <label class="field">
          <span>角色名</span>
          <input value={name} maxLength={16} onInput={event => setName((event.currentTarget as HTMLInputElement).value)} placeholder="2–16 个字符" />
        </label>
        <div class="choice-group">
          <span>职业</span>
          <div class="choice-grid">
            {CLASS_NAMES.map((label, index) => (
              <button type="button" key={label} class={`choice${classId === index ? " is-selected" : ""}`} onClick={() => setClassId(index)}>
                <b>{classGlyph(index)}</b><span>{label}</span>
              </button>
            ))}
          </div>
        </div>
        <div class="choice-group">
          <span>外观</span>
          <div class="segmented">
            <button type="button" class={gender === 0 ? "is-selected" : ""} onClick={() => setGender(0)}>男性</button>
            <button type="button" class={gender === 1 ? "is-selected" : ""} onClick={() => setGender(1)}>女性</button>
          </div>
        </div>
        <div class="modal__actions">
          <button type="button" class="button button--ghost" onClick={onClose}>取消</button>
          <button type="button" class="button button--primary" disabled={!name.trim()} onClick={() => { onCreate(name, gender, classId); onClose(); }}>创建角色</button>
        </div>
      </section>
    </div>
  );
}
