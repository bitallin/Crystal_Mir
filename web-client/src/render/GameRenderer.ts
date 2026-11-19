import { Application, Assets, Container, Graphics, Rectangle, Sprite, Text, Texture } from "pixi.js";
import type { LibraryFrame, LibraryPack } from "../assets/assetStore";
import type { Point } from "../protocol/types";
import type { GameSession } from "../session/GameSession";
import { CELL_HEIGHT, CELL_WIDTH, cellAt, type MapCell } from "../world/map";
import type { WorldEffect, WorldEntity } from "../world/WorldState";
import {
  cameraForViewport,
  cellCenter,
  preferredZoom,
  screenToCell,
  visibleCellBounds,
  type CameraTransform
} from "./projection";

export interface RendererHooks {
  onStatus?(message: string | null): void;
  onError?(message: string): void;
  onFps?(fps: number): void;
}

interface ActorView {
  root: Container;
  body: Graphics;
  shadow: Graphics;
  selection: Graphics;
  health: Graphics;
  name: Text;
  entity: WorldEntity;
  targetX: number;
  targetY: number;
  initialised: boolean;
  lastKind: string;
  lastName: string;
}

interface FloatingEffect {
  view: Container;
  age: number;
  duration: number;
  driftX: number;
  driftY: number;
}

const CLASS_COLOURS = [0xd7b16d, 0x86b7cf, 0xb69bd8, 0x87c49a, 0xc98c8c, 0xa7bcc8];

function hashColour(value: number): number {
  const hue = ((value * 2654435761) >>> 0) % 360;
  const saturation = 0.42 + ((value >>> 8) % 18) / 100;
  const lightness = 0.48 + ((value >>> 16) % 16) / 100;
  const c = (1 - Math.abs(2 * lightness - 1)) * saturation;
  const x = c * (1 - Math.abs(((hue / 60) % 2) - 1));
  const m = lightness - c / 2;
  let r = 0;
  let g = 0;
  let b = 0;
  if (hue < 60) [r, g, b] = [c, x, 0];
  else if (hue < 120) [r, g, b] = [x, c, 0];
  else if (hue < 180) [r, g, b] = [0, c, x];
  else if (hue < 240) [r, g, b] = [0, x, c];
  else if (hue < 300) [r, g, b] = [x, 0, c];
  else [r, g, b] = [c, 0, x];
  const red = Math.round((r + m) * 255);
  const green = Math.round((g + m) * 255);
  const blue = Math.round((b + m) * 255);
  return (red << 16) | (green << 8) | blue;
}

function entityFeet(entity: WorldEntity): { x: number; y: number } {
  return {
    x: entity.location.x * CELL_WIDTH + CELL_WIDTH / 2,
    y: entity.location.y * CELL_HEIGHT + CELL_HEIGHT
  };
}

export class GameRenderer {
  private readonly app = new Application();
  private readonly worldLayer = new Container();
  private readonly fallbackLayer = new Graphics();
  private readonly groundLayer = new Container();
  private readonly actorLayer = new Container();
  private readonly frontLayer = new Container();
  private readonly effectLayer = new Container();
  private readonly actorViews = new Map<number, ActorView>();
  private readonly pageTextures = new Map<string, Texture[]>();
  private readonly frameTextures = new Map<string, Texture>();
  private readonly floatingEffects: FloatingEffect[] = [];
  private readonly hooks: RendererHooks;
  private session: GameSession | null = null;
  private host: HTMLElement | null = null;
  private resizeObserver: ResizeObserver | null = null;
  private mapPacks = new Map<number, LibraryPack>();
  private camera: CameraTransform = { x: 0, y: 0, zoom: 1 };
  private targetCamera: CameraTransform = { x: 0, y: 0, zoom: 1 };
  private targetId: number | null = null;
  private mapToken = 0;
  private mapKey = "";
  private tileSignature = "";
  private focusKey = "";
  private elapsed = 0;
  private fpsReport = 0;
  private disposed = false;

  constructor(hooks: RendererHooks = {}) {
    this.hooks = hooks;
  }

