import type { Point } from "../protocol/types";
import type { GameRenderer } from "../render/GameRenderer";
import type { GameSession } from "../session/GameSession";
import { directionTo, findPath, isWalkable, nextPoint } from "../world/map";
import type { WorldEntity } from "../world/WorldState";

export interface InputHooks {
  onTargetChanged?(objectId: number | null): void;
  onNotice?(message: string): void;
}

type PendingAction = { kind: "attack" | "pickup" | "npc"; objectId: number };

const KEY_DIRECTIONS: Readonly<Record<string, Point>> = {
  KeyW: { x: 0, y: -1 },
  ArrowUp: { x: 0, y: -1 },
  KeyD: { x: 1, y: 0 },
  ArrowRight: { x: 1, y: 0 },
  KeyS: { x: 0, y: 1 },
  ArrowDown: { x: 0, y: 1 },
  KeyA: { x: -1, y: 0 },
  ArrowLeft: { x: -1, y: 0 }
};

const DIRECTION_TABLE: ReadonlyArray<Point & { direction: number }> = [
  { x: 0, y: -1, direction: 0 },
  { x: 1, y: -1, direction: 1 },
  { x: 1, y: 0, direction: 2 },
  { x: 1, y: 1, direction: 3 },
  { x: 0, y: 1, direction: 4 },
  { x: -1, y: 1, direction: 5 },
  { x: -1, y: 0, direction: 6 },
  { x: -1, y: -1, direction: 7 }
];

function isEditable(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false;
  const tag = target.tagName;
  return target.isContentEditable || tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT";
}

function samePoint(left: Point, right: Point): boolean {
  return left.x === right.x && left.y === right.y;
}

function chebyshev(left: Point, right: Point): number {
  return Math.max(Math.abs(left.x - right.x), Math.abs(left.y - right.y));
}

export class InputController {
  private readonly session: GameSession;
  private readonly renderer: GameRenderer;
  private readonly hooks: InputHooks;
  private readonly keys = new Set<string>();
  private host: HTMLElement | null = null;
  private moveTimer: number | null = null;
  private virtualDirection: number | null = null;
  private virtualRunning = false;
  private running = false;
  private path: number[] = [];
  private pathGoal: Point | null = null;
  private pendingAction: PendingAction | null = null;
  private pendingStepFrom: Point | null = null;
  private pendingStepAt = 0;
  private attached = false;
  private targetId: number | null = null;

  constructor(session: GameSession, renderer: GameRenderer, hooks: InputHooks = {}) {
    this.session = session;
    this.renderer = renderer;
    this.hooks = hooks;
  }

  attach(host: HTMLElement): void {
    if (this.attached) return;
    this.attached = true;
    this.host = host;
    host.addEventListener("pointerdown", this.onPointerDown);
    host.addEventListener("contextmenu", this.onContextMenu);
    window.addEventListener("keydown", this.onKeyDown);
    window.addEventListener("keyup", this.onKeyUp);
    window.addEventListener("blur", this.onBlur);
    document.addEventListener("visibilitychange", this.onVisibility);
    this.moveTimer = window.setInterval(() => this.movementTick(), 150);
  }

  detach(): void {
    if (!this.attached) return;
    this.attached = false;
    if (this.moveTimer !== null) window.clearInterval(this.moveTimer);
    this.moveTimer = null;
    this.host?.removeEventListener("pointerdown", this.onPointerDown);
    this.host?.removeEventListener("contextmenu", this.onContextMenu);
    window.removeEventListener("keydown", this.onKeyDown);
    window.removeEventListener("keyup", this.onKeyUp);
    window.removeEventListener("blur", this.onBlur);
    document.removeEventListener("visibilitychange", this.onVisibility);
    this.host = null;
  }

  setVirtualDirection(direction: number | null, running = false): void {
    this.virtualDirection = direction;
    this.virtualRunning = running;
    if (direction !== null) this.clearPath();
  }

  setTarget(objectId: number | null): void {
    this.targetId = objectId;
    this.renderer.setTarget(objectId);
    this.hooks.onTargetChanged?.(objectId);
  }

  attack(): void {
    if (!this.canControl()) return;
    const target = this.targetEntity();
    if (target && target.kind === "monster" && !target.dead) {
      this.engage(target);
      return;
    }
    const nearest = this.nearestMonster(8);
    if (nearest) {
      this.engage(nearest);
      return;
    }
    this.session.attack(this.session.world.direction);
  }

