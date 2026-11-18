# 0012 Zone Agent 控制闭环与进程级内容换版

- 状态：已接受（运行时指标部分由 0013 扩展）
- 日期：2026-11-17

## 背景

M2 已打通 Control API 的签名发布和 ZoneServer 启动期只读消费，但发布后的区服仍需要人工下载、停机、
替换目录并手动重启。M3 需要把“发布 -> 区服下载 -> staging -> 激活 -> 失败恢复”变成可操作、可审计的服务端闭环。

当前游戏运行时仍由单区 Crystal `Envir`/`Map` 主循环承载，没有安全的多实例切换、玩家状态迁移或进程内快照替换能力。
本轮按研究项目口径，先接受短暂停机，锁定进程级换版边界，不引入 Control.Web、灰度、CDN、许可证或授权新模型。

## 决策

### 1. 控制面管命令，Agent 管进程

Control API 负责区服登记、心跳、命令队列、发布包下载端点和区服投影；它不启动、停止或探测 ZoneServer 进程，也不直接访问游戏主循环。

每个区服对应一个 `Crystal.ZoneAgent` 和一个 ZoneServer 子进程。Agent 作为父进程负责本地内容缓存、命令执行、
SIGTERM、ready 文件和异常退出重启。Agent 只引用 MIT 的 `Crystal.Agent.Contracts` 与 `Crystal.Content`，
通过可执行文件边界启动 GPL 的 ZoneServer，不链接 `Server/`、`Crystal.LegacyRuntime` 或 `Crystal.ZoneServer`。

### 2. 持久命令队列与租约

新增 `zone` 和 `zone_command` 表。命令状态机为：

```text
queued -> delivered -> succeeded | failed | rejected
                    \-> lease 过期后重新可领取
queued/delivered -> expired（超过 expires_at）
```

- 命令使用 UUID `command_id`，默认 10 分钟过期，最长 24 小时。
- Agent 通过 `claim=true` 领取命令；默认租约 60 秒，租约过期后可重投。
- `content.activate`、`content.rollback`、`content.reload` 和 `zone.stop` 每个区服同时最多一个未完成命令。
- 结果接口只接受 `succeeded`、`failed`、`rejected`；重复提交相同终态返回当前记录，冲突终态返回 409。
- Agent 在本地 `state/command-results/` 缓存结果；重复投递只重报结果，不重复执行换版。

### 3. 签名发布包与本地 staging

Control API 提供 `GET /api/v1/releases/{releaseId}/artifact`，只允许 `published` 或 `active` 且签名有效的发布。
Agent 下载 ZIP 后：

1. 拒绝绝对路径、`..`、空路径段和非 `manifest.json`/`summary.json`/`entities/` 文件。
2. 校验 manifest、实体文件大小/SHA256、`contentHash` 和 ECDSA P-256 签名。
3. 校验通过后原子移动到 `releases/{releaseId}-{contentHash}`。
4. 保留 `state/active.json` 记录当前版本、上一版本和激活时间。

发布包内容不可变；Agent 不修改 Control API 的 `content_release.status`。区服激活只更新 `zone` 投影，
避免把“某个区服正在运行”与“某个版本是全局 active”混为一谈。

### 4. 进程级激活与 ready 文件

Agent 激活候选版本时：

1. 停止旧 ZoneServer：优先 SIGTERM，超时后终止进程树。
2. 使用 `--content-release <dir>`、`--content-public-key`、`--content-key-id` 和 `--ready-file <path>` 启动候选进程。
3. 等待 ready JSON 中的 `pid` 与子进程 PID 一致。
4. 成功后写入 `active.json` 并上报 active 版本；候选失败时重新启动上一版本。
5. 当前无可恢复版本时进入 degraded，保留错误和日志路径供心跳/CLI 查看。

ZoneServer 增加 `--ready-file` 和 `CRYSTAL_ZONE_READY_FILE`，在 Envir 和网络就绪后原子写入 JSON，
退出时删除。未提供 ready 参数时 legacy 行为不变。

### 5. 停机与故障恢复边界

- 允许一次短暂的进程重启窗口，不做进程内热切换、双实例或玩家状态迁移。
- 子进程意外退出时，Agent 按 2/5/10/30 秒退避重启当前 active 版本；连续失败时心跳为 degraded。
- `zone.stop` 清理本地 active 状态并在控制面清空 active 投影；后续重新 `activate` 恢复服务。
- Agent 重启会读取 `state/active.json`，重新验签本地发布并恢复上次 active 版本。

### 6. 本轮安全边界

控制面与 Agent 位于可信私有网络，复用现有 `X-Api-Key`/JWT。管理侧命令创建要求 `zone:write`；Agent 面向的注册、
心跳、领取和结果端点目前不新增授权模型，便于研究环境闭环。该边界必须在生产化前重新评估。

本轮不实现：Control.Web 区服页面、客户端启动器、灰度/CDN、进程内热切换、玩家状态迁移、许可证和素材授权。
其中 Control.Web 区服页面随后已使用现有 API 落地；指标桥接由 `0013-zone-runtime-metrics-file.md` 扩展。

## 备选方案

1. Agent 嵌入 ZoneServer：会污染游戏主循环、难以独立升级和隔离故障，拒绝。
2. Control API 直接启动 ZoneServer：控制面需要节点级进程权限，破坏控制面/数据面分离，拒绝。
3. 原地覆盖内容目录：失败会破坏正在运行的区服，且无法证明下载完整性，拒绝。
4. 进程内原子替换快照：本轮缺少玩家状态迁移和主循环安全点设计，拒绝。
5. 多 Agent HA 或双实例蓝绿：需要会话粘滞、共享状态和停写协议，超出研究里程碑，拒绝。

## 影响

- 发布到区服运行形成端到端最小闭环：发布、下载、验签、staging、进程重启、ready、心跳和失败恢复。
- 换版会短暂停机；运维必须使用维护窗口和玩家公告。
- Control API 与 Agent 的故障边界清晰：控制面故障不直接杀死运行中的 ZoneServer，Agent 故障由 systemd 重启。
- 本 ADR 落地时区服 metrics 由 Agent 上报为 0；后续 `0013-zone-runtime-metrics-file.md` 已接入 ZoneServer 真实指标文件和
  `metricsSource=zone-runtime|unavailable|process-stopped`。
- 命令和结果持久化到 PostgreSQL，Agent 侧结果也可在控制面短暂不可用时待重报。

## 重新评估条件

- 需要零停机或双实例换版时。
- 需要把玩家会话和世界状态迁移纳入切换协议时。
- 需要灰度、CDN、对象存储直连或多 Agent HA 时。
- 需要生产级 Agent 双向认证、细粒度授权或密钥轮换时。
- 需要从本地指标文件升级到 Prometheus/OpenTelemetry 或跨主机汇聚时。
