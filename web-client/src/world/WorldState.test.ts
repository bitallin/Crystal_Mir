import { describe, expect, it } from "vitest";
import { WorldState } from "./WorldState";

describe("WorldState", () => {
  it("tracks self, remote objects, movement, health and removal", () => {
    const world = new WorldState();
    world.apply({
      kind: "UserInformation",
      objectId: 1,
      realId: 1,
      name: "凌云",
      guildName: "",
      guildRank: "",
      nameColour: 0xffffffff,
      classId: 0,
      gender: 0,
      level: 42,
      location: { x: 10, y: 10 },
      direction: 4,
      hair: 1,
      hp: 300,
      mp: 100,
      experience: 1n,
      maxExperience: 10n,
      levelEffects: 0,
      hasHero: false,
      heroBehaviour: 0,
      inventory: null,
      equipment: null,
      questInventory: null,
      gold: 0,
      credit: 0,
      hasExpandedStorage: false,
      hasStoragePassword: false,
      requireStoragePassword: false,
      storagePasswordLastSet: 0n,
      expandedStorageExpiryTime: 0n,
      magics: [],
      summonedCreatureType: 0,
      creatureSummoned: false,
      allowObserve: false,
      observer: false
    });
    world.apply({
      kind: "ObjectMonster",
      objectId: 9,
      name: "鸡",
      nameColour: 0xffffffff,
      location: { x: 12, y: 11 },
      image: 10,
      direction: 4,
      effect: 0,
      ai: 1,
      light: 0,
      dead: false,
      skeleton: false,
      poison: 0,
      hidden: false,
      shockTime: 0n,
      bindingShotCenter: false,
      extra: false,
      extraByte: 0,
      masterObjectId: 0,
      rarity: 0,
      buffs: []
    });
    world.apply({ kind: "ObjectMove", movement: "walk", objectId: 1, location: { x: 11, y: 10 }, direction: 2 });
    world.apply({ kind: "ObjectHealth", objectId: 9, percent: 60, expire: 0 });
    world.apply({ kind: "DamageIndicator", objectId: 9, damage: 12, damageType: 0 });
    expect(world.location).toEqual({ x: 11, y: 10 });
    expect(world.entities.get(9)?.hpPercent).toBe(60);
    expect(world.drainEffects()).toHaveLength(1);
    world.apply({ kind: "ObjectRemove", objectId: 9 });
    expect(world.entities.has(9)).toBe(false);
  });

  it("keeps the local player across map transitions and clears the rest", () => {
    const world = new WorldState();
    world.selfId = 1;
    world.entities.set(1, { objectId: 1, kind: "player", name: "self", location: { x: 1, y: 1 }, direction: 4, dead: false, image: 0 });
    world.entities.set(2, { objectId: 2, kind: "monster", name: "mob", location: { x: 2, y: 2 }, direction: 4, dead: false, image: 1 });
    world.clearMap(true);
    expect(world.entities.has(1)).toBe(true);
    expect(world.entities.has(2)).toBe(false);
  });
});
