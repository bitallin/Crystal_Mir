# 热更新架构

> 最后更新：2026-11-17
> 状态：生效

## 1. 两条更新通道

| 通道 | 更新对象 | 触发方 | 失败影响 |
| --- | --- | --- | --- |
| 客户端通道 | 客户端程序、图库、地图、音效、UI 资源 | 启动器 | 玩家无法进入或资源不一致 |
| 服务端通道 | 内容包、脚本、配置、地图元数据 | Zone Agent | 区服内容不一致或重载失败 |

两条通道共享 `release_id`、`schema_version`、`protocol_version`，登录时必须校验兼容性。

## 2. 客户端更新

### 2.1 流程

```text
Launcher 启动
-> 请求 https://patch.example.com/channels/{channel}/manifest.json
-> 校验 Ed25519 签名和有效期
-> 扫描本地文件，计算 sha256
-> 生成差异列表（新增/修改/删除）
-> 优先下载 delta，失败回退全量
-> 下载到 staging 目录
-> 校验哈希和签名
-> 原子替换 / 两阶段提交
-> 启动 Client
```

### 2.2 Manifest

```json
{
  "channel": "stable",
  "release": "2026.11.09.1",
  "min_launcher": "0.1.0",
  "protocol_version": 1,
  "content_schema_version": 1,
  "generated_at": "2026-11-09T10:00:00Z",
  "expires_at": "2026-12-09T10:00:00Z",
  "files": [
    {
      "path": "Data/Items.lib",
      "size": 12345678,
      "sha256": "...",
      "url": "https://cdn.example.com/assets/sha256/...",
      "delta_from": [
        { "base_sha256": "...", "url": "https://cdn.example.com/delta/..." }
      ]
    }
  ],
  "deleted": ["Data/Old.lib"],
  "signature": "ed25519:..."
}
```

### 2.3 安全与可靠性

- 只信任签名 manifest；文件哈希必须在 manifest 内。
- 使用内容寻址 URL，CDN 不可信也不会导致错误内容。
- 下载支持断点续传、并发限速、磁盘空间检查。
- 替换前保留上一版本；失败自动回滚。
- 防降级：manifest 包含最低允许版本和发布时间。
- 灰度：按账号/区服/百分比分配 channel。
- 代码更新：启动器自身两阶段自更新；客户端主程序更新后必须重新校验资源。

### 2.4 与现有补丁工具的关系

现有 `PList.gz` 和 `AutoPatcherAdminV2` 继续作为过渡方案：

- 保留文件格式读取能力，降低迁移风险。
- 新发布系统输出兼容的 `PList.gz` 清单，逐步替换上传流程。
- 新启动器稳定后，再移除旧的 FTP/SFTP 手工发布路径。

## 3. 服务端内容更新

### 3.1 流程

```text
Control API 发布签名 release 并写入持久命令
-> Zone Agent 轮询领取命令（租约 + 过期时间）
-> 下载发布 ZIP 到本地 staging 临时目录
-> 拒绝路径穿越并校验 manifest/实体哈希、contentHash 和 ECDSA 签名
-> 原子移动到 releases/{releaseId}-{contentHash}
-> 激活时 SIGTERM 旧 ZoneServer，启动候选进程并等待 ready 文件
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
state/command-results/                已执行命令结果，供重投/重启后重报
logs/                                 每次 ZoneServer 启动的 stdout/stderr
```

ZoneServer 仍保留 M2 的启动期消费路径和 legacy 模式。Agent 模式使用 `--content-release <dir>`、
`--content-public-key <path>`、`--content-key-id <id>` 和 `--ready-file <path>`；也可用
`CRYSTAL_ZONE_CONTENT_RELEASE`、`CRYSTAL_ZONE_CONTENT_PUBLIC_KEY`、`CRYSTAL_ZONE_CONTENT_KEY_ID`、
`CRYSTAL_ZONE_READY_FILE` 注入。签名缺失、哈希不匹配、key ID 不匹配、地图/脚本/配方物化失败或 ready 超时
都会使候选启动失败。未提供发布参数时继续使用 `--content-root` 下的 `Server.MirDB` 和运行文件。

详见 `adr/0012-zone-agent-process-level-content-switching.md`。本轮不做进程内原子切换、灰度/CDN、Control.Web
区服页面、客户端启动器、玩家状态迁移和许可证/授权新模型。

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
| 客户端替换失败 | 从备份目录恢复 |
| 协议不兼容 | 拒绝登录，提示更新客户端 |

## 6. 审计与可观测

每次发布和更新记录：

- `release_id`、操作者、时间、渠道、目标区服。
- 下载耗时、失败原因、重试次数。
- 命令 ID、状态、租约、attempts、执行耗时和结果错误。
- 进程 PID、ready 文件、激活/回滚耗时、恢复次数和当前/上一版本。
- 客户端更新成功率、按版本分布。

本轮 Agent 上报 `metricsSource=process-supervisor`，玩家数、Tick P95 和错误率为 0；真实主循环指标接入留待后续。

