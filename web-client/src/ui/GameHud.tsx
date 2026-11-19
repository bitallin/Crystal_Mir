import { useEffect, useRef, useState } from "preact/hooks";
import type { ComponentChildren } from "preact";
import type { InputController } from "../input/InputController";
import type { ClientItemInfo, UserItem } from "../protocol/types";
import type { GameSession, SessionState } from "../session/GameSession";
import type { WorldEntity } from "../world/WorldState";
import {
  EQUIPMENT_SLOT_NAMES,
  classGlyph,
  classLabel,
  equipmentSlotFor,
  formatNumber,
  formatTime,
  itemLabel,
  itemTypeLabel
} from "./format";

export interface ControlHandle {
  current: InputController | null;
}

type Panel = "none" | "inventory" | "equipment" | "help";

const CHANNEL_LABELS: Readonly<Record<string, string>> = {
  system: "系统",
  local: "附近",
  world: "世界",
  group: "组队",
  combat: "战斗"
};

export function GameHud({
  session,
  state,
  controls,
  targetId,
  fps,
  status,
  onCloseNotice
}: {
  session: GameSession;
  state: SessionState;
  controls: ControlHandle;
  targetId: number | null;
  fps: number;
  status: string | null;
  onCloseNotice(): void;
}) {
  const [panel, setPanel] = useState<Panel>("none");
  const [chatOpen, setChatOpen] = useState(false);
  const [chatValue, setChatValue] = useState("");
  const chatInput = useRef<HTMLInputElement>(null);
  const chatEnd = useRef<HTMLDivElement>(null);
  const target = targetId === null ? null : session.world.entities.get(targetId) ?? null;

  useEffect(() => {
    chatEnd.current?.scrollIntoView({ block: "end" });
  }, [state.messages]);

  const openChat = (): void => {
    setChatOpen(true);
    requestAnimationFrame(() => chatInput.current?.focus());
  };

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent): void => {
      const targetElement = event.target;
      const editable = targetElement instanceof HTMLElement &&
        (targetElement.isContentEditable || ["INPUT", "TEXTAREA", "SELECT"].includes(targetElement.tagName));
      if (editable) {
        if (event.code === "Escape" && targetElement === chatInput.current) {
          setChatOpen(false);
          setChatValue("");
          chatInput.current?.blur();
        }
        return;
      }
      if (event.code === "KeyI") setPanel(value => value === "inventory" ? "none" : "inventory");
      else if (event.code === "KeyC") setPanel(value => value === "equipment" ? "none" : "equipment");
      else if (event.code === "Escape") setPanel("none");
      else if (event.code === "Enter") {
        event.preventDefault();
        openChat();
      }
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, []);

  const submitChat = (): void => {
    const text = chatValue.trim();
    if (!text) return;
    session.chat(text);
    setChatValue("");
    setChatOpen(false);
    chatInput.current?.blur();
  };

  return (
    <div class="hud">
      <PlayerStatus state={state} fps={fps} />
      <MapPlate state={state} />
      <TopActions panel={panel} setPanel={setPanel} />

      {target && (
        <TargetPlate
          entity={target}
          distance={Math.max(Math.abs(target.location.x - session.world.location.x), Math.abs(target.location.y - session.world.location.y))}
          onAttack={() => controls.current?.attack()}
          onClear={() => controls.current?.setTarget(null)}
        />
      )}

      {status && (
        <div class="world-toast" role="status">
          <span>{status}</span>
          <button type="button" onClick={onCloseNotice} aria-label="关闭提示">×</button>
        </div>
      )}

      <section class={`chat-dock${chatOpen ? " is-open" : ""}`} aria-label="聊天">
        <div class="chat-log">
          {state.messages.slice(-8).map(line => (
            <p key={line.id} class={`chat-line chat-line--${line.channel}`}>
              <span>{CHANNEL_LABELS[line.channel] ?? line.channel}</span>
              <time>{formatTime(line.at)}</time>
              <b>{line.text}</b>
            </p>
          ))}
          <div ref={chatEnd} />
        </div>
        {chatOpen ? (
          <form class="chat-entry" onSubmit={event => { event.preventDefault(); submitChat(); }}>
            <span>附近</span>
            <input
              ref={chatInput}
              value={chatValue}
              maxLength={120}
              onInput={event => setChatValue((event.currentTarget as HTMLInputElement).value)}
              placeholder="按 Enter 发送，Esc 取消"
            />
            <button type="submit">发送</button>
          </form>
        ) : (
          <button type="button" class="chat-trigger" onClick={openChat}>
            <span class="chat-trigger__key">Enter</span> 聊天
          </button>
        )}
      </section>

      <DesktopHints />
      <MobileControls controls={controls} />

      {panel === "inventory" && <InventoryPanel session={session} state={state} onClose={() => setPanel("none")} />}
      {panel === "equipment" && <EquipmentPanel session={session} state={state} onClose={() => setPanel("none")} />}
      {panel === "help" && <HelpPanel onClose={() => setPanel("none")} />}
      {state.npcDialog && <NpcPanel session={session} state={state} />}
      {state.defeated && <DeathPanel session={session} />}
      {(state.phase === "reconnecting" || state.link === "connecting") && <ReconnectOverlay state={state} />}
    </div>
  );
}

