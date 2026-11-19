import { BinaryReader, BinaryWriter } from "./binary";
import { encodeFrame, MAX_PACKET_BYTES, type RawPacketFrame } from "./frame";
import { ClientPacketId, ServerPacketId } from "./ids";
import type {
  AwakeData,
  ClientItemInfo,
  ClientMagic,
  ClientMovementInfo,
  ClientNpcInfo,
  ClientPacket,
  Point,
  SelectInfo,
  ServerPacket,
  StatValue,
  UserItem
} from "./types";
import { ProtocolError } from "./ProtocolError";

const COMPRESSED_SERVER_PACKETS = new Set<number>([ServerPacketId.NPCGoods]);

function point(reader: BinaryReader): Point {
  return { x: reader.readInt32(), y: reader.readInt32() };
}

function writePoint(writer: BinaryWriter, value: Point): void {
  writer.writeInt32(value.x);
  writer.writeInt32(value.y);
}

function readSelectInfo(reader: BinaryReader): SelectInfo {
  return {
    index: reader.readInt32(),
    name: reader.readString(),
    level: reader.readUint16(),
    classId: reader.readUint8(),
    gender: reader.readUint8(),
    lastAccess: reader.readInt64()
  };
}

function writeSelectInfo(writer: BinaryWriter, value: SelectInfo): void {
  writer.writeInt32(value.index);
  writer.writeString(value.name);
  writer.writeUint16(value.level);
  writer.writeUint8(value.classId);
  writer.writeUint8(value.gender);
  writer.writeInt64(value.lastAccess);
}

function readStatValues(reader: BinaryReader): StatValue[] {
  const count = reader.readCount("stat", 4096);
  const values: StatValue[] = [];
  for (let index = 0; index < count; index++) {
    values.push({ stat: reader.readUint8(), value: reader.readInt32() });
  }
  return values;
}

function writeStatValues(writer: BinaryWriter, values: StatValue[]): void {
  writer.writeInt32(values.length);
  for (const value of values) {
    writer.writeUint8(value.stat);
    writer.writeInt32(value.value);
  }
}

function readAwake(reader: BinaryReader): AwakeData {
  const type = reader.readUint8();
  const count = reader.readCount("awake value", 16);
  const values: number[] = [];
  for (let index = 0; index < count; index++) values.push(reader.readUint8());
  return { type, values };
}

function writeAwake(writer: BinaryWriter, value: AwakeData): void {
  writer.writeUint8(value.type);
  writer.writeInt32(value.values.length);
  for (const item of value.values) writer.writeUint8(item);
}

function readItemInfo(reader: BinaryReader): ClientItemInfo {
  const index = reader.readInt32();
  const name = reader.readString();
  const type = reader.readUint8();
  const grade = reader.readUint8();
  const requiredType = reader.readUint8();
  const requiredClass = reader.readUint8();
  const requiredGender = reader.readUint8();
  const set = reader.readUint8();
  const shape = reader.readInt16();
  const weight = reader.readUint8();
  const light = reader.readUint8();
  const requiredAmount = reader.readUint8();
  const image = reader.readUint16();
  const durability = reader.readUint16();
  const stackSize = reader.readUint16();
  const price = reader.readUint32();
  const startItem = reader.readBoolean();
  const effect = reader.readUint8();
  const flags = reader.readUint8();
  const bind = reader.readInt16();
  const unique = reader.readInt16();
  const randomStatsId = reader.readUint8();
  const canFastRun = reader.readBoolean();
  const canAwakening = reader.readBoolean();
  const slots = reader.readUint8();
  return {
    index,
    name,
    type,
    grade,
    requiredType,
    requiredClass,
    requiredGender,
    set,
    shape,
    weight,
    light,
    requiredAmount,
    image,
    durability,
    stackSize,
    price,
    startItem,
    effect,
    flags,
    bind,
    unique,
    randomStatsId,
    canFastRun,
    canAwakening,
    slots,
    tail: reader.readBytes(reader.remaining).slice()
  };
}

function writeItemInfo(writer: BinaryWriter, value: ClientItemInfo): void {
  writer.writeInt32(value.index);
  writer.writeString(value.name);
  writer.writeUint8(value.type);
  writer.writeUint8(value.grade);
  writer.writeUint8(value.requiredType);
  writer.writeUint8(value.requiredClass);
  writer.writeUint8(value.requiredGender);
  writer.writeUint8(value.set);
  writer.writeInt16(value.shape);
  writer.writeUint8(value.weight);
  writer.writeUint8(value.light);
  writer.writeUint8(value.requiredAmount);
  writer.writeUint16(value.image);
  writer.writeUint16(value.durability);
  writer.writeUint16(value.stackSize);
  writer.writeUint32(value.price);
  writer.writeBoolean(value.startItem);
  writer.writeUint8(value.effect);
  writer.writeUint8(value.flags);
  writer.writeInt16(value.bind);
  writer.writeInt16(value.unique);
  writer.writeUint8(value.randomStatsId);
  writer.writeBoolean(value.canFastRun);
  writer.writeBoolean(value.canAwakening);
  writer.writeUint8(value.slots);
  writer.writeBytes(value.tail);
}

export function readUserItem(reader: BinaryReader, depth = 0): UserItem {
  if (depth > 8) throw new ProtocolError("UserItem nesting is too deep");
  const uniqueId = reader.readUint64();
  const itemIndex = reader.readInt32();
  const currentDurability = reader.readUint16();
  const maxDurability = reader.readUint16();
  const count = reader.readUint16();
  const soulBoundId = reader.readInt32();
  const flags = reader.readUint8();

  const slotCount = reader.readCount("item slot", 64);
  const slots: Array<UserItem | null> = new Array(slotCount);
  for (let index = 0; index < slotCount; index++) {
    slots[index] = reader.readBoolean() ? null : readUserItem(reader, depth + 1);
  }

  const gemCount = reader.readUint16();
  const addedStats = readStatValues(reader);
  const awake = readAwake(reader);
  const refinedValue = reader.readUint8();
  const refineAdded = reader.readUint8();
  const refineSuccessChance = reader.readInt32();
  const weddingRing = reader.readInt32();

  const item: UserItem = {
    uniqueId,
    itemIndex,
    currentDurability,
    maxDurability,
    count,
    soulBoundId,
    identified: (flags & 0x01) !== 0,
    cursed: (flags & 0x02) !== 0,
    slots,
    gemCount,
    addedStats,
    awake,
    refinedValue,
    refineAdded,
    refineSuccessChance,
    weddingRing,
    isShopItem: false,
    gmMade: false
  };

  if (reader.readBoolean()) item.expireBinary = reader.readInt64();
  if (reader.readBoolean()) {
    item.rental = {
      ownerName: reader.readString(),
      bindingFlags: reader.readInt16(),
      expiryBinary: reader.readInt64(),
      locked: reader.readBoolean()
    };
  }
  item.isShopItem = reader.readBoolean();
  if (reader.readBoolean()) {
    item.sealed = { expiryBinary: reader.readInt64(), nextSealBinary: reader.readInt64() };
  }
  item.gmMade = reader.readBoolean();
  return item;
}