  async init(host: HTMLElement, session: GameSession): Promise<void> {
    this.host = host;
    this.session = session;
    await this.app.init({
      width: Math.max(1, host.clientWidth),
      height: Math.max(1, host.clientHeight),
      background: 0x07110e,
      antialias: true,
      autoDensity: true,
      resolution: Math.min(window.devicePixelRatio || 1, 2),
      preference: "webgl",
      powerPreference: "high-performance",
      hello: false
    });
    if (this.disposed) {
      this.app.destroy({ removeView: true }, { children: true });
      return;
    }
    this.app.canvas.setAttribute("aria-label", "Crystal 游戏世界");
    this.app.canvas.classList.add("game-canvas");
    host.appendChild(this.app.canvas);

    this.worldLayer.addChild(this.fallbackLayer, this.groundLayer, this.actorLayer, this.frontLayer, this.effectLayer);
    this.actorLayer.sortableChildren = true;
    this.frontLayer.sortableChildren = true;
    this.app.stage.addChild(this.worldLayer);

    this.resizeObserver = new ResizeObserver(() => this.resize());
    this.resizeObserver.observe(host);
    this.resize();
    this.snapCamera();
    this.rebuildFloor(true);
    void this.loadMapVisuals();
    this.app.ticker.add(this.tick);
  }

  destroy(): void {
    if (this.disposed) return;
    this.disposed = true;
    this.mapToken++;
    this.resizeObserver?.disconnect();
    this.resizeObserver = null;
    if (this.app.ticker) this.app.ticker.remove(this.tick);
    this.actorViews.clear();
    this.floatingEffects.length = 0;
    this.pageTextures.clear();
    this.frameTextures.clear();
    this.app.destroy({ removeView: true }, { children: true });
    this.host = null;
    this.session = null;
  }

  setTarget(objectId: number | null): void {
    this.targetId = objectId;
    for (const [id, view] of this.actorViews) view.selection.visible = id === objectId;
  }

  getTarget(): number | null {
    return this.targetId;
  }

  screenToCellPoint(clientX: number, clientY: number): Point | null {
    const session = this.session;
    if (!session?.current.mapAsset || !this.app.canvas) return null;
    const viewportPoint = this.toViewportPoint(clientX, clientY);
    const cell = screenToCell(viewportPoint, this.camera);
    const { width, height } = session.current.mapAsset.meta;
    if (cell.x < 0 || cell.y < 0 || cell.x >= width || cell.y >= height) return null;
    return cell;
  }

  entityAtScreen(clientX: number, clientY: number): WorldEntity | null {
    const point = this.toViewportPoint(clientX, clientY);
    let best: { entity: WorldEntity; distance: number } | null = null;
    for (const view of this.actorViews.values()) {
      const screen = {
        x: view.root.x * this.camera.zoom + this.camera.x,
        y: view.root.y * this.camera.zoom + this.camera.y
      };
      const width = view.entity.kind === "monster" ? 42 : 32;
      if (point.x < screen.x - width || point.x > screen.x + width) continue;
      if (point.y < screen.y - 86 * this.camera.zoom || point.y > screen.y + 12) continue;
      const distance = Math.hypot(point.x - screen.x, point.y - screen.y + 24);
      if (!best || distance < best.distance) best = { entity: view.entity, distance };
    }
    return best?.entity ?? null;
  }

  getFps(): number {
    return this.app.ticker?.FPS ?? 0;
  }

  private readonly tick = (): void => {
    if (this.disposed || !this.session) return;
    const deltaSeconds = Math.min(0.05, this.app.ticker.deltaMS / 1000);
    this.elapsed += deltaSeconds;

    const focus = this.session.world.location;
    const zoom = preferredZoom({ width: this.app.screen.width, height: this.app.screen.height });
    this.targetCamera = cameraForViewport({ width: this.app.screen.width, height: this.app.screen.height }, focus, zoom);
    const blend = 1 - Math.pow(0.001, deltaSeconds);
    this.camera.x += (this.targetCamera.x - this.camera.x) * blend;
    this.camera.y += (this.targetCamera.y - this.camera.y) * blend;
    this.camera.zoom += (this.targetCamera.zoom - this.camera.zoom) * blend;
    this.worldLayer.position.set(this.camera.x, this.camera.y);
    this.worldLayer.scale.set(this.camera.zoom);

    const focusKey = `${focus.x}:${focus.y}:${this.mapKey}`;
    if (focusKey !== this.focusKey) {
      this.focusKey = focusKey;
      this.rebuildFloor(false);
    }
    this.syncActors(deltaSeconds);
    this.consumeEffects();
    this.updateEffects(deltaSeconds);

    if (this.elapsed - this.fpsReport > 1) {
      this.fpsReport = this.elapsed;
      this.hooks.onFps?.(Math.round(this.app.ticker.FPS));
    }
  };

