# Crystal.Control.Web 管理后台

高保真、可点击的传奇运营后台。当前版本不依赖前端构建工具：登录、内容总览、导入批次、15 个内容域（其中 13 类读取真实批次，含掉落、配方、Buff、NPC 脚本和地图资源）、10 类 Schema 驱动草稿编辑、引用选择、JSONL 导入导出、地图关系 CRUD、地图降采样预览、脚本 AST 与编译校验、资源完整性下载、发布中心、区服总览/详情、账号生命周期、角色和审计已接入 `Crystal.Control.Api`；实时模式还会按权限点过滤导航、直达路由和页面动作。发布中心支持从导入批次或草稿创建版本、校验、编译、签名、发布、查看 manifest/实体计数/校验报告和回滚。区服页展示真实在线数、Tick P95、错误率、重启失败、Agent/进程状态、active/staged 版本、内容哈希、最后心跳、指标趋势、健康告警和命令历史，并可按 `zone:write` 发起 stage、activate、rollback、reload、stop。其余写操作与依赖未来 API 的页面仍使用原型数据，API 不可用或未登录时继续以演示模式浏览。

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

实时模式根据 JWT 中的权限点控制访问：`content:read`、`zone:read`、`zone:write`、`account:read`、`role:read`、`audit:read` 等权限缺失时，侧栏入口和全局搜索会隐藏，直接访问路由会显示 403 空状态，不加载或渲染业务样本。区服写命令还要求 `zone:write`；离线、只读或已有生命周期命令时隐藏或禁用写入口。服务端 `RequireAuthorization` 仍是最终授权边界。

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
| drop | 怪物名称 | `DropPath` / 文件 | 掉落条目数 |
| recipe | 配方文件名 | `FileName` / 产出 | 材料/工具数 |
| buff | `Type` | `StackType` / `Properties` | `Icon` / 可见性 |
| script | `FileName` | 页面 / 行数 | `#INCLUDE` / `CALL` 数 |
| asset | `relativePath` | 资源类型 / 地图格式 | 尺寸 / 文件大小 |

配方、Buff、NPC 脚本和资源已按当前批次接入：配方详情展示真实产出、材料、工具和解锁条件，脚本详情展示原文、页面、静态引用、AST、错误/警告和编译哈希；地图关系展示真实传送、安全区、NPC、NPC 脚本、刷新点、怪物掉落和地图尺寸/阻挡，并可打开降采样网格预览；资源详情可下载文件并在浏览器内复核 SHA256。在线坐标和坐标画布仍使用原型尺寸与运行时在线人物数据。

## 内容编辑器

- `item`、`monster`、`map`、`npc`、`script`、`drop`、`magic`、`quest`、`gameshop` 和 `recipe` 使用 Control API 的 `entityType` 注册表动态生成分组表单，前端不复制字段清单。
- 存在活动草稿时，十类内容列表读取草稿查询接口；可写用户点击记录进入编辑器，只读用户仍进入原详情抽屉。
- 编辑器支持草稿内新建、保存、删除、字段级差异、草稿校验、引用选择、400 字段错误映射和 409 revision 冲突恢复。
- 新建内容由服务端解析 `localIndex` 和内容键；物品/怪物/地图/NPC/任务显示“自动分配”，技能使用 `Spell`，配方和脚本使用服务端生成的确定性键。
- 物品、怪物和地图保留既有字段覆盖；NPC 支持地图/脚本/任务引用；技能、任务、商城和配方覆盖专用字段及交叉引用；商城保存时由服务端同步 `ItemIndex`/`Info`。
- 掉落编辑器支持物品、金币、嵌套掉落组、概率、任务限定和物品引用；脚本编辑器编辑源码并只读展示 AST、页面、静态引用、问题、源码 SHA256 和编译哈希。
- 地图编辑器覆盖 6 个分组、44 个顶层标量字段；`WeatherParticles` 只读。关系面板支持传送、安全区、矿区、激活坐标整体保存，以及刷新点和 NPC 行级 CRUD；坐标和引用失败显示在面板内。
- 地图与脚本列表支持 Schema 声明的搜索字段；内容工具栏提供 JSONL dry-run、应用导入和导出下载。

## 范围

