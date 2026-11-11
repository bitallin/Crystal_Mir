(function () {
  const types = [];

  types.push({
    id: "items", entity: "item", icon: "sword", label: "物品与装备", singular: "物品",
    total: 1628, draft: 23, issues: 4, source: "Server.MirDB", tone: "gold",
    description: "装备、消耗品、材料与任务道具的全部字段、资源绑定、价格和掉落引用。",
    columns: ["index", "name", "category", "level", "metric", "status", "updated"],
    columnLabels: { index: "旧索引", name: "物品", category: "分类", level: "需求等级", metric: "默认售价", status: "状态", updated: "最近更新" },
    rows: [
      { index: 1001, name: "裁决之杖", subtitle: "武器 · 战士", category: "武器", level: 45, metric: "128,000 金币", status: "published", updated: "今天 18:20", icon: "sword", tags: ["稀有", "可交易"], attributes: [["需求等级", "45"], ["攻击", "5 - 35"], ["重量", "28"], ["持久", "32 / 32"], ["售价", "128,000 金币"], ["绑定", "拾取后绑定"]] },
      { index: 1002, name: "雷霆战甲（男）", subtitle: "衣服 · 战士", category: "衣服", level: 42, metric: "96,500 金币", status: "published", updated: "今天 16:42", icon: "shield", tags: ["史诗", "可交易"], attributes: [["需求等级", "42"], ["防御", "8 - 18"], ["魔御", "3 - 7"], ["重量", "38"], ["售价", "96,500 金币"], ["绑定", "无"]] },
      { index: 1003, name: "天尊道袍（女）", subtitle: "衣服 · 道士", category: "衣服", level: 40, metric: "88,000 金币", status: "validating", updated: "今天 15:18", icon: "shield", tags: ["史诗"], attributes: [["需求等级", "40"], ["防御", "6 - 14"], ["道术", "3 - 8"], ["重量", "26"], ["售价", "88,000 金币"], ["绑定", "无"]] },
      { index: 1004, name: "圣战宝甲", subtitle: "衣服 · 战士", category: "衣服", level: 38, metric: "72,000 金币", status: "draft", updated: "今天 14:03", icon: "shield", tags: ["稀有", "草稿"], attributes: [["需求等级", "38"], ["防御", "7 - 15"], ["重量", "34"], ["售价", "72,000 金币"], ["绑定", "穿戴后绑定"], ["校验", "缺少图标引用"]] },
      { index: 1005, name: "流星项链", subtitle: "项链 · 通用", category: "首饰", level: 35, metric: "46,000 金币", status: "published", updated: "昨天 21:31", icon: "star", tags: ["普通", "可交易"], attributes: [["需求等级", "35"], ["攻击", "1 - 6"], ["魔法", "1 - 5"], ["重量", "3"], ["售价", "46,000 金币"], ["绑定", "无"]] },
      { index: 1006, name: "屠龙", subtitle: "武器 · 战士", category: "武器", level: 50, metric: "—", status: "published", updated: "今天 16:58", icon: "sword", tags: ["Boss", "稀有"], attributes: [["需求等级", "50"], ["攻击", "8 - 42"], ["重量", "38"], ["售价", "绑定"], ["来源", "祖玛教主"], ["绑定", "拾取后绑定"]] },
      { index: 1007, name: "骨玉权杖", subtitle: "武器 · 法师", category: "武器", level: 45, metric: "380,000 金币", status: "published", updated: "今天 15:22", icon: "wand", tags: ["Boss", "法师"], attributes: [["需求等级", "45"], ["魔法", "4 - 28"], ["重量", "20"], ["来源", "沃玛教主 / 赤月恶魔"], ["绑定", "无"]] },
      { index: 1008, name: "龙纹剑", subtitle: "武器 · 道士", category: "武器", level: 45, metric: "320,000 金币", status: "published", updated: "今天 14:46", icon: "sword", tags: ["Boss", "道士"], attributes: [["需求等级", "45"], ["道术", "4 - 26"], ["重量", "22"], ["来源", "赤月恶魔"], ["绑定", "无"]] },
      { index: 1009, name: "法神披风", subtitle: "衣服 · 法师", category: "衣服", level: 42, metric: "210,000 金币", status: "published", updated: "今天 13:52", icon: "shield", tags: ["Boss", "法师"], attributes: [["需求等级", "42"], ["防御", "7 - 16"], ["魔法", "3 - 7"], ["来源", "祖玛教主"], ["绑定", "无"]] },
      { index: 1010, name: "祖玛头像", subtitle: "材料 · 任务", category: "材料", level: 1, metric: "12,000 金币", status: "published", updated: "今天 12:37", icon: "package", tags: ["材料", "任务"], attributes: [["类型", "任务材料"], ["堆叠", "20"], ["来源", "祖玛教主 / 祖玛卫士"], ["用途", "行会与任务"]] },
      { index: 1011, name: "祝福油", subtitle: "消耗品 · 强化", category: "消耗品", level: 1, metric: "8,000 金币", status: "published", updated: "今天 11:41", icon: "drop", tags: ["消耗品"], attributes: [["类型", "武器强化"], ["堆叠", "10"], ["来源", "Boss 掉落"], ["失败惩罚", "无"]] },
      { index: 1012, name: "赤月魔血", subtitle: "材料 · 锻造", category: "材料", level: 1, metric: "6,500 金币", status: "published", updated: "今天 10:35", icon: "package", tags: ["材料", "锻造"], attributes: [["类型", "锻造材料"], ["堆叠", "50"], ["来源", "赤月恶魔"], ["用途", "高级装备"]] },
      { index: 1013, name: "太阳水", subtitle: "药品 · 恢复", category: "药品", level: 1, metric: "1,200 金币", status: "published", updated: "昨天 22:11", icon: "heart", tags: ["药品"], attributes: [["类型", "恢复"], ["回复", "HP / MP 50%"], ["冷却", "2 秒"], ["来源", "怪物 / 商店"]] },
      { index: 1014, name: "回城卷", subtitle: "消耗品 · 传送", category: "消耗品", level: 1, metric: "500 金币", status: "published", updated: "昨天 21:43", icon: "teleport", tags: ["消耗品", "传送"], attributes: [["类型", "传送"], ["目标", "最近主城"], ["堆叠", "20"], ["来源", "怪物 / 商店"]] },
      { index: 1015, name: "沃玛号角", subtitle: "任务道具 · 行会", category: "任务道具", level: 1, metric: "—", status: "published", updated: "昨天 20:30", icon: "crown", tags: ["任务", "行会"], attributes: [["类型", "行会任务"], ["堆叠", "1"], ["来源", "沃玛教主"], ["用途", "创建行会"]] },
      { index: 1016, name: "月华宝箱", subtitle: "活动道具 · 限时", category: "活动道具", level: 1, metric: "—", status: "draft", updated: "昨天 18:24", icon: "gift", tags: ["活动"], attributes: [["类型", "活动奖励"], ["堆叠", "99"], ["来源", "月华活动"], ["校验", "活动未发布"]] }
    ]
  });

  types.push({
    id: "monsters", entity: "monster", icon: "skull", label: "怪物与 AI", singular: "怪物",
    total: 555, draft: 11, issues: 2, source: "Server.MirDB", tone: "red",
    description: "基础属性、AI Profile、技能、掉落组、刷新点与战斗行为参数。",
    columns: ["index", "name", "level", "metric", "category", "status", "updated"],
    columnLabels: { index: "旧索引", name: "怪物", level: "等级", metric: "生命值", category: "AI 配置", status: "状态", updated: "最近更新" },
    rows: [
      { index: 2001, name: "祖玛教主", subtitle: "Boss · 祖玛寺庙", level: 55, metric: "145,000", category: "Boss · 召唤", status: "published", updated: "今天 17:55", icon: "crown", tags: ["Boss", "召唤"], attributes: [["等级", "55"], ["生命值", "145,000"], ["经验", "82,000"], ["AI Profile", "boss_zuma_summoner"], ["攻击", "68 - 128"], ["防御", "22 - 42"]] },
      { index: 2002, name: "赤月恶魔", subtitle: "Boss · 赤月峡谷", level: 60, metric: "220,000", category: "Boss · 范围", status: "draft", updated: "今天 16:20", icon: "skull", tags: ["Boss", "范围伤害"], attributes: [["等级", "60"], ["生命值", "220,000"], ["经验", "120,000"], ["AI Profile", "boss_redmoon_aoe"], ["攻击", "86 - 156"], ["防御", "28 - 52"]] },
      { index: 2003, name: "虹魔猪卫", subtitle: "精英 · 虹魔神殿", level: 48, metric: "68,000", category: "Guard · 追击", status: "validating", updated: "今天 13:47", icon: "shield", tags: ["精英", "守卫"], attributes: [["等级", "48"], ["生命值", "68,000"], ["经验", "18,000"], ["AI Profile", "guard_chase"], ["攻击", "38 - 72"], ["防御", "16 - 30"]] },
      { index: 2004, name: "白野猪", subtitle: "普通 · 石墓阵", level: 38, metric: "32,000", category: "Aggressive", status: "published", updated: "昨天 19:12", icon: "monster", tags: ["普通", "主动"], attributes: [["等级", "38"], ["生命值", "32,000"], ["经验", "8,500"], ["AI Profile", "aggressive_melee"], ["攻击", "22 - 45"], ["防御", "8 - 16"]] },
      { index: 2005, name: "双头金刚", subtitle: "精英 · 苍月岛", level: 52, metric: "118,000", category: "Elite · 重击", status: "published", updated: "昨天 17:28", icon: "monster", tags: ["精英", "重击"], attributes: [["等级", "52"], ["生命值", "118,000"], ["经验", "36,000"], ["AI Profile", "elite_heavy_strike"], ["攻击", "52 - 98"], ["防御", "20 - 36"]] },
      { index: 2006, name: "沃玛教主", subtitle: "Boss · 沃玛寺庙", level: 50, metric: "126,000", category: "Boss · 火焰", status: "published", updated: "今天 12:44", icon: "crown", tags: ["Boss", "任务道具"], attributes: [["等级", "50"], ["生命值", "126,000"], ["经验", "64,000"], ["AI Profile", "boss_woma_fire"], ["攻击", "58 - 112"], ["防御", "18 - 34"]] },
      { index: 2007, name: "祖玛卫士", subtitle: "普通 · 祖玛寺庙", level: 42, metric: "24,000", category: "Guard · 近战", status: "published", updated: "今天 10:18", icon: "shield", tags: ["普通", "守卫"], attributes: [["等级", "42"], ["生命值", "24,000"], ["经验", "6,200"], ["AI Profile", "guard_melee"], ["攻击", "28 - 58"], ["防御", "12 - 22"]] }
    ]
  });

  types.push({
    id: "maps", entity: "map", icon: "map", label: "地图与传送", singular: "地图",
    total: 463, draft: 7, issues: 1, source: "Server.MirDB + .map", tone: "green",
    description: "地图元数据、阻挡文件、安全区、传送点、刷怪区域与地图资源引用。",
    columns: ["key", "name", "metric", "category", "level", "status", "updated"],
    columnLabels: { key: "文件名", name: "地图", metric: "尺寸", category: "安全区", level: "刷怪点", status: "状态", updated: "最近更新" },
    rows: [
      { key: "3.map", name: "祖玛寺庙三层", subtitle: "地下城 · 组队", metric: "180 × 180", category: "0 个安全区", level: 12, status: "published", updated: "今天 17:10", icon: "map", tags: ["地下城", "Boss"], attributes: [["文件名", "3.map"], ["地图尺寸", "180 × 180"], ["安全区", "0"], ["刷怪点", "12"], ["传送点", "4"], ["允许 PK", "是"]] },
      { key: "D2001.map", name: "比奇城", subtitle: "主城 · 安全区", metric: "320 × 320", category: "4 个安全区", level: 0, status: "published", updated: "今天 15:44", icon: "globe", tags: ["主城", "NPC 密集"], attributes: [["文件名", "D2001.map"], ["地图尺寸", "320 × 320"], ["安全区", "4"], ["NPC", "38"], ["传送点", "9"], ["允许 PK", "否"]] },
      { key: "4.map", name: "赤月峡谷北", subtitle: "野外 · 高危险", metric: "240 × 240", category: "0 个安全区", level: 18, status: "validating", updated: "今天 14:35", icon: "map", tags: ["野外", "高危险"], attributes: [["文件名", "4.map"], ["地图尺寸", "240 × 240"], ["安全区", "0"], ["刷怪点", "18"], ["传送点", "3"], ["允许 PK", "是"]] },
      { key: "D401.map", name: "苍月岛", subtitle: "野外 · 海岛", metric: "300 × 300", category: "2 个安全区", level: 9, status: "published", updated: "昨天 22:09", icon: "compass", tags: ["野外", "海岛"], attributes: [["文件名", "D401.map"], ["地图尺寸", "300 × 300"], ["安全区", "2"], ["NPC", "17"], ["传送点", "6"], ["允许 PK", "是"]] },
      { key: "0150.map", name: "石墓阵", subtitle: "地下城 · 迷宫", metric: "160 × 160", category: "0 个安全区", level: 15, status: "draft", updated: "昨天 18:52", icon: "route", tags: ["迷宫", "草稿"], attributes: [["文件名", "0150.map"], ["地图尺寸", "160 × 160"], ["安全区", "0"], ["刷怪点", "15"], ["传送点", "2"], ["校验", "阻挡文件哈希不一致"]] },
      { key: "1.map", name: "祖玛寺庙一层", subtitle: "地下城 · 入口", metric: "180 × 180", category: "0 个安全区", level: 8, status: "published", updated: "今天 16:02", icon: "map", tags: ["地下城", "入口"], attributes: [["文件名", "1.map"], ["地图尺寸", "180 × 180"], ["入口坐标", "42, 168"], ["传送点", "2"], ["允许 PK", "是"]] },
      { key: "2.map", name: "祖玛寺庙二层", subtitle: "地下城 · 中段", metric: "180 × 180", category: "0 个安全区", level: 10, status: "published", updated: "今天 15:57", icon: "map", tags: ["地下城", "楼梯"], attributes: [["文件名", "2.map"], ["地图尺寸", "180 × 180"], ["上行坐标", "126, 154"], ["下行坐标", "42, 22"], ["刷怪点", "15"]] },
      { key: "zuma-boss.map", name: "祖玛教主大厅", subtitle: "Boss 房 · 高危险", metric: "120 × 120", category: "0 个安全区", level: 2, status: "published", updated: "今天 15:39", icon: "crown", tags: ["Boss", "封闭"], attributes: [["文件名", "zuma-boss.map"], ["地图尺寸", "120 × 120"], ["入口坐标", "60, 108"], ["Boss", "祖玛教主"], ["允许 PK", "是"]] },
      { key: "D3002.map", name: "沃玛寺庙二层", subtitle: "地下城 · Boss 层", metric: "160 × 160", category: "0 个安全区", level: 9, status: "published", updated: "今天 11:18", icon: "map", tags: ["地下城", "沃玛"], attributes: [["文件名", "D3002.map"], ["地图尺寸", "160 × 160"], ["上行坐标", "80, 150"], ["下行坐标", "42, 22"], ["Boss", "沃玛教主"]] },
      { key: "D2003.map", name: "盟重省", subtitle: "主城 · 传送枢纽", metric: "360 × 360", category: "1 个安全区", level: 0, status: "published", updated: "昨天 23:12", icon: "globe", tags: ["主城", "传送"], attributes: [["文件名", "D2003.map"], ["地图尺寸", "360 × 360"], ["安全区", "1"], ["NPC", "21"], ["传送目标", "7"]] }
    ]
  });

  types.push({
    id: "npcs", entity: "npc", icon: "message", label: "NPC 与对话", singular: "NPC",
    total: 375, draft: 9, issues: 3, source: "Server.MirDB + 脚本", tone: "blue",
    description: "NPC 所在地图、商店、仓库、传送、任务和文本 DSL 对话脚本。",
    columns: ["index", "name", "category", "coord", "metric", "status", "updated"],
    columnLabels: { index: "旧索引", name: "NPC", category: "所在地图", coord: "坐标", metric: "脚本", status: "状态", updated: "最近更新" },
    rows: [
      { index: 3001, name: "比奇武器商", subtitle: "商店 · 武器", category: "比奇城", coord: "315, 262", metric: "merchant_weapon_01", level: 18, status: "published", updated: "今天 16:11", icon: "bag", tags: ["商店", "武器"], attributes: [["所在地图", "比奇城"], ["脚本", "merchant_weapon_01"], ["对话段", "18"], ["商品", "42"], ["任务关联", "3"], ["最后校验", "通过"]] },
      { index: 3002, name: "仓库管理员", subtitle: "仓库 · 通用", category: "比奇城", coord: "290, 258", metric: "storage_keeper", level: 8, status: "published", updated: "今天 12:28", icon: "archive", tags: ["仓库"], attributes: [["所在地图", "比奇城"], ["脚本", "storage_keeper"], ["对话段", "8"], ["商品", "0"], ["任务关联", "0"], ["最后校验", "通过"]] },
      { index: 3003, name: "盟重传送员", subtitle: "传送 · 跨地图", category: "盟重省", coord: "180, 180", metric: "teleport_mentor", level: 24, status: "validating", updated: "今天 11:54", icon: "teleport", tags: ["传送", "校验中"], attributes: [["所在地图", "盟重省"], ["脚本", "teleport_mentor"], ["对话段", "24"], ["传送目标", "7"], ["价格", "按目标"], ["校验", "2 个目标地图未发布"]] },
      { index: 3004, name: "任务使者", subtitle: "任务 · 主线", category: "比奇城", coord: "310, 270", metric: "quest_ambassador", level: 36, status: "draft", updated: "昨天 20:16", icon: "file", tags: ["任务", "草稿"], attributes: [["所在地图", "比奇城"], ["脚本", "quest_ambassador"], ["对话段", "36"], ["任务", "12"], ["任务关联", "12"], ["校验", "引用任务 104 不存在"]] },
      { index: 3005, name: "药店老板", subtitle: "商店 · 药品", category: "苍月岛", coord: "160, 112", metric: "merchant_potion", level: 12, status: "published", updated: "昨天 16:40", icon: "heart", tags: ["商店", "药品"], attributes: [["所在地图", "苍月岛"], ["脚本", "merchant_potion"], ["对话段", "12"], ["商品", "18"], ["刷新", "每日 06:00"], ["最后校验", "通过"]] },
      { index: 3006, name: "比奇传送员", subtitle: "传送 · 区域", category: "比奇城", coord: "320, 274", metric: "teleport_bichon", level: 14, status: "published", updated: "今天 10:26", icon: "teleport", tags: ["传送"], attributes: [["所在地图", "比奇城"], ["坐标", "320, 274"], ["脚本", "teleport_bichon"], ["传送目标", "5"], ["对话段", "14"], ["最后校验", "通过"]] },
      { index: 3007, name: "沙巴克管理员", subtitle: "攻城 · 行会", category: "沙巴克", coord: "120, 120", metric: "sabuk_manager", level: 22, status: "published", updated: "今天 09:48", icon: "crown", tags: ["攻城", "行会"], attributes: [["所在地图", "沙巴克"], ["坐标", "120, 120"], ["脚本", "sabuk_manager"], ["功能", "攻城 / 城主"], ["对话段", "22"], ["最后校验", "通过"]] }
    ]
  });

  types.push({
    id: "drops", entity: "drop", icon: "drop", label: "掉落与爆率", singular: "掉落组",
    total: 141, draft: 28, issues: 5, source: "外部掉落文本（M1 导入）", tone: "orange",
    description: "按怪物、地图和活动组合的掉落组，支持概率模拟、期望产出与冲突检查。",
    columns: ["key", "name", "category", "metric", "level", "status", "updated"],
    columnLabels: { key: "内容键", name: "掉落组", category: "关联怪物", metric: "期望产出", level: "物品数", status: "状态", updated: "最近更新" },
    rows: [
      { key: "drop.zuma.boss", name: "祖玛教主 · 核心掉落", subtitle: "Boss 掉落组", category: "祖玛教主", metric: "2.84 件 / 次", level: 18, status: "published", updated: "今天 17:21", icon: "drop", tags: ["Boss", "核心"], attributes: [["关联怪物", "祖玛教主"], ["物品数", "18"], ["必掉", "2"], ["期望产出", "2.84 件 / 次"], ["稀有期望", "0.12 件 / 次"], ["经济评分", "A"]] },
      { key: "drop.redmoon.lord", name: "赤月恶魔 · 稀有掉落", subtitle: "Boss 掉落组", category: "赤月恶魔", metric: "3.16 件 / 次", level: 24, status: "validating", updated: "今天 16:06", icon: "star", tags: ["Boss", "高价值"], attributes: [["关联怪物", "赤月恶魔"], ["物品数", "24"], ["必掉", "1"], ["期望产出", "3.16 件 / 次"], ["稀有期望", "0.21 件 / 次"], ["经济评分", "A+"]] },
      { key: "drop.wildboar.common", name: "白野猪 · 普通掉落", subtitle: "普通怪物掉落组", category: "白野猪", metric: "0.72 件 / 次", level: 9, status: "published", updated: "今天 11:43", icon: "bag", tags: ["普通", "材料"], attributes: [["关联怪物", "白野猪"], ["物品数", "9"], ["必掉", "0"], ["期望产出", "0.72 件 / 次"], ["金币期望", "1,840"], ["经济评分", "B"]] },
      { key: "drop.stone.tomb", name: "石墓阵 · 地图掉落", subtitle: "地图掉落组", category: "石墓阵", metric: "1.08 件 / 次", level: 12, status: "draft", updated: "昨天 22:35", icon: "map", tags: ["地图", "草稿"], attributes: [["关联地图", "石墓阵"], ["物品数", "12"], ["必掉", "0"], ["期望产出", "1.08 件 / 次"], ["稀有期望", "0.04 件 / 次"], ["校验", "物品 1308 已删除"]] },
      { key: "drop.event.moon", name: "中秋活动 · 月华宝箱", subtitle: "活动掉落组", category: "全服活动", metric: "0.18 件 / 次", level: 6, status: "draft", updated: "昨天 18:24", icon: "gift", tags: ["活动", "限时"], attributes: [["关联活动", "中秋活动"], ["物品数", "6"], ["必掉", "0"], ["期望产出", "0.18 件 / 次"], ["活动窗口", "7 天"], ["校验", "活动未发布"]] }
    ]
  });

  types.push({
    id: "skills", entity: "magic", icon: "wand", label: "技能与魔法", singular: "技能",
    total: 111, draft: 4, issues: 1, source: "Server.MirDB", tone: "violet",
    description: "技能学习条件、职业、等级、消耗、冷却、效果参数与客户端特效引用。",
    columns: ["index", "name", "category", "level", "metric", "status", "updated"],
    columnLabels: { index: "旧索引", name: "技能", category: "职业", level: "学习等级", metric: "冷却", status: "状态", updated: "最近更新" },
    rows: [
      { index: 4001, name: "烈火剑法", subtitle: "战士 · 主动", category: "战士", level: 35, metric: "8.0 秒", status: "published", updated: "今天 14:50", icon: "zap", tags: ["主动", "爆发"], attributes: [["职业", "战士"], ["学习等级", "35"], ["冷却", "8.0 秒"], ["消耗", "12 MP"], ["伤害倍率", "2.4×"], ["特效", "effect_fire_slash"]] },
      { index: 4002, name: "召唤神兽", subtitle: "道士 · 召唤", category: "道士", level: 35, metric: "30.0 秒", status: "published", updated: "今天 13:09", icon: "sparkles", tags: ["召唤", "持续"], attributes: [["职业", "道士"], ["学习等级", "35"], ["冷却", "30.0 秒"], ["消耗", "40 MP"], ["持续", "600 秒"], ["宠物模板", "divine_beast"]] },
      { index: 4003, name: "冰咆哮", subtitle: "法师 · 范围", category: "法师", level: 32, metric: "6.5 秒", status: "validating", updated: "今天 11:26", icon: "cloud", tags: ["范围", "控制"], attributes: [["职业", "法师"], ["学习等级", "32"], ["冷却", "6.5 秒"], ["消耗", "32 MP"], ["范围", "5 × 5"], ["特效", "effect_ice_roar"]] },
      { index: 4004, name: "施毒术", subtitle: "道士 · 减益", category: "道士", level: 18, metric: "3.0 秒", status: "published", updated: "昨天 19:34", icon: "drop", tags: ["减益", "持续"], attributes: [["职业", "道士"], ["学习等级", "18"], ["冷却", "3.0 秒"], ["消耗", "8 MP"], ["持续", "12 秒"], ["毒药类型", "红毒 / 绿毒"]] },
      { index: 4005, name: "抗拒火环", subtitle: "法师 · 位移", category: "法师", level: 22, metric: "4.5 秒", status: "draft", updated: "昨天 17:15", icon: "target", tags: ["位移", "草稿"], attributes: [["职业", "法师"], ["学习等级", "22"], ["冷却", "4.5 秒"], ["消耗", "15 MP"], ["击退距离", "2 格"], ["校验", "缺少学习来源"]] }
    ]
  });

  window.MirContentTypes = (window.MirContentTypes || []).concat(types);
})();
