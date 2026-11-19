import type { ClientNpcInfo, Point, ServerPacket } from "../protocol/types";

export type EntityKind = "player" | "hero" | "monster" | "npc" | "item" | "gold";

export interface WorldEntity {
  objectId: number;
  kind: EntityKind;
  name: string;
  location: Point;
  direction: number;
  dead: boolean;
  image: number;
  level?: number;
  className?: number;
  hpPercent?: number;
  ownerName?: string;
  npc?: ClientNpcInfo;
}

export interface WorldMap {
  mapIndex: number;
  fileName: string;
  title: string;
  width?: number;
  height?: number;
  miniMap: number;
  bigMap: number;
  lights: number;
  mapDarkLight: number;
  weatherParticles: number;
}

export interface WorldEffect {
  kind: "damage" | "chat" | "struck";
  objectId: number;
  text?: string;
  damage?: number;
  damageType?: number;
  at: number;
}

export class WorldState {
  readonly entities = new Map<number, WorldEntity>();
  readonly npcs = new Map<number, ClientNpcInfo>();
  map: WorldMap | null = null;
  selfId: number | null = null;
  location: Point = { x: 0, y: 0 };
  direction = 4;
  private readonly listeners = new Set<() => void>();
  private readonly effects: WorldEffect[] = [];

  subscribe(listener: () => void): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  notify(): void {
    for (const listener of this.listeners) listener();
  }

  clearMap(keepSelf = false): void {
    const self = this.selfId !== null ? this.entities.get(this.selfId) : undefined;
    this.entities.clear();
    if (keepSelf && self) this.entities.set(self.objectId, self);
    this.npcs.clear();
    this.effects.length = 0;
    this.notify();
  }

  apply(packet: ServerPacket): void {
    let changed = true;
    switch (packet.kind) {
      case "UserInformation":
        this.selfId = packet.objectId;
        this.location = packet.location;
        this.direction = packet.direction;
        this.entities.set(packet.objectId, {
          objectId: packet.objectId,
          kind: "player",
          name: packet.name,
          location: packet.location,
          direction: packet.direction,
          dead: false,
          image: 0,
          level: packet.level,
          className: packet.classId
        });
        break;
      case "UserLocation":
        this.location = packet.location;
        this.direction = packet.direction;
        if (this.selfId !== null) {
          const self = this.entities.get(this.selfId);
          if (self) {
            self.location = packet.location;
            self.direction = packet.direction;
          }
        }
        break;
      case "ObjectPlayer":
        this.entities.set(packet.objectId, {
          objectId: packet.objectId,
          kind: packet.hero ? "hero" : "player",
          name: packet.name,
          location: packet.location,
          direction: packet.direction,
          dead: packet.dead,
          image: 0,
          level: packet.level,
          className: packet.classId,
          ownerName: packet.ownerName ?? ""
        });
        break;
      case "ObjectMonster":
        this.entities.set(packet.objectId, {
          objectId: packet.objectId,
          kind: "monster",
          name: packet.name,
          location: packet.location,
          direction: packet.direction,
          dead: packet.dead,
          image: packet.image
        });
        break;
      case "ObjectNpc": {
        const known = this.npcs.get(packet.objectId);
        const entity: WorldEntity = {
          objectId: packet.objectId,
          kind: "npc",
          name: packet.name,
          location: packet.location,
          direction: packet.direction,
          dead: false,
          image: packet.image
        };
        if (known) entity.npc = known;
        this.entities.set(packet.objectId, entity);
        break;
      }
      case "ObjectItem":
        this.entities.set(packet.objectId, {
          objectId: packet.objectId,
          kind: "item",
          name: packet.name,
          location: packet.location,
          direction: 0,
          dead: false,
          image: packet.image
        });
        break;
      case "ObjectGold":
        this.entities.set(packet.objectId, {
          objectId: packet.objectId,
          kind: "gold",
          name: `${packet.gold} 金币`,
          location: packet.location,
          direction: 0,
          dead: false,
          image: 0
        });
        break;
      case "ObjectRemove":
        this.entities.delete(packet.objectId);
        break;
      case "ObjectMove": {
        const entity = this.entities.get(packet.objectId);
        if (entity) {
          entity.location = packet.location;
          entity.direction = packet.direction;
        }
        if (packet.objectId === this.selfId) {
          this.location = packet.location;
          this.direction = packet.direction;
        }
        break;
      }
      case "ObjectDied": {
        const entity = this.entities.get(packet.objectId);
        if (entity) {
          entity.dead = true;
          entity.location = packet.location;
          entity.direction = packet.direction;
        }
        break;
      }
      case "ObjectRevived": {
        const entity = this.entities.get(packet.objectId);
        if (entity) entity.dead = false;
        break;
      }
      case "ObjectHealth": {
        const entity = this.entities.get(packet.objectId);
        if (entity) entity.hpPercent = packet.percent;
        break;
      }
      case "ObjectHide": {
        const entity = this.entities.get(packet.objectId);
        if (entity && packet.objectId !== this.selfId) this.entities.delete(packet.objectId);
        break;
      }
      case "ObjectShow":
        break;
      case "DamageIndicator":
        this.effects.push({ kind: "damage", objectId: packet.objectId, damage: packet.damage, damageType: packet.damageType, at: performance.now() });
        break;
      case "ObjectChat":
        this.effects.push({ kind: "chat", objectId: packet.objectId, text: packet.text, at: performance.now() });
        break;
      case "ObjectStruck":
        this.effects.push({ kind: "struck", objectId: packet.objectId, at: performance.now() });
        break;
      default:
        changed = false;
    }
    if (changed) this.notify();
  }

  drainEffects(): WorldEffect[] {
    return this.effects.splice(0);
  }
}
