# 运行时与模块边界

> 最后更新：2026-11-17
> 状态：生效

## 1. 当前运行时事实

| 项目 | 现状 | 关键位置 |
| --- | --- | --- |
| 服务端宿主 | WinForms 进程同时承载服务端和编辑器 | `Server.MirForms/Program.cs`、`Server.MirForms/SMain.cs` |
| 世界循环 | `Envir.WorkLoop` 单主循环，可选多线程处理怪物 | `Server/MirEnvir/Envir.cs` |
| 地图 | `Map` 负责地图加载、格子、移动、刷怪等 | `Server/MirEnvir/Map.cs` |
| 网络 | 单 TCP 端口，登录/选角/游戏同连接 | `Server/MirEnvir/Envir.cs`、`Server/MirNetwork/MirConnection.cs` |
| 数据库 | 二进制 `Server.MirDB`，版本 117 | `Server/MirEnvir/Envir.cs:55`、`:2439`、`:2821` |
| 脚本 | NPC 文本 DSL，约 191 条命令 | `Server/MirObjects/NPC/NPCScript.cs`、`NPCSegment.cs` |
| 客户端 | WinForms + SlimDX/Direct3D9，Windows 专用 | `Client/Client.csproj` |
| 补丁 | `PList.gz` + 手工桌面工具 | `Client/Forms/AMain.cs`、`AutoPatcherAdminV2/` |

## 2. 目标程序集

```text
Crystal.Shared                协议、枚举、纯 DTO（MIT）
Crystal.Content               内容模型、schema、校验、编译器（MIT）
Crystal.GameCore              游戏模拟、对象、地图、战斗、AI（GPL 边界）
Crystal.ZoneServer            区服宿主、网络和内容加载（GPL 边界；Agent 独立进程）
Crystal.Agent.Contracts       Agent 与控制面的 gRPC/HTTP 契约（MIT）
Crystal.ZoneAgent             单区进程监督、内容 staging 和命令执行（MIT）
Crystal.Control.Api           控制面 API（独立许可，不链接 GPL 代码）
Crystal.Control.Web           管理后台前端
Crystal.Patcher               清单、签名、差分、发布工具
Crystal.Cli                   运维与开发 CLI
```

当前已落地（M1）：

```text
src/Crystal.Content           内容快照、哈希、校验（MIT）
tools/Crystal.ContentTool     内容快照签名与验证 CLI，仅依赖 Crystal.Content（MIT）
src/Crystal.GameCore          运行时抽象骨架，只依赖 BCL + Shared（M1 不迁移游戏逻辑）
src/Crystal.Agent.Contracts   Agent 注册、心跳、命令和内容版本契约（MIT，无项目依赖）
src/Crystal.ZoneAgent         单区 Agent，下载/验签/持久化命令结果并管理 ZoneServer 子进程（MIT）
src/Crystal.Control.Api       内容、账号、RBAC 和审计 API，ASP.NET Core + Npgsql
src/Crystal.ZoneServer        Linux 区服控制台宿主，加载旧数据库和内容并监听游戏端口（GPL 边界）
tools/Crystal.LegacyImporter  二进制导入、快照/PostgreSQL 重建和 PostgreSQL 写入（GPL 边界工具）
db/migrations                 PostgreSQL schema
```

`Crystal.Control.Api` 只依赖 `Crystal.Content`、Npgsql 和 ASP.NET Core，不引用 GPL 游戏代码。
当前提供健康检查、导入批次、内容只读查询、账号登录、短时 JWT、刷新会话、RBAC、只追加审计和账号
生命周期（停用/启用、重置/自助改密、角色调整）。内容接口暴露 15 类实体（含掉落、配方、Buff、脚本和资源元数据）；
`Crystal.Control.Web` 已通过受控 CORS 接入登录、内容总览、导入批次、15 个内容域（其中 13 类读取真实批次）、
全量地图目录、地图/NPC 脚本/怪物掉落/配方/资源关系、账号管理、角色和审计，并按 JWT 权限点过滤导航与直达路由。
M1 已补齐可编辑内容草稿、逐实体乐观并发、修订记录、草稿校验和发布回滚；发布中心使用真实
`GET /releases`、manifest、校验报告和回滚元数据，不再展示虚构的区服灰度或 Ed25519 信息。
地图关系页已可检索当前批次全部地图，配方、Buff 和脚本已读取真实批次，脚本抽屉已读取 AST/编译校验，
资源域已展示地图格式、尺寸、可走/阻挡/门格，并可通过受权限保护的文件端点下载经路径、大小和 SHA256 校验的资源；
内容快照支持 ECDSA P-256 签名与验证，坐标画布仍使用原型尺寸，其他资源二进制尚未接入；进程级换版已由 M3 Agent 接入，进程内热切换仍未实现。

`Crystal.GameCore` 在 M1 只提供运行时边界契约（`IContentSnapshotProvider<TSnapshot>`、
`IPlayerRepository<TSnapshot>`、`IWorldClock`、`IZoneEventSink`、`IChatTransport` 和运行时事件类型），
`Crystal.ZoneServer` 只增加项目引用，现有 `Envir`/`Map` 逻辑没有迁移。`Crystal.Agent.Contracts`
定义稳定的 JSON 字段、路由和命令字符串（`content.stage`、`content.activate`、`content.rollback`、
`content.reload`、`zone.stop`）。M3 已注册 Control API 的区服/命令路由并落地独立 `Crystal.ZoneAgent`：
Agent 领取持久命令、下载并验签发布 ZIP、写入本地 staging、以 SIGTERM/ready 文件执行进程级激活或回滚。
Agent 不引用 ZoneServer 或 `LegacyRuntime`，只通过 `ZoneExecutable` 启动子进程；详细边界见
`docs/adr/0012-zone-agent-process-level-content-switching.md`。