function PlayerStatus({ state, fps }: { state: SessionState; fps: number }) {
  const self = state.self;
  if (!self) return null;
  const experience = Number(self.experience);
  const maxExperience = Math.max(1, Number(self.maxExperience));
  const experiencePercent = Math.max(0, Math.min(100, (experience / maxExperience) * 100));
  return (
    <section class="player-status" aria-label="角色状态">
      <div class="player-status__portrait">{classGlyph(self.classId)}</div>
      <div class="player-status__body">
        <div class="player-status__line">
          <strong>{self.name}</strong>
          <span>Lv.{self.level}</span>
          <em>{classLabel(self.classId)}</em>
        </div>
        <div class="vitals">
          <span class="vital vital--hp"><i>生命</i><b>{formatNumber(self.hp)}</b></span>
          <span class="vital vital--mp"><i>法力</i><b>{formatNumber(self.mp)}</b></span>
          <span class="vital vital--gold"><i>金币</i><b>{formatNumber(self.gold)}</b></span>
        </div>
        <div class="experience-track" title={`经验 ${formatNumber(self.experience)} / ${formatNumber(self.maxExperience)}`}>
          <i style={{ width: `${experiencePercent}%` }} />
        </div>
      </div>
      <div class="player-status__net">
        <span class={`signal-dot${state.link === "open" ? "" : " is-off"}`} />
        <b>{state.pingMs !== null ? `${state.pingMs}ms` : "在线"}</b>
        <small>{fps} FPS</small>
      </div>
    </section>
  );
}

function MapPlate({ state }: { state: SessionState }) {
  return (
    <div class="map-plate">
      <span>当前位置</span>
      <b>{state.map?.title || state.map?.fileName || "未知地图"}</b>
      <small>{state.map ? `${state.map.mapIndex} · ${state.self?.location.x ?? 0},${state.self?.location.y ?? 0}` : ""}</small>
    </div>
  );
}

function TopActions({ panel, setPanel }: { panel: Panel; setPanel(value: Panel | ((current: Panel) => Panel)): void }) {
  return (
    <nav class="top-actions" aria-label="游戏面板">
      <button type="button" class={panel === "inventory" ? "is-active" : ""} onClick={() => setPanel(value => value === "inventory" ? "none" : "inventory")}>
        <b>囊</b><span>背包</span><kbd>I</kbd>
      </button>
      <button type="button" class={panel === "equipment" ? "is-active" : ""} onClick={() => setPanel(value => value === "equipment" ? "none" : "equipment")}>
        <b>人</b><span>装备</span><kbd>C</kbd>
      </button>
      <button type="button" class={panel === "help" ? "is-active" : ""} onClick={() => setPanel(value => value === "help" ? "none" : "help")}>
        <b>?</b><span>帮助</span>
      </button>
    </nav>
  );
}

