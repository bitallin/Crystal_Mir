# 热更新架构

> 最后更新：2026-11-18
> 状态：生效

## 1. 两条更新通道

| 通道 | 更新对象 | 触发方 | 失败影响 |
| --- | --- | --- | --- |
| 客户端通道 | 客户端程序、图库、地图、音效、UI 资源 | 启动器 | 玩家无法进入或资源不一致 |
| 服务端通道 | 内容包、脚本、配置、地图元数据 | Zone Agent | 区服内容不一致或重载失败 |

两条通道共享发布版本、schema 版本和协议版本语义，登录时必须校验兼容性。

## 2. 客户端更新

### 2.1 流程

```text
Launcher 启动
-> 请求 https://patch.example.com/channels/{channel}/manifest.json
-> 校验 ECDSA P-256 签名、keyId、格式、渠道和有效期
-> 校验最低 launcher/client 版本、协议版本和内容 schema 版本
-> 拒绝早于上次已应用 manifest 的降级重放
-> 扫描本地文件，计算 SHA256
-> 生成文件级差异列表（新增/修改/删除）
-> 下载变化文件到 .crystal-update/staging，支持 HTTP Range 续传
-> 逐文件校验大小和 SHA256
-> 写 pending 事务、备份旧文件、移入新文件、写 state
-> 启动 Client
```

失败时恢复本事务备份；进程在事务中崩溃时，下次启动读取 `.crystal-update/pending.json` 回滚未完成提交。

### 2.2 Manifest

```json
{
  "formatVersion": 1,
  "channel": "stable",
  "release": "2026.11.18.1",
  "minimumLauncherVersion": "1.0.0",
  "minimumClientVersion": "1.0.0",
  "protocolVersion": 1,
  "contentSchemaVersion": 1,
  "generatedAt": "2026-11-18T00:00:00+00:00",
  "expiresAt": "2026-12-18T00:00:00+00:00",
  "files": [
    {
      "path": "Data/Items.lib",
      "size": 12345678,
      "sha256": "…64 位小写十六进制…",
      "url": "https://cdn.example.com/releases/2026.11.18.1/Data/Items.lib"
    }
  ],
  "deleted": ["Data/Old.lib"],
  "signature": {
    "algorithm": "ecdsa-p256-sha256",
    "keyId": "release-2026",
    "signedAt": "2026-11-18T00:00:00+00:00",
    "value": "…Base64 IEEE P1363 签名…"
  }
}
```

完整字段和 CLI 见 `reference/client-patch-manifest.md`。

### 2.3 安全与可靠性

- 只信任签名 manifest；文件哈希必须在 manifest 内。
- 签名使用 ADR 0005 同源的 ECDSA P-256/SHA-256 和 `keyId`，载荷前缀为 `crystal-client-patch-v1`。
- 路径拒绝绝对路径、`..`、空段、冒号/控制字符、Windows 非法字符/设备名/尾部点或空格、大小写不敏感重复路径和 `.crystal-update/` 保留目录。
- 下载支持 HTTP Range 断点续传；每个文件落盘后校验大小和 SHA256。
- 替换前保留上一版本；失败自动回滚。事务只处理 manifest 声明的路径，未知本地文件不删除。
- 防降级：校验最低版本、协议/schema 版本、有效期，并拒绝早于上次已应用 `generatedAt` 的 manifest。
- 灰度：按账号/区服/百分比分配 channel。
- 代码更新：本轮 launcher 自身两阶段自更新未实现，manifest 应排除运行中的 launcher；客户端主程序更新后必须重新校验资源。

### 2.4 与现有补丁工具的关系

`Crystal.Patcher`（MIT、`net8.0`）和 `Crystal.PatcherTool` 已实现签名 manifest 的文件级增量、事务应用和回滚。
现有 `PList.gz` 和 `AutoPatcherAdminV2` 继续作为兼容路径：

- 保留文件格式读取能力，降低迁移风险。
- `Launcher:ManifestUrl` 为空时旧启动器流程完全不变；配置后改用签名 manifest，失败时禁用启动，不静默回退。
- 新发布系统可继续输出兼容的 `PList.gz` 清单，逐步替换上传流程。
- 新启动器稳定后，再移除旧的 FTP/SFTP 手工发布路径。

本轮未实现：二进制 delta、灰度/CDN 渠道分发、并发限速、磁盘空间预检、launcher 自更新和 Windows 运行冒烟。

## 3. 服务端内容更新