export function writeUserItem(writer: BinaryWriter, item: UserItem): void {
  writer.writeUint64(item.uniqueId);
  writer.writeInt32(item.itemIndex);
  writer.writeUint16(item.currentDurability);
  writer.writeUint16(item.maxDurability);
  writer.writeUint16(item.count);
  writer.writeInt32(item.soulBoundId);
  writer.writeUint8((item.identified ? 0x01 : 0) | (item.cursed ? 0x02 : 0));
  writer.writeInt32(item.slots.length);
  for (const slot of item.slots) {
    writer.writeBoolean(slot === null);
    if (slot) writeUserItem(writer, slot);
  }
  writer.writeUint16(item.gemCount);
  writeStatValues(writer, item.addedStats);
  writeAwake(writer, item.awake);
  writer.writeUint8(item.refinedValue);
  writer.writeUint8(item.refineAdded);
  writer.writeInt32(item.refineSuccessChance);
  writer.writeInt32(item.weddingRing);
  writer.writeBoolean(item.expireBinary !== undefined);
  if (item.expireBinary !== undefined) writer.writeInt64(item.expireBinary);
  writer.writeBoolean(item.rental !== undefined);
  if (item.rental) {
    writer.writeString(item.rental.ownerName);
    writer.writeInt16(item.rental.bindingFlags);
    writer.writeInt64(item.rental.expiryBinary);
    writer.writeBoolean(item.rental.locked);
  }
  writer.writeBoolean(item.isShopItem);
  writer.writeBoolean(item.sealed !== undefined);
  if (item.sealed) {
    writer.writeInt64(item.sealed.expiryBinary);
    writer.writeInt64(item.sealed.nextSealBinary);
  }
  writer.writeBoolean(item.gmMade);
}

function readOptionalItems(reader: BinaryReader): Array<UserItem | null> | null {
  if (!reader.readBoolean()) return null;
  const count = reader.readCount("inventory slot", 512);
  const items: Array<UserItem | null> = new Array(count);
  for (let index = 0; index < count; index++) {
    items[index] = reader.readBoolean() ? readUserItem(reader) : null;
  }
  return items;
}

function writeOptionalItems(writer: BinaryWriter, items: Array<UserItem | null> | null): void {
  writer.writeBoolean(items !== null);
  if (!items) return;
  writer.writeInt32(items.length);
  for (const item of items) {
    writer.writeBoolean(item !== null);
    if (item) writeUserItem(writer, item);
  }
}

function readClientMagic(reader: BinaryReader): ClientMagic {
  const name = reader.readString();
  const spell = reader.readUint8();
  const baseCost = reader.readUint8();
  const levelCost = reader.readUint8();
  const icon = reader.readUint8();
  const levels: [number, number, number] = [reader.readUint8(), reader.readUint8(), reader.readUint8()];
  const needs: [number, number, number] = [reader.readUint16(), reader.readUint16(), reader.readUint16()];
  const level = reader.readUint8();
  const key = reader.readUint8();
  const experience = reader.readUint16();
  const delay = reader.readInt64();
  const range = reader.readUint8();
  const castTime = reader.readInt64();
  return { name, spell, baseCost, levelCost, icon, levels, needs, level, key, range, experience, delay, castTime };
}

function writeClientMagic(writer: BinaryWriter, magic: ClientMagic): void {
  writer.writeString(magic.name);
  writer.writeUint8(magic.spell);
  writer.writeUint8(magic.baseCost);
  writer.writeUint8(magic.levelCost);
  writer.writeUint8(magic.icon);
  for (const level of magic.levels) writer.writeUint8(level);
  for (const need of magic.needs) writer.writeUint16(need);
  writer.writeUint8(magic.level);
  writer.writeUint8(magic.key);
  writer.writeUint16(magic.experience);
  writer.writeInt64(magic.delay);
  writer.writeUint8(magic.range);
  writer.writeInt64(magic.castTime);
}

function skipIntelligentCreature(reader: BinaryReader): void {
  reader.readUint8();
  reader.readInt32();
  reader.readString();
  reader.readInt32();
  reader.readInt32();
  reader.readInt64();
  reader.readInt64();
  reader.readUint8();

  reader.readInt32();
  reader.readBoolean();
  reader.readInt32();
  reader.readBoolean();
  reader.readInt32();
  reader.readBoolean();
  reader.readInt32();
  reader.readBoolean();

  for (let index = 0; index < 9; index++) reader.readBoolean();
  reader.readUint8();
  reader.readInt64();
}

function readMovementInfo(reader: BinaryReader): ClientMovementInfo {
  return { destination: reader.readInt32(), title: reader.readString(), location: point(reader), icon: reader.readInt32() };
}

function writeMovementInfo(writer: BinaryWriter, value: ClientMovementInfo): void {
  writer.writeInt32(value.destination);
  writer.writeString(value.title);
  writePoint(writer, value.location);
  writer.writeInt32(value.icon);
}

function readNpcInfo(reader: BinaryReader): ClientNpcInfo {
  const index = reader.readInt32();
  const fileName = reader.readString();
  const name = reader.readString();
  const mapIndex = reader.readInt32();
  const location = point(reader);
  const image = reader.readUint16();
  const rate = reader.readUint16();
  const showOnBigMap = reader.readBoolean();
  const bigMapIcon = reader.readInt32();
  const objectId = reader.readUint32();
  const icon = reader.readInt32();
  const canTeleportTo = reader.readBoolean();
  return {
    index,
    fileName,
    name,
    mapIndex,
    location,
    image,
    rate,
    showOnBigMap,
    bigMapIcon,
    objectId,
    icon,
    canTeleportTo
  };
}

function writeNpcInfo(writer: BinaryWriter, value: ClientNpcInfo): void {
  writer.writeInt32(value.index);
  writer.writeString(value.fileName);
  writer.writeString(value.name);
  writer.writeInt32(value.mapIndex);
  writePoint(writer, value.location);
  writer.writeUint16(value.image);
  writer.writeUint16(value.rate);
  writer.writeBoolean(value.showOnBigMap);
  writer.writeInt32(value.bigMapIcon);
  writer.writeUint32(value.objectId);
  writer.writeInt32(value.icon);
  writer.writeBoolean(value.canTeleportTo);
}

function readMapInfo(reader: BinaryReader): Extract<ServerPacket, { kind: "NewMapInfo" }>["info"] {
  const title = reader.readString();
  const width = reader.readInt32();
  const height = reader.readInt32();
  const bigMap = reader.readInt32();
  const movementCount = reader.readCount("map movement", 100_000);
  const movements: ClientMovementInfo[] = [];
  for (let index = 0; index < movementCount; index++) movements.push(readMovementInfo(reader));
  const npcCount = reader.readCount("map npc", 100_000);
  const npcs: ClientNpcInfo[] = [];
  for (let index = 0; index < npcCount; index++) npcs.push(readNpcInfo(reader));
  return { title, width, height, bigMap, movements, npcs };
}