function TargetPlate({ entity, distance, onAttack, onClear }: { entity: WorldEntity; distance: number; onAttack(): void; onClear(): void }) {
  return (
    <aside class={`target-plate target-plate--${entity.kind}`}>
      <div class="target-plate__mark">目</div>
      <div class="target-plate__body">
        <span>{entity.kind === "monster" ? "敌对目标" : entity.kind === "npc" ? "场景人物" : "目标"}</span>
        <strong>{entity.name}</strong>
        <small>{distance} 格 {entity.level ? `· Lv.${entity.level}` : ""}</small>
        {entity.hpPercent !== undefined && <i class="target-plate__hp"><b style={{ width: `${entity.hpPercent}%` }} /></i>}
      </div>
      <button type="button" class="target-plate__attack" onClick={onAttack}>攻击</button>
      <button type="button" class="target-plate__clear" onClick={onClear} aria-label="取消目标">×</button>
    </aside>
  );
}

function DesktopHints() {
  return (
    <div class="desktop-hints">
      <span><kbd>WASD</kbd> 移动</span>
      <span><kbd>Shift</kbd> 奔跑</span>
      <span><kbd>Space</kbd> 攻击</span>
      <span><kbd>F</kbd> 拾取</span>
      <span><kbd>Enter</kbd> 聊天</span>
    </div>
  );
}

function MobileControls({ controls }: { controls: ControlHandle }) {
  return (
    <div class="mobile-controls">
      <Joystick onChange={(direction, running) => controls.current?.setVirtualDirection(direction, running)} />
      <div class="mobile-actions">
        <button type="button" class="mobile-action mobile-action--pickup" onClick={() => controls.current?.pickup()}>
          <b>拾</b><span>拾取</span>
        </button>
        <button type="button" class="mobile-action mobile-action--attack" onClick={() => controls.current?.attack()}>
          <b>斩</b><span>攻击</span>
        </button>
      </div>
    </div>
  );
}

function Joystick({ onChange }: { onChange(direction: number | null, running: boolean): void }) {
  const base = useRef<HTMLDivElement>(null);
  const pointer = useRef<number | null>(null);
  const [knob, setKnob] = useState({ x: 0, y: 0 });

  const update = (event: PointerEvent): void => {
    const element = base.current;
    if (!element) return;
    const rect = element.getBoundingClientRect();
    const radius = rect.width * 0.34;
    const rawX = event.clientX - (rect.left + rect.width / 2);
    const rawY = event.clientY - (rect.top + rect.height / 2);
    const distance = Math.hypot(rawX, rawY);
    const scale = distance > radius ? radius / distance : 1;
    const x = rawX * scale;
    const y = rawY * scale;
    setKnob({ x, y });
    if (distance < radius * 0.24) {
      onChange(null, false);
      return;
    }
    const angle = (Math.atan2(y, x) * 180) / Math.PI;
    let direction: number;
    if (angle >= -22.5 && angle < 22.5) direction = 2;
    else if (angle >= 22.5 && angle < 67.5) direction = 3;
    else if (angle >= 67.5 && angle < 112.5) direction = 4;
    else if (angle >= 112.5 && angle < 157.5) direction = 5;
    else if (angle >= 157.5 || angle < -157.5) direction = 6;
    else if (angle >= -157.5 && angle < -112.5) direction = 7;
    else if (angle >= -112.5 && angle < -67.5) direction = 0;
    else direction = 1;
    onChange(direction, distance > radius * 0.78);
  };

  const release = (event: PointerEvent): void => {
    if (pointer.current !== event.pointerId) return;
    pointer.current = null;
    setKnob({ x: 0, y: 0 });
    onChange(null, false);
  };

  return (
    <div
      ref={base}
      class="joystick"
      onPointerDown={event => {
        event.preventDefault();
        pointer.current = event.pointerId;
        (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId);
        update(event as PointerEvent);
      }}
      onPointerMove={event => {
        if (pointer.current === event.pointerId) update(event as PointerEvent);
      }}
      onPointerUp={event => release(event as PointerEvent)}
      onPointerCancel={event => release(event as PointerEvent)}
      aria-label="移动摇杆"
      role="application"
    >
      <span class="joystick__ring" />
      <span class="joystick__knob" style={{ transform: `translate(${knob.x}px, ${knob.y}px)` }} />
      <i class="joystick__hint">移动</i>
    </div>
  );
}

