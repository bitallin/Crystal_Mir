# Crystal.Control.Web 管理后台

高保真、可点击的传奇运营后台。当前版本不依赖前端构建工具：登录、内容总览、导入批次、15 个内容域（其中 13 类读取真实批次，含掉落、配方、Buff、NPC 脚本和地图资源）、`item`/`monster` 类型化草稿编辑、地图/NPC 脚本/怪物/配方/资源真实关系、脚本 AST 与编译校验、资源完整性下载、发布中心、账号生命周期、角色和审计已接入 `Crystal.Control.Api`；实时模式还会按权限点过滤导航、直达路由和页面动作。发布中心支持从导入批次或草稿创建版本、校验、编译、签名、发布、查看 manifest/实体计数/校验报告和回滚。其余写操作与依赖未来 API 的页面仍使用原型数据，API 不可用或未登录时继续以演示模式浏览。

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
| drop | 怪物名称 | `DropPath` / 文件 | 掉落条目数 |
| recipe | 配方文件名 | `FileName` / 产出 | 材料/工具数 |
| buff | `Type` | `StackType` / `Properties` | `Icon` / 可见性 |
| script | `FileName` | 页面 / 行数 | `#INCLUDE` / `CALL` 数 |
| asset | `relativePath` | 资源类型 / 地图格式 | 尺寸 / 文件大小 |

配方、Buff、NPC 脚本和资源已按当前批次接入：配方详情展示真实产出、材料、工具和解锁条件，脚本详情展示原文、页面、静态引用、AST、错误/警告和编译哈希；地图关系展示真实传送、安全区、NPC、NPC 脚本、刷新点、怪物掉落和地图尺寸/阻挡；资源详情可下载文件并在浏览器内复核 SHA256。在线坐标和坐标画布仍使用原型尺寸与运行时在线人物数据。

## 内容编辑器

- `item`、`monster` 使用 Control API 的 `entityType` 注册表动态生成分组表单，前端不复制字段清单。
- 存在活动草稿时，物品和怪物列表读取草稿查询接口；可写用户点击记录进入编辑器，只读用户仍进入原详情抽屉。
- 编辑器支持草稿内新建、保存、删除、字段级差异、草稿校验、400 字段错误映射和 409 revision 冲突恢复。
- 新建物品/怪物由服务端分配下一个 `localIndex`，客户端只显示“自动分配”状态。
- 怪物编辑器展示只读掉落关系；`Drops`、脚本派生字段和掉落 CRUD 不在本轮范围。
- 物品编辑器继续覆盖 33 个顶层字段、`Stats.Values` 和 70 个随机属性；怪物编辑器覆盖 20 个可编辑字段和 13 个固定属性键。

## 范围

- 39 个页面入口（38 个后台业务页面 + 登录页），另有 6 个区服详情动态路由，共 45 个可访问路由，覆盖 6 个一级功能域。
- 15 个内容域：物品、怪物、地图、NPC、掉落、技能、任务、商店、NPC 脚本、配方、称号、Buff、活动、刷新点、地图与资源文件；其中除称号和活动外的 13 类读取真实批次。
- 地图拓扑与坐标：地图层级、入口/出口、安全区、NPC、刷怪点、Boss 和在线人物坐标；按怪物种类汇总数量、占比、刷新点数和刷新时间，再展开每个刷怪点的坐标明细。实时模式额外展示批次地图关系，可检索全量地图、切换地图并打开真实怪物掉落。
- 世界关系图谱：地图 → 刷怪点 → 怪物 → 掉落组 → 装备正查，以及装备 → 来源怪物 → 地图坐标反查；实时模式展示服务端真实关系，原型链路继续用于坐标和玩法说明。
- 内容导入、校验、发布、回滚、资源与脚本工作台。
- 区服、开区模板、GM、公告活动、合区。
- 玩家、订单发货、财务对账。
- 审批、管理账号、角色权限、审计。
- 运行监控、任务中心、许可合规、系统设置。
- 登录页、全局搜索、内容详情抽屉、审批/高风险确认弹窗、移动端导航。
- 真实接入：账号登录/刷新/登出/自助改密、内容总览 KPI 与批次计数、导入批次、13 类内容域分页检索（含 `drop`、`recipe`、`buff`、`script`、`asset`）、`item`/`monster` 草稿编辑、地图/NPC 脚本/怪物/配方/资源关系查询、脚本 AST/校验、资源下载与 SHA256 复核、真实发布中心（创建/校验/编译/签名/发布/详情/回滚）、账号列表与生命周期、角色权限矩阵、审计事件查询和当前查询结果 CSV 导出。
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
assets/content-editor.js   Schema 表单、动态属性、字段差异和表单读取
assets/app.js              路由、页面渲染和交互
```

## 边界

当前是联调版本，不是生产前端：

- 登录、内容总览、导入批次、内容查询、`item`/`monster` 类型化草稿编辑、发布中心、账号生命周期、角色和审计使用真实 API；地图/NPC/掉落等其余内容编辑、GM、支付和审批仍为原型或通用草稿写入。
- JWT 保存在 `sessionStorage`，刷新令牌自动轮换；生产环境仍需接入 OIDC/MFA 和前端构建链。
- 审计导出为浏览器内 CSV，仅覆盖当前已加载的最多 100 条查询结果，不是服务端异步导出任务。
- 地图关系页已通过轻量目录加载当前批次全部地图摘要并支持检索，并展示真实尺寸/阻挡/门格与资源下载；坐标画布仍使用原型尺寸。配方、Buff、脚本和资源已读取真实批次，运行时在线坐标继续使用原型数据。
- 尚未接入字段级权限、服务端审计导出任务、自定义角色 CRUD 和生产 CSP。

## 发布中心边界

- 列表、KPI、状态、实体计数、manifest、签名算法/Key ID/签名时间和校验报告均来自 Control API。
- 从草稿创建版本后，版本实体已冻结；后续草稿修改不会改变已有版本。
- 发布只把版本标记为 `published`；`active` 由回滚事务设置。没有 `active` 时页面显示最新 `published`，不展示虚构区服灰度。
- 所有动作按 `release:read`、`release:write`、`release:publish`、`release:rollback` 显示或隐藏。
- 草稿后端写入和 `item`/`monster` 类型化编辑器已接入；其他内容域的专用表单和可视化编辑器仍待 M2 后续切片。

信息架构和页面设计说明见 `docs/product/control-web-prototype.md`。