- 39 个页面入口（38 个后台业务页面 + 登录页），另有 6 个区服详情动态路由，共 45 个可访问路由，覆盖 6 个一级功能域。
- 15 个内容域：物品、怪物、地图、NPC、掉落、技能、任务、商店、NPC 脚本、配方、称号、Buff、活动、刷新点、地图与资源文件；其中除称号和活动外的 13 类读取真实批次。
- 地图拓扑与坐标：地图层级、入口/出口、安全区、NPC、刷怪点、Boss 和在线人物坐标；按怪物种类汇总数量、占比、刷新点数和刷新时间，再展开每个刷怪点的坐标明细。实时模式额外展示批次地图关系，可检索全量地图、切换地图并打开真实怪物掉落。
- 世界关系图谱：地图 → 刷怪点 → 怪物 → 掉落组 → 装备正查，以及装备 → 来源怪物 → 地图坐标反查；实时模式展示服务端真实关系，原型链路继续用于坐标和玩法说明。
- 内容导入、校验、发布、回滚、资源与脚本工作台。
- 开区模板、GM、公告活动、合区；区服总览/详情、指标和命令历史使用真实 API。
- 玩家、订单发货、财务对账。
- 审批、管理账号、角色权限、审计。
- 运行监控、任务中心、许可合规、系统设置。
- 登录页、全局搜索、内容详情抽屉、审批/高风险确认弹窗、移动端导航。
- 真实接入：账号登录/刷新/登出/自助改密、内容总览 KPI 与批次计数、导入批次、13 类内容域分页检索（含 `drop`、`recipe`、`buff`、`script`、`asset`）、10 类草稿编辑、引用选择、JSONL 导入导出、地图关系 CRUD、地图网格预览、地图/NPC 脚本/怪物/配方/资源关系查询、脚本 AST/校验、资源下载与 SHA256 复核、真实发布中心（创建/校验/编译/签名/发布/详情/回滚）、区服列表/详情/实时指标/指标趋势/健康告警/命令历史/五类生命周期命令、账号列表与生命周期、角色权限矩阵、审计事件查询和当前查询结果 CSV 导出。
- 权限接入：路由级导航过滤、直达 403 空状态、审计/账号/角色页面真实数据、区服 `zone:read`/`zone:write` 动作级显隐，以及账号创建、停用/启用、重置密码、角色调整、角色保存和审计入口的动作级显隐。

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
assets/content-editor.js   Schema 表单、动态属性、字段差异和表单读取
assets/app.js              路由、页面渲染和交互
```

## 边界

当前是联调版本，不是生产前端：

- 登录、内容总览、导入批次、内容查询、10 类类型化草稿编辑、地图关系、JSONL 导入导出、发布中心、区服指标/命令、账号生命周期、角色和审计使用真实 API；GM、支付和审批等运营页面仍为原型。
- JWT 保存在 `sessionStorage`，刷新令牌自动轮换；生产环境仍需接入 OIDC/MFA 和前端构建链。
- 审计导出为浏览器内 CSV，仅覆盖当前已加载的最多 100 条查询结果，不是服务端异步导出任务。
- 地图关系页已通过轻量目录加载当前批次全部地图摘要并支持检索，并展示真实尺寸/阻挡/门格、降采样网格与资源下载；坐标画布仍使用原型尺寸。配方、Buff、脚本和资源已读取真实批次，运行时在线坐标继续使用原型数据。
- 区服列表每 5 秒刷新，详情和命令历史每 2 秒刷新；离开路由后清理定时器。指标来自 Agent 心跳，`unavailable` 显示占位，
  `process-stopped` 显示 Agent 上报的零值并保留来源标记，不伪造运行中数值。
- 区服详情读取 `GET /zones/{id}/metrics` 展示最近 1 小时样本趋势和实时健康等级；metrics 读取失败只隐藏趋势并提示，
  不影响命令操作。告警通知、状态机、静默和远端指标汇聚仍未实现。
- 尚未接入字段级权限、服务端审计导出任务、自定义角色 CRUD 和生产 CSP。

## 发布中心边界

- 列表、KPI、状态、实体计数、manifest、签名算法/Key ID/签名时间和校验报告均来自 Control API。
- 从草稿创建版本后，版本实体已冻结；后续草稿修改不会改变已有版本。
- 发布只把版本标记为 `published`；`active` 由回滚事务设置。没有 `active` 时页面显示最新 `published`，不展示虚构区服灰度。
- 所有动作按 `release:read`、`release:write`、`release:publish`、`release:rollback` 显示或隐藏。
- 草稿后端写入和 10 类内容编辑器已接入；语句块拖拽/排序、`.lib` 图标缩略图和资源上传仍不在范围。

信息架构和页面设计说明见 `docs/product/control-web-prototype.md`。