  private resize(): void {
    if (!this.host || this.disposed) return;
    const width = Math.max(1, Math.floor(this.host.clientWidth));
    const height = Math.max(1, Math.floor(this.host.clientHeight));
    if (width === this.app.screen.width && height === this.app.screen.height) return;
    this.app.renderer.resize(width, height);
    this.snapCamera();
    this.rebuildFloor(true);
  }

  private snapCamera(): void {
    if (!this.session) return;
    const viewport = { width: this.app.screen.width, height: this.app.screen.height };
    this.camera = cameraForViewport(viewport, this.session.world.location, preferredZoom(viewport));
    this.targetCamera = { ...this.camera };
    this.worldLayer.position.set(this.camera.x, this.camera.y);
    this.worldLayer.scale.set(this.camera.zoom);
  }

  private async loadMapVisuals(): Promise<void> {
    const session = this.session;
    const map = session?.current;
    if (!session || !map?.mapAsset) return;
    const token = ++this.mapToken;
    this.mapKey = `${map.mapAsset.meta.file}:${map.map?.mapIndex ?? 0}`;
    this.hooks.onStatus?.("正在加载地图图库…");
    try {
      const packs = await session.assets.loadMapLibraries(map.mapAsset.meta.file);
      if (this.disposed || token !== this.mapToken) return;
      const usable = new Map<number, LibraryPack>();
      for (const [libraryIndex, pack] of packs) {
        const textures = await this.loadLibraryTextures(pack);
        if (this.disposed || token !== this.mapToken) return;
        if (textures) usable.set(libraryIndex, pack);
      }
      this.mapPacks = usable;
      this.rebuildFloor(true);
      this.hooks.onStatus?.(usable.size === 0 ? "地图图库未发布，使用地形占位渲染。" : null);
    } catch (exception) {
      if (this.disposed || token !== this.mapToken) return;
      this.mapPacks.clear();
      this.rebuildFloor(true);
      const message = exception instanceof Error ? exception.message : "地图图库加载失败。";
      this.hooks.onStatus?.(`图库加载失败：${message}。使用地形占位渲染。`);
    }
  }

  private async loadLibraryTextures(pack: LibraryPack): Promise<Texture[] | null> {
    const cached = this.pageTextures.get(pack.id);
    if (cached) return cached;
    try {
      const textures = await Promise.all(pack.pageUrls.map(async url => Assets.load<Texture>(url)));
      if (this.disposed) return null;
      this.pageTextures.set(pack.id, textures);
      return textures;
    } catch {
      return null;
    }
  }

  private rebuildFloor(force: boolean): void {
    const asset = this.session?.current.mapAsset;
    if (!asset) return;
    const viewport = { width: this.app.screen.width, height: this.app.screen.height };
    const signature = `${this.mapKey}:${this.session?.world.location.x}:${this.session?.world.location.y}:${viewport.width}x${viewport.height}:${this.camera.zoom.toFixed(2)}:${this.mapPacks.size}`;
    if (!force && signature === this.tileSignature) return;
    this.tileSignature = signature;

    this.fallbackLayer.clear();
    this.groundLayer.removeChildren().forEach(child => child.destroy());
    this.frontLayer.removeChildren().forEach(child => child.destroy());

    const bounds = visibleCellBounds(this.targetCamera, viewport, 4);
    const minX = Math.max(0, bounds.minX);
    const minY = Math.max(0, bounds.minY);
    const maxX = Math.min(asset.meta.width - 1, bounds.maxX);
    const maxY = Math.min(asset.meta.height - 1, bounds.maxY);

    for (let y = minY; y <= maxY; y++) {
      for (let x = minX; x <= maxX; x++) {
        const cell = cellAt(asset.cells, asset.meta.width, asset.meta.height, x, y);
        if (!cell) continue;
        this.drawFallbackCell(cell, x, y);
        this.drawMapCell(cell, x, y);
      }
    }
  }

  private drawFallbackCell(cell: MapCell, x: number, y: number): void {
    const parity = (x * 7 + y * 11 + cell.light) & 1;
    const base = cell.walkable ? (parity ? 0x17251e : 0x192a22) : (parity ? 0x241d19 : 0x2b211b);
    this.fallbackLayer.rect(x * CELL_WIDTH, y * CELL_HEIGHT, CELL_WIDTH - 1, CELL_HEIGHT - 1).fill({ color: base, alpha: 0.94 });
    if (!cell.walkable) {
      this.fallbackLayer.moveTo(x * CELL_WIDTH + 8, y * CELL_HEIGHT + 8)
        .lineTo(x * CELL_WIDTH + CELL_WIDTH - 9, y * CELL_HEIGHT + CELL_HEIGHT - 9)
        .stroke({ color: 0x6d4b37, width: 1, alpha: 0.4 });
    }
  }

