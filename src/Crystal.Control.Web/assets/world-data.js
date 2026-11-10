(function () {
  const MAPS = [
    { key: "D2001.map", name: "比奇城", group: "比奇省", layer: "主城", size: [320, 320], level: "0", terrain: "城镇", status: "published", tone: "green", file: "D2001.map", safeZones: [{ x: 320, y: 270, size: 10, start: true }, { x: 310, y: 262, size: 7, start: false }], desc: "主城安全区，武器、药店、仓库、任务使者和传送员集中。" },
    { key: "0.map", name: "比奇省", group: "比奇省", layer: "地面", size: [600, 600], level: "1-15", terrain: "野外", status: "published", tone: "green", file: "0.map", safeZones: [], desc: "新手野外与主城外围，连接沃玛森林和多个低级练级区。" },
    { key: "D2002.map", name: "沃玛森林", group: "沃玛寺庙", layer: "野外", size: [420, 420], level: "18-28", terrain: "森林", status: "published", tone: "orange", file: "D2002.map", safeZones: [], desc: "沃玛寺庙入口所在野外，刷怪密度中等，存在精英怪。" },
    { key: "D3001.map", name: "沃玛寺庙一层", group: "沃玛寺庙", layer: "1F", size: [160, 160], level: "20-30", terrain: "地下城", status: "published", tone: "violet", file: "D3001.map", safeZones: [], desc: "沃玛寺庙第一层，入口与二层楼梯形成双向连接。" },
    { key: "D3002.map", name: "沃玛寺庙二层", group: "沃玛寺庙", layer: "2F", size: [160, 160], level: "25-35", terrain: "地下城", status: "published", tone: "violet", file: "D3002.map", safeZones: [], desc: "沃玛教主主要刷新层，连接一层与三层。" },
    { key: "D3003.map", name: "沃玛寺庙三层", group: "沃玛寺庙", layer: "3F", size: [160, 160], level: "30-40", terrain: "地下城", status: "validating", tone: "violet", file: "D3003.map", safeZones: [], desc: "沃玛寺庙深层区域，含教主大厅前置走廊。" },
    { key: "1.map", name: "祖玛寺庙一层", group: "祖玛寺庙", layer: "1F", size: [180, 180], level: "30-38", terrain: "地下城", status: "published", tone: "violet", file: "1.map", safeZones: [], desc: "祖玛寺庙入口层，连接比奇外围与二层楼梯。" },
    { key: "2.map", name: "祖玛寺庙二层", group: "祖玛寺庙", layer: "2F", size: [180, 180], level: "35-42", terrain: "地下城", status: "published", tone: "violet", file: "2.map", safeZones: [], desc: "祖玛寺庙中段，祖玛卫士与雕像密集分布。" },
    { key: "3.map", name: "祖玛寺庙三层", group: "祖玛寺庙", layer: "3F", size: [180, 180], level: "38-48", terrain: "地下城", status: "published", tone: "violet", file: "3.map", safeZones: [], desc: "祖玛教主与核心装备的主要掉落层，西侧连接二层。" },
    { key: "zuma-boss.map", name: "祖玛教主大厅", group: "祖玛寺庙", layer: "Boss", size: [120, 120], level: "45+", terrain: "Boss 房", status: "published", tone: "red", file: "zuma-boss.map", safeZones: [], desc: "祖玛教主独立战斗空间，入口坐标与撤退坐标必须成对维护。" },
    { key: "4.map", name: "赤月峡谷北", group: "赤月峡谷", layer: "北谷", size: [240, 240], level: "40-55", terrain: "峡谷", status: "validating", tone: "red", file: "4.map", safeZones: [], desc: "赤月恶魔与虹魔系怪物活动区域，存在高价值装备掉落。" },
    { key: "D401.map", name: "苍月岛", group: "苍月岛", layer: "海岛", size: [300, 300], level: "15-40", terrain: "海岛", status: "published", tone: "blue", file: "D401.map", safeZones: [{ x: 160, y: 112, size: 8, start: true }], desc: "海岛野外与药店补给点，双头金刚和材料怪的主要分布区。" },
    { key: "0150.map", name: "石墓阵", group: "石墓阵", layer: "迷宫", size: [160, 160], level: "30-45", terrain: "迷宫", status: "draft", tone: "orange", file: "0150.map", safeZones: [], desc: "高密度白野猪刷新地图，地图阻挡文件和传送坐标需要重点校验。" },
    { key: "D2003.map", name: "盟重省", group: "盟重省", layer: "主城", size: [360, 360], level: "20+", terrain: "城镇", status: "published", tone: "green", file: "D2003.map", safeZones: [{ x: 180, y: 172, size: 8, start: true }], desc: "中后期主城，传送员连接赤月、苍月和沙巴克等区域。" },
    { key: "D6001.map", name: "沙巴克", group: "沙巴克", layer: "城池", size: [240, 240], level: "30+", terrain: "攻城地图", status: "published", tone: "gold", file: "D6001.map", safeZones: [{ x: 120, y: 120, size: 6, start: false }], desc: "行会攻城与城主归属地图，NPC 和传送点受攻城状态影响。" }
  ];

  const PORTALS = [
    { id: "portal.bichon.field", from: "D2001.map", fromName: "比奇城", fromPoint: [332, 266], to: "0.map", toName: "比奇省", toPoint: [333, 267], kind: "地图边界", trigger: "踩点移动", needMove: true, status: "published" },
    { id: "portal.field.bichon", from: "0.map", fromName: "比奇省", fromPoint: [333, 267], to: "D2001.map", toName: "比奇城", toPoint: [332, 266], kind: "地图边界", trigger: "踩点移动", needMove: true, status: "published" },
    { id: "portal.field.woma", from: "0.map", fromName: "比奇省", fromPoint: [612, 112], to: "D2002.map", toName: "沃玛森林", toPoint: [45, 285], kind: "地图边界", trigger: "踩点移动", needMove: true, status: "published" },
    { id: "portal.woma.field", from: "D2002.map", fromName: "沃玛森林", fromPoint: [45, 285], to: "0.map", toName: "比奇省", toPoint: [612, 112], kind: "地图边界", trigger: "踩点移动", needMove: true, status: "published" },
    { id: "portal.woma.temple1", from: "D2002.map", fromName: "沃玛森林", fromPoint: [18, 42], to: "D3001.map", toName: "沃玛寺庙一层", toPoint: [29, 32], kind: "洞口", trigger: "踩点移动", needMove: true, status: "published" },
    { id: "portal.woma1.field", from: "D3001.map", fromName: "沃玛寺庙一层", fromPoint: [29, 32], to: "D2002.map", toName: "沃玛森林", toPoint: [18, 42], kind: "洞口", trigger: "踩点移动", needMove: true, status: "published" },
    { id: "portal.woma1.2.up", from: "D3001.map", fromName: "沃玛寺庙一层", fromPoint: [42, 168], to: "D3002.map", toName: "沃玛寺庙二层", toPoint: [42, 22], kind: "楼梯上行", trigger: "踩点移动", needMove: true, status: "published" },
    { id: "portal.woma2.1.down", from: "D3002.map", fromName: "沃玛寺庙二层", fromPoint: [42, 22], to: "D3001.map", toName: "沃玛寺庙一层", toPoint: [42, 168], kind: "楼梯下行", trigger: "踩点移动", needMove: true, status: "published" },
    { id: "portal.woma2.3.up", from: "D3002.map", fromName: "沃玛寺庙二层", fromPoint: [80, 150], to: "D3003.map", toName: "沃玛寺庙三层", toPoint: [80, 18], kind: "楼梯上行", trigger: "踩点移动", needMove: true, status: "validating" },
    { id: "portal.woma3.2.down", from: "D3003.map", fromName: "沃玛寺庙三层", fromPoint: [80, 18], to: "D3002.map", toName: "沃玛寺庙二层", toPoint: [80, 150], kind: "楼梯下行", trigger: "踩点移动", needMove: true, status: "validating" },
    { id: "portal.zuma1.2.up", from: "1.map", fromName: "祖玛寺庙一层", fromPoint: [42, 168], to: "2.map", toName: "祖玛寺庙二层", toPoint: [42, 22], kind: "楼梯上行", trigger: "踩点移动", needMove: true, status: "published" },
    { id: "portal.zuma2.1.down", from: "2.map", fromName: "祖玛寺庙二层", fromPoint: [42, 22], to: "1.map", toName: "祖玛寺庙一层", toPoint: [42, 168], kind: "楼梯下行", trigger: "踩点移动", needMove: true, status: "published" },
    { id: "portal.zuma2.3.up", from: "2.map", fromName: "祖玛寺庙二层", fromPoint: [126, 154], to: "3.map", toName: "祖玛寺庙三层", toPoint: [126, 20], kind: "楼梯上行", trigger: "踩点移动", needMove: true, status: "published" },
    { id: "portal.zuma3.2.down", from: "3.map", fromName: "祖玛寺庙三层", fromPoint: [126, 20], to: "2.map", toName: "祖玛寺庙二层", toPoint: [126, 154], kind: "楼梯下行", trigger: "踩点移动", needMove: true, status: "published" },
    { id: "portal.zuma3.boss", from: "3.map", fromName: "祖玛寺庙三层", fromPoint: [132, 88], to: "zuma-boss.map", toName: "祖玛教主大厅", toPoint: [60, 108], kind: "Boss 门", trigger: "踩点移动", needMove: true, status: "published" },
    { id: "portal.zuma.boss3", from: "zuma-boss.map", fromName: "祖玛教主大厅", fromPoint: [60, 108], to: "3.map", toName: "祖玛寺庙三层", toPoint: [132, 88], kind: "Boss 门", trigger: "踩点移动", needMove: true, status: "published" },
    { id: "portal.mengzhong.redmoon", from: "D2003.map", fromName: "盟重省", fromPoint: [286, 118], to: "4.map", toName: "赤月峡谷北", toPoint: [24, 220], kind: "NPC 传送", trigger: "对话框", needMove: false, status: "published" },
    { id: "portal.mengzhong.moon", from: "D2003.map", fromName: "盟重省", fromPoint: [278, 132], to: "D401.map", toName: "苍月岛", toPoint: [30, 280], kind: "NPC 传送", trigger: "对话框", needMove: false, status: "published" },
    { id: "portal.moon.mengzhong", from: "D401.map", fromName: "苍月岛", fromPoint: [30, 280], to: "D2003.map", toName: "盟重省", toPoint: [278, 132], kind: "NPC 传送", trigger: "对话框", needMove: false, status: "published" },
    { id: "portal.siege.mengzhong", from: "D6001.map", fromName: "沙巴克", fromPoint: [120, 220], to: "D2003.map", toName: "盟重省", toPoint: [180, 180], kind: "城门", trigger: "攻城状态", needMove: true, status: "published" }
  ];

  const NPC_POINTS = [
    { npcKey: "3001", name: "比奇武器商", role: "武器商店", mapKey: "D2001.map", x: 315, y: 262, direction: "南", script: "merchant_weapon_01", status: "published" },
    { npcKey: "3002", name: "仓库管理员", role: "仓库", mapKey: "D2001.map", x: 290, y: 258, direction: "南", script: "storage_keeper", status: "published" },
    { npcKey: "3003", name: "盟重传送员", role: "跨地图传送", mapKey: "D2003.map", x: 180, y: 180, direction: "南", script: "teleport_mentor", status: "validating" },
    { npcKey: "3004", name: "任务使者", role: "主线任务", mapKey: "D2001.map", x: 310, y: 270, direction: "南", script: "quest_ambassador", status: "draft" },
    { npcKey: "3005", name: "药店老板", role: "药品商店", mapKey: "D401.map", x: 160, y: 112, direction: "东", script: "merchant_potion", status: "published" },
    { npcKey: "3006", name: "比奇传送员", role: "区域传送", mapKey: "D2001.map", x: 320, y: 274, direction: "南", script: "teleport_bichon", status: "published" },
    { npcKey: "3007", name: "沙巴克管理员", role: "攻城 / 行会", mapKey: "D6001.map", x: 120, y: 120, direction: "南", script: "sabuk_manager", status: "published" }
  ];

  const SPAWNS = [
    { id: "7001", mapKey: "3.map", monsterKey: "2001", x: 132, y: 88, count: 2, spread: 3, delay: 120, randomDelay: 15, direction: "东", route: "zuma_guard_loop", saveRespawnTime: true, status: "published", tier: "Boss" },
    { id: "7002", mapKey: "4.map", monsterKey: "2002", x: 168, y: 72, count: 1, spread: 2, delay: 240, randomDelay: 30, direction: "西", route: "redmoon_center", saveRespawnTime: true, status: "published", tier: "Boss" },
    { id: "7003", mapKey: "0150.map", monsterKey: "2004", x: 70, y: 70, count: 18, spread: 10, delay: 12, randomDelay: 4, direction: "随机", route: "", saveRespawnTime: false, status: "validating", tier: "普通" },
    { id: "7004", mapKey: "D401.map", monsterKey: "2005", x: 180, y: 240, count: 12, spread: 12, delay: 15, randomDelay: 5, direction: "随机", route: "coast_patrol", saveRespawnTime: false, status: "published", tier: "精英" },
    { id: "7005", mapKey: "4.map", monsterKey: "2003", x: 88, y: 132, count: 6, spread: 8, delay: 30, randomDelay: 10, direction: "随机", route: "temple_guard", saveRespawnTime: false, status: "published", tier: "精英" },
    { id: "7006", mapKey: "3.map", monsterKey: "2007", x: 40, y: 44, count: 12, spread: 6, delay: 18, randomDelay: 6, direction: "随机", route: "", saveRespawnTime: false, status: "published", tier: "普通" },
    { id: "7007", mapKey: "D3002.map", monsterKey: "2006", x: 80, y: 96, count: 1, spread: 2, delay: 180, randomDelay: 20, direction: "南", route: "woma_boss_route", saveRespawnTime: true, status: "published", tier: "Boss" },
    { id: "7008", mapKey: "3.map", monsterKey: "2007", x: 78, y: 112, count: 8, spread: 8, delay: 18, randomDelay: 6, direction: "随机", route: "", saveRespawnTime: false, status: "published", tier: "普通" },
    { id: "7009", mapKey: "zuma-boss.map", monsterKey: "2001", x: 60, y: 60, count: 1, spread: 2, delay: 180, randomDelay: 30, direction: "南", route: "boss_room", saveRespawnTime: true, status: "published", tier: "Boss" },
    { id: "7010", mapKey: "0150.map", monsterKey: "2004", x: 108, y: 82, count: 6, spread: 8, delay: 12, randomDelay: 4, direction: "随机", route: "", saveRespawnTime: false, status: "published", tier: "普通" }
  ];

  const DROP_GROUPS = [
    { key: "drop.zuma.boss", monsterKey: "2001", mode: "独立概率 + 必掉组", expected: "2.84 件 / 次", gold: "8,000 - 20,000", entries: [
      { itemKey: "1006", name: "屠龙", category: "武器", chanceText: "1 / 20,000", chance: 1 / 20000, qty: "1", group: "稀有" },
      { itemKey: "1001", name: "裁决之杖", category: "武器", chanceText: "1 / 3,200", chance: 1 / 3200, qty: "1", group: "核心" },
      { itemKey: "1009", name: "法神披风", category: "衣服", chanceText: "1 / 1,800", chance: 1 / 1800, qty: "1", group: "核心" },
      { itemKey: "1010", name: "祖玛头像", category: "材料", chanceText: "1 / 80", chance: 1 / 80, qty: "1", group: "材料" },
      { itemKey: "1011", name: "祝福油", category: "消耗品", chanceText: "1 / 12", chance: 1 / 12, qty: "1", group: "常用" }
    ] },
    { key: "drop.redmoon.lord", monsterKey: "2002", mode: "独立概率 + 稀有池", expected: "3.16 件 / 次", gold: "12,000 - 32,000", entries: [
      { itemKey: "1008", name: "龙纹剑", category: "武器", chanceText: "1 / 6,400", chance: 1 / 6400, qty: "1", group: "稀有" },
      { itemKey: "1007", name: "骨玉权杖", category: "武器", chanceText: "1 / 4,800", chance: 1 / 4800, qty: "1", group: "稀有" },
      { itemKey: "1002", name: "雷霆战甲（男）", category: "衣服", chanceText: "1 / 2,400", chance: 1 / 2400, qty: "1", group: "核心" },
      { itemKey: "1012", name: "赤月魔血", category: "材料", chanceText: "1 / 20", chance: 1 / 20, qty: "1", group: "材料" }
    ] },
    { key: "drop.wildboar.common", monsterKey: "2004", mode: "独立概率", expected: "0.72 件 / 次", gold: "1,000 - 3,200", entries: [
      { itemKey: "1005", name: "流星项链", category: "首饰", chanceText: "1 / 900", chance: 1 / 900, qty: "1", group: "装备" },
      { itemKey: "1013", name: "太阳水", category: "药品", chanceText: "1 / 3", chance: 1 / 3, qty: "1-2", group: "补给" },
      { itemKey: "1014", name: "回城卷", category: "消耗品", chanceText: "1 / 5", chance: 1 / 5, qty: "1", group: "补给" }
    ] },
    { key: "drop.woma.boss", monsterKey: "2006", mode: "独立概率 + 必掉组", expected: "2.41 件 / 次", gold: "6,000 - 15,000", entries: [
      { itemKey: "1007", name: "骨玉权杖", category: "武器", chanceText: "1 / 1,200", chance: 1 / 1200, qty: "1", group: "核心" },
      { itemKey: "1015", name: "沃玛号角", category: "任务道具", chanceText: "1 / 3", chance: 1 / 3, qty: "1", group: "任务" },
      { itemKey: "1011", name: "祝福油", category: "消耗品", chanceText: "1 / 8", chance: 1 / 8, qty: "1", group: "常用" }
    ] },
    { key: "drop.stone.tomb", monsterKey: "2004", mode: "地图掉落组", expected: "1.08 件 / 次", gold: "800 - 2,000", entries: [
      { itemKey: "1005", name: "流星项链", category: "首饰", chanceText: "1 / 600", chance: 1 / 600, qty: "1", group: "装备" },
      { itemKey: "1010", name: "祖玛头像", category: "材料", chanceText: "1 / 50", chance: 1 / 50, qty: "1", group: "材料" },
      { itemKey: "1013", name: "太阳水", category: "药品", chanceText: "1 / 4", chance: 1 / 4, qty: "1", group: "补给" }
    ] },
    { key: "drop.event.moon", monsterKey: "2004", mode: "活动限定", expected: "0.18 件 / 次", gold: "—", entries: [
      { itemKey: "1016", name: "月华宝箱", category: "活动道具", chanceText: "1 / 120", chance: 1 / 120, qty: "1", group: "活动" }
    ] }
  ];

  const SCENARIOS = [
    { key: "zuma", label: "祖玛教主", mapKey: "3.map", spawnId: "7001", monsterKey: "2001", dropKey: "drop.zuma.boss", title: "祖玛教主 · 经典 Boss 链", note: "三层固定刷新点 → Boss → 稀有装备掉落，适合用来检查刷新时间、掉落组和装备产出是否闭环。" },
    { key: "redmoon", label: "赤月恶魔", mapKey: "4.map", spawnId: "7002", monsterKey: "2002", dropKey: "drop.redmoon.lord", title: "赤月恶魔 · 高价值掉落链", note: "北谷深处刷新 → 赤月恶魔 → 武器与衣服掉落，装备和地图资源需要同时通过发布门禁。" },
    { key: "boar", label: "白野猪", mapKey: "0150.map", spawnId: "7003", monsterKey: "2004", dropKey: "drop.wildboar.common", title: "白野猪 · 高密度普通怪链", note: "石墓阵高密度刷新 → 白野猪 → 首饰与补给，适合验证刷新间隔、掉落倍率和地图容量。" },
    { key: "woma", label: "沃玛教主", mapKey: "D3002.map", spawnId: "7007", monsterKey: "2006", dropKey: "drop.woma.boss", title: "沃玛教主 · 任务道具链", note: "二层教主房 → 沃玛教主 → 沃玛号角与核心武器，任务、地图、怪物和掉落需要保持引用一致。" }
  ];

  const ONLINE_COORDS = {
    "风起云涌": { mapKey: "3.map", x: 152, y: 112, direction: "东", state: "战斗中" },
    "月下听风": { mapKey: "D401.map", x: 182, y: 240, direction: "东", state: "移动中" },
    "孤城落日": { mapKey: "4.map", x: 120, y: 88, direction: "西", state: "战斗中" },
    "铁血战魂": { mapKey: "D6001.map", x: 118, y: 122, direction: "南", state: "空闲" },
    "青灯古卷": { mapKey: "0150.map", x: 72, y: 68, direction: "北", state: "战斗中" }
  };

  window.MirWorld = { MAPS, PORTALS, NPC_POINTS, SPAWNS, DROP_GROUPS, SCENARIOS, ONLINE_COORDS };
})();