## 3. 依赖规则

```text
Control.Web -> Control.Api -> Content / Shared
ZoneServer  -> GameCore -> Shared
ZoneServer  -> Content / Agent.Contracts
ZoneAgent   -> Agent.Contracts / Content（通过可执行文件边界启动 ZoneServer）
Control.Api -> Agent.Contracts（仅允许 MIT 契约）
Patcher     -> Shared / Content.Contracts

禁止：
Control.Api -> GameCore / ZoneServer
ZoneAgent    -> Server / LegacyRuntime / ZoneServer 项目引用
GameCore    -> EF Core / ASP.NET Core / WinForms
Content     -> GameCore（只能依赖纯契约）
Shared      -> 任何服务端实现
```

### 3.1 GPL 边界

`Server/`、`Server.MirForms/`、`Tests/` 是 GPL-3.0-only。控制面不得链接这些程序集。跨边界只使用 MIT 许可的 `Crystal.Agent.Contracts`、`Crystal.Shared` 和纯数据契约。详见 `adr/0004-gpl-boundary.md`。

### 3.2 GameCore 边界

`Crystal.GameCore` 只依赖 .NET 基础库和 `Crystal.Shared`，不依赖数据库、HTTP、文件系统实现细节。当前 `Envir` 中的数据库、网络、配置、文件加载职责逐步外移：

- `IContentSnapshotProvider`：提供只读内容快照。
- `IPlayerRepository`：玩家数据读写。
- `IWorldClock`：时间与定时器抽象。
- `IZoneEventSink`：指标、日志、审计事件。
- `IChatTransport`：广播与聊天输出。

## 4. 现有代码抽离顺序

采用绞杀者模式，不做一次性重写。

### 步骤 1：建立可编译基线

- 在 Windows 环境恢复完整构建。
- 引入 `Crystal.Shared` 现有代码，不改变行为。
- 建立 `dotnet build` 和最小回归测试。

### 步骤 2：抽离数据库访问

- 保留二进制数据库读写作为 `LegacyBinaryContentStore`。
- 定义 `IContentRepository`，先实现二进制适配器。
- 新增 PostgreSQL 适配器和导入器，双读比对。

### 步骤 3：抽离区服宿主

- 新建 `Crystal.ZoneServer` 控制台宿主。
- 把 `Server.MirForms` 中的启动、停止、配置加载迁移过去。
- WinForms 只保留为开发期调试工具，最终删除或降级。

### 步骤 4：内容快照

- 把 `Envir` 中的 `ItemInfoList`、`MonsterInfoList`、`MapInfoList`、脚本等改为从 `IContentSnapshotProvider` 读取。
- 保留旧二进制加载路径作为回退。

### 步骤 5：Agent 与控制面

- 已实现：Agent 启动后向 Control API 注册并上报心跳；玩家数、Tick P95 和错误率本轮固定为 0，并标记
  `metricsSource=process-supervisor`。
- 已实现：从 Control API 流式下载签名发布 ZIP，拒绝路径穿越，校验实体哈希、内容哈希和 ECDSA 签名后原子 staging。
- 已实现：`stage`、`activate`、`rollback`、`reload`、`stop` 持久命令；命令有租约、过期和单生命周期互斥。
- 已实现：进程级换版、候选 ready 检查和失败后恢复上一版本；意外退出按退避策略重启 active 版本。
- 待后续：真实主循环指标、玩家状态迁移、进程内原子快照切换、灰度和多 Agent HA。

### 步骤 6：逻辑数据化

- 怪物 AI 的 `switch` 分支逐步映射为 `AIProfile` 配置。
- 技能/特效参数进入 `MagicInfo` 扩展字段或内容包。
- 活动、任务、掉落、回收、内挂、自动拾取优先用规则 DSL 或 Lua。

## 5. 线程与状态约束

- 世界主循环保持单写入者模型；跨线程操作通过消息队列进入主循环。
- 内容快照只读，切换时先构建新快照，再原子替换引用。
- 当前换版采用进程级重启，不做运行中快照替换；同一区服同一时刻只保留一个 Agent 和一个 ZoneServer 子进程。
- 热重载不能在中途修改正在被引用的可变对象；未来需要修改时复制对象或延迟到安全点。
- 玩家数据落库通过主循环产生事件，由独立持久化队列写入，避免阻塞 Tick。
- 所有区服命令必须幂等，并带 `command_id` 和超时。

## 6. 协议兼容

- `Crystal.Shared` 保留现有 `Packet`、`ClientPackets`、`ServerPackets`。
- 新网关只能在协议层做路由、限流和版本检查，不改变包体语义。
- 破坏性协议变更必须提升 `protocol_version`，并提供至少一个版本的兼容窗口。
- 客户端内容版本与服务端内容版本通过登录/进服握手校验。

## 7. 模块负责人视角

| 模块 | 首要目标 | 不能破坏的约束 |
| --- | --- | --- |
| GameCore | 行为可回归、可测试 | 现有战斗/地图/任务行为 |
| ZoneServer | 独立托管、可观测 | 主循环 Tick 稳定性 |
| Content | 可校验、可编译 | 发布不可变、可回滚 |
| Control API | 安全、可审计 | 不直接操作游戏内存 |
| Control Web | 编辑效率、权限 | 不绕过 API |
| Patcher | 安全、可靠更新 | 签名、原子性、可回滚 |

