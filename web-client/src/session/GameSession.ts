import { AssetStore, type LoadedMap, type WebAssetManifest } from "../assets/assetStore";
import type { ClientItemInfo, ClientPacket, SelectInfo, ServerPacket, UserItem } from "../protocol/types";
import { WorldState, type WorldMap } from "../world/WorldState";
import { loadBootstrap, resolveWebSocketUrl, versionHashBytes, type BootstrapInfo } from "./bootstrap";
import { GatewayConnection, type GatewayCloseReason } from "./GatewayConnection";

export type SessionPhase = "boot" | "login" | "select" | "entering" | "game" | "blocked" | "reconnecting" | "error";
export type LinkState = "idle" | "connecting" | "open" | "closed";

export interface ChatLine {
  id: number;
  channel: "system" | "local" | "world" | "group" | "combat";
  text: string;
  at: number;
}

export interface ShopState {
  items: UserItem[];
  rate: number;
  panelType: number;
  hideAddedStats: boolean;
}

export interface NpcDialogState {
  pages: string[];
}

export interface SessionState {
  phase: SessionPhase;
  link: LinkState;
  error: string | null;
  notice: string | null;
  bootstrap: BootstrapInfo | null;
  assetManifest: WebAssetManifest | null;
  characters: SelectInfo[];
  selectedCharacterIndex: number | null;
  self: Extract<ServerPacket, { kind: "UserInformation" }> | null;
  map: Extract<ServerPacket, { kind: "MapInformation" }> | null;
  mapAsset: LoadedMap | null;
  mapLoading: boolean;
  inventory: Array<UserItem | null>;
  equipment: Array<UserItem | null>;
  itemInfo: Record<number, ClientItemInfo>;
  messages: ChatLine[];
  npcDialog: NpcDialogState | null;
  shop: ShopState | null;
  pingMs: number | null;
  reconnectAttempt: number;
  defeated: boolean;
  lastCloseReason: GatewayCloseReason | null;
}

const initialState: SessionState = {
  phase: "boot",
  link: "idle",
  error: null,
  notice: null,
  bootstrap: null,
  assetManifest: null,
  characters: [],
  selectedCharacterIndex: null,
  self: null,
  map: null,
  mapAsset: null,
  mapLoading: false,
  inventory: [],
  equipment: [],
  itemInfo: {},
  messages: [],
  npcDialog: null,
  shop: null,
  pingMs: null,
  reconnectAttempt: 0,
  defeated: false,
  lastCloseReason: null
};

const loginErrors: Record<number, string> = {
  0: "登录功能已关闭。",
  1: "账号格式不正确。",
  2: "密码格式不正确。",
  3: "账号不存在。",
  4: "密码错误。"
};

const startGameErrors: Record<number, string> = {
  0: "进入游戏已关闭。",
  1: "尚未登录。",
  2: "角色不存在。",
  3: "没有可用的地图或出生点。"
};

const newCharacterErrors: Record<number, string> = {
  0: "角色创建已关闭。",
  1: "角色名不可用。",
  2: "性别参数无效。",
  3: "职业参数无效。",
  4: "角色数量已达上限。",
  5: "角色名已存在。"
};

export class GameSession {
  readonly world = new WorldState();
  readonly assets = new AssetStore();
  private state: SessionState = initialState;
  private readonly listeners = new Set<() => void>();
  private connection: GatewayConnection | null = null;
  private account: string | null = null;
  private password: string | null = null;
  private reconnectTimer: number | null = null;
  private keepAliveTimer: number | null = null;
  private disposed = false;
  private autoLogin = false;
  private mapRequest = 0;
  private messageSequence = 0;
  private readonly requestedItemInfo = new Set<number>();

  get current(): SessionState {
    return this.state;
  }

  subscribe(listener: () => void): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  async start(): Promise<void> {
    this.disposed = false;
    this.update({ phase: "boot", link: "idle", error: null, notice: "正在读取区服配置…", reconnectAttempt: 0 });
    try {
      const bootstrap = await loadBootstrap();
      this.update({ bootstrap, notice: "正在校验 Web 资源版本…" });
      const manifest = await this.assets.load(bootstrap.assetManifestUrl);
      if (manifest.assetVersion !== bootstrap.assetVersion) {
        throw new Error(`资源版本不匹配：网关 ${bootstrap.assetVersion}，CDN ${manifest.assetVersion}`);
      }
      if (manifest.clientBuildHash && manifest.clientBuildHash !== bootstrap.clientBuildHash) {
        throw new Error("Web 客户端构建哈希与资源 manifest 不一致。");
      }
      this.update({ assetManifest: manifest, notice: "正在连接区服网关…" });
      await this.connect(false);
    } catch (exception) {
      this.fail(exception);
    }
  }