function writeMapInfo(writer: BinaryWriter, info: Extract<ServerPacket, { kind: "NewMapInfo" }>["info"]): void {
  writer.writeString(info.title);
  writer.writeInt32(info.width);
  writer.writeInt32(info.height);
  writer.writeInt32(info.bigMap);
  writer.writeInt32(info.movements.length);
  for (const movement of info.movements) writeMovementInfo(writer, movement);
  writer.writeInt32(info.npcs.length);
  for (const npc of info.npcs) writeNpcInfo(writer, npc);
}

function readObjectPlayer(reader: BinaryReader): Omit<Extract<ServerPacket, { kind: "ObjectPlayer" }>, "kind" | "hero" | "ownerName"> {
  const objectId = reader.readUint32();
  const name = reader.readString();
  const guildName = reader.readString();
  const guildRankName = reader.readString();
  const nameColour = reader.readUint32();
  const classId = reader.readUint8();
  const gender = reader.readUint8();
  const level = reader.readUint16();
  const location = point(reader);
  const direction = reader.readUint8();
  const hair = reader.readUint8();
  const light = reader.readUint8();
  const weapon = reader.readInt16();
  const weaponEffect = reader.readInt16();
  const armour = reader.readInt16();
  const poison = reader.readUint16();
  const dead = reader.readBoolean();
  const hidden = reader.readBoolean();
  const effect = reader.readUint8();
  const wingEffect = reader.readUint8();
  const extra = reader.readBoolean();
  const mountType = reader.readInt16();
  const ridingMount = reader.readBoolean();
  const fishing = reader.readBoolean();
  const transformType = reader.readInt16();
  const elementOrbEffect = reader.readUint32();
  const elementOrbLevel = reader.readUint32();
  const elementOrbMax = reader.readUint32();
  const buffCount = reader.readCount("buff", 256);
  const buffs: number[] = [];
  for (let index = 0; index < buffCount; index++) buffs.push(reader.readUint8());
  const levelEffects = reader.readUint16();
  return {
    objectId,
    name,
    guildName,
    guildRankName,
    nameColour,
    classId,
    gender,
    level,
    location,
    direction,
    hair,
    light,
    weapon,
    weaponEffect,
    armour,
    poison,
    dead,
    hidden,
    effect,
    wingEffect,
    extra,
    mountType,
    ridingMount,
    fishing,
    transformType,
    elementOrbEffect,
    elementOrbLevel,
    elementOrbMax,
    buffs,
    levelEffects
  };
}

function writeObjectPlayer(writer: BinaryWriter, value: Extract<ServerPacket, { kind: "ObjectPlayer" }>): void {
  writer.writeUint32(value.objectId);
  writer.writeString(value.name);
  writer.writeString(value.guildName);
  writer.writeString(value.guildRankName);
  writer.writeUint32(value.nameColour);
  writer.writeUint8(value.classId);
  writer.writeUint8(value.gender);
  writer.writeUint16(value.level);
  writePoint(writer, value.location);
  writer.writeUint8(value.direction);
  writer.writeUint8(value.hair);
  writer.writeUint8(value.light);
  writer.writeInt16(value.weapon);
  writer.writeInt16(value.weaponEffect);
  writer.writeInt16(value.armour);
  writer.writeUint16(value.poison);
  writer.writeBoolean(value.dead);
  writer.writeBoolean(value.hidden);
  writer.writeUint8(value.effect);
  writer.writeUint8(value.wingEffect);
  writer.writeBoolean(value.extra);
  writer.writeInt16(value.mountType);
  writer.writeBoolean(value.ridingMount);
  writer.writeBoolean(value.fishing);
  writer.writeInt16(value.transformType);
  writer.writeUint32(value.elementOrbEffect);
  writer.writeUint32(value.elementOrbLevel);
  writer.writeUint32(value.elementOrbMax);
  writer.writeInt32(value.buffs.length);
  for (const buff of value.buffs) writer.writeUint8(buff);
  writer.writeUint16(value.levelEffects);
  if (value.hero) writer.writeString(value.ownerName ?? "");
}

export function decodeClientBody(id: number, body: Uint8Array): ClientPacket {
  const reader = new BinaryReader(body);
  let packet: ClientPacket;
  switch (id) {
    case ClientPacketId.ClientVersion: {
      const length = reader.readCount("version hash", 1024);
      packet = { kind: "ClientVersion", versionHash: reader.readBytes(length).slice() };
      break;
    }
    case ClientPacketId.KeepAlive:
      packet = { kind: "KeepAlive", time: reader.readInt64() };
      break;
    case ClientPacketId.Login:
      packet = { kind: "Login", accountId: reader.readString(), password: reader.readString() };
      break;
    case ClientPacketId.NewCharacter:
      packet = { kind: "NewCharacter", name: reader.readString(), gender: reader.readUint8(), classId: reader.readUint8() };
      break;
    case ClientPacketId.DeleteCharacter:
      packet = { kind: "DeleteCharacter", characterIndex: reader.readInt32() };
      break;
    case ClientPacketId.StartGame:
      packet = { kind: "StartGame", characterIndex: reader.readInt32() };
      break;
    case ClientPacketId.LogOut:
      packet = { kind: "LogOut" };
      break;
    case ClientPacketId.Turn:
      packet = { kind: "Turn", direction: reader.readUint8() };
      break;
    case ClientPacketId.Walk:
      packet = { kind: "Walk", direction: reader.readUint8() };
      break;
    case ClientPacketId.Run:
      packet = { kind: "Run", direction: reader.readUint8() };
      break;
    case ClientPacketId.Chat: {
      const message = reader.readString();
      const linkedCount = reader.readCount("linked chat item", 64);
      for (let index = 0; index < linkedCount; index++) {
        reader.readUint64();
        reader.readString();
        reader.readUint8();
      }
      packet = { kind: "Chat", message };
      break;
    }
    case ClientPacketId.MoveItem:
      packet = { kind: "MoveItem", grid: reader.readUint8(), from: reader.readInt32(), to: reader.readInt32() };
      break;
    case ClientPacketId.Attack:
      packet = { kind: "Attack", direction: reader.readUint8(), spell: reader.readUint8() };
      break;
    case ClientPacketId.PickUp:
      packet = { kind: "PickUp" };
      break;
    case ClientPacketId.RequestItemInfo:
      packet = { kind: "RequestItemInfo", itemIndex: reader.readInt32() };
      break;
    case ClientPacketId.CallNPC:
      packet = { kind: "CallNPC", objectId: reader.readUint32(), key: reader.readString() };
      break;
    case ClientPacketId.BuyItem:
      packet = { kind: "BuyItem", itemIndex: reader.readUint64(), count: reader.readUint16(), panelType: reader.readUint8() };
      break;
    case ClientPacketId.SellItem:
      packet = { kind: "SellItem", uniqueId: reader.readUint64(), count: reader.readUint16() };
      break;
    case ClientPacketId.UseItem:
      packet = { kind: "UseItem", uniqueId: reader.readUint64(), grid: reader.readUint8() };
      break;
    case ClientPacketId.EquipItem:
      packet = { kind: "EquipItem", grid: reader.readUint8(), uniqueId: reader.readUint64(), to: reader.readInt32() };
      break;
    case ClientPacketId.RemoveItem:
      packet = { kind: "RemoveItem", grid: reader.readUint8(), uniqueId: reader.readUint64(), to: reader.readInt32() };
      break;
    case ClientPacketId.DropItem:
      packet = { kind: "DropItem", uniqueId: reader.readUint64(), count: reader.readUint16() };
      reader.readBoolean();
      break;
    case ClientPacketId.TownRevive:
      packet = { kind: "TownRevive" };
      break;
    default:
      return { kind: "Raw", id, body: body.slice() };
  }
  reader.expectEnd();
  return packet;
}

