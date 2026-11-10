# 运行时与模块边界

> 最后更新：2026-11-10
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
Crystal.ZoneServer            区服宿主、网络、内容加载、Agent（GPL 边界）
Crystal.Agent.Contracts       Agent 与控制面的 gRPC/HTTP 契约（MIT）
Crystal.Control.Api           控制面 API（独立许可，不链接 GPL 代码）
Crystal.Control.Web           管理后台前端
Crystal.Patcher               清单、签名、差分、发布工具
Crystal.Cli                   运维与开发 CLI
```

当前已落地（M1）：

```text
src/Crystal.Content           内容快照、哈希、校验（MIT）
src/Crystal.Control.Api       内容、账号、RBAC 和审计 API，ASP.NET Core + Npgsql
src/Crystal.ZoneServer        Linux 区服控制台宿主，加载旧数据库和内容并监听游戏端口（GPL 边界）
tools/Crystal.LegacyImporter  二进制导入、快照/PostgreSQL 重建和 PostgreSQL 写入（GPL 边界工具）
db/migrations                 PostgreSQL schema
```

`Crystal.Control.Api` 只依赖 `Crystal.Content`、Npgsql 和 ASP.NET Core，不引用 GPL 游戏代码。
当前提供健康检查、导入批次、内容只读查询、账号登录、短时 JWT、刷新会话、RBAC、只追加审计和账号
生命周期（停用/启用、重置/自助改密、角色调整）；`Crystal.Control.Web` 已通过受控 CORS 接入登录、
内容总览、导入批次、10 类内容查询、账号管理、角色和审计，并按 JWT 权限点过滤导航与直达路由。
内容写入/发布 API 和真实世界关系 API 尚未实现。

## 3. 依赖规则

```text
Control.Web -> Control.Api -> Content / Agent.Contracts / Shared
ZoneServer  -> GameCore -> Shared
ZoneServer  -> Content / Agent.Contracts
Patcher     -> Shared / Content.Contracts

禁止：
Control.Api -> GameCore / ZoneServer
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

- 区服启动后向 Agent 注册，上报心跳、玩家数、Tick 延迟、错误率。
- 从控制面拉取签名内容包，加载到 staging 快照。
- 通过命令切换内容版本、重载、停机、备份。

### 步骤 6：逻辑数据化

- 怪物 AI 的 `switch` 分支逐步映射为 `AIProfile` 配置。
- 技能/特效参数进入 `MagicInfo` 扩展字段或内容包。
- 活动、任务、掉落、回收、内挂、自动拾取优先用规则 DSL 或 Lua。

## 5. 线程与状态约束

- 世界主循环保持单写入者模型；跨线程操作通过消息队列进入主循环。
- 内容快照只读，切换时先构建新快照，再原子替换引用。
- 热重载不能在中途修改正在被引用的可变对象；需要修改时复制对象或延迟到安全点。
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