function InventoryPanel({ session, state, onClose }: { session: GameSession; state: SessionState; onClose(): void }) {
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const selected = state.inventory.find(item => item && String(item.uniqueId) === selectedId) ?? null;
  const info = selected ? state.itemInfo[selected.itemIndex] : undefined;
  const slotCount = Math.max(48, state.inventory.length);
  return (
    <PanelFrame title="行囊" eyebrow="INVENTORY" onClose={onClose} wide>
      <div class="inventory-layout">
        <div class="inventory-grid">
          {Array.from({ length: slotCount }, (_, index) => {
            const item = state.inventory[index] ?? null;
            return (
              <button
                type="button"
                key={index}
                class={`item-slot${item ? " has-item" : ""}${item && String(item.uniqueId) === selectedId ? " is-selected" : ""}`}
                onClick={() => {
                  if (selectedId !== null) {
                    const selectedIndex = state.inventory.findIndex(value => value && String(value.uniqueId) === selectedId);
                    if (selectedIndex === index) setSelectedId(null);
                    else if (selectedIndex >= 0) {
                      session.moveItem(selectedIndex, index);
                      setSelectedId(null);
                    }
                  } else if (item) {
                    setSelectedId(String(item.uniqueId));
                  }
                  if (item) session.requestItemInfo(item.itemIndex);
                }}
                aria-label={item ? itemLabel(item, state.itemInfo) : `空格 ${index + 1}`}
              >
                {item ? <ItemGlyph item={item} info={state.itemInfo[item.itemIndex]} /> : <span class="item-slot__number">{index + 1}</span>}
              </button>
            );
          })}
        </div>
        <aside class="item-inspector">
          {selected ? (
            <>
              <ItemGlyph item={selected} info={info} large />
              <p class="eyebrow">{itemTypeLabel(info)}</p>
              <h3>{itemLabel(selected, state.itemInfo)}</h3>
              <p class="item-inspector__meta">
                {selected.count > 1 && <span>数量 {selected.count}</span>}
                {selected.maxDurability > 0 && <span>耐久 {selected.currentDurability}/{selected.maxDurability}</span>}
                {info && <span>重量 {info.weight}</span>}
                {info && <span>价值 {formatNumber(info.price)}</span>}
              </p>
              <div class="item-inspector__actions">
                <button type="button" class="button button--primary" onClick={() => session.useItem(selected.uniqueId)}>使用</button>
                <button
                  type="button"
                  class="button button--ghost"
                  disabled={!info || equipmentSlotFor(info) === null}
                  onClick={() => {
                    if (info && equipmentSlotFor(info) !== null) session.equip(selected.uniqueId, chooseEquipmentSlot(info, state.equipment));
                  }}
                >装备</button>
                <button type="button" class="button button--danger-ghost" onClick={() => session.drop(selected.uniqueId, 1)}>丢弃</button>
              </div>
            </>
          ) : (
            <div class="empty-inspector">
              <span>囊</span>
              <p>选择物品查看详情</p>
              <small>物品资料按需从区服读取，不随前端包体下载。</small>
            </div>
          )}
          <p class="panel-footnote">选择物品后点击目标格可移动；装备、使用和丢弃使用右侧操作。</p>
        </aside>
      </div>
    </PanelFrame>
  );
}