  pickup(): void {
    if (!this.canControl()) return;
    const candidates = [...this.session.world.entities.values()]
      .filter(entity => (entity.kind === "item" || entity.kind === "gold") && chebyshev(entity.location, this.session.world.location) <= 1)
      .sort((left, right) => chebyshev(left.location, this.session.world.location) - chebyshev(right.location, this.session.world.location));
    const nearest = candidates[0];
    if (nearest) {
      this.session.pickup();
      return;
    }
    const visible = [...this.session.world.entities.values()].find(entity => entity.kind === "item" || entity.kind === "gold");
    if (visible) this.engage(visible);
  }

  chooseNearestTarget(): void {
    const target = this.nearestMonster(10);
    this.setTarget(target?.objectId ?? null);
  }

  stop(): void {
    this.clearPath();
    this.keys.clear();
    this.virtualDirection = null;
    this.pendingStepFrom = null;
  }

  private readonly onPointerDown = (event: PointerEvent): void => {
    if (event.button !== 0 || isEditable(event.target)) return;
    if (!this.canControl()) return;
    event.preventDefault();
    const entity = this.renderer.entityAtScreen(event.clientX, event.clientY);
    if (entity) {
      this.setTarget(entity.objectId);
      if (entity.kind === "monster" || entity.kind === "item" || entity.kind === "gold" || entity.kind === "npc") this.engage(entity);
      return;
    }
    const cell = this.renderer.screenToCellPoint(event.clientX, event.clientY);
    if (!cell) return;
    this.setTarget(null);
    this.pendingAction = null;
    this.walkTo(cell);
  };

  private readonly onContextMenu = (event: MouseEvent): void => {
    event.preventDefault();
    if (this.canControl()) this.attack();
  };

  private readonly onKeyDown = (event: KeyboardEvent): void => {
    if (isEditable(event.target) || event.ctrlKey || event.metaKey || event.altKey) return;
    if (event.code === "ShiftLeft" || event.code === "ShiftRight") {
      this.running = true;
      return;
    }
    if (event.code === "Space") {
      event.preventDefault();
      this.attack();
      return;
    }
    if (event.code === "KeyF") {
      event.preventDefault();
      this.pickup();
      return;
    }
    if (!KEY_DIRECTIONS[event.code]) return;
    event.preventDefault();
    this.keys.add(event.code);
    this.clearPath();
  };

  private readonly onKeyUp = (event: KeyboardEvent): void => {
    if (event.code === "ShiftLeft" || event.code === "ShiftRight") {
      this.running = false;
      return;
    }
    this.keys.delete(event.code);
  };

  private readonly onBlur = (): void => {
    this.keys.clear();
    this.virtualDirection = null;
    this.pendingStepFrom = null;
  };

  private readonly onVisibility = (): void => {
    if (document.visibilityState !== "visible") this.onBlur();
  };

  private canControl(): boolean {
    const state = this.session.current;
    return state.phase === "game" && state.link === "open" && !state.defeated;
  }

  private currentDirectionFromKeys(): number | null {
    let x = 0;
    let y = 0;
    for (const code of this.keys) {
      const direction = KEY_DIRECTIONS[code];
      if (!direction) continue;
      x += direction.x;
      y += direction.y;
    }
    if (x === 0 && y === 0) return null;
    const normalized = { x: Math.sign(x), y: Math.sign(y) };
    return DIRECTION_TABLE.find(value => value.x === normalized.x && value.y === normalized.y)?.direction ?? null;
  }

  private movementTick(): void {
    if (!this.canControl()) return;
    const now = performance.now();
    const self = this.session.world.location;
    if (this.pendingStepFrom) {
      const moved = !samePoint(self, this.pendingStepFrom);
      if (!moved && now - this.pendingStepAt < 650) return;
      this.pendingStepFrom = null;
      if (!moved && this.pathGoal && now - this.pendingStepAt >= 650) {
        this.replanTo(this.pathGoal);
        return;
      }
    }

    this.resolvePendingAction();
    if (this.pendingAction) return;

    let direction = this.virtualDirection;
    let run = this.virtualRunning || (this.virtualDirection === null && (this.running || this.keys.has("ShiftLeft") || this.keys.has("ShiftRight")));
    if (direction === null) {
      direction = this.currentDirectionFromKeys();
      run = this.running;
    }
    if (direction === null && this.path.length > 0) {
      direction = this.path.shift() ?? null;
      if (this.path.length === 0) this.pathGoal = null;
      run = false;
    }
    if (direction === null) return;
    this.pendingStepFrom = { ...self };
    this.pendingStepAt = now;
    if (run) this.session.run(direction);
    else this.session.walk(direction);
  }

