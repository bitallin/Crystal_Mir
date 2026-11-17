# 0011 ZoneServer 启动期只读消费签名内容快照

- 状态：已接受
- 日期：2026-11-17

## 背景

M1 已能生成不可变内容快照、ECDSA 签名和发布 manifest，Control API 也能编译和发布版本，但 `Crystal.ZoneServer` 仍只从内容目录读取旧 `Server.MirDB`。如果 M2 不打通消费路径，后台发布的内容无法成为区服运行的已验证事实。

完整热更新需要 Zone Agent、staging 切换、玩家状态迁移和失败回滚，属于 M3。本轮需要在保持现有二进制库启动模式完全兼容的前提下，增加启动期只读消费已签名快照的能力，并明确失败时不得进入 ready。

## 决策

### 1. 共享 GPL 边界内的运行库

新增 `Crystal.LegacyRuntime`，承接快照重建、旧实体 JSON 反序列化、旧二进制导出和发布工作区物化逻辑。该库链接 `Server.Library`，位于 GPL 边界内；`Crystal.LegacyImporter`、`Crystal.ZoneServer` 可以引用它，`Crystal.Control.Api` 不得引用它。

Control API 只通过 MIT 的 `Crystal.Content`、契约文件和发布产物与运行库交互。

### 2. 启动参数与兼容回退

`Crystal.ZoneServer` 增加：

```text
--content-release <dir>
--content-public-key <path>
--content-key-id <id>
```

同时接受 `CRYSTAL_ZONE_CONTENT_RELEASE`、`CRYSTAL_ZONE_CONTENT_PUBLIC_KEY` 和 `CRYSTAL_ZONE_CONTENT_KEY_ID`。未提供 `--content-release` 时继续使用现有 `--content-root` 下的 `Server.MirDB`、`Configs/`、`Envir/` 和 `Maps/`，legacy 模式行为不变。

### 3. 加载前完成完整性校验

发布模式先校验 manifest 哈希、实体文件和 ECDSA P-256 签名，要求 manifest 中的 key ID 与 `--content-key-id` 一致。签名缺失、哈希不匹配、key ID 不匹配或快照读取失败时，启动失败并输出明确日志，不加载旧内容，也不进入 ready。

### 4. 在隔离目录重建运行内容

校验通过后，在系统临时目录创建隔离运行根：

- 重建 `Server.MirDB`。
- 将 `drop`、`recipe`、`script`、`quest` 实体物化到临时 `Envir/` 覆盖目录。
- 地图二进制、配置和其余运行文件从 `--content-root` 软链接或复制到覆盖目录。
- 不修改正在运行的发布目录或源内容目录。

随后按现有 `Settings.Load()`、`LoadDB()`、`Envir.Start()` 流程启动。地图、脚本、配方或运行文件无法物化时启动失败；成功进入 ready 后记录地图、物品、怪物、NPC 和任务计数。SIGTERM/SIGINT 继续走原有保存和优雅退出路径，临时工作区在退出后清理。

### 5. 本轮只做启动期只读消费

发布模式不轮询、不热重载、不切换 staging、不执行回滚命令，也不更新运行中的快照。区服要换版本必须重启。Agent、灰度、staging 和热切换留给 M3。

## 备选方案

1. 直接在 `Crystal.Control.Api` 中重建旧库：会让控制面链接 GPL 代码，拒绝。
2. 由 ZoneServer 直接读取 PostgreSQL：把控制面存储耦合进数据面，并绕过不可变发布产物，拒绝。
3. 在现有内容目录原地覆盖：失败时会破坏正在运行的内容，拒绝。
4. 本轮实现 Agent 热切换：超出 M2 范围，且需要玩家状态迁移和回滚协议，拒绝。

## 影响

- 已签名发布版本可以成为 ZoneServer 的实际启动输入，后台发布到区服运行形成闭环。
- 旧二进制库启动模式保留为默认回退，不需要迁移现有部署。
- 签名或完整性失败是启动失败，不会静默使用旧数据库。
- Control API 的许可证边界保持不变。
- 发布目录和源内容根在运行期间只读；临时物化目录可清理。
- M2 不提供零停机换版，版本切换仍需要重启维护窗口。

## 重新评估条件

- 实现 M3 Zone Agent 或热切换时。
- 需要从对象存储直接拉取发布产物或支持多版本缓存时。
- 需要就地更新地图/资源而不重启时。
- 需要支持回滚命令、灰度路由或运行中内容版本上报时。