### 3.1 流程

```text
Control API 发布签名 release 并写入持久命令
-> Zone Agent 轮询领取命令（租约 + 过期时间）
-> 下载发布 ZIP 到本地 staging 临时目录
-> 拒绝路径穿越并校验 manifest/实体哈希、contentHash 和 ECDSA 签名
-> 原子移动到 releases/{releaseId}-{contentHash}
-> 激活时 SIGTERM 旧 ZoneServer，启动候选进程并等待 ready 文件
-> ZoneServer ready 后每秒原子写出指标文件
-> Agent 每次心跳校验并上报指标；Control.Web 读取区服投影和命令历史
-> 成功后更新 active.json、心跳和命令结果
-> 候选失败则重启上一版本；结果写回 Control API
```

当前实现是进程级换版，允许短暂停机。`stage` 只下载和验证，不改变运行版本；`activate`、`rollback` 和 `reload`
会重启 ZoneServer；`stop` 停止子进程并清空 active 状态。命令状态、租约和结果幂等由 `zone_command` 持久化。
同一区服同时只允许一个未完成的激活、回滚、重载或停机命令。

### 3.2 M3 当前实现：Agent 闭环

当前闭环为：

```text
Control API
  -> zone / zone_command / release artifact
  -> Zone Agent
       -> releases/、staging/、state/、logs/
       -> SIGTERM + --content-release + --ready-file
       -> ZoneServer 子进程
       -> settings / rebuild / Envir.Start / ready JSON
```

Agent 目录约定：

```text
releases/{releaseId}-{contentHash}/   已验签的不可变发布
staging/                              下载/解压临时目录
state/active.json                     当前和上一版本
state/ready.json                      Agent 等待的 ZoneServer ready 文件
state/metrics.json                    ZoneServer 原子写出的 60 秒运行时指标
state/command-results/                已执行命令结果，供重投/重启后重报
logs/                                 每次 ZoneServer 启动的 stdout/stderr
```

ZoneServer 仍保留 M2 的启动期消费路径和 legacy 模式。Agent 模式使用 `--content-release <dir>`、
`--content-public-key <path>`、`--content-key-id <id>` 和 `--ready-file <path>`；也可用
`CRYSTAL_ZONE_CONTENT_RELEASE`、`CRYSTAL_ZONE_CONTENT_PUBLIC_KEY`、`CRYSTAL_ZONE_CONTENT_KEY_ID`、
`CRYSTAL_ZONE_READY_FILE` 注入。签名缺失、哈希不匹配、key ID 不匹配、地图/脚本/配方物化失败或 ready 超时
都会使候选启动失败。未提供发布参数时继续使用 `--content-root` 下的 `Server.MirDB` 和运行文件。

详见 `adr/0012-zone-agent-process-level-content-switching.md`。Control.Web 已使用现有区服、命令和发布查询接口
实现区服总览/详情、真实指标、命令历史和 stage/activate/rollback/reload/stop 操作，不新增 API 路由或 DTO。
本轮仍不做进程内原子切换、灰度/CDN、客户端启动器、玩家状态迁移和许可证/授权新模型。

### 3.2.1 运行时指标桥接

ZoneServer 通过 `--metrics-file <path>` / `CRYSTAL_ZONE_METRICS_FILE` 在 ready 后写出：

```json
{
  "schemaVersion": 1,
  "zoneId": "zone-1",
  "pid": 1234,
  "processStartedAt": "2026-11-18T00:00:00Z",
  "observedAt": "2026-11-18T00:00:10Z",
  "playerCount": 37,
  "tickP95Ms": 42.5,
  "errorRate": 0.03,
  "completedTicks": 12345,
  "errorCount": 7,
  "sampleCount": 600,
  "windowSeconds": 60
}
```

采样由 `Crystal.GameCore` 的 MIT 采样器完成：60 秒窗口、100 ms 采样、最近秩 P95，按 `completedTicks` 去重世界循环样本；
错误率为窗口内异常增量除以 Tick 增量并封顶 1。Agent 只接受 schema、zoneId、PID、时间范围和 15 秒时效均有效的文件；
无效时上报 0 并标记 `metricsSource=unavailable`，进程停止时标记 `process-stopped`，连续三次失效且子进程仍运行则降级。
指标写入失败不会终止 ZoneServer。详见 `adr/0013-zone-runtime-metrics-file.md`。