export function encodeClientBody(packet: ClientPacket): { id: number; body: Uint8Array } {
  const writer = new BinaryWriter();
  let id: number;
  switch (packet.kind) {
    case "ClientVersion":
      id = ClientPacketId.ClientVersion;
      writer.writeInt32(packet.versionHash.length);
      writer.writeBytes(packet.versionHash);
      break;
    case "KeepAlive":
      id = ClientPacketId.KeepAlive;
      writer.writeInt64(packet.time);
      break;
    case "Login":
      id = ClientPacketId.Login;
      writer.writeString(packet.accountId);
      writer.writeString(packet.password);
      break;
    case "NewCharacter":
      id = ClientPacketId.NewCharacter;
      writer.writeString(packet.name);
      writer.writeUint8(packet.gender);
      writer.writeUint8(packet.classId);
      break;
    case "DeleteCharacter":
      id = ClientPacketId.DeleteCharacter;
      writer.writeInt32(packet.characterIndex);
      break;
    case "StartGame":
      id = ClientPacketId.StartGame;
      writer.writeInt32(packet.characterIndex);
      break;
    case "LogOut":
      id = ClientPacketId.LogOut;
      break;
    case "Turn":
      id = ClientPacketId.Turn;
      writer.writeUint8(packet.direction);
      break;
    case "Walk":
      id = ClientPacketId.Walk;
      writer.writeUint8(packet.direction);
      break;
    case "Run":
      id = ClientPacketId.Run;
      writer.writeUint8(packet.direction);
      break;
    case "Chat":
      id = ClientPacketId.Chat;
      writer.writeString(packet.message);
      writer.writeInt32(0);
      break;
    case "MoveItem":
      id = ClientPacketId.MoveItem;
      writer.writeUint8(packet.grid);
      writer.writeInt32(packet.from);
      writer.writeInt32(packet.to);
      break;
    case "Attack":
      id = ClientPacketId.Attack;
      writer.writeUint8(packet.direction);
      writer.writeUint8(packet.spell);
      break;
    case "PickUp":
      id = ClientPacketId.PickUp;
      break;
    case "RequestItemInfo":
      id = ClientPacketId.RequestItemInfo;
      writer.writeInt32(packet.itemIndex);
      break;
    case "CallNPC":
      id = ClientPacketId.CallNPC;
      writer.writeUint32(packet.objectId);
      writer.writeString(packet.key);
      break;
    case "BuyItem":
      id = ClientPacketId.BuyItem;
      writer.writeUint64(packet.itemIndex);
      writer.writeUint16(packet.count);
      writer.writeUint8(packet.panelType);
      break;
    case "SellItem":
      id = ClientPacketId.SellItem;
      writer.writeUint64(packet.uniqueId);
      writer.writeUint16(packet.count);
      break;
    case "UseItem":
      id = ClientPacketId.UseItem;
      writer.writeUint64(packet.uniqueId);
      writer.writeUint8(packet.grid);
      break;
    case "EquipItem":
      id = ClientPacketId.EquipItem;
      writer.writeUint8(packet.grid);
      writer.writeUint64(packet.uniqueId);
      writer.writeInt32(packet.to);
      break;
    case "RemoveItem":
      id = ClientPacketId.RemoveItem;
      writer.writeUint8(packet.grid);
      writer.writeUint64(packet.uniqueId);
      writer.writeInt32(packet.to);
      break;
    case "DropItem":
      id = ClientPacketId.DropItem;
      writer.writeUint64(packet.uniqueId);
      writer.writeUint16(packet.count);
      writer.writeBoolean(false);
      break;
    case "TownRevive":
      id = ClientPacketId.TownRevive;
      break;
    case "Raw":
      return { id: packet.id, body: packet.body };
  }
  return { id, body: writer.toUint8Array() };
}

export function encodeClientFrame(packet: ClientPacket): Uint8Array {
  const encoded = encodeClientBody(packet);
  return encodeFrame(encoded.id, encoded.body);
}