  private drawMapCell(cell: MapCell, x: number, y: number): void {
    const drawX = x * CELL_WIDTH;
    const drawY = y * CELL_HEIGHT;

    if ((x & 1) === 0 && (y & 1) === 0) {
      const backImage = cell.backImage & 0x1fffffff;
      if (backImage > 0 && cell.backIndex >= 0) {
        this.addMapSprite(this.groundLayer, cell.backIndex, backImage - 1, drawX, drawY, false, 0);
      }
    }

    if (cell.middleImage > 0 && cell.middleIndex >= 0) {
      const frame = this.frameFor(cell.middleIndex, cell.middleImage - 1);
      const bottom = frame ? ((frame.width === CELL_WIDTH && frame.height === CELL_HEIGHT) || (frame.width === CELL_WIDTH * 2 && frame.height === CELL_HEIGHT * 2)) : true;
      this.addMapSprite(this.groundLayer, cell.middleIndex, cell.middleImage - 1, drawX, drawY, !bottom, 1);
    }

    const frontImage = cell.frontImage & 0x7fff;
    if (frontImage > 0 && cell.frontIndex >= 0 && cell.frontIndex !== 200) {
      this.addMapSprite(this.frontLayer, cell.frontIndex, frontImage - 1, drawX, drawY, true, 2);
    }
  }

  private frameFor(libraryIndex: number, frameIndex: number): LibraryFrame | undefined {
    return this.mapPacks.get(libraryIndex)?.byIndex.get(frameIndex);
  }

  private addMapSprite(
    layer: Container,
    libraryIndex: number,
    frameIndex: number,
    x: number,
    y: number,
    bottomAligned: boolean,
    zIndex: number
  ): void {
    const pack = this.mapPacks.get(libraryIndex);
    const frame = pack?.byIndex.get(frameIndex);
    if (!pack || !frame || frame.page < 0) return;
    const texture = this.frameTexture(pack, frame);
    if (!texture) return;
    const sprite = new Sprite(texture);
    sprite.x = x;
    sprite.y = bottomAligned ? y + CELL_HEIGHT - frame.height : y;
    sprite.zIndex = zIndex;
    layer.addChild(sprite);
  }

  private frameTexture(pack: LibraryPack, frame: LibraryFrame): Texture | null {
    const key = `${pack.id}:${frame.index}`;
    const cached = this.frameTextures.get(key);
    if (cached) return cached;
    const pages = this.pageTextures.get(pack.id);
    const page = pages?.[frame.page];
    if (!page) return null;
    const texture = new Texture({
      source: page.source,
      frame: new Rectangle(frame.x, frame.y, frame.width, frame.height)
    });
    this.frameTextures.set(key, texture);
    return texture;
  }

  private syncActors(deltaSeconds: number): void {
    const world = this.session?.world;
    if (!world) return;
    for (const [id, view] of this.actorViews) {
      if (!world.entities.has(id)) {
        view.root.destroy({ children: true });
        this.actorViews.delete(id);
      }
    }
    for (const entity of world.entities.values()) {
      let view = this.actorViews.get(entity.objectId);
      if (!view) {
        view = this.createActorView(entity);
        this.actorViews.set(entity.objectId, view);
        this.actorLayer.addChild(view.root);
      }
      this.updateActorView(view, entity, deltaSeconds);
    }
    for (const view of this.actorViews.values()) view.root.zIndex = Math.round(view.root.y * 10) + (view.entity.kind === "item" || view.entity.kind === "gold" ? -1000 : 0);
  }

  private createActorView(entity: WorldEntity): ActorView {
    const root = new Container();
    const shadow = new Graphics();
    const body = new Graphics();
    const selection = new Graphics();
    const health = new Graphics();
    const name = new Text({
      text: entity.name,
      style: {
        fontFamily: "\"Noto Sans SC\", \"Microsoft YaHei\", sans-serif",
        fontSize: 13,
        fontWeight: "600",
        fill: 0xf0f5ec,
        stroke: { color: 0x06100d, width: 4 },
        align: "center"
      }
    });
    name.anchor.set(0.5, 1);
    name.y = -74;
    health.y = -66;
    selection.ellipse(0, 2, 23, 10).stroke({ color: 0xe6c26b, width: 2, alpha: 0.95 });
    selection.visible = entity.objectId === this.targetId;
    root.addChild(selection, shadow, body, health, name);
    return {
      root,
      body,
      shadow,
      selection,
      health,
      name,
      entity,
      targetX: 0,
      targetY: 0,
      initialised: false,
      lastKind: "",
      lastName: ""
    };
  }