Control API 在每次心跳同一事务中写入 `zone_metric_sample`，以 `(zone_id, observed_at)` 去重。区服详情通过
`GET /api/v1/zones/{zoneId}/metrics` 读取最近窗口样本、汇总和健康评估；健康等级按最新投影与可配置阈值实时计算，
不改变 Agent 心跳协议、`ZoneDto` 或 ZoneServer 指标文件。告警状态机、通知渠道和 Prometheus/OpenTelemetry 汇聚仍
属于后续范围，详见 `adr/0014-zone-metric-history-and-health-evaluation.md`。

### 3.3 快照模型

```text
ContentSnapshot
├── ReleaseId
├── SchemaVersion
├── Items / Monsters / Maps / NPCs / Quests / Magic
├── Scripts（已编译）
├── Assets（哈希与本地缓存路径）
└── Capabilities（协议、客户端最低版本）
```

运行中的对象只持有不可变快照引用。需要修改对象时，复制到新实例，不原地修改旧快照。

### 3.4 热重载边界

下表是未来进程内热重载的评估口径，不代表本轮已实现；当前生产路径仍是 Agent 进程级重启。

| 内容 | 是否可热重载 | 约束 |
| --- | --- | --- |
| 物品/怪物/掉落/技能参数 | 是 | 新引用生效；已存在实例按迁移规则处理 |
| NPC 脚本/对话 | 是 | 重新编译并替换脚本表 |
| 任务/活动配置 | 是 | 需要处理进行中的任务状态 |
| 地图元数据 | 是 | 已加载地图实例可能需要重建 |
| 地图几何/阻挡 | 条件 | 需要重建地图实例，建议维护窗口 |
| 协议/包结构 | 否 | 必须客户端版本同步 |
| 数据库 schema | 否 | 走迁移和停机/双写流程 |

### 3.5 玩家状态迁移

内容变更可能影响角色数据：

- 删除物品：先检查持有量，禁止直接发布或提供替换规则。
- 修改技能：保留旧技能记录，按 `legacy_key` 映射。
- 修改地图：角色所在地图被删除时，传送到安全点并记录审计。
- 修改任务：进行中的任务按旧版本完成或标记为失效并补偿。

## 4. 协议版本

三类版本必须同时校验：

| 版本 | 作用 | 变更影响 |
| --- | --- | --- |
| `protocol_version` | 网络包结构 | 不兼容客户端，必须同步更新 |
| `content_schema_version` | 内容字段结构 | 服务端内容包兼容性 |
| `client_min_version` | 客户端最低版本 | 登录/启动时拦截旧客户端 |

登录握手返回服务端版本和区服内容版本；版本不匹配时给出明确升级提示。

## 5. 失败处理

| 场景 | 处理 |
| --- | --- |
| manifest/实体哈希或签名失败 | stage 失败，删除临时目录，旧 ZoneServer 继续运行并上报 failed |
| 下载中断 | 删除不完整 ZIP 和 staging，命令失败；重试时重新下载完整包 |
| 哈希不匹配 | 删除临时文件，重试全量；Agent 本轮只做一次完整下载并由命令重试驱动 |
| 候选进程启动失败/ready 超时 | SIGTERM 候选进程，重新启动上一 active 版本；无上一版本则 degraded |
| Agent 重启 | 读取 `state/active.json`，重新验签本地发布并恢复 active 版本 |
| ZoneServer 意外退出 | 按 2/5/10/30 秒退避重启 active 版本；持续失败时心跳 degraded |
| 指标文件缺失/过期/PID 不匹配 | 心跳三项指标归零并标记 `metricsSource=unavailable`；连续三次失败时 degraded |
| ZoneServer 已停止 | 指标归零并标记 `metricsSource=process-stopped` |
| 客户端替换失败 | 从备份目录恢复 |
| 协议不兼容 | 拒绝登录，提示更新客户端 |

## 6. 审计与可观测

每次发布和更新记录：

- `release_id`、操作者、时间、渠道、目标区服。
- 下载耗时、失败原因、重试次数。
- 命令 ID、状态、租约、attempts、执行耗时和结果错误。
- 进程 PID、ready 文件、激活/回滚耗时、恢复次数、当前/上一版本和指标窗口/样本数。
- 指标历史窗口的首末时间、样本数、健康等级和触发的告警代码。
- 客户端更新成功率、按版本分布。

Agent 上报 `metricsSource=zone-runtime|unavailable|process-stopped`；`restartFailures` 继续独立展示，
不与玩家数、Tick P95 或错误率混用。