  async retry(): Promise<void> {
    this.disconnect(true);
    await this.start();
  }

  dispose(): void {
    this.disposed = true;
    this.clearReconnectTimer();
    this.clearKeepAlive();
    this.disconnect(true);
  }

  login(accountId: string, password: string): void {
    this.account = accountId.trim();
    this.password = password;
    this.autoLogin = true;
    this.update({ phase: "login", error: null, notice: "正在登录…" });
    this.send({ kind: "Login", accountId: this.account, password });
  }

  createCharacter(name: string, gender: number, classId: number): void {
    this.send({ kind: "NewCharacter", name: name.trim(), gender, classId });
  }

  deleteCharacter(characterIndex: number): void {
    this.send({ kind: "DeleteCharacter", characterIndex });
  }

  selectCharacter(characterIndex: number): void {
    this.update({ selectedCharacterIndex: characterIndex, phase: "entering", error: null, notice: "正在进入世界…" });
    this.send({ kind: "StartGame", characterIndex });
  }

  retryMap(): void {
    const map = this.state.map;
    if (map) void this.prepareMap(map);
  }

  walk(direction: number): void {
    this.send({ kind: "Walk", direction });
  }

  run(direction: number): void {
    this.send({ kind: "Run", direction });
  }

  turn(direction: number): void {
    this.send({ kind: "Turn", direction });
  }

  attack(direction: number): void {
    this.send({ kind: "Attack", direction, spell: 0 });
  }

  pickup(): void {
    this.send({ kind: "PickUp" });
  }

  requestItemInfo(itemIndex: number): void {
    if (itemIndex <= 0 || this.state.itemInfo[itemIndex] || this.requestedItemInfo.has(itemIndex)) return;
    this.requestedItemInfo.add(itemIndex);
    this.send({ kind: "RequestItemInfo", itemIndex }, true);
  }

  chat(message: string): void {
    const text = message.trim();
    if (!text) return;
    this.send({ kind: "Chat", message: text });
    this.pushMessage("local", text);
  }

  moveItem(from: number, to: number): void {
    if (from === to || from < 0 || to < 0) return;
    this.send({ kind: "MoveItem", grid: 1, from, to });
  }

  callNpc(objectId: number, key: string): void {
    this.send({ kind: "CallNPC", objectId, key });
  }

  buy(itemIndex: bigint, count = 1): void {
    this.send({ kind: "BuyItem", itemIndex, count, panelType: this.state.shop?.panelType ?? 0 });
  }

  sell(uniqueId: bigint, count = 1): void {
    this.send({ kind: "SellItem", uniqueId, count });
  }

  useItem(uniqueId: bigint): void {
    this.send({ kind: "UseItem", uniqueId, grid: 1 });
  }

  equip(uniqueId: bigint, to: number): void {
    this.send({ kind: "EquipItem", grid: 1, uniqueId, to });
  }

  unequip(uniqueId: bigint, to: number): void {
    this.send({ kind: "RemoveItem", grid: 2, uniqueId, to });
  }

  drop(uniqueId: bigint, count = 1): void {
    this.send({ kind: "DropItem", uniqueId, count });
  }

  revive(): void {
    this.send({ kind: "TownRevive" });
  }

  closeNpc(): void {
    this.update({ npcDialog: null, shop: null });
  }

  logout(): void {
    this.send({ kind: "LogOut" });
    this.password = null;
    this.autoLogin = false;
  }

  disconnect(manual: boolean): void {
    this.clearReconnectTimer();
    if (manual) this.connection?.close();
    this.connection = null;
    this.update({ link: "idle" });
  }