function decodeServerBodyUncompressed(id: number, body: Uint8Array): ServerPacket {
  const reader = new BinaryReader(body);
  let packet: ServerPacket;
  switch (id) {
    case ServerPacketId.Connected:
      packet = { kind: "Connected" };
      break;
    case ServerPacketId.ClientVersion:
      packet = { kind: "ClientVersion", result: reader.readUint8() };
      break;
    case ServerPacketId.Disconnect:
      packet = { kind: "Disconnect", reason: reader.readUint8() };
      break;
    case ServerPacketId.KeepAlive:
      packet = { kind: "KeepAlive", time: reader.readInt64() };
      break;
    case ServerPacketId.Login:
      packet = { kind: "Login", result: reader.readUint8() };
      break;
    case ServerPacketId.LoginBanned:
      packet = { kind: "LoginBanned", reason: reader.readString(), expiryBinary: reader.readInt64() };
      break;
    case ServerPacketId.LoginSuccess: {
      const count = reader.readCount("character", 16);
      const characters: SelectInfo[] = [];
      for (let index = 0; index < count; index++) characters.push(readSelectInfo(reader));
      packet = { kind: "LoginSuccess", characters };
      break;
    }
    case ServerPacketId.NewCharacter:
      packet = { kind: "NewCharacter", result: reader.readUint8() };
      break;
    case ServerPacketId.NewCharacterSuccess:
      packet = { kind: "NewCharacterSuccess", character: readSelectInfo(reader) };
      break;
    case ServerPacketId.DeleteCharacter:
      packet = { kind: "DeleteCharacter", result: reader.readUint8() };
      break;
    case ServerPacketId.DeleteCharacterSuccess:
      packet = { kind: "DeleteCharacterSuccess", characterIndex: reader.readInt32() };
      break;
    case ServerPacketId.StartGame:
      packet = { kind: "StartGame", result: reader.readUint8(), resolution: reader.readInt32() };
      break;
    case ServerPacketId.StartGameBanned:
      packet = { kind: "StartGameBanned", reason: reader.readString(), expiryBinary: reader.readInt64() };
      break;
    case ServerPacketId.StartGameDelay:
      packet = { kind: "StartGameDelay", milliseconds: reader.readInt64() };
      break;
    case ServerPacketId.MapInformation: {
      const mapIndex = reader.readInt32();
      const fileName = reader.readString();
      const title = reader.readString();
      const miniMap = reader.readUint16();
      const bigMap = reader.readUint16();
      const lights = reader.readUint8();
      const flags = reader.readUint8();
      const mapDarkLight = reader.readUint8();
      const music = reader.readUint16();
      const weatherParticles = reader.readUint16();
      packet = {
        kind: "MapInformation",
        mapIndex,
        fileName,
        title,
        miniMap,
        bigMap,
        lights,
        lightning: (flags & 1) !== 0,
        fire: (flags & 2) !== 0,
        mapDarkLight,
        music,
        weatherParticles
      };
      break;
    }
    case ServerPacketId.NewMapInfo:
      packet = { kind: "NewMapInfo", mapIndex: reader.readInt32(), info: readMapInfo(reader) };
      break;
    case ServerPacketId.UserInformation: {
      const objectId = reader.readUint32();
      const realId = reader.readUint32();
      const name = reader.readString();
      const guildName = reader.readString();
      const guildRank = reader.readString();
      const nameColour = reader.readUint32();
      const classId = reader.readUint8();
      const gender = reader.readUint8();
      const level = reader.readUint16();
      const location = point(reader);
      const direction = reader.readUint8();
      const hair = reader.readUint8();
      const hp = reader.readInt32();
      const mp = reader.readInt32();
      const experience = reader.readInt64();
      const maxExperience = reader.readInt64();
      const levelEffects = reader.readUint16();
      const hasHero = reader.readBoolean();
      const heroBehaviour = reader.readUint8();
      const inventory = readOptionalItems(reader);
      const equipment = readOptionalItems(reader);
      const questInventory = readOptionalItems(reader);
      const gold = reader.readUint32();
      const credit = reader.readUint32();
      const hasExpandedStorage = reader.readBoolean();
      const hasStoragePassword = reader.readBoolean();
      const requireStoragePassword = reader.readBoolean();
      const storagePasswordLastSet = reader.readInt64();
      const expandedStorageExpiryTime = reader.readInt64();
      const magicCount = reader.readCount("magic", 1024);
      const magics: ClientMagic[] = [];
      for (let index = 0; index < magicCount; index++) magics.push(readClientMagic(reader));
      const creatureCount = reader.readCount("intelligent creature", 256);
      for (let index = 0; index < creatureCount; index++) skipIntelligentCreature(reader);
      const summonedCreatureType = reader.readUint8();
      const creatureSummoned = reader.readBoolean();
      const allowObserve = reader.readBoolean();
      const observer = reader.readBoolean();
      packet = {
        kind: "UserInformation",
        objectId,
        realId,
        name,
        guildName,
        guildRank,
        nameColour,
        classId,
        gender,
        level,
        location,
        direction,
        hair,
        hp,
        mp,
        experience,
        maxExperience,
        levelEffects,
        hasHero,
        heroBehaviour,
        inventory,
        equipment,
        questInventory,
        gold,
        credit,
        hasExpandedStorage,
        hasStoragePassword,
        requireStoragePassword,
        storagePasswordLastSet,
        expandedStorageExpiryTime,
        magics,
        summonedCreatureType,
        creatureSummoned,
        allowObserve,
        observer
      };
      break;
    }
    case ServerPacketId.UserSlotsRefresh: {
      const inventory = readOptionalItems(reader);
      const equipment = readOptionalItems(reader);
      packet = { kind: "UserSlotsRefresh", inventory, equipment };
      break;
    }
    case ServerPacketId.UserLocation:
      packet = { kind: "UserLocation", location: point(reader), direction: reader.readUint8() };
      break;
    case ServerPacketId.ObjectPlayer: {
      const value = readObjectPlayer(reader);
      packet = { kind: "ObjectPlayer", ...value, hero: false };
      break;
    }
    case ServerPacketId.ObjectHero: {
      const value = readObjectPlayer(reader);
      packet = { kind: "ObjectPlayer", ...value, hero: true, ownerName: reader.readString() };
      break;
    }
    case ServerPacketId.ObjectRemove:
      packet = { kind: "ObjectRemove", objectId: reader.readUint32() };
      break;
    case ServerPacketId.ObjectTurn:
      packet = { kind: "ObjectMove", movement: "turn", objectId: reader.readUint32(), location: point(reader), direction: reader.readUint8() };
      break;
    case ServerPacketId.ObjectWalk:
      packet = { kind: "ObjectMove", movement: "walk", objectId: reader.readUint32(), location: point(reader), direction: reader.readUint8() };
      break;
    case ServerPacketId.ObjectRun:
      packet = { kind: "ObjectMove", movement: "run", objectId: reader.readUint32(), location: point(reader), direction: reader.readUint8() };
      break;
    case ServerPacketId.Chat:
      packet = { kind: "Chat", message: reader.readString(), chatType: reader.readUint8() };
      break;
    case ServerPacketId.ObjectChat:
      packet = { kind: "ObjectChat", objectId: reader.readUint32(), text: reader.readString(), chatType: reader.readUint8() };
      break;
    case ServerPacketId.MoveItem:
      packet = { kind: "MoveItem", grid: reader.readUint8(), from: reader.readInt32(), to: reader.readInt32(), success: reader.readBoolean() };
      break;
    case ServerPacketId.NewItemInfo:
      packet = { kind: "NewItemInfo", info: readItemInfo(reader) };
      break;
    case ServerPacketId.ObjectItem:
      packet = {
        kind: "ObjectItem",
        objectId: reader.readUint32(),
        name: reader.readString(),
        nameColour: reader.readUint32(),
        location: point(reader),
        image: reader.readUint16(),
        grade: reader.readUint8()
      };
      break;
    case ServerPacketId.ObjectGold:
      packet = { kind: "ObjectGold", objectId: reader.readUint32(), gold: reader.readUint32(), location: point(reader) };
      break;
    case ServerPacketId.ObjectMonster:
      packet = {
        kind: "ObjectMonster",
        objectId: reader.readUint32(),
        name: reader.readString(),
        nameColour: reader.readUint32(),
        location: point(reader),
        image: reader.readUint16(),
        direction: reader.readUint8(),
        effect: reader.readUint8(),
        ai: reader.readUint8(),
        light: reader.readUint8(),
        dead: reader.readBoolean(),
        skeleton: reader.readBoolean(),
        poison: reader.readUint16(),
        hidden: reader.readBoolean(),
        shockTime: reader.readInt64(),
        bindingShotCenter: reader.readBoolean(),
        extra: reader.readBoolean(),
        extraByte: reader.readUint8(),
        masterObjectId: reader.readUint32(),
        rarity: reader.readUint8(),
        buffs: (() => {
          const count = reader.readCount("buff", 256);
          const values: number[] = [];
          for (let index = 0; index < count; index++) values.push(reader.readUint8());
          return values;
        })()
      };
      break;
    case ServerPacketId.ObjectNpc: {
      const objectId = reader.readUint32();
      const name = reader.readString();
      const nameColour = reader.readUint32();
      const image = reader.readUint16();
      const colour = reader.readUint32();
      const location = point(reader);
      const direction = reader.readUint8();
      const count = reader.readCount("quest id", 1024);
      const questIds: number[] = [];
      for (let index = 0; index < count; index++) questIds.push(reader.readInt32());
      packet = { kind: "ObjectNpc", objectId, name, nameColour, image, colour, location, direction, questIds };
      break;
    }
    case ServerPacketId.NPCResponse: {
      const count = reader.readCount("npc page", 10_000);
      const pages: string[] = [];
      for (let index = 0; index < count; index++) pages.push(reader.readString());
      packet = { kind: "NPCResponse", pages };
      break;
    }
    case ServerPacketId.NPCGoods: {
      const count = reader.readCount("npc goods", 10_000);
      const items: UserItem[] = [];
      for (let index = 0; index < count; index++) items.push(readUserItem(reader));
      packet = { kind: "NPCGoods", items, rate: reader.readSingle(), panelType: reader.readUint8(), hideAddedStats: reader.readBoolean() };
      break;
    }
    case ServerPacketId.GainedItem:
      packet = { kind: "GainedItem", item: readUserItem(reader) };
      break;
    case ServerPacketId.GainedGold:
      packet = { kind: "GainedGold", gold: reader.readUint32() };
      break;
    case ServerPacketId.LoseGold:
      packet = { kind: "LoseGold", gold: reader.readUint32() };
      break;
    case ServerPacketId.DeleteItem:
      packet = { kind: "DeleteItem", uniqueId: reader.readUint64(), count: reader.readUint16() };
      break;
    case ServerPacketId.DuraChanged:
      packet = { kind: "DuraChanged", uniqueId: reader.readUint64(), currentDurability: reader.readUint16() };
      break;
    case ServerPacketId.HealthChanged:
      packet = { kind: "HealthChanged", hp: reader.readInt32(), mp: reader.readInt32() };
      break;
    case ServerPacketId.DamageIndicator:
      packet = { kind: "DamageIndicator", damage: reader.readInt32(), damageType: reader.readUint8(), objectId: reader.readUint32() };
      break;
    case ServerPacketId.Struck:
      packet = { kind: "Struck", attackerId: reader.readUint32() };
      break;
    case ServerPacketId.ObjectStruck:
      packet = { kind: "ObjectStruck", objectId: reader.readUint32(), attackerId: reader.readUint32(), location: point(reader), direction: reader.readUint8() };
      break;
    case ServerPacketId.Death:
      packet = { kind: "Death", location: point(reader), direction: reader.readUint8() };
      break;
    case ServerPacketId.ObjectDied:
      packet = { kind: "ObjectDied", objectId: reader.readUint32(), location: point(reader), direction: reader.readUint8(), dieType: reader.readUint8() };
      break;
    case ServerPacketId.Revived:
      packet = { kind: "Revived" };
      break;
    case ServerPacketId.ObjectRevived:
      packet = { kind: "ObjectRevived", objectId: reader.readUint32(), effect: reader.readBoolean() };
      break;
    case ServerPacketId.MapChanged: {
      const mapIndex = reader.readInt32();
      const fileName = reader.readString();
      reader.readString();
      reader.readUint16();
      reader.readUint16();
      reader.readUint8();
      const location = point(reader);
      const direction = reader.readUint8();
      const mapDarkLight = reader.readUint8();
      const music = reader.readUint16();
      const weather = reader.readUint16();
      packet = { kind: "MapChanged", mapIndex, fileName, location, direction, mapDarkLight, music, weather };
      break;
    }
    case ServerPacketId.ObjectHide:
      packet = { kind: "ObjectHide", objectId: reader.readUint32() };
      break;
    case ServerPacketId.ObjectShow:
      packet = { kind: "ObjectShow", objectId: reader.readUint32() };
      break;
    case ServerPacketId.ObjectTeleportOut:
      packet = { kind: "ObjectTeleportOut", objectId: reader.readUint32(), teleportType: reader.readUint8() };
      break;
    case ServerPacketId.ObjectTeleportIn:
      packet = { kind: "ObjectTeleportIn", objectId: reader.readUint32(), teleportType: reader.readUint8() };
      break;
    case ServerPacketId.TeleportIn:
      packet = { kind: "TeleportIn" };
      break;
    case ServerPacketId.ObjectHealth:
      packet = { kind: "ObjectHealth", objectId: reader.readUint32(), percent: reader.readUint8(), expire: reader.readUint8() };
      break;
    case ServerPacketId.ObjectMana:
      packet = { kind: "ObjectMana", objectId: reader.readUint32(), percent: reader.readUint8() };
      break;
    case ServerPacketId.ObjectLeveled:
      packet = { kind: "ObjectLeveled", objectId: reader.readUint32() };
      break;
    case ServerPacketId.LevelChanged:
      packet = { kind: "LevelChanged", level: reader.readUint16(), experience: reader.readInt64(), maxExperience: reader.readInt64() };
      break;
    case ServerPacketId.GainExperience:
      packet = { kind: "GainExperience", amount: reader.readUint32() };
      break;
    case ServerPacketId.TimeOfDay:
      packet = { kind: "TimeOfDay", lights: reader.readUint8() };
      break;
    case ServerPacketId.LogOutSuccess: {
      const count = reader.readCount("logout character", 16);
      const characters: SelectInfo[] = [];
      for (let index = 0; index < count; index++) characters.push(readSelectInfo(reader));
      packet = { kind: "LogOutSuccess", characters };
      break;
    }
    case ServerPacketId.LogOutFailed:
      packet = { kind: "LogOutFailed" };
      break;
    case ServerPacketId.ReturnToLogin:
      packet = { kind: "ReturnToLogin" };
      break;
    default:
      return { kind: "unknown", id, body: body.slice() };
  }
  reader.expectEnd();
  return packet;
}

