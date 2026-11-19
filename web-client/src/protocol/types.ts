export interface Point {
  x: number;
  y: number;
}

export interface SelectInfo {
  index: number;
  name: string;
  level: number;
  classId: number;
  gender: number;
  lastAccess: bigint;
}

export interface StatValue {
  stat: number;
  value: number;
}

export interface AwakeData {
  type: number;
  values: number[];
}

export interface UserItem {
  uniqueId: bigint;
  itemIndex: number;
  currentDurability: number;
  maxDurability: number;
  count: number;
  soulBoundId: number;
  identified: boolean;
  cursed: boolean;
  slots: Array<UserItem | null>;
  gemCount: number;
  addedStats: StatValue[];
  awake: AwakeData;
  refinedValue: number;
  refineAdded: number;
  refineSuccessChance: number;
  weddingRing: number;
  expireBinary?: bigint;
  rental?: { ownerName: string; bindingFlags: number; expiryBinary: bigint; locked: boolean };
  isShopItem: boolean;
  sealed?: { expiryBinary: bigint; nextSealBinary: bigint };
  gmMade: boolean;
}

export interface ClientMagic {
  name: string;
  spell: number;
  baseCost: number;
  levelCost: number;
  icon: number;
  levels: [number, number, number];
  needs: [number, number, number];
  level: number;
  key: number;
  range: number;
  experience: number;
  delay: bigint;
  castTime: bigint;
}

export interface ClientMovementInfo {
  destination: number;
  title: string;
  location: Point;
  icon: number;
}

export interface ClientNpcInfo {
  index: number;
  fileName: string;
  name: string;
  mapIndex: number;
  location: Point;
  image: number;
  rate: number;
  showOnBigMap: boolean;
  bigMapIcon: number;
  objectId: number;
  icon: number;
  canTeleportTo: boolean;
}

export interface ClientItemInfo {
  index: number;
  name: string;
  type: number;
  grade: number;
  requiredType: number;
  requiredClass: number;
  requiredGender: number;
  set: number;
  shape: number;
  weight: number;
  light: number;
  requiredAmount: number;
  image: number;
  durability: number;
  stackSize: number;
  price: number;
  startItem: boolean;
  effect: number;
  flags: number;
  bind: number;
  unique: number;
  randomStatsId: number;
  canFastRun: boolean;
  canAwakening: boolean;
  slots: number;
  tail: Uint8Array;
}

export interface UnknownPacket {
  kind: "unknown";
  id: number;
  body: Uint8Array;
}