  private async connect(reconnecting: boolean): Promise<void> {
    if (this.disposed || !this.state.bootstrap) return;
    this.clearReconnectTimer();
    this.update({ link: "connecting", error: null, notice: reconnecting ? "正在重新连接…" : "正在连接区服网关…" });
    const connection = new GatewayConnection(resolveWebSocketUrl(this.state.bootstrap), {
      open: () => this.onOpen(),
      packet: packet => this.onPacket(packet),
      close: (reason, detail) => this.onClose(reason, detail),
      error: error => this.update({ error: error.message })
    });
    this.connection = connection;
    try {
      await connection.connect();
    } catch (exception) {
      if (this.connection === connection) this.connection = null;
      if (this.disposed) return;
      this.update({ link: "closed", error: exception instanceof Error ? exception.message : "无法连接区服网关。" });
      this.scheduleReconnect();
    }
  }

  private onOpen(): void {
    this.update({ link: "open", error: null });
    this.startKeepAlive();
  }

  private onClose(reason: GatewayCloseReason, detail: string): void {
    if (this.disposed) return;
    this.clearKeepAlive();
    const active = this.state.phase === "game" || this.state.phase === "select" || this.state.phase === "entering";
    const protocolFailure = reason === "protocol";
    this.update({
      link: "closed",
      lastCloseReason: reason,
      error: reason === "manual" ? null : detail,
      phase: protocolFailure ? "error" : active ? "reconnecting" : this.state.phase,
      notice: protocolFailure || reason === "manual" ? null : active ? "连接中断，正在恢复会话…" : this.state.notice
    });
    if (reason !== "manual" && !protocolFailure) this.scheduleReconnect();
  }

  private scheduleReconnect(): void {
    if (this.disposed || this.reconnectTimer !== null) return;
    const attempt = this.state.reconnectAttempt + 1;
    if (attempt > 5) {
      this.update({ phase: "error", error: "多次重连失败，请检查网络后重试。", reconnectAttempt: attempt });
      return;
    }
    const delay = Math.min(1000 * 2 ** (attempt - 1), 16_000);
    this.update({ reconnectAttempt: attempt, phase: this.state.phase === "error" ? "error" : "reconnecting" });
    this.reconnectTimer = window.setTimeout(() => {
      this.reconnectTimer = null;
      void this.connect(true);
    }, delay);
  }