  private engage(entity: WorldEntity): void {
    this.setTarget(entity.objectId);
    if (entity.kind === "monster") {
      if (chebyshev(entity.location, this.session.world.location) <= 2) {
        this.session.attack(directionTo(this.session.world.location, entity.location));
        this.pendingAction = null;
        return;
      }
      this.pendingAction = { kind: "attack", objectId: entity.objectId };
      this.moveAdjacentTo(entity.location);
      return;
    }
    if (entity.kind === "item" || entity.kind === "gold") {
      if (chebyshev(entity.location, this.session.world.location) <= 1) {
        this.session.pickup();
        this.pendingAction = null;
        return;
      }
      this.pendingAction = { kind: "pickup", objectId: entity.objectId };
      this.walkTo(entity.location);
      return;
    }
    if (entity.kind === "npc") {
      if (chebyshev(entity.location, this.session.world.location) <= 3) {
        this.session.callNpc(entity.objectId, "");
        this.pendingAction = null;
        return;
      }
      this.pendingAction = { kind: "npc", objectId: entity.objectId };
      this.moveAdjacentTo(entity.location);
    }
  }

  private resolvePendingAction(): void {
    if (!this.pendingAction) return;
    const entity = this.session.world.entities.get(this.pendingAction.objectId);
    if (!entity) {
      this.pendingAction = null;
      this.clearPath();
      return;
    }
    const distance = chebyshev(entity.location, this.session.world.location);
    if (this.pendingAction.kind === "attack" && distance <= 2 && !entity.dead) {
      this.session.attack(directionTo(this.session.world.location, entity.location));
      this.pendingAction = null;
      this.clearPath();
    } else if (this.pendingAction.kind === "pickup" && distance <= 1) {
      this.session.pickup();
      this.pendingAction = null;
      this.clearPath();
    } else if (this.pendingAction.kind === "npc" && distance <= 3) {
      this.session.callNpc(entity.objectId, "");
      this.pendingAction = null;
      this.clearPath();
    }
  }

  private targetEntity(): WorldEntity | null {
    return this.targetId === null ? null : this.session.world.entities.get(this.targetId) ?? null;
  }

  private nearestMonster(maxDistance: number): WorldEntity | null {
    const self = this.session.world.location;
    return [...this.session.world.entities.values()]
      .filter(entity => entity.kind === "monster" && !entity.dead && chebyshev(entity.location, self) <= maxDistance)
      .sort((left, right) => chebyshev(left.location, self) - chebyshev(right.location, self))[0] ?? null;
  }

  private moveAdjacentTo(target: Point): void {
    const map = this.session.current.mapAsset;
    if (!map) return;
    const candidates = DIRECTION_TABLE
      .map(value => ({ x: target.x + value.x, y: target.y + value.y }))
      .filter(point => isWalkable(map.cells, map.meta.width, map.meta.height, point))
      .sort((left, right) => chebyshev(left, this.session.world.location) - chebyshev(right, this.session.world.location));
    const destination = candidates[0];
    if (destination) this.walkTo(destination);
  }

  private walkTo(destination: Point): void {
    const map = this.session.current.mapAsset;
    if (!map) return;
    const start = this.session.world.location;
    const path = findPath(map.cells, map.meta.width, map.meta.height, start, destination, 4096);
    if (path.length === 0) {
      this.hooks.onNotice?.("那里无法到达。");
      return;
    }
    this.path = path;
    this.pathGoal = { ...destination };
    this.pendingStepFrom = null;
    this.movementTick();
  }

  private replanTo(destination: Point): void {
    this.path = [];
    this.pathGoal = null;
    this.walkTo(destination);
  }

  private clearPath(): void {
    this.path = [];
    this.pathGoal = null;
    this.pendingStepFrom = null;
  }
}