async function gzipDecompress(bytes: Uint8Array): Promise<Uint8Array> {
  const copy = new Uint8Array(bytes.byteLength);
  copy.set(bytes);
  const stream = new Blob([copy.buffer]).stream().pipeThrough(new DecompressionStream("gzip"));
  return new Uint8Array(await new Response(stream).arrayBuffer());
}

async function gzipCompress(bytes: Uint8Array): Promise<Uint8Array> {
  const copy = new Uint8Array(bytes.byteLength);
  copy.set(bytes);
  const stream = new Blob([copy.buffer]).stream().pipeThrough(new CompressionStream("gzip"));
  return new Uint8Array(await new Response(stream).arrayBuffer());
}

export async function decodeServerFrame(frame: RawPacketFrame): Promise<ServerPacket> {
  const body = COMPRESSED_SERVER_PACKETS.has(frame.id) ? await gzipDecompress(frame.body) : frame.body;
  return decodeServerBodyUncompressed(frame.id, body);
}

export async function encodeServerFrame(packet: ServerPacket): Promise<Uint8Array> {
  const writer = new BinaryWriter();
  let id: number;
  switch (packet.kind) {
    case "Connected":
      id = ServerPacketId.Connected;
      break;
    case "ClientVersion":
      id = ServerPacketId.ClientVersion;
      writer.writeUint8(packet.result);
      break;
    case "Disconnect":
      id = ServerPacketId.Disconnect;
      writer.writeUint8(packet.reason);
      break;
    case "KeepAlive":
      id = ServerPacketId.KeepAlive;
      writer.writeInt64(packet.time);
      break;
    case "Login":
      id = ServerPacketId.Login;
      writer.writeUint8(packet.result);
      break;
    case "LoginBanned":
      id = ServerPacketId.LoginBanned;
      writer.writeString(packet.reason);
      writer.writeInt64(packet.expiryBinary);
      break;
    case "LoginSuccess":
      id = ServerPacketId.LoginSuccess;
      writer.writeInt32(packet.characters.length);
      for (const character of packet.characters) writeSelectInfo(writer, character);
      break;
    case "NewCharacter":
      id = ServerPacketId.NewCharacter;
      writer.writeUint8(packet.result);
      break;
    case "NewCharacterSuccess":
      id = ServerPacketId.NewCharacterSuccess;
      writeSelectInfo(writer, packet.character);
      break;
    case "DeleteCharacter":
      id = ServerPacketId.DeleteCharacter;
      writer.writeUint8(packet.result);
      break;
    case "DeleteCharacterSuccess":
      id = ServerPacketId.DeleteCharacterSuccess;
      writer.writeInt32(packet.characterIndex);
      break;
    case "StartGame":
      id = ServerPacketId.StartGame;
      writer.writeUint8(packet.result);
      writer.writeInt32(packet.resolution);
      break;
    case "StartGameBanned":
      id = ServerPacketId.StartGameBanned;
      writer.writeString(packet.reason);
      writer.writeInt64(packet.expiryBinary);
      break;
    case "StartGameDelay":
      id = ServerPacketId.StartGameDelay;
      writer.writeInt64(packet.milliseconds);
      break;
    case "MapInformation":
      id = ServerPacketId.MapInformation;
      writer.writeInt32(packet.mapIndex);
      writer.writeString(packet.fileName);
      writer.writeString(packet.title);
      writer.writeUint16(packet.miniMap);
      writer.writeUint16(packet.bigMap);
      writer.writeUint8(packet.lights);
      writer.writeUint8((packet.lightning ? 1 : 0) | (packet.fire ? 2 : 0));
      writer.writeUint8(packet.mapDarkLight);
      writer.writeUint16(packet.music);
      writer.writeUint16(packet.weatherParticles);
      break;
    case "NewMapInfo":
      id = ServerPacketId.NewMapInfo;
      writer.writeInt32(packet.mapIndex);
      writeMapInfo(writer, packet.info);
      break;
    case "UserInformation":
      id = ServerPacketId.UserInformation;
      writer.writeUint32(packet.objectId);
      writer.writeUint32(packet.realId);
      writer.writeString(packet.name);
      writer.writeString(packet.guildName);
      writer.writeString(packet.guildRank);
      writer.writeUint32(packet.nameColour);
      writer.writeUint8(packet.classId);
      writer.writeUint8(packet.gender);
      writer.writeUint16(packet.level);
      writePoint(writer, packet.location);
      writer.writeUint8(packet.direction);
      writer.writeUint8(packet.hair);
      writer.writeInt32(packet.hp);
      writer.writeInt32(packet.mp);
      writer.writeInt64(packet.experience);
      writer.writeInt64(packet.maxExperience);
      writer.writeUint16(packet.levelEffects);
      writer.writeBoolean(packet.hasHero);
      writer.writeUint8(packet.heroBehaviour);
      writeOptionalItems(writer, packet.inventory);
      writeOptionalItems(writer, packet.equipment);
      writeOptionalItems(writer, packet.questInventory);
      writer.writeUint32(packet.gold);
      writer.writeUint32(packet.credit);
      writer.writeBoolean(packet.hasExpandedStorage);
      writer.writeBoolean(packet.hasStoragePassword);
      writer.writeBoolean(packet.requireStoragePassword);
      writer.writeInt64(packet.storagePasswordLastSet);
      writer.writeInt64(packet.expandedStorageExpiryTime);
      writer.writeInt32(packet.magics.length);
      for (const magic of packet.magics) writeClientMagic(writer, magic);
      writer.writeInt32(0);
      writer.writeUint8(packet.summonedCreatureType);
      writer.writeBoolean(packet.creatureSummoned);
      writer.writeBoolean(packet.allowObserve);
      writer.writeBoolean(packet.observer);
      break;
    case "UserSlotsRefresh":
      id = ServerPacketId.UserSlotsRefresh;
      writeOptionalItems(writer, packet.inventory);
      writeOptionalItems(writer, packet.equipment);
      break;
    case "UserLocation":
      id = ServerPacketId.UserLocation;
      writePoint(writer, packet.location);
      writer.writeUint8(packet.direction);
      break;
    case "ObjectPlayer":
      id = packet.hero ? ServerPacketId.ObjectHero : ServerPacketId.ObjectPlayer;
      writeObjectPlayer(writer, packet);
      break;
    case "ObjectRemove":
      id = ServerPacketId.ObjectRemove;
      writer.writeUint32(packet.objectId);
      break;
    case "ObjectMove":
      id = packet.movement === "turn" ? ServerPacketId.ObjectTurn : packet.movement === "walk" ? ServerPacketId.ObjectWalk : ServerPacketId.ObjectRun;
      writer.writeUint32(packet.objectId);
      writePoint(writer, packet.location);
      writer.writeUint8(packet.direction);
      break;
    case "Chat":
      id = ServerPacketId.Chat;
      writer.writeString(packet.message);
      writer.writeUint8(packet.chatType);
      break;
    case "ObjectChat":
      id = ServerPacketId.ObjectChat;
      writer.writeUint32(packet.objectId);
      writer.writeString(packet.text);
      writer.writeUint8(packet.chatType);
      break;
    case "MoveItem":
      id = ServerPacketId.MoveItem;
      writer.writeUint8(packet.grid);
      writer.writeInt32(packet.from);
      writer.writeInt32(packet.to);
      writer.writeBoolean(packet.success);
      break;
    case "NewItemInfo":
      id = ServerPacketId.NewItemInfo;
      writeItemInfo(writer, packet.info);
      break;
    case "ObjectItem":
      id = ServerPacketId.ObjectItem;
      writer.writeUint32(packet.objectId);
      writer.writeString(packet.name);
      writer.writeUint32(packet.nameColour);
      writePoint(writer, packet.location);
      writer.writeUint16(packet.image);
      writer.writeUint8(packet.grade);
      break;
    case "ObjectGold":
      id = ServerPacketId.ObjectGold;
      writer.writeUint32(packet.objectId);
      writer.writeUint32(packet.gold);
      writePoint(writer, packet.location);
      break;
    case "ObjectMonster":
      id = ServerPacketId.ObjectMonster;
      writer.writeUint32(packet.objectId);
      writer.writeString(packet.name);
      writer.writeUint32(packet.nameColour);
      writePoint(writer, packet.location);
      writer.writeUint16(packet.image);
      writer.writeUint8(packet.direction);
      writer.writeUint8(packet.effect);
      writer.writeUint8(packet.ai);
      writer.writeUint8(packet.light);
      writer.writeBoolean(packet.dead);
      writer.writeBoolean(packet.skeleton);
      writer.writeUint16(packet.poison);
      writer.writeBoolean(packet.hidden);
      writer.writeInt64(packet.shockTime);
      writer.writeBoolean(packet.bindingShotCenter);
      writer.writeBoolean(packet.extra);
      writer.writeUint8(packet.extraByte);
      writer.writeUint32(packet.masterObjectId);
      writer.writeUint8(packet.rarity);
      writer.writeInt32(packet.buffs.length);
      for (const buff of packet.buffs) writer.writeUint8(buff);
      break;
    case "ObjectNpc":
      id = ServerPacketId.ObjectNpc;
      writer.writeUint32(packet.objectId);
      writer.writeString(packet.name);
      writer.writeUint32(packet.nameColour);
      writer.writeUint16(packet.image);
      writer.writeUint32(packet.colour);
      writePoint(writer, packet.location);
      writer.writeUint8(packet.direction);
      writer.writeInt32(packet.questIds.length);
      for (const questId of packet.questIds) writer.writeInt32(questId);
      break;
    case "NPCResponse":
      id = ServerPacketId.NPCResponse;
      writer.writeInt32(packet.pages.length);
      for (const page of packet.pages) writer.writeString(page);
      break;
    case "NPCGoods": {
      id = ServerPacketId.NPCGoods;
      writer.writeInt32(packet.items.length);
      for (const item of packet.items) writeUserItem(writer, item);
      writer.writeSingle(packet.rate);
      writer.writeUint8(packet.panelType);
      writer.writeBoolean(packet.hideAddedStats);
      const compressed = await gzipCompress(writer.toUint8Array());
      return encodeFrame(id, compressed);
    }
    case "GainedItem":
      id = ServerPacketId.GainedItem;
      writeUserItem(writer, packet.item);
      break;
    case "GainedGold":
      id = ServerPacketId.GainedGold;
      writer.writeUint32(packet.gold);
      break;
    case "LoseGold":
      id = ServerPacketId.LoseGold;
      writer.writeUint32(packet.gold);
      break;
    case "DeleteItem":
      id = ServerPacketId.DeleteItem;
      writer.writeUint64(packet.uniqueId);
      writer.writeUint16(packet.count);
      break;
    case "DuraChanged":
      id = ServerPacketId.DuraChanged;
      writer.writeUint64(packet.uniqueId);
      writer.writeUint16(packet.currentDurability);
      break;
    case "HealthChanged":
      id = ServerPacketId.HealthChanged;
      writer.writeInt32(packet.hp);
      writer.writeInt32(packet.mp);
      break;
    case "DamageIndicator":
      id = ServerPacketId.DamageIndicator;
      writer.writeInt32(packet.damage);
      writer.writeUint8(packet.damageType);
      writer.writeUint32(packet.objectId);
      break;
    case "Struck":
      id = ServerPacketId.Struck;
      writer.writeUint32(packet.attackerId);
      break;
    case "ObjectStruck":
      id = ServerPacketId.ObjectStruck;
      writer.writeUint32(packet.objectId);
      writer.writeUint32(packet.attackerId);
      writePoint(writer, packet.location);
      writer.writeUint8(packet.direction);
      break;
    case "Death":
      id = ServerPacketId.Death;
      writePoint(writer, packet.location);
      writer.writeUint8(packet.direction);
      break;
    case "ObjectDied":
      id = ServerPacketId.ObjectDied;
      writer.writeUint32(packet.objectId);
      writePoint(writer, packet.location);
      writer.writeUint8(packet.direction);
      writer.writeUint8(packet.dieType);
      break;
    case "Revived":
      id = ServerPacketId.Revived;
      break;
    case "ObjectRevived":
      id = ServerPacketId.ObjectRevived;
      writer.writeUint32(packet.objectId);
      writer.writeBoolean(packet.effect);
      break;
    case "MapChanged":
      id = ServerPacketId.MapChanged;
      writer.writeInt32(packet.mapIndex);
      writer.writeString(packet.fileName);
      writer.writeString("");
      writer.writeUint16(0);
      writer.writeUint16(0);
      writer.writeUint8(0);
      writePoint(writer, packet.location);
      writer.writeUint8(packet.direction);
      writer.writeUint8(packet.mapDarkLight);
      writer.writeUint16(packet.music);
      writer.writeUint16(packet.weather);
      break;
    case "ObjectHide":
      id = ServerPacketId.ObjectHide;
      writer.writeUint32(packet.objectId);
      break;
    case "ObjectShow":
      id = ServerPacketId.ObjectShow;
      writer.writeUint32(packet.objectId);
      break;
    case "ObjectTeleportOut":
      id = ServerPacketId.ObjectTeleportOut;
      writer.writeUint32(packet.objectId);
      writer.writeUint8(packet.teleportType);
      break;
    case "ObjectTeleportIn":
      id = ServerPacketId.ObjectTeleportIn;
      writer.writeUint32(packet.objectId);
      writer.writeUint8(packet.teleportType);
      break;
    case "TeleportIn":
      id = ServerPacketId.TeleportIn;
      break;
    case "ObjectHealth":
      id = ServerPacketId.ObjectHealth;
      writer.writeUint32(packet.objectId);
      writer.writeUint8(packet.percent);
      writer.writeUint8(packet.expire);
      break;
    case "ObjectMana":
      id = ServerPacketId.ObjectMana;
      writer.writeUint32(packet.objectId);
      writer.writeUint8(packet.percent);
      break;
    case "ObjectLeveled":
      id = ServerPacketId.ObjectLeveled;
      writer.writeUint32(packet.objectId);
      break;
    case "LevelChanged":
      id = ServerPacketId.LevelChanged;
      writer.writeUint16(packet.level);
      writer.writeInt64(packet.experience);
      writer.writeInt64(packet.maxExperience);
      break;
    case "GainExperience":
      id = ServerPacketId.GainExperience;
      writer.writeUint32(packet.amount);
      break;
    case "TimeOfDay":
      id = ServerPacketId.TimeOfDay;
      writer.writeUint8(packet.lights);
      break;
    case "LogOutSuccess":
      id = ServerPacketId.LogOutSuccess;
      writer.writeInt32(packet.characters.length);
      for (const character of packet.characters) writeSelectInfo(writer, character);
      break;
    case "LogOutFailed":
      id = ServerPacketId.LogOutFailed;
      break;
    case "ReturnToLogin":
      id = ServerPacketId.ReturnToLogin;
      break;
    case "unknown":
      return encodeFrame(packet.id, packet.body);
  }
  return encodeFrame(id, writer.toUint8Array());
}

export function packetIsCritical(id: number): boolean {
  return id >= ServerPacketId.Connected && id <= ServerPacketId.MapInformation;
}

export function ensureFrameSize(bytes: Uint8Array): void {
  if (bytes.byteLength > MAX_PACKET_BYTES) throw new ProtocolError("Packet exceeds the legacy 16-bit length limit");
}