  private onPacket(packet: ServerPacket): void {
    this.world.apply(packet);
    switch (packet.kind) {
      case "Connected":
        this.sendVersion();
        break;
      case "ClientVersion":
        if (packet.result === 1) {
          this.update({ phase: "login", notice: this.account ? "正在恢复登录…" : null, reconnectAttempt: 0 });
          if (this.autoLogin && this.account && this.password) this.login(this.account, this.password);
        } else {
          this.update({ phase: "error", error: "客户端版本不匹配，请联系管理员更新 Web 版本哈希。" });
          this.connection?.close();
        }
        break;
      case "Login":
        this.update({ phase: "login", error: loginErrors[packet.result] ?? `登录失败（${packet.result}）`, notice: null });
        break;
      case "LoginBanned":
        this.update({ phase: "login", error: `账号已被封禁：${packet.reason}`, notice: null });
        break;
      case "LoginSuccess":
        this.update({
          phase: "select",
          characters: packet.characters,
          selectedCharacterIndex: packet.characters[0]?.index ?? null,
          error: null,
          notice: null,
          reconnectAttempt: 0
        });
        break;
      case "NewCharacter":
        this.update({ error: newCharacterErrors[packet.result] ?? `角色创建失败（${packet.result}）` });
        break;
      case "NewCharacterSuccess":
        this.update({
          characters: [packet.character, ...this.state.characters.filter(value => value.index !== packet.character.index)],
          selectedCharacterIndex: packet.character.index,
          error: null,
          notice: "角色创建成功。"
        });
        break;
      case "DeleteCharacter":
        this.update({ error: packet.result === 0 ? "角色删除已关闭。" : "角色不存在或无法删除。" });
        break;
      case "DeleteCharacterSuccess":
        this.update({
          characters: this.state.characters.filter(value => value.index !== packet.characterIndex),
          selectedCharacterIndex: null,
          error: null,
          notice: "角色已删除。"
        });
        break;
      case "StartGame":
        if (packet.result !== 4) {
          this.update({ phase: "select", error: startGameErrors[packet.result] ?? `进入游戏失败（${packet.result}）`, notice: null });
        }
        break;
      case "StartGameBanned":
        this.update({ phase: "select", error: `无法进入游戏：${packet.reason}`, notice: null });
        break;
      case "StartGameDelay":
        this.update({ phase: "select", error: `角色仍在线，请等待 ${Math.ceil(Number(packet.milliseconds) / 1000)} 秒。` });
        break;
      case "MapInformation": {
        const map = this.mapFromInformation(packet);
        this.world.map = map;
        this.update({ map: packet, phase: "entering", notice: `正在载入 ${packet.title || packet.fileName}…`, error: null });
        void this.prepareMap(packet);
        break;
      }
      case "MapChanged": {
        const map: WorldMap = {
          mapIndex: packet.mapIndex,
          fileName: packet.fileName,
          title: "",
          miniMap: 0,
          bigMap: 0,
          lights: 0,
          mapDarkLight: packet.mapDarkLight,
          weatherParticles: packet.weather
        };
        this.world.map = map;
        this.world.clearMap(true);
        const mapInfo: Extract<ServerPacket, { kind: "MapInformation" }> = {
          kind: "MapInformation",
          mapIndex: packet.mapIndex,
          fileName: packet.fileName,
          title: this.state.map?.title ?? "",
          miniMap: 0,
          bigMap: 0,
          lights: 0,
          lightning: false,
          fire: false,
          mapDarkLight: packet.mapDarkLight,
          music: packet.music,
          weatherParticles: packet.weather
        };
        this.update({ map: mapInfo, phase: "entering", mapAsset: null });
        void this.prepareMap(mapInfo);
        break;
      }
      case "UserInformation":
        this.update({
          self: packet,
          inventory: packet.inventory ?? [],
          equipment: packet.equipment ?? [],
          defeated: false
        });
        this.requestItemInfoFor([...(packet.inventory ?? []), ...(packet.equipment ?? [])]);
        if (this.state.phase === "entering") this.update({ phase: "game", notice: null });
        break;
      case "UserSlotsRefresh":
        this.update({
          inventory: packet.inventory ?? this.state.inventory,
          equipment: packet.equipment ?? this.state.equipment
        });
        this.requestItemInfoFor([...(packet.inventory ?? []), ...(packet.equipment ?? [])]);
        break;
      case "NewItemInfo":
        this.requestedItemInfo.delete(packet.info.index);
        this.update({ itemInfo: { ...this.state.itemInfo, [packet.info.index]: packet.info } });
        break;
      case "HealthChanged":
        if (this.state.self) this.update({ self: { ...this.state.self, hp: packet.hp, mp: packet.mp } });
        break;
      case "Chat":
        this.pushMessage(this.channelForChat(packet.chatType), packet.message);
        break;
      case "ObjectChat":
        this.pushMessage("local", packet.text, packet.objectId);
        break;
      case "MoveItem":
        if (packet.success && packet.grid === 1) {
          const inventory = [...this.state.inventory];
          [inventory[packet.from], inventory[packet.to]] = [inventory[packet.to] ?? null, inventory[packet.from] ?? null];
          this.update({ inventory });
        }
        break;
      case "NPCResponse":
        this.update({ npcDialog: { pages: packet.pages }, shop: null });
        break;
      case "NPCGoods":
        this.update({ shop: { items: packet.items, rate: packet.rate, panelType: packet.panelType, hideAddedStats: packet.hideAddedStats } });
        this.requestItemInfoFor(packet.items);
        break;
      case "GainedItem":
        this.requestItemInfo(packet.item.itemIndex);
        this.pushMessage("system", `获得物品 #${packet.item.itemIndex} × ${packet.item.count}`);
        break;
      case "GainedGold":
        this.pushMessage("system", `获得 ${packet.gold} 金币`);
        break;
      case "Death":
        this.update({ defeated: true });
        break;
      case "Revived":
        this.update({ defeated: false });
        break;
      case "ObjectRevived":
        if (packet.objectId === this.state.self?.objectId) this.update({ defeated: false });
        break;
      case "LogOutSuccess":
        this.world.clearMap(false);
        this.update({
          phase: "select",
          characters: packet.characters,
          selectedCharacterIndex: packet.characters[0]?.index ?? null,
          self: null,
          map: null,
          mapAsset: null,
          npcDialog: null,
          shop: null,
          itemInfo: {},
          defeated: false,
          notice: "已返回角色选择。"
        });
        this.requestedItemInfo.clear();
        break;
      case "ReturnToLogin":
        this.world.clearMap(false);
        this.update({ phase: "select", self: null, map: null, mapAsset: null, npcDialog: null, shop: null, defeated: false });
        break;
      case "Disconnect":
        this.update({ error: ["区服已关闭。", "账号在别处登录。", "协议错误。", "区服异常。"][packet.reason] ?? "连接已断开。" });
        this.connection?.close();
        break;
      case "KeepAlive":
        if (packet.time > 0n) this.update({ pingMs: Math.max(0, Math.round(performance.now() - Number(packet.time))) });
        break;
      case "ObjectDied":
        if (packet.objectId === this.state.self?.objectId) this.update({ defeated: true });
        break;
      default:
        break;
    }
  }