export type ServerPacket =
  | { kind: "Connected" }
  | { kind: "ClientVersion"; result: number }
  | { kind: "Disconnect"; reason: number }
  | { kind: "KeepAlive"; time: bigint }
  | { kind: "Login"; result: number }
  | { kind: "LoginBanned"; reason: string; expiryBinary: bigint }
  | { kind: "LoginSuccess"; characters: SelectInfo[] }
  | { kind: "NewCharacter"; result: number }
  | { kind: "NewCharacterSuccess"; character: SelectInfo }
  | { kind: "DeleteCharacter"; result: number }
  | { kind: "DeleteCharacterSuccess"; characterIndex: number }
  | { kind: "StartGame"; result: number; resolution: number }
  | { kind: "StartGameBanned"; reason: string; expiryBinary: bigint }
  | { kind: "StartGameDelay"; milliseconds: bigint }
  | {
      kind: "MapInformation";
      mapIndex: number;
      fileName: string;
      title: string;
      miniMap: number;
      bigMap: number;
      lights: number;
      lightning: boolean;
      fire: boolean;
      mapDarkLight: number;
      music: number;
      weatherParticles: number;
    }
  | {
      kind: "NewMapInfo";
      mapIndex: number;
      info: {
        title: string;
        width: number;
        height: number;
        bigMap: number;
        movements: ClientMovementInfo[];
        npcs: ClientNpcInfo[];
      };
    }
  | {
      kind: "UserInformation";
      objectId: number;
      realId: number;
      name: string;
      guildName: string;
      guildRank: string;
      nameColour: number;
      classId: number;
      gender: number;
      level: number;
      location: Point;
      direction: number;
      hair: number;
      hp: number;
      mp: number;
      experience: bigint;
      maxExperience: bigint;
      levelEffects: number;
      hasHero: boolean;
      heroBehaviour: number;
      inventory: Array<UserItem | null> | null;
      equipment: Array<UserItem | null> | null;
      questInventory: Array<UserItem | null> | null;
      gold: number;
      credit: number;
      hasExpandedStorage: boolean;
      hasStoragePassword: boolean;
      requireStoragePassword: boolean;
      storagePasswordLastSet: bigint;
      expandedStorageExpiryTime: bigint;
      magics: ClientMagic[];
      summonedCreatureType: number;
      creatureSummoned: boolean;
      allowObserve: boolean;
      observer: boolean;
    }
  | { kind: "UserSlotsRefresh"; inventory: Array<UserItem | null> | null; equipment: Array<UserItem | null> | null }
  | { kind: "UserLocation"; location: Point; direction: number }
  | {
      kind: "ObjectPlayer";
      objectId: number;
      name: string;
      guildName: string;
      guildRankName: string;
      nameColour: number;
      classId: number;
      gender: number;
      level: number;
      location: Point;
      direction: number;
      hair: number;
      light: number;
      weapon: number;
      weaponEffect: number;
      armour: number;
      poison: number;
      dead: boolean;
      hidden: boolean;
      effect: number;
      wingEffect: number;
      extra: boolean;
      mountType: number;
      ridingMount: boolean;
      fishing: boolean;
      transformType: number;
      elementOrbEffect: number;
      elementOrbLevel: number;
      elementOrbMax: number;
      buffs: number[];
      levelEffects: number;
      hero: boolean;
      ownerName?: string;
    }
  | { kind: "ObjectRemove"; objectId: number }
  | { kind: "ObjectMove"; movement: "turn" | "walk" | "run"; objectId: number; location: Point; direction: number }
  | { kind: "Chat"; message: string; chatType: number }
  | { kind: "ObjectChat"; objectId: number; text: string; chatType: number }
  | { kind: "MoveItem"; grid: number; from: number; to: number; success: boolean }
  | { kind: "NewItemInfo"; info: ClientItemInfo }
  | {
      kind: "ObjectItem";
      objectId: number;
      name: string;
      nameColour: number;
      location: Point;
      image: number;
      grade: number;
    }
  | { kind: "ObjectGold"; objectId: number; gold: number; location: Point }
  | {
      kind: "ObjectMonster";
      objectId: number;
      name: string;
      nameColour: number;
      location: Point;
      image: number;
      direction: number;
      effect: number;
      ai: number;
      light: number;
      dead: boolean;
      skeleton: boolean;
      poison: number;
      hidden: boolean;
      shockTime: bigint;
      bindingShotCenter: boolean;
      extra: boolean;
      extraByte: number;
      masterObjectId: number;
      rarity: number;
      buffs: number[];
    }
  | {
      kind: "ObjectNpc";
      objectId: number;
      name: string;
      nameColour: number;
      image: number;
      colour: number;
      location: Point;
      direction: number;
      questIds: number[];
    }
  | { kind: "NPCResponse"; pages: string[] }
  | { kind: "NPCGoods"; items: UserItem[]; rate: number; panelType: number; hideAddedStats: boolean }
  | { kind: "GainedItem"; item: UserItem }
  | { kind: "GainedGold"; gold: number }
  | { kind: "LoseGold"; gold: number }
  | { kind: "DeleteItem"; uniqueId: bigint; count: number }
  | { kind: "DuraChanged"; uniqueId: bigint; currentDurability: number }
  | { kind: "HealthChanged"; hp: number; mp: number }
  | { kind: "DamageIndicator"; damage: number; damageType: number; objectId: number }
  | { kind: "Struck"; attackerId: number }
  | { kind: "ObjectStruck"; objectId: number; attackerId: number; location: Point; direction: number }
  | { kind: "Death"; location: Point; direction: number }
  | { kind: "ObjectDied"; objectId: number; location: Point; direction: number; dieType: number }
  | { kind: "Revived" }
  | { kind: "ObjectRevived"; objectId: number; effect: boolean }
  | { kind: "MapChanged"; mapIndex: number; fileName: string; location: Point; direction: number; mapDarkLight: number; music: number; weather: number }
  | { kind: "ObjectHide"; objectId: number }
  | { kind: "ObjectShow"; objectId: number }
  | { kind: "ObjectTeleportOut"; objectId: number; teleportType: number }
  | { kind: "ObjectTeleportIn"; objectId: number; teleportType: number }
  | { kind: "TeleportIn" }
  | { kind: "ObjectHealth"; objectId: number; percent: number; expire: number }
  | { kind: "ObjectMana"; objectId: number; percent: number }
  | { kind: "ObjectLeveled"; objectId: number }
  | { kind: "LevelChanged"; level: number; experience: bigint; maxExperience: bigint }
  | { kind: "GainExperience"; amount: number }
  | { kind: "TimeOfDay"; lights: number }
  | { kind: "LogOutSuccess"; characters: SelectInfo[] }
  | { kind: "LogOutFailed" }
  | { kind: "ReturnToLogin" }
  | UnknownPacket;

export type ClientPacket =
  | { kind: "ClientVersion"; versionHash: Uint8Array }
  | { kind: "KeepAlive"; time: bigint }
  | { kind: "Login"; accountId: string; password: string }
  | { kind: "NewCharacter"; name: string; gender: number; classId: number }
  | { kind: "DeleteCharacter"; characterIndex: number }
  | { kind: "StartGame"; characterIndex: number }
  | { kind: "LogOut" }
  | { kind: "Turn"; direction: number }
  | { kind: "Walk"; direction: number }
  | { kind: "Run"; direction: number }
  | { kind: "Chat"; message: string }
  | { kind: "MoveItem"; grid: number; from: number; to: number }
  | { kind: "Attack"; direction: number; spell: number }
  | { kind: "PickUp" }
  | { kind: "RequestItemInfo"; itemIndex: number }
  | { kind: "CallNPC"; objectId: number; key: string }
  | { kind: "BuyItem"; itemIndex: bigint; count: number; panelType: number }
  | { kind: "SellItem"; uniqueId: bigint; count: number }
  | { kind: "UseItem"; uniqueId: bigint; grid: number }
  | { kind: "EquipItem"; grid: number; uniqueId: bigint; to: number }
  | { kind: "RemoveItem"; grid: number; uniqueId: bigint; to: number }
  | { kind: "DropItem"; uniqueId: bigint; count: number }
  | { kind: "TownRevive" }
  | { kind: "Raw"; id: number; body: Uint8Array };