  private updateActorView(view: ActorView, entity: WorldEntity, deltaSeconds: number): void {
    view.entity = entity;
    const feet = entityFeet(entity);
    view.targetX = feet.x;
    view.targetY = feet.y;
    if (!view.initialised) {
      view.root.x = feet.x;
      view.root.y = feet.y;
      view.initialised = true;
    } else {
      const blend = 1 - Math.pow(0.00002, deltaSeconds);
      view.root.x += (feet.x - view.root.x) * blend;
      view.root.y += (feet.y - view.root.y) * blend;
    }
    view.selection.visible = entity.objectId === this.targetId;
    view.selection.y = 0;
    view.name.visible = entity.kind !== "item" && entity.kind !== "gold";
    view.health.visible = entity.hpPercent !== undefined && entity.hpPercent < 100 && !entity.dead;
    view.root.alpha = entity.dead ? 0.46 : 1;
    view.root.rotation = entity.dead ? -0.08 : 0;
    if (entity.kind === "monster" || entity.kind === "player" || entity.kind === "hero") view.root.scale.x = entity.direction >= 4 ? 1 : -1;
    else view.root.scale.x = 1;

    if (view.lastKind !== entity.kind || view.lastName !== entity.name) {
      view.lastKind = entity.kind;
      view.lastName = entity.name;
      view.name.text = entity.name;
      this.drawActorBody(view, entity);
    }
    this.drawHealth(view, entity);
  }

  private drawActorBody(view: ActorView, entity: WorldEntity): void {
    const { body, shadow } = view;
    body.clear();
    shadow.clear();
    const playerColour = entity.className !== undefined ? CLASS_COLOURS[entity.className % CLASS_COLOURS.length] ?? 0x9ac7b2 : 0x9ac7b2;
    let colour = entity.kind === "hero" ? 0x69dfcf : hashColour(entity.image * 31 + entity.name.length * 17 + entity.objectId);
    if (entity.kind === "player") colour = entity.objectId === this.session?.world.selfId ? 0xd9e8da : playerColour;
    if (entity.kind === "npc") colour = 0xe6c26b;
    if (entity.kind === "item") colour = 0x9fd9ff;
    if (entity.kind === "gold") colour = 0xf4c84c;

    if (entity.kind === "item") {
      shadow.ellipse(0, 0, 9, 4).fill({ color: 0x000000, alpha: 0.3 });
      body.moveTo(0, -30).lineTo(10, -18).lineTo(0, -7).lineTo(-10, -18).closePath().fill({ color: 0x9fd9ff, alpha: 0.9 });
      body.moveTo(0, -26).lineTo(4, -18).lineTo(0, -11).lineTo(-4, -18).closePath().fill({ color: 0xf0fbff, alpha: 0.9 });
      return;
    }
    if (entity.kind === "gold") {
      shadow.ellipse(0, 0, 10, 4).fill({ color: 0x000000, alpha: 0.3 });
      body.circle(0, -13, 9).fill({ color: 0xf4c84c });
      body.circle(0, -13, 5).stroke({ color: 0x8a5b16, width: 2 });
      return;
    }

    shadow.ellipse(0, 0, entity.kind === "monster" ? 23 : 17, entity.kind === "monster" ? 10 : 7).fill({ color: 0x000000, alpha: 0.36 });
    if (entity.kind === "monster") {
      body.ellipse(0, -20, 21, 18).fill({ color: colour });
      body.ellipse(-8, -25, 4, 3).fill({ color: 0x0a0d0d, alpha: 0.85 });
      body.ellipse(8, -25, 4, 3).fill({ color: 0x0a0d0d, alpha: 0.85 });
      body.moveTo(-16, -33).lineTo(-24, -43).lineTo(-9, -37).closePath().fill({ color: colour });
      body.moveTo(16, -33).lineTo(24, -43).lineTo(9, -37).closePath().fill({ color: colour });
      body.rect(-12, -4, 7, 8).fill({ color: colour });
      body.rect(5, -4, 7, 8).fill({ color: colour });
      return;
    }

    body.roundRect(-12, -38, 24, 26, 8).fill({ color: colour });
    body.rect(-8, -14, 6, 14).fill({ color: colour });
    body.rect(2, -14, 6, 14).fill({ color: colour });
    body.circle(0, -47, 8).fill({ color: entity.kind === "npc" ? 0xd8b878 : 0xc8aa8a });
    if (entity.kind === "npc") {
      body.moveTo(-16, -35).lineTo(16, -35).lineTo(12, -8).lineTo(-12, -8).closePath().fill({ color: 0xd1ad6a });
      body.circle(0, -49, 9).stroke({ color: 0xffe3a5, width: 2, alpha: 0.8 });
    } else {
      body.moveTo(-10, -40).lineTo(0, -54).lineTo(10, -40).closePath().fill({ color: entity.kind === "hero" ? 0x69dfcf : 0x2c4840 });
      if (entity.objectId === this.session?.world.selfId) {
        body.circle(0, -47, 10).stroke({ color: 0xf2d17c, width: 2, alpha: 0.85 });
      }
    }
  }

