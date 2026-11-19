import type { ClientItemInfo, SelectInfo, UserItem } from "../protocol/types";

export const CLASS_NAMES = ["战士", "法师", "道士", "刺客", "弓箭手"] as const;
export const CLASS_GLYPHS = ["刃", "焰", "符", "影", "羽"] as const;
export const GENDER_NAMES = ["男", "女"] as const;

export const ITEM_TYPE_NAMES: Readonly<Record<number, string>> = {
  1: "武器",
  2: "衣服",
  4: "头盔",
  5: "项链",
  6: "手镯",
  7: "戒指",
  8: "护符",
  9: "腰带",
  10: "靴子",
  11: "宝石",
  12: "火把",
  13: "药品",
  14: "矿石",
  15: "肉类",
  17: "卷轴",
  20: "书籍",
  27: "食物",
  33: "鱼类",
  34: "任务物品",
  37: "变身",
  39: "镶嵌",
  42: "英雄"
};

export const EQUIPMENT_SLOT_NAMES = [
  "武器",
  "衣服",
  "头盔",
  "火把",
  "项链",
  "左手镯",
  "右手镯",
  "左戒指",
  "右戒指",
  "护符",
  "腰带",
  "靴子",
  "宝石",
  "坐骑"
] as const;

export function classLabel(classId: number): string {
  return CLASS_NAMES[classId] ?? `职业 ${classId}`;
}

export function classGlyph(classId: number): string {
  return CLASS_GLYPHS[classId] ?? "印";
}

export function genderLabel(gender: number): string {
  return GENDER_NAMES[gender] ?? "未知";
}

export function itemLabel(item: UserItem, infos: Record<number, ClientItemInfo>): string {
  return infos[item.itemIndex]?.name || `物品 #${item.itemIndex}`;
}

export function itemTypeLabel(info: ClientItemInfo | undefined): string {
  if (!info) return "读取中";
  return ITEM_TYPE_NAMES[info.type] ?? `类型 ${info.type}`;
}

export function equipmentSlotFor(info: ClientItemInfo | undefined): number | null {
  if (!info) return null;
  switch (info.type) {
    case 1: return 0;
    case 2: return 1;
    case 4: return 2;
    case 12: return 3;
    case 5: return 4;
    case 6: return 5;
    case 7: return 7;
    case 8: return 9;
    case 9: return 10;
    case 10: return 11;
    case 11: return 12;
    case 19: return 13;
    default: return null;
  }
}

export function characterSubtitle(character: SelectInfo): string {
  return `${classLabel(character.classId)} · ${genderLabel(character.gender)} · Lv.${character.level}`;
}

export function formatNumber(value: number | bigint): string {
  const numeric = typeof value === "bigint" ? Number(value) : value;
  if (!Number.isFinite(numeric)) return "0";
  return Math.max(0, Math.round(numeric)).toLocaleString("zh-CN");
}

export function formatTime(timestamp: number): string {
  return new Date(timestamp).toLocaleTimeString("zh-CN", { hour: "2-digit", minute: "2-digit" });
}