function EquipmentPanel({ session, state, onClose }: { session: GameSession; state: SessionState; onClose(): void }) {
  const firstEmpty = state.inventory.findIndex(item => item === null);
  return (
    <PanelFrame title="装备" eyebrow="EQUIPMENT" onClose={onClose}>
      <div class="equipment-layout">
        <div class="equipment-figure">
          <span>{state.self ? classGlyph(state.self.classId) : "人"}</span>
          <b>{state.self?.name}</b>
          <small>{state.self ? `${classLabel(state.self.classId)} · Lv.${state.self.level}` : ""}</small>
        </div>
        <div class="equipment-columns">
          {EQUIPMENT_SLOT_NAMES.map((name, index) => {
            const item = state.equipment[index] ?? null;
            const info = item ? state.itemInfo[item.itemIndex] : undefined;
            return (
              <div class="equipment-row" key={name}>
                <span>{name}</span>
                <button
                  type="button"
                  class={`equipment-slot${item ? " has-item" : ""}`}
                  disabled={!item || firstEmpty < 0}
                  title={item ? `点击卸下 ${itemLabel(item, state.itemInfo)}` : name}
                  onClick={() => {
                    if (item && firstEmpty >= 0) session.unequip(item.uniqueId, firstEmpty);
                  }}
                >
                  {item ? <ItemGlyph item={item} info={info} compact /> : <i>空</i>}
                </button>
              </div>
            );
          })}
        </div>
      </div>
      <p class="panel-footnote">点击已装备物品可放回背包。装备槽位由物品类型与服务端校验共同决定。</p>
    </PanelFrame>
  );
}

function HelpPanel({ onClose }: { onClose(): void }) {
  return (
    <PanelFrame title="旅途手册" eyebrow="FIELD GUIDE" onClose={onClose}>
      <div class="help-grid">
        <article><b>移动</b><p>WASD 或方向键移动，Shift 奔跑。点击地面可寻路，路线仍由服务端逐步确认。</p></article>
        <article><b>战斗</b><p>点击怪物锁定目标，靠近后按 Space 或攻击按钮。攻击方向由角色与目标的相对位置决定。</p></article>
        <article><b>拾取</b><p>按 F 或拾取按钮拾取脚下物品。点击远处掉落物会先自动走近。</p></article>
        <article><b>面板</b><p>I 打开背包，C 打开装备，Esc 关闭面板。移动端使用右下角动作区。</p></article>
      </div>
      <p class="panel-footnote">浏览器只做输入与表现层预测。生命、位置、掉落、经验和战斗结果全部以区服包为准。</p>
    </PanelFrame>
  );
}

function PanelFrame({
  title,
  eyebrow,
  onClose,
  children,
  wide = false
}: {
  title: string;
  eyebrow: string;
  onClose(): void;
  children: ComponentChildren;
  wide?: boolean;
}) {
  return (
    <section class={`game-panel${wide ? " game-panel--wide" : ""}`} role="dialog" aria-modal="false" aria-label={title}>
      <header class="game-panel__head">
        <div><p class="eyebrow">{eyebrow}</p><h2>{title}</h2></div>
        <button type="button" onClick={onClose} aria-label="关闭面板">×</button>
      </header>
      <div class="game-panel__body">{children}</div>
    </section>
  );
}

function ItemGlyph({ item, info, large = false, compact = false }: { item: UserItem; info: ClientItemInfo | undefined; large?: boolean; compact?: boolean }) {
  const hue = (item.itemIndex * 47) % 360;
  const glyph = info ? itemTypeGlyph(info.type) : "?";
  return (
    <span
      class={`item-glyph${large ? " item-glyph--large" : ""}${compact ? " item-glyph--compact" : ""}`}
      style={{ background: `radial-gradient(circle at 35% 28%, hsla(${hue}, 46%, 58%, .55), transparent 40%), linear-gradient(145deg, hsl(${hue}, 23%, 24%), hsl(${hue}, 22%, 13%))` }}
    >
      <b>{glyph}</b>
      {item.count > 1 && <em>{item.count}</em>}
      {item.maxDurability > 0 && !compact && <i style={{ width: `${Math.min(100, (item.currentDurability / item.maxDurability) * 100)}%` }} />}
    </span>
  );
}

function itemTypeGlyph(type: number): string {
  const glyphs: Record<number, string> = {
    1: "刃", 2: "衣", 4: "盔", 5: "链", 6: "镯", 7: "戒", 8: "符", 9: "带", 10: "靴", 11: "晶", 12: "炬",
    13: "药", 14: "矿", 15: "肉", 17: "卷", 20: "书", 27: "食", 33: "鱼", 34: "任", 37: "变", 39: "嵌"
  };
  return glyphs[type] ?? "物";
}