  private drawHealth(view: ActorView, entity: WorldEntity): void {
    view.health.clear();
    if (entity.hpPercent === undefined || entity.dead) return;
    const width = 28;
    const pct = Math.max(0, Math.min(100, entity.hpPercent));
    view.health.rect(-width / 2, 0, width, 4).fill({ color: 0x08120f, alpha: 0.85 });
    view.health.rect(-width / 2, 0, (width * pct) / 100, 4).fill({ color: pct < 30 ? 0xda685b : 0x76b88a });
  }

  private consumeEffects(): void {
    const world = this.session?.world;
    if (!world) return;
    for (const effect of world.drainEffects()) this.spawnEffect(effect);
  }

  private spawnEffect(effect: WorldEffect): void {
    const entity = this.session?.world.entities.get(effect.objectId);
    const position = entity ? entityFeet(entity) : cellCenter(this.session?.world.location ?? { x: 0, y: 0 });
    if (effect.kind === "damage") {
      const text = new Text({
        text: String(effect.damage ?? ""),
        style: {
          fontFamily: "Georgia, serif",
          fontSize: 22,
          fontWeight: "700",
          fill: effect.damageType === 1 ? 0x91e5ff : 0xffd58a,
          stroke: { color: 0x180f08, width: 5 }
        }
      });
      text.anchor.set(0.5);
      text.position.set(position.x + 14, position.y - 62);
      this.effectLayer.addChild(text);
      this.floatingEffects.push({ view: text, age: 0, duration: 0.9, driftX: 18, driftY: -54 });
      return;
    }
    if (effect.kind === "struck") {
      const ring = new Graphics();
      ring.circle(position.x, position.y - 28, 16).stroke({ color: 0xffb27a, width: 3 });
      this.effectLayer.addChild(ring);
      this.floatingEffects.push({ view: ring, age: 0, duration: 0.35, driftX: 0, driftY: 0 });
      return;
    }
    const bubble = new Text({
      text: effect.text ?? "",
      style: {
        fontFamily: "\"Noto Sans SC\", \"Microsoft YaHei\", sans-serif",
        fontSize: 13,
        fill: 0xf5f1dd,
        stroke: { color: 0x0a100e, width: 5 },
        wordWrap: true,
        wordWrapWidth: 230,
        align: "center"
      }
    });
    bubble.anchor.set(0.5, 1);
    bubble.position.set(position.x, position.y - 82);
    this.effectLayer.addChild(bubble);
    this.floatingEffects.push({ view: bubble, age: 0, duration: 3.4, driftX: 0, driftY: -10 });
  }

  private updateEffects(deltaSeconds: number): void {
    for (let i = this.floatingEffects.length - 1; i >= 0; i--) {
      const effect = this.floatingEffects[i]!;
      effect.age += deltaSeconds;
      const progress = effect.age / effect.duration;
      if (progress >= 1) {
        effect.view.destroy({ children: true });
        this.floatingEffects.splice(i, 1);
        continue;
      }
      effect.view.x += effect.driftX * deltaSeconds;
      effect.view.y += effect.driftY * deltaSeconds;
      effect.view.alpha = progress < 0.65 ? 1 : 1 - (progress - 0.65) / 0.35;
    }
  }

  private toViewportPoint(clientX: number, clientY: number): { x: number; y: number } {
    const canvas = this.app.canvas;
    const rect = canvas.getBoundingClientRect();
    return {
      x: ((clientX - rect.left) * this.app.screen.width) / Math.max(1, rect.width),
      y: ((clientY - rect.top) * this.app.screen.height) / Math.max(1, rect.height)
    };
  }
}
