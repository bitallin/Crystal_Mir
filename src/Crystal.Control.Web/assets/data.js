(function () {
  const NAV = [
    {
      label: "总览",
      items: [
        { id: "dashboard", label: "运营驾驶舱", icon: "home" },
        { id: "prototype-map", label: "后台原型地图", icon: "flow" }
      ]
    },
    {
      label: "内容中心",
      items: [
        { id: "content-overview", label: "内容总览", icon: "layers" },
        { id: "content/items", label: "物品与装备", icon: "sword", badge: "1.6k" },
        { id: "content/monsters", label: "怪物与 AI", icon: "skull", badge: "555" },
        { id: "content/maps", label: "地图与传送", icon: "map", badge: "463" },
        { id: "world-map", label: "地图拓扑与坐标", icon: "mapPin" },
        { id: "content/npcs", label: "NPC 与对话", icon: "message", badge: "375" },
        { id: "content/drops", label: "掉落与爆率", icon: "drop", badge: "!" },
        { id: "content-relations", label: "世界关系图谱", icon: "flow" },
        { id: "content/skills", label: "技能与魔法", icon: "wand", badge: "111" },
        { id: "content/scripts", label: "脚本工作台", icon: "code" },
        { id: "import-runs", label: "导入批次", icon: "upload" },
        { id: "content-releases", label: "发布中心", icon: "rocket", badge: "3" },
        { id: "assets", label: "资源库", icon: "image" }
      ]
    },
    {
      label: "区服运营",
      items: [
        { id: "zones", label: "区服总览", icon: "server", badge: "12" },
        { id: "zone-templates", label: "开区模板", icon: "copy" },
        { id: "gm", label: "GM 工作台", icon: "command" },
        { id: "announcements", label: "公告与活动", icon: "megaphone" },
        { id: "merges", label: "合区中心", icon: "merge" }
      ]
    },
    {
      label: "玩家与交易",
      items: [
        { id: "players", label: "玩家查询", icon: "users" },
        { id: "orders", label: "订单与发货", icon: "receipt" },
        { id: "reconciliation", label: "财务对账", icon: "scale" }
      ]
    },
    {
      label: "权限与安全",
      items: [
        { id: "approvals", label: "审批中心", icon: "check", badge: "4", badgeClass: "danger" },
        { id: "accounts", label: "管理账号", icon: "user" },
        { id: "roles", label: "角色权限", icon: "shield" },
        { id: "audit", label: "审计日志", icon: "audit" }
      ]
    },
    {
      label: "平台",
      items: [
        { id: "monitoring", label: "运行监控", icon: "activity" },
        { id: "jobs", label: "任务中心", icon: "queue", badge: "2" },
        { id: "licenses", label: "许可与合规", icon: "book" },
        { id: "settings", label: "系统设置", icon: "settings" }
      ]
    }
  ];

  const ZONES = [
    { id: "legend-01", name: "传奇一区 · 龙腾", short: "龙腾", status: "open", players: 1842, capacity: 3000, tick: 18, release: "2026.11.09.1", uptime: "42 天 6 小时", host: "mir-01", port: 7001, maps: 128, guilds: 36, revenue: "¥ 62,480", region: "华东", gateway: "gw-east-1", health: 99.98 },
    { id: "legend-02", name: "传奇二区 · 烈焰", short: "烈焰", status: "open", players: 1608, capacity: 3000, tick: 22, release: "2026.11.09.1", uptime: "39 天 12 小时", host: "mir-02", port: 7002, maps: 128, guilds: 31, revenue: "¥ 48,210", region: "华南", gateway: "gw-south-1", health: 99.94 },
    { id: "legend-03", name: "传奇三区 · 逐月", short: "逐月", status: "open", players: 1329, capacity: 2500, tick: 26, release: "2026.11.08.2", uptime: "35 天 8 小时", host: "mir-03", port: 7003, maps: 126, guilds: 28, revenue: "¥ 35,760", region: "华北", gateway: "gw-north-1", health: 99.87 },
    { id: "legend-04", name: "传奇四区 · 沙城", short: "沙城", status: "maintenance", players: 0, capacity: 2500, tick: 0, release: "2026.11.10.0-rc2", uptime: "维护 00:42", host: "mir-04", port: 7004, maps: 128, guilds: 24, revenue: "¥ 21,320", region: "华东", gateway: "gw-east-2", health: 100 },
    { id: "legend-05", name: "传奇五区 · 赤月", short: "赤月", status: "open", players: 986, capacity: 2000, tick: 31, release: "2026.11.08.2", uptime: "18 天 2 小时", host: "mir-05", port: 7005, maps: 120, guilds: 19, revenue: "¥ 12,860", region: "西南", gateway: "gw-west-1", health: 99.63 },
    { id: "legend-06", name: "传奇六区 · 苍月", short: "苍月", status: "warming", players: 0, capacity: 2000, tick: 0, release: "2026.11.10.0", uptime: "预热 00:18", host: "mir-06", port: 7006, maps: 128, guilds: 0, revenue: "¥ 0", region: "华中", gateway: "gw-central-1", health: 100 }
  ];

  const RELEASES = [
    { id: "rel-20261109-1", version: "2026.11.09.1", status: "active", channel: "stable", zones: 5, entities: 9734, hash: "7e7a6dd14c091b63", author: "林策", published: "2026-11-09 22:10", notes: "祖玛掉落调整；修复 3 个物品图标引用。" },
    { id: "rel-20261110-rc2", version: "2026.11.10.0-rc2", status: "signed", channel: "staging", zones: 1, entities: 9741, hash: "b41e8a696de771ce", author: "周遥", published: "2026-11-10 15:22", notes: "月华活动预置内容，仅四区灰度。" },
    { id: "rel-20261110-rc1", version: "2026.11.10.0-rc1", status: "validating", channel: "staging", zones: 0, entities: 9741, hash: "27fa9c412a18de30", author: "周遥", published: "2026-11-10 14:48", notes: "月华活动内容包，等待资源校验。" },
    { id: "rel-20261108-2", version: "2026.11.08.2", status: "archived", channel: "stable", zones: 2, entities: 9728, hash: "f11ac9b4078b22d9", author: "林策", published: "2026-11-08 20:06", notes: "技能冷却修正与商城推荐位。" },
    { id: "rel-20261108-1", version: "2026.11.08.1", status: "rolled_back", channel: "stable", zones: 0, entities: 9728, hash: "9c8e17d6c6a49bd1", author: "陈屿", published: "2026-11-08 18:31", notes: "因地图阻挡哈希不一致回滚。" }
  ];

  const IMPORT_RUNS = [
    { id: 32, source: "Server.MirDB", hash: "37358b0d95eafe45", version: 117, status: "imported", count: 9734, started: "2026-11-10 10:14:22", duration: "18.4 秒", actor: "导入 Worker", summary: { item: 1628, monster: 555, map: 463, npc: 375, quest: 154, magic: 111, gameshop: 105, respawn: 6341, conquest: 1, dragon: 1 } },
    { id: 31, source: "Server.MirDB", hash: "4971a2b862fe0c81", version: 117, status: "imported", count: 9728, started: "2026-11-09 22:02:10", duration: "17.9 秒", actor: "导入 Worker", summary: { item: 1626, monster: 555, map: 463, npc: 375, quest: 154, magic: 111, gameshop: 104, respawn: 6328 } },
    { id: 30, source: "Server.MirDB", hash: "f6eb0a3bd97d41e8", version: 111, status: "imported", count: 9701, started: "2026-11-09 16:44:17", duration: "19.1 秒", actor: "周遥", summary: { item: 1622, monster: 552, map: 460, npc: 373, quest: 151, magic: 111, gameshop: 101, respawn: 6320 } },
    { id: 29, source: "Server.MirDB", hash: "2298c15e72a3d806", version: 111, status: "failed", count: 0, started: "2026-11-09 16:38:02", duration: "2.7 秒", actor: "周遥", summary: {}, error: "检测到循环引用：npc.3004 -> quest.5004 -> npc.3004" },
    { id: 28, source: "Server.MirDB", hash: "83e40f61b7a9dc22", version: 111, status: "imported", count: 9688, started: "2026-11-08 20:01:44", duration: "18.8 秒", actor: "导入 Worker", summary: { item: 1620, monster: 550, map: 459, npc: 371, quest: 150, magic: 110, gameshop: 101, respawn: 6317 } }
  ];

  const ASSETS = [
    { name: "Items.lib", type: "图库", size: "128.4 MB", refs: 1628, license: "需确认", tone: "red", icon: "image", hash: "sha256:6f3b...91e2" },
    { name: "Monsters.lib", type: "图库", size: "246.8 MB", refs: 555, license: "需确认", tone: "red", icon: "image", hash: "sha256:ab19...20fc" },
    { name: "Objects.lib", type: "图库", size: "84.1 MB", refs: 862, license: "需确认", tone: "red", icon: "package", hash: "sha256:3c79...e4aa" },
    { name: "3.map", type: "地图", size: "2.8 MB", refs: 1, license: "内部制作", tone: "green", icon: "map", hash: "sha256:88e3...7ac1" },
    { name: "D2001.map", type: "地图", size: "5.6 MB", refs: 1, license: "内部制作", tone: "green", icon: "map", hash: "sha256:be62...2d04" },
    { name: "sound_bgm_city.wav", type: "音效", size: "12.2 MB", refs: 1, license: "待归档", tone: "orange", icon: "activity", hash: "sha256:aa7d...5c83" },
    { name: "effect_fire_slash.lib", type: "特效", size: "8.7 MB", refs: 12, license: "需确认", tone: "red", icon: "sparkles", hash: "sha256:1d80...f112" },
    { name: "launcher-patch-20261109.zip", type: "补丁包", size: "318.2 MB", refs: 1, license: "内部构建", tone: "green", icon: "archive", hash: "sha256:c024...9acb" }
  ];

  const ACCOUNTS = [
    { name: "林策", username: "lin.ce", email: "lin.ce@example.com", role: "平台管理员", roles: ["platform_admin"], status: "active", mfa: true, last: "今天 18:42", ip: "10.24.8.16", avatar: "LC" },
    { name: "周遥", username: "zhou.yao", email: "zhou.yao@example.com", role: "版本作者", roles: ["content_author"], status: "active", mfa: true, last: "今天 17:58", ip: "10.24.8.22", avatar: "ZY" },
    { name: "陈屿", username: "chen.yu", email: "chen.yu@example.com", role: "发布审核", roles: ["release_approver"], status: "active", mfa: true, last: "今天 16:21", ip: "10.24.9.4", avatar: "CY" },
    { name: "苏禾", username: "su.he", email: "su.he@example.com", role: "GM", roles: ["gm"], status: "active", mfa: false, last: "今天 18:06", ip: "10.24.11.32", avatar: "SH" },
    { name: "顾宁", username: "gu.ning", email: "gu.ning@example.com", role: "财务", roles: ["finance"], status: "locked", mfa: true, last: "昨天 20:33", ip: "10.24.12.8", avatar: "GN" },
    { name: "许安", username: "xu.an", email: "xu.an@example.com", role: "审计", roles: ["auditor"], status: "active", mfa: true, last: "11-08 15:12", ip: "10.24.12.19", avatar: "XA" }
  ];

  const ROLES = [
    { code: "platform_admin", name: "平台管理员", desc: "系统、账号、区服和全部内容", members: 2, system: true },
    { code: "content_author", name: "版本作者", desc: "内容编辑、资源上传、发布申请", members: 5, system: true },
    { code: "release_approver", name: "发布审核", desc: "审核、发布和回滚内容版本", members: 3, system: true },
    { code: "gm", name: "GM", desc: "在线管理、公告、补偿和封禁", members: 8, system: true },
    { code: "finance", name: "财务", desc: "订单、对账、退款和账本", members: 3, system: true },
    { code: "auditor", name: "审计", desc: "只读审计、日志导出和合规检查", members: 2, system: true }
  ];

  const APPROVALS = [
    { id: "AP-2026-1042", title: "发布内容版本 2026.11.10.0-rc2", type: "内容发布", risk: "high", requester: "周遥", role: "版本作者", reason: "月华活动预置内容，申请四区灰度发布。", time: "今天 15:24", scope: "四区 · 灰度 10%", checks: "资源校验通过；经济模拟 A；影响实体 7" },
    { id: "AP-2026-1041", title: "全服发放 10,000 金币补偿", type: "GM 补偿", risk: "high", requester: "苏禾", role: "GM", reason: "四区维护延迟 42 分钟，按运营方案补偿。", time: "今天 14:58", scope: "四区 · 12,480 角色", checks: "金额上限 20,000；二次复核待完成" },
    { id: "AP-2026-1040", title: "调整 GM 角色权限", type: "权限变更", risk: "medium", requester: "林策", role: "平台管理员", reason: "新增临时活动运营权限，有效期 7 天。", time: "今天 13:36", scope: "账号 su.he", checks: "与现有角色无冲突" },
    { id: "AP-2026-1039", title: "订单退款 ¥ 648.00", type: "财务退款", risk: "medium", requester: "顾宁", role: "财务", reason: "用户重复支付，订单已核验。", time: "今天 11:17", scope: "订单 MIR-20261110-0321", checks: "支付渠道已确认；未发货" }
  ];

  const AUDIT_EVENTS = [
    { time: "今天 18:42:16", actor: "林策", action: "auth.login", resource: "session", ip: "10.24.8.16", result: "success", detail: "管理员登录成功，MFA 已验证。" },
    { time: "今天 18:06:03", actor: "苏禾", action: "gm.mail.send", resource: "zone.legend-04", ip: "10.24.11.32", result: "pending", detail: "申请向 12,480 个角色发放补偿邮件，等待审批。" },
    { time: "今天 17:58:41", actor: "周遥", action: "content.update", resource: "item.1004", ip: "10.24.8.22", result: "success", detail: "更新圣战宝甲图标引用与重量字段。" },
    { time: "今天 16:21:19", actor: "陈屿", action: "release.sign", resource: "release.2026.11.10.0-rc2", ip: "10.24.9.4", result: "success", detail: "内容版本签名完成，签名算法 Ed25519。" },
    { time: "今天 15:24:09", actor: "周遥", action: "release.request", resource: "release.2026.11.10.0-rc2", ip: "10.24.8.22", result: "success", detail: "提交灰度发布审批，目标四区。" },
    { time: "今天 14:58:22", actor: "苏禾", action: "gm.compensation.request", resource: "zone.legend-04", ip: "10.24.11.32", result: "denied", detail: "首次申请因补偿原因过短被策略拒绝。" },
    { time: "今天 13:36:55", actor: "林策", action: "account.role.change", resource: "account.su.he", ip: "10.24.8.16", result: "pending", detail: "申请新增临时活动运营角色。" },
    { time: "今天 11:17:08", actor: "顾宁", action: "payment.refund", resource: "order.MIR-20261110-0321", ip: "10.24.12.8", result: "success", detail: "退款 ¥648.00，支付渠道回调已验签。" }
  ];

  const ONLINE_PLAYERS = [
    { name: "风起云涌", account: "mir_88421", zone: "传奇一区 · 龙腾", level: 48, class: "战士", map: "祖玛寺庙三层", ip: "112.81.•.•", login: "今天 17:20", status: "online" },
    { name: "月下听风", account: "mir_77103", zone: "传奇一区 · 龙腾", level: 45, class: "道士", map: "苍月岛", ip: "39.144.•.•", login: "今天 16:48", status: "online" },
    { name: "孤城落日", account: "mir_66312", zone: "传奇二区 · 烈焰", level: 42, class: "法师", map: "赤月峡谷北", ip: "120.36.•.•", login: "今天 18:02", status: "online" },
    { name: "铁血战魂", account: "mir_55918", zone: "传奇三区 · 逐月", level: 51, class: "战士", map: "沙巴克", ip: "218.76.•.•", login: "今天 12:11", status: "away" },
    { name: "青灯古卷", account: "mir_42008", zone: "传奇五区 · 赤月", level: 37, class: "道士", map: "石墓阵", ip: "183.62.•.•", login: "今天 18:16", status: "online" }
  ];

  const ORDERS = [
    { id: "MIR-20261110-0438", player: "风起云涌", zone: "龙腾", product: "月卡 · 30 天", amount: "¥ 30.00", channel: "微信支付", status: "delivered", delivery: "已发货", time: "今天 18:31", risk: "低" },
    { id: "MIR-20261110-0437", player: "月下听风", zone: "龙腾", product: "元宝 × 648", amount: "¥ 64.80", channel: "支付宝", status: "delivering", delivery: "发货中", time: "今天 18:27", risk: "低" },
    { id: "MIR-20261110-0436", player: "孤城落日", zone: "烈焰", product: "元宝 × 3,280", amount: "¥ 328.00", channel: "微信支付", status: "paid", delivery: "待发货", time: "今天 18:19", risk: "低" },
    { id: "MIR-20261110-0435", player: "铁血战魂", zone: "逐月", product: "至尊礼包", amount: "¥ 648.00", channel: "Apple Pay", status: "failed", delivery: "重试 2/5", time: "今天 18:08", risk: "中" },
    { id: "MIR-20261110-0434", player: "青灯古卷", zone: "赤月", product: "元宝 × 648", amount: "¥ 64.80", channel: "支付宝", status: "refunded", delivery: "已退款", time: "今天 17:52", risk: "中" },
    { id: "MIR-20261110-0433", player: "旧梦无痕", zone: "龙腾", product: "月卡 · 30 天", amount: "¥ 30.00", channel: "微信支付", status: "delivered", delivery: "已发货", time: "今天 17:40", risk: "低" }
  ];

  const JOBS = [
    { id: "job-9f7e42", type: "内容导入", title: "导入 Server.MirDB · 批次 #32", status: "success", progress: 100, owner: "导入 Worker", started: "今天 10:14", duration: "18.4 秒" },
    { id: "job-c81b03", type: "发布", title: "签名 2026.11.10.0-rc2", status: "success", progress: 100, owner: "陈屿", started: "今天 16:21", duration: "11.2 秒" },
    { id: "job-48ac11", type: "区服预热", title: "传奇六区 · 苍月 预热", status: "running", progress: 62, owner: "Zone Agent", started: "今天 18:12", duration: "06:18" },
    { id: "job-7a201d", type: "热重载", title: "四区加载 2026.11.10.0-rc2", status: "waiting", progress: 0, owner: "发布队列", started: "等待审批", duration: "—" },
    { id: "job-2e10aa", type: "合区预检", title: "三区 → 一区 合区预检", status: "failed", progress: 38, owner: "合区 Worker", started: "今天 14:04", duration: "02:11" },
    { id: "job-90bb4c", type: "资源扫描", title: "扫描客户端 Data / Map / Sound", status: "running", progress: 74, owner: "资源 Worker", started: "今天 17:50", duration: "25:40" }
  ];

  const SERVICES = [
    { name: "Control API", status: "up", latency: "34 ms", note: "2 / 2 实例" },
    { name: "PostgreSQL", status: "up", latency: "7 ms", note: "主备正常" },
    { name: "Redis", status: "up", latency: "2 ms", note: "3 节点" },
    { name: "MinIO / S3", status: "up", latency: "18 ms", note: "对象存储" },
    { name: "Zone Agent", status: "up", latency: "21 ms", note: "12 / 12 在线" },
    { name: "发布 Worker", status: "warn", latency: "1.8 s", note: "队列积压 3" },
    { name: "支付回调", status: "up", latency: "86 ms", note: "成功率 99.97%" },
    { name: "CDN", status: "up", latency: "—", note: "命中率 96.2%" }
  ];

  const ALERTS = [
    { level: "danger", title: "四区内容加载失败", detail: "2026.11.10.0-rc2 地图 0150.map 哈希不一致，已保留旧快照。", time: "8 分钟前" },
    { level: "warning", title: "发布 Worker 队列积压", detail: "当前 3 个任务等待，最久 6 分 18 秒。", time: "12 分钟前" },
    { level: "warning", title: "三区 Tick 延迟抬升", detail: "P95 从 24ms 升至 31ms，仍低于 50ms 阈值。", time: "26 分钟前" },
    { level: "info", title: "素材许可证待确认", detail: "Items/Monsters/Objects 图库来源需要补齐授权记录。", time: "1 小时前" }
  ];

  const ACTIVITY = [
    { icon: "rocket", tone: "gold", title: "陈屿完成 rc2 内容签名", detail: "2026.11.10.0-rc2 · Ed25519", time: "今天 16:21" },
    { icon: "upload", tone: "blue", title: "批次 #32 导入完成", detail: "9,734 个实体 · 18.4 秒", time: "今天 10:14" },
    { icon: "shield", tone: "green", title: "苏禾完成 MFA 验证", detail: "来自 10.24.11.32", time: "今天 09:48" },
    { icon: "server", tone: "violet", title: "六区进入预热", detail: "苍月 · 等待健康门禁", time: "今天 08:30" }
  ];

  const TEMPLATES = [
    { name: "经典 1.76 · 复古", version: "v12", zones: 6, release: "2026.11.09.1", exp: "1×", drop: "1×", maps: 128, icon: "crown", tone: "gold", desc: "经典三职业、慢节奏成长、祖玛/赤月/沙巴克完整线。" },
    { name: "微变 · 高爆率", version: "v8", zones: 3, release: "2026.11.08.2", exp: "5×", drop: "3×", maps: 114, icon: "zap", tone: "orange", desc: "提高成长速度与掉落密度，适合短周期赛季。" },
    { name: "单职业 · 战神", version: "v5", zones: 2, release: "2026.11.09.1", exp: "10×", drop: "6×", maps: 96, icon: "sword", tone: "red", desc: "单职业战士模板，强调 Boss 挑战与装备养成。" },
    { name: "测试 · 沙盒", version: "v3", zones: 1, release: "2026.11.10.0-rc2", exp: "50×", drop: "20×", maps: 42, icon: "box", tone: "blue", desc: "内部验证模板，开放 GM 和快速传送。" }
  ];

  const MERGES = [
    { id: "merge-202611-01", source: "传奇三区 · 逐月", target: "传奇一区 · 龙腾", status: "preflight", progress: 38, windows: "2026-11-12 02:00", issues: 3, owner: "林策", scope: "账号 / 角色 / 行会 / 资产" },
    { id: "merge-202610-02", source: "传奇七区 · 测试", target: "传奇二区 · 烈焰", status: "completed", progress: 100, windows: "2026-10-28 02:00", issues: 0, owner: "陈屿", scope: "全量数据" },
    { id: "merge-202610-01", source: "传奇九区 · 沙盒", target: "传奇五区 · 赤月", status: "rolled_back", progress: 100, windows: "2026-10-21 02:00", issues: 2, owner: "林策", scope: "全量数据" }
  ];

  const ANNOUNCEMENTS = [
    { title: "11 月 12 日四区维护公告", channel: "游戏内 + 官网", scope: "传奇四区 · 沙城", status: "scheduled", time: "2026-11-12 01:30", author: "苏禾" },
    { title: "月华庆典活动开启", channel: "游戏内 + 社群", scope: "全部区服", status: "draft", time: "待发布", author: "周遥" },
    { title: "周末双倍经验", channel: "游戏内", scope: "一区至五区", status: "published", time: "2026-11-08 10:00", author: "苏禾" },
    { title: "违规账号封禁名单（第 45 期）", channel: "官网 + 社群", scope: "全服", status: "published", time: "2026-11-07 18:00", author: "林策" }
  ];

  const chartOnline = [42, 50, 47, 55, 61, 58, 65, 71, 68, 74, 82, 88, 84, 79, 76, 81, 87, 92, 96, 91, 88, 83, 79, 74];
  const chartRevenue = [24, 31, 28, 36, 42, 39, 46, 51, 48, 58, 64, 62, 71, 68, 73, 77, 75, 82, 88, 86, 91, 84, 79, 75];

  window.MirData = {
    NAV, ZONES, RELEASES, IMPORT_RUNS, ASSETS, ACCOUNTS, ROLES, APPROVALS,
    AUDIT_EVENTS, ONLINE_PLAYERS, ORDERS, JOBS, SERVICES, ALERTS, ACTIVITY, TEMPLATES,
    MERGES, ANNOUNCEMENTS, chartOnline, chartRevenue
  };
})();