function chooseEquipmentSlot(info: ClientItemInfo, equipment: Array<UserItem | null>): number {
  if (info.type === 6) return equipment[5] ? 6 : 5;
  if (info.type === 7) return equipment[7] ? 8 : 7;
  return equipmentSlotFor(info) ?? 0;
}

function NpcPanel({ session, state }: { session: GameSession; state: SessionState }) {
  const [tab, setTab] = useState<"dialog" | "buy" | "sell">(state.npcDialog ? "dialog" : "buy");
  const pages = state.npcDialog?.pages ?? [];
  return (
    <section class="npc-panel" role="dialog" aria-label="NPC 交互">
      <header>
        <div>
          <p class="eyebrow">NPC INTERACTION</p>
          <h2>{state.shop ? "杂货交易" : "对话"}</h2>
        </div>
        <button type="button" onClick={() => session.closeNpc()} aria-label="关闭">×</button>
      </header>
      {(state.npcDialog || state.shop) && (
        <nav class="npc-tabs">
          {state.npcDialog && <button type="button" class={tab === "dialog" ? "is-active" : ""} onClick={() => setTab("dialog")}>对话</button>}
          {state.shop && <button type="button" class={tab === "buy" ? "is-active" : ""} onClick={() => setTab("buy")}>购买</button>}
          {state.shop && <button type="button" class={tab === "sell" ? "is-active" : ""} onClick={() => setTab("sell")}>出售</button>}
        </nav>
      )}
      <div class="npc-panel__body">
        {tab === "dialog" && (
          <div class="npc-copy">
            {pages.map((page, index) => <p key={index}>{page}</p>)}
            {pages.length === 0 && <p>对方沉默地看着你。</p>}
          </div>
        )}
        {tab === "buy" && state.shop && (
          <div class="shop-list">
            {state.shop.items.map(item => {
              const info = state.itemInfo[item.itemIndex];
              return (
                <article class="shop-row" key={String(item.uniqueId)}>
                  <ItemGlyph item={item} info={info} compact />
                  <div><strong>{info?.name || `商品 #${item.itemIndex}`}</strong><span>{itemTypeLabel(info)}</span></div>
                  <b>{formatNumber(info?.price ?? 0)}</b>
                  <button type="button" onClick={() => session.buy(BigInt(item.itemIndex), 1)}>购买</button>
                </article>
              );
            })}
            {state.shop.items.length === 0 && <p class="empty-line">货架上暂时没有商品。</p>}
          </div>
        )}
        {tab === "sell" && (
          <div class="shop-list">
            {state.inventory.filter((item): item is UserItem => item !== null).map(item => {
              const info = state.itemInfo[item.itemIndex];
              return (
                <article class="shop-row" key={String(item.uniqueId)}>
                  <ItemGlyph item={item} info={info} compact />
                  <div><strong>{info?.name || `物品 #${item.itemIndex}`}</strong><span>{itemTypeLabel(info)} · ×{item.count}</span></div>
                  <b>{formatNumber(Math.floor((info?.price ?? 0) * 0.5))}</b>
                  <button type="button" onClick={() => session.sell(item.uniqueId, 1)}>出售</button>
                </article>
              );
            })}
          </div>
        )}
      </div>
    </section>
  );
}

function DeathPanel({ session }: { session: GameSession }) {
  return (
    <div class="death-overlay">
      <div class="death-sigil" aria-hidden="true">◇</div>
      <p class="eyebrow">THE SHADOW CLAIMS YOU</p>
      <h2>你倒下了</h2>
      <p>选择在城镇复活，角色状态由服务端恢复。</p>
      <button type="button" class="button button--primary button--large" onClick={() => session.revive()}>返回城镇复活</button>
    </div>
  );
}

function ReconnectOverlay({ state }: { state: SessionState }) {
  return (
    <div class="reconnect-overlay" role="status">
      <div class="reconnect-orbit"><span /></div>
      <strong>正在重新连接</strong>
      <p>{state.notice || "网络波动，会话恢复后将回到角色选择。"}</p>
      {state.reconnectAttempt > 1 && <small>第 {state.reconnectAttempt} 次尝试</small>}
    </div>
  );
}
