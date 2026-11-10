# Crystal.Control.Web 管理后台

高保真、可点击的传奇运营后台。当前版本不依赖前端构建工具：登录、内容总览、导入批次、10 类内容只读查询、账号生命周期、角色和审计已接入 `Crystal.Control.Api`；实时模式还会按权限点过滤导航、直达路由和页面动作。其余写操作与依赖未来 API 的页面仍使用原型数据，API 不可用或未登录时继续以演示模式浏览。

## 预览

演示模式不需要 API：

```bash
python3 -m http.server 4173 --bind 127.0.0.1 --directory src/Crystal.Control.Web
```

打开 `http://127.0.0.1:4173/#/dashboard`，或使用 `./scripts/preview-control-web.sh`。

连接 Control API 时，通过 `api` 查询参数指定 API 地址；地址会保存在当前浏览器会话中：

```text
http://127.0.0.1:4173/?api=http://127.0.0.1:5080#/login
```

API 需要允许该静态站点来源：

```bash
CRYSTAL_CONTROL_CORS_ORIGINS='http://127.0.0.1:4173' \
  dotnet run --project src/Crystal.Control.Api
```

登录后顶部显示“实时 API”徽标。账号会话使用短时 JWT 和刷新令牌轮换；登出会撤销数据库会话。API 连接失败时页面回退原型数据并显示降级提示。

实时模式根据 JWT 中的权限点控制访问：`content:read`、`account:read`、`role:read`、`audit:read` 等权限缺失时，侧栏入口和全局搜索会隐藏，直接访问路由会显示 403 空状态，不加载或渲染业务样本。服务端 `RequireAuthorization` 仍是最终授权边界。

## 内容字段映射

前端不直接绑定旧实体类，而是把批次 `data` 映射为统一表格字段：

| 内容域 | 名称 | 次要信息 | 指标 |
| --- | --- | --- | --- |
| item | `Name` | `Type` / `RequiredClass` | `Price` |
| monster | `Name` | Boss/AI | `Experience` |
| map | `Title` / `FileName` | 安全区数量 | 文件名/刷怪点数 |
| npc | `Name` | `FileName` | `Location` |
| quest | `Name` | `Group` / `Type` | 任务目标数 |
| magic | `Name` | `Spell` | `DelayBase` |
| gameshop | `Info.Name` / `ItemIndex` | `Category` | `GoldPrice` |
| conquest | `Name` | `Type` | 旧索引 |
| dragon | 固定名称 | 全局配置 | `Level` |
| respawn | 怪物索引 | 地图文件 | `Delay` / `RandomDelay` |

掉落、配方、Buff、资源、地图关系和在线坐标尚未进入 MirDB 批次，继续使用原型数据。

## 范围

- 39 个页面入口（38 个后台业务页面 + 登录页），另有 6 个区服详情动态路由，共 45 个可访问路由，覆盖 6 个一级功能域。
- 13 类内容域：物品、怪物、地图、NPC、掉落、技能、任务、商店、配方、称号、Buff、活动、刷新点。
- 地图拓扑与坐标：地图层级、入口/出口、安全区、NPC、刷怪点、Boss 和在线人物坐标；按怪物种类汇总数量、占比、刷新点数和刷新时间，再展开每个刷怪点的坐标明细。
- 世界关系图谱：地图 → 刷怪点 → 怪物 → 掉落组 → 装备正查，以及装备 → 来源怪物 → 地图坐标反查。
- 内容导入、校验、发布、回滚、资源与脚本工作台。
- 区服、开区模板、GM、公告活动、合区。
- 玩家、订单发货、财务对账。
- 审批、管理账号、角色权限、审计。
- 运行监控、任务中心、许可合规、系统设置。
- 登录页、全局搜索、内容详情抽屉、审批/高风险确认弹窗、移动端导航。
- 真实接入：账号登录/刷新/登出/自助改密、内容总览 KPI 与批次计数、导入批次、10 类 MirDB 内容域分页检索、账号列表与生命周期、角色权限矩阵、审计事件查询和当前查询结果 CSV 导出。
- 权限接入：路由级导航过滤、直达 403 空状态、审计/账号/角色页面真实数据，以及账号创建、停用/启用、重置密码、角色调整、角色保存和审计入口的动作级显隐。

## 文件

```text
index.html                 原型入口
assets/styles.css          设计变量、布局和组件样式
assets/icons.js            内联 SVG 图标
assets/config.js           API 地址和运行配置
assets/api.js              JWT 会话、刷新轮换、Control API 客户端
assets/data.js             导航和运营演示数据
assets/world-data.js       地图、传送、NPC、刷怪点、掉落组和人物坐标关系数据
assets/favicon.svg         站点图标
assets/content-data-core.js    物品/怪物/地图/NPC/掉落/技能
assets/content-data-more.js    任务/商店/配方/称号/Buff/活动/刷新点
assets/app.js              路由、页面渲染和交互
```

## 边界

当前是只读联调版本，不是生产前端：

- 登录、内容总览、导入批次、内容查询、账号生命周期、角色和审计使用真实 API；内容写入、发布、GM、支付和审批仍为前端模拟。
- JWT 保存在 `sessionStorage`，刷新令牌自动轮换；生产环境仍需接入 OIDC/MFA 和前端构建链。
- 审计导出为浏览器内 CSV，仅覆盖当前已加载的最多 100 条查询结果，不是服务端异步导出任务。
- 掉落、配方、Buff、资源、地图关系和运行时坐标仍使用原型数据。
- 尚未接入字段级权限、服务端审计导出任务、自定义角色 CRUD 和生产 CSP。

信息架构和页面设计说明见 `docs/product/control-web-prototype.md`。
