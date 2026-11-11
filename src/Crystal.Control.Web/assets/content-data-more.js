(function () {
  const types = [];

  types.push({
    id: "quests", entity: "quest", icon: "file", label: "任务与剧情", singular: "任务",
    total: 154, draft: 16, issues: 2, source: "Server.MirDB", tone: "blue",
    description: "主线、支线和日常任务的目标、步骤、状态迁移、奖励与 NPC 引用。",
    columns: ["index", "name", "category", "level", "metric", "status", "updated"],
    columnLabels: { index: "旧索引", name: "任务", category: "类型", level: "需求等级", metric: "主要奖励", status: "状态", updated: "最近更新" },
    rows: [
      { index: 5001, name: "祖玛的威胁", subtitle: "主线 · 第三章", category: "主线", level: 45, metric: "经验 120,000", status: "published", updated: "今天 15:32", icon: "file", tags: ["主线", "组队"], attributes: [["类型", "主线"], ["需求等级", "45"], ["前置任务", "5000"], ["步骤", "4"], ["奖励", "经验 120,000 / 裁决之杖"], ["NPC", "任务使者"]] },
      { index: 5002, name: "苍月岛的来信", subtitle: "支线 · 探索", category: "支线", level: 30, metric: "金币 25,000", status: "published", updated: "今天 12:21", icon: "mail", tags: ["支线", "探索"], attributes: [["类型", "支线"], ["需求等级", "30"], ["前置任务", "无"], ["步骤", "3"], ["奖励", "金币 25,000"], ["NPC", "仓库管理员"]] },
      { index: 5003, name: "每日除魔", subtitle: "日常 · 循环", category: "日常", level: 25, metric: "活跃度 20", status: "validating", updated: "今天 10:08", icon: "refresh", tags: ["日常", "循环"], attributes: [["类型", "日常"], ["需求等级", "25"], ["重置", "每日 05:00"], ["步骤", "1"], ["奖励", "活跃度 20 / 经验 18,000"], ["NPC", "任务使者"]] },
      { index: 5004, name: "石墓阵调查", subtitle: "支线 · 地下城", category: "支线", level: 36, metric: "经验 48,000", status: "draft", updated: "昨天 21:47", icon: "mapPin", tags: ["支线", "草稿"], attributes: [["类型", "支线"], ["需求等级", "36"], ["前置任务", "5002"], ["步骤", "5"], ["奖励", "经验 48,000"], ["校验", "目标怪物不存在"]] },
      { index: 5005, name: "月华收集", subtitle: "活动 · 收集", category: "活动", level: 20, metric: "月华宝箱 × 1", status: "draft", updated: "昨天 17:05", icon: "gift", tags: ["活动", "限时"], attributes: [["类型", "活动"], ["需求等级", "20"], ["活动窗口", "7 天"], ["步骤", "2"], ["奖励", "月华宝箱 × 1"], ["校验", "活动未发布"]] }
    ]
  });

  types.push({
    id: "shops", entity: "gameshop", icon: "bag", label: "商城与商店", singular: "商城",
    total: 105, draft: 8, issues: 1, source: "Server.MirDB", tone: "gold",
    description: "NPC 商店和游戏商城商品、货币、限购、刷新规则与支付商品映射。",
    columns: ["index", "name", "category", "level", "metric", "status", "updated"],
    columnLabels: { index: "旧索引", name: "商店", category: "地图 / NPC", level: "商品数", metric: "货币", status: "状态", updated: "最近更新" },
    rows: [
      { index: 6001, name: "比奇武器商店", subtitle: "NPC 商店", category: "比奇城 · 武器商", level: 42, metric: "金币", status: "published", updated: "今天 14:11", icon: "sword", tags: ["NPC", "金币"], attributes: [["类型", "NPC 商店"], ["地图", "比奇城"], ["商品数", "42"], ["货币", "金币"], ["刷新", "每日 06:00"], ["限购", "否"]] },
      { index: 6002, name: "苍月药品商店", subtitle: "NPC 商店", category: "苍月岛 · 药店", level: 18, metric: "金币", status: "published", updated: "今天 12:56", icon: "heart", tags: ["NPC", "药品"], attributes: [["类型", "NPC 商店"], ["地图", "苍月岛"], ["商品数", "18"], ["货币", "金币"], ["刷新", "每日 06:00"], ["限购", "否"]] },
      { index: 6003, name: "元宝商城 · 推荐", subtitle: "游戏商城", category: "全服", level: 36, metric: "元宝", status: "validating", updated: "今天 10:33", icon: "coins", tags: ["支付", "推荐"], attributes: [["类型", "游戏商城"], ["分区", "全服"], ["商品数", "36"], ["货币", "元宝"], ["限购", "部分"], ["支付映射", "待校验"]] },
      { index: 6004, name: "活动兑换商店", subtitle: "活动商店", category: "月华活动", level: 12, metric: "活动代币", status: "draft", updated: "昨天 19:50", icon: "gift", tags: ["活动", "限时"], attributes: [["类型", "活动商店"], ["活动", "月华活动"], ["商品数", "12"], ["货币", "月华碎片"], ["限购", "是"], ["校验", "活动未发布"]] },
      { index: 6005, name: "沙巴克荣誉商店", subtitle: "荣誉商店", category: "沙巴克", level: 24, metric: "荣誉点", status: "published", updated: "昨天 15:18", icon: "crown", tags: ["攻城", "荣誉"], attributes: [["类型", "荣誉商店"], ["归属", "沙巴克"], ["商品数", "24"], ["货币", "荣誉点"], ["限购", "是"], ["刷新", "每周一"]] }
    ]
  });

  types.push({
    id: "npc-scripts", entity: "script", icon: "code", label: "NPC 脚本", singular: "脚本",
    total: 635, draft: 0, issues: 0, source: "外部 NPC 文本（待导入）", tone: "blue",
    description: "NPC 对话、商店、任务、传送和行为脚本，保留来源路径、页面、包含关系和内容哈希。",
    columns: ["key", "name", "category", "level", "metric", "status", "updated"],
    columnLabels: { key: "内容键", name: "脚本文件", category: "语言", level: "行数", metric: "页面", status: "状态", updated: "最近更新" },
    rows: [
      { key: "script.BichonProvince.BorderVillage.Transport", name: "BorderVillage/Transport", subtitle: "Envir/NPCs/BichonProvince/BorderVillage/Transport.txt", category: "CrystalNpc", level: 95, metric: "11 个页面", status: "published", updated: "今天 18:22", icon: "code", tags: ["NPC", "传送"], attributes: [["文件", "Envir/NPCs/BichonProvince/BorderVillage/Transport.txt"], ["页面", "11"], ["行数", "95"], ["语言", "CrystalNpc"], ["调用", "无"], ["内容哈希", "4411fb16…"]] },
      { key: "script.BichonProvince.BichonWall.Blacksmith", name: "BichonWall/Blacksmith", subtitle: "Envir/NPCs/BichonProvince/BichonWall/Blacksmith.txt", category: "CrystalNpc", level: 45, metric: "3 个页面", status: "published", updated: "今天 17:48", icon: "code", tags: ["NPC", "修理"], attributes: [["文件", "Envir/NPCs/BichonProvince/BichonWall/Blacksmith.txt"], ["页面", "3"], ["行数", "45"], ["语言", "CrystalNpc"], ["商店类型", "武器"], ["内容哈希", "7f3a…"]] },
      { key: "script.BichonProvince.BichonWall.Sir.Mogu", name: "BichonWall/Sir.Mogu", subtitle: "Envir/NPCs/BichonProvince/BichonWall/Sir.Mogu.txt", category: "CrystalNpc", level: 31, metric: "包含 1 个页面", status: "validating", updated: "昨天 21:16", icon: "code", tags: ["NPC", "包含"], attributes: [["文件", "Envir/NPCs/BichonProvince/BichonWall/Sir.Mogu.txt"], ["包含", "SystemScripts/SharedNPCS/Tavern.txt @Main"], ["页面", "多个"], ["语言", "CrystalNpc"], ["校验", "等待全量脚本导入"]] }
    ]
  });

  types.push({
    id: "recipes", entity: "recipe", icon: "hammer", label: "配方与锻造", singular: "配方",
    total: 86, draft: 14, issues: 2, source: "外部配方文本（待导入）", tone: "orange",
    description: "锻造、合成与材料配方，包含材料校验、成功率、产出模拟和解锁条件。",
    columns: ["key", "name", "category", "level", "metric", "status", "updated"],
    columnLabels: { key: "内容键", name: "配方", category: "类型", level: "材料数", metric: "产出", status: "状态", updated: "最近更新" },
    rows: [
      { key: "recipe.weapon.flame", name: "烈焰之刃锻造", subtitle: "武器锻造", category: "武器", level: 4, metric: "烈焰之刃 × 1", status: "published", updated: "今天 13:42", icon: "hammer", tags: ["武器", "橙色"], attributes: [["类型", "武器锻造"], ["材料数", "4"], ["成功率", "65%"], ["产出", "烈焰之刃 × 1"], ["解锁", "锻造等级 8"], ["经济评分", "A"]] },
      { key: "recipe.armor.thunder", name: "雷霆战甲合成", subtitle: "衣服合成", category: "衣服", level: 5, metric: "雷霆战甲 × 1", status: "validating", updated: "今天 11:18", icon: "shield", tags: ["衣服", "史诗"], attributes: [["类型", "衣服合成"], ["材料数", "5"], ["成功率", "52%"], ["产出", "雷霆战甲（男） × 1"], ["解锁", "锻造等级 10"], ["经济评分", "A"]] },
      { key: "recipe.potion.moon", name: "月华丹炼制", subtitle: "药品炼制", category: "消耗品", level: 3, metric: "月华丹 × 5", status: "draft", updated: "昨天 22:12", icon: "heart", tags: ["活动", "消耗品"], attributes: [["类型", "药品炼制"], ["材料数", "3"], ["成功率", "85%"], ["产出", "月华丹 × 5"], ["解锁", "活动期间"], ["校验", "材料 9012 不存在"]] },
      { key: "recipe.jewel.soul", name: "灵魂宝石淬炼", subtitle: "宝石淬炼", category: "宝石", level: 3, metric: "灵魂宝石 × 1", status: "published", updated: "昨天 18:36", icon: "sparkles", tags: ["宝石", "稀有"], attributes: [["类型", "宝石淬炼"], ["材料数", "3"], ["成功率", "40%"], ["产出", "灵魂宝石 × 1"], ["解锁", "锻造等级 5"], ["经济评分", "B+"]] },
      { key: "recipe.scroll.teleport", name: "随机传送卷制作", subtitle: "卷轴制作", category: "卷轴", level: 2, metric: "随机传送卷 × 10", status: "published", updated: "昨天 16:22", icon: "file", tags: ["卷轴", "日常"], attributes: [["类型", "卷轴制作"], ["材料数", "2"], ["成功率", "100%"], ["产出", "随机传送卷 × 10"], ["解锁", "锻造等级 1"], ["经济评分", "B"]] }
    ]
  });

  types.push({
    id: "titles", entity: "title", icon: "crown", label: "称号与成就", singular: "称号",
    total: 24, draft: 6, issues: 0, source: "新模型（待建）", tone: "violet",
    description: "独立称号、属性加成、来源、展示优先级、有效期和角色佩戴记录。",
    columns: ["key", "name", "category", "level", "metric", "status", "updated"],
    columnLabels: { key: "内容键", name: "称号", category: "来源", level: "阶段", metric: "属性加成", status: "状态", updated: "最近更新" },
    rows: [
      { key: "title.zone.first", name: "开服先锋", subtitle: "成就称号", category: "开服活动", level: 1, metric: "全属性 +1", status: "published", updated: "今天 10:20", icon: "crown", tags: ["成就", "限时"], attributes: [["来源", "开服活动"], ["阶段", "1"], ["属性", "全属性 +1"], ["有效期", "永久"], ["展示优先级", "80"], ["佩戴角色", "1,282"]] },
      { key: "title.boss.first", name: "祖玛征服者", subtitle: "Boss 首杀", category: "首杀成就", level: 2, metric: "攻击 +3", status: "published", updated: "今天 09:54", icon: "skull", tags: ["首杀", "稀有"], attributes: [["来源", "Boss 首杀"], ["阶段", "2"], ["属性", "攻击 +3"], ["有效期", "永久"], ["展示优先级", "90"], ["佩戴角色", "38"]] },
      { key: "title.arena.top", name: "沙城霸主", subtitle: "攻城称号", category: "沙巴克", level: 3, metric: "HP +5%", status: "validating", updated: "昨天 21:04", icon: "shield", tags: ["攻城", "赛季"], attributes: [["来源", "沙巴克"], ["阶段", "3"], ["属性", "HP +5%"], ["有效期", "7 天"], ["展示优先级", "100"], ["校验", "赛季规则未配置"]] },
      { key: "title.event.moon", name: "月华使者", subtitle: "活动称号", category: "中秋活动", level: 1, metric: "幸运 +2", status: "draft", updated: "昨天 17:41", icon: "sparkles", tags: ["活动", "草稿"], attributes: [["来源", "中秋活动"], ["阶段", "1"], ["属性", "幸运 +2"], ["有效期", "30 天"], ["展示优先级", "60"], ["校验", "活动未发布"]] }
    ]
  });

  types.push({
    id: "buffs", entity: "buff", icon: "sparkles", label: "Buff 与效果", singular: "Buff",
    total: 38, draft: 5, issues: 1, source: "新模型（待建）", tone: "green",
    description: "增益、减益、持续时间、叠加规则、来源和客户端图标/特效绑定。",
    columns: ["key", "name", "category", "level", "metric", "status", "updated"],
    columnLabels: { key: "内容键", name: "Buff", category: "类型", level: "叠加", metric: "持续时间", status: "状态", updated: "最近更新" },
    rows: [
      { key: "buff.attack.up", name: "攻击强化", subtitle: "增益 · 攻击", category: "增益", level: 3, metric: "600 秒", status: "published", updated: "今天 12:10", icon: "zap", tags: ["增益", "可驱散"], attributes: [["类型", "增益"], ["叠加", "3 层"], ["持续时间", "600 秒"], ["效果", "攻击 +5% / 层"], ["来源", "药水 / 技能"], ["图标", "icon_attack_up"]] },
      { key: "buff.poison.red", name: "红毒", subtitle: "减益 · 持续伤害", category: "减益", level: 1, metric: "12 秒", status: "published", updated: "今天 09:43", icon: "drop", tags: ["减益", "可驱散"], attributes: [["类型", "减益"], ["叠加", "不叠加"], ["持续时间", "12 秒"], ["效果", "防御 -2 / 秒"], ["来源", "施毒术"], ["图标", "icon_poison_red"]] },
      { key: "buff.event.moon", name: "月华祝福", subtitle: "活动 · 增益", category: "活动", level: 1, metric: "1,800 秒", status: "draft", updated: "昨天 20:33", icon: "sparkles", tags: ["活动", "草稿"], attributes: [["类型", "活动增益"], ["叠加", "不叠加"], ["持续时间", "1,800 秒"], ["效果", "经验 +20%"], ["来源", "月华活动"], ["校验", "图标缺失"]] },
      { key: "buff.guild.guard", name: "行会守护", subtitle: "增益 · 防御", category: "增益", level: 1, metric: "900 秒", status: "validating", updated: "昨天 18:11", icon: "shield", tags: ["行会", "防御"], attributes: [["类型", "增益"], ["叠加", "不叠加"], ["持续时间", "900 秒"], ["效果", "防御 +8%"], ["来源", "行会技能"], ["校验", "行会技能未建模"]] }
    ]
  });

  types.push({
    id: "events", entity: "event", icon: "calendar", label: "活动与玩法", singular: "活动",
    total: 12, draft: 4, issues: 2, source: "新模型（待建）", tone: "orange",
    description: "活动编排、开放窗口、参与条件、奖励、公告和区服灰度范围。",
    columns: ["key", "name", "category", "level", "metric", "status", "updated"],
    columnLabels: { key: "内容键", name: "活动", category: "类型", level: "参与等级", metric: "开放窗口", status: "状态", updated: "最近更新" },
    rows: [
      { key: "event.moon.festival", name: "月华庆典", subtitle: "限时活动", category: "收集 / 兑换", level: 20, metric: "11-12 → 11-19", status: "validating", updated: "今天 11:05", icon: "calendar", tags: ["限时", "全服"], attributes: [["类型", "收集 / 兑换"], ["参与等级", "20"], ["开放窗口", "11-12 → 11-19"], ["奖励", "称号 / Buff / 宝箱"], ["范围", "全部区服"], ["校验", "2 个奖励内容未发布"]] },
      { key: "event.siege.weekly", name: "沙巴克攻城", subtitle: "周期性玩法", category: "攻城", level: 30, metric: "每周六 20:00", status: "published", updated: "今天 09:22", icon: "crown", tags: ["攻城", "行会"], attributes: [["类型", "攻城"], ["参与等级", "30"], ["开放窗口", "每周六 20:00"], ["奖励", "荣誉 / 税收"], ["范围", "指定区服"], ["报名", "行会会长"]] },
      { key: "event.arena.season", name: "竞技场 S3", subtitle: "赛季活动", category: "PVP", level: 35, metric: "11-01 → 12-31", status: "published", updated: "昨天 19:36", icon: "target", tags: ["PVP", "赛季"], attributes: [["类型", "PVP"], ["参与等级", "35"], ["开放窗口", "11-01 → 12-31"], ["奖励", "赛季称号 / 元宝"], ["范围", "S1-S6"], ["匹配", "积分段位"]] },
      { key: "event.double.exp", name: "周末双倍经验", subtitle: "常规活动", category: "增益", level: 1, metric: "周六 / 周日", status: "draft", updated: "昨天 15:12", icon: "zap", tags: ["常规", "草稿"], attributes: [["类型", "增益"], ["参与等级", "1"], ["开放窗口", "周六 / 周日 10:00"], ["奖励", "经验 ×2"], ["范围", "全部区服"], ["校验", "Buff 未发布"]] }
    ]
  });

  types.push({
    id: "respawns", entity: "respawn", icon: "route", label: "刷怪与刷新点", singular: "刷新点",
    total: 6341, draft: 0, issues: 0, source: "Server.MirDB", tone: "green",
    description: "地图刷怪点位、怪物组合、刷新半径、数量、间隔和区域容量。",
    columns: ["index", "name", "category", "coord", "level", "metric", "status", "updated"],
    columnLabels: { index: "旧索引", name: "刷新点", category: "地图", coord: "坐标", level: "怪物数", metric: "刷新间隔", status: "状态", updated: "最近更新" },
    rows: [
      { index: 7001, name: "祖玛寺庙三层 · 东区", subtitle: "Boss 刷新点", category: "祖玛寺庙三层", coord: "132, 88", level: 2, metric: "120 分钟", status: "published", updated: "今天 17:04", icon: "spawn", tags: ["Boss", "固定点"], attributes: [["地图", "祖玛寺庙三层"], ["怪物数", "2"], ["刷新半径", "3 格"], ["刷新间隔", "120 分钟"], ["容量", "2"], ["刷新条件", "无"]] },
      { index: 7002, name: "赤月峡谷北 · 西区", subtitle: "精英刷新点", category: "赤月峡谷北", coord: "168, 72", level: 6, metric: "30 分钟", status: "published", updated: "今天 16:47", icon: "spawn", tags: ["精英", "区域"], attributes: [["地图", "赤月峡谷北"], ["怪物数", "6"], ["刷新半径", "8 格"], ["刷新间隔", "30 分钟"], ["容量", "6"], ["刷新条件", "无"]] },
      { index: 7003, name: "石墓阵 · 中心", subtitle: "普通刷新点", category: "石墓阵", coord: "70, 70", level: 18, metric: "12 分钟", status: "validating", updated: "今天 12:39", icon: "route", tags: ["普通", "密集"], attributes: [["地图", "石墓阵"], ["怪物数", "18"], ["刷新半径", "10 格"], ["刷新间隔", "12 分钟"], ["容量", "24"], ["校验", "区域重叠"]] },
      { index: 7004, name: "苍月岛 · 海岸", subtitle: "普通刷新点", category: "苍月岛", coord: "180, 240", level: 12, metric: "15 分钟", status: "published", updated: "昨天 20:58", icon: "mapPin", tags: ["野外", "材料"], attributes: [["地图", "苍月岛"], ["怪物数", "12"], ["刷新半径", "12 格"], ["刷新间隔", "15 分钟"], ["容量", "20"], ["刷新条件", "无"]] },
      { index: 7005, name: "赤月峡谷北 · 虹魔区", subtitle: "精英刷新点", category: "赤月峡谷北", coord: "88, 132", level: 6, metric: "30 分钟", status: "published", updated: "今天 15:16", icon: "spawn", tags: ["精英", "区域"], attributes: [["地图", "赤月峡谷北"], ["坐标", "88, 132"], ["怪物", "虹魔猪卫"], ["怪物数", "6"], ["刷新半径", "8 格"], ["刷新间隔", "30 分钟"]] },
      { index: 7006, name: "祖玛寺庙三层 · 卫士走廊", subtitle: "普通刷新点", category: "祖玛寺庙三层", coord: "40, 44", level: 12, metric: "18 分钟", status: "published", updated: "今天 14:58", icon: "route", tags: ["普通", "走廊"], attributes: [["地图", "祖玛寺庙三层"], ["坐标", "40, 44"], ["怪物", "祖玛卫士"], ["怪物数", "12"], ["刷新半径", "6 格"], ["刷新间隔", "18 分钟"]] },
      { index: 7007, name: "沃玛寺庙二层 · 教主房", subtitle: "Boss 刷新点", category: "沃玛寺庙二层", coord: "80, 96", level: 1, metric: "180 分钟", status: "published", updated: "今天 13:38", icon: "crown", tags: ["Boss", "固定点"], attributes: [["地图", "沃玛寺庙二层"], ["坐标", "80, 96"], ["怪物", "沃玛教主"], ["怪物数", "1"], ["刷新半径", "2 格"], ["刷新间隔", "180 分钟"]] }
    ]
  });

  types.push({
    id: "resources", entity: "asset", icon: "image", label: "地图与资源文件", singular: "资源",
    total: 1, draft: 0, issues: 0, source: "内容根目录", tone: "blue",
    description: "地图、预览图和后续图库/音效资源的路径、大小、哈希、格式、尺寸与阻挡统计。",
    columns: ["key", "name", "category", "level", "metric", "status", "updated"],
    columnLabels: { key: "内容键", name: "文件", category: "类型", level: "尺寸 / 格子", metric: "大小", status: "状态", updated: "最近更新" },
    rows: [
      { key: "asset.maps.0.map", name: "Maps/0.map", subtitle: "地图资源", category: "map", level: "10 × 10", metric: "1.2 KB", status: "published", updated: "今天 06:00", icon: "map", tags: ["地图", "已校验"], attributes: [["类型", "map"], ["尺寸", "10 × 10"], ["阻挡格", "1"], ["门格", "1"], ["哈希", "3ab00f38…"]] }
    ]
  });

  window.MirContentTypes = (window.MirContentTypes || []).concat(types);
})();