  private async prepareMap(map: Extract<ServerPacket, { kind: "MapInformation" }>): Promise<void> {
    const request = ++this.mapRequest;
    this.update({ mapLoading: true, phase: "entering", error: null });
    try {
      const mapAsset = await this.assets.loadMap(map.fileName);
      if (request !== this.mapRequest || this.disposed) return;
      this.world.map = { ...(this.world.map ?? this.mapFromInformation(map)), width: mapAsset.meta.width, height: mapAsset.meta.height };
      this.update({ mapAsset, mapLoading: false, phase: "game", notice: null, error: null });
    } catch (exception) {
      if (request !== this.mapRequest || this.disposed) return;
      const detail = exception instanceof Error ? exception.message : "地图资源加载失败";
      this.update({ mapLoading: false, phase: "blocked", error: `无法进入地图：${detail}`, notice: null });
    }
  }

  private mapFromInformation(packet: Extract<ServerPacket, { kind: "MapInformation" }>): WorldMap {
    return {
      mapIndex: packet.mapIndex,
      fileName: packet.fileName,
      title: packet.title,
      miniMap: packet.miniMap,
      bigMap: packet.bigMap,
      lights: packet.lights,
      mapDarkLight: packet.mapDarkLight,
      weatherParticles: packet.weatherParticles
    };
  }

  private sendVersion(): void {
    const bootstrap = this.state.bootstrap;
    if (!bootstrap) return;
    this.send({ kind: "ClientVersion", versionHash: versionHashBytes(bootstrap.clientBuildHash) }, true);
  }

  private send(packet: ClientPacket, quiet = false): void {
    try {
      this.connection?.send(packet);
    } catch (exception) {
      if (!quiet) {
        this.update({ error: exception instanceof Error ? exception.message : "发送游戏指令失败。" });
      }
    }
  }

  private startKeepAlive(): void {
    this.clearKeepAlive();
    this.keepAliveTimer = window.setInterval(() => {
      if (this.connection?.isOpen) {
        this.send({ kind: "KeepAlive", time: BigInt(Math.floor(performance.now())) }, true);
      }
    }, 5_000);
  }

  private clearKeepAlive(): void {
    if (this.keepAliveTimer !== null) window.clearInterval(this.keepAliveTimer);
    this.keepAliveTimer = null;
  }

  private clearReconnectTimer(): void {
    if (this.reconnectTimer !== null) window.clearTimeout(this.reconnectTimer);
    this.reconnectTimer = null;
  }

  private fail(exception: unknown): void {
    const message = exception instanceof Error ? exception.message : "初始化浏览器客户端失败。";
    this.update({ phase: "error", link: "closed", error: message, notice: null });
  }

  private pushMessage(channel: ChatLine["channel"], text: string, objectId?: number): void {
    const id = ++this.messageSequence;
    const line: ChatLine = { id, channel, text, at: Date.now() };
    this.update({ messages: [...this.state.messages.slice(-199), line] });
    if (objectId !== undefined) this.world.notify();
  }

  private channelForChat(chatType: number): ChatLine["channel"] {
    if (chatType === 1 || chatType === 14 || chatType === 15) return "world";
    if (chatType === 5) return "group";
    if (chatType === 2 || chatType === 3 || chatType === 4 || chatType === 10) return "system";
    return "local";
  }

  private requestItemInfoFor(items: ReadonlyArray<UserItem | null>): void {
    for (const item of items) {
      if (item) this.requestItemInfo(item.itemIndex);
    }
  }

  private update(patch: Partial<SessionState>): void {
    this.state = { ...this.state, ...patch };
    for (const listener of this.listeners) listener();
  }
}
