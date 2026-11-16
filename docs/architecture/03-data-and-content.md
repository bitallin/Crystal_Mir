# 数据与内容架构

> 最后更新：2026-11-13
> 状态：生效

## 1. 数据域

| 数据域 | 例子 | 事实来源 |
| --- | --- | --- |
| 身份与账号 | 账号、登录方式、封禁、会话 | PostgreSQL |
| 角色与资产 | 角色、背包、装备、技能、任务、邮件、拍卖 | PostgreSQL |
| 世界内容 | 物品、怪物、地图、NPC、掉落、技能、任务、活动 | PostgreSQL + 内容包 |
| 资源 | 图库、地图文件、音效、特效、补丁包 | 对象存储 + 内容寻址 |
| 运营 | 区服、模板、开区任务、合区任务、补偿 | PostgreSQL |
| 交易 | 订单、支付流水、账本、发货记录 | PostgreSQL |
| 审计 | 管理操作、发布记录、GM 操作、安全事件 | PostgreSQL + 只读归档 |

## 2. 数据库选择

- 主库：PostgreSQL 16+。
- 缓存/锁：Redis 7+。
- 对象存储：MinIO 或兼容 S3 的存储。
- 搜索：初期 PostgreSQL 全文索引；内容规模变大后再引入 Meilisearch/OpenSearch。
- 消息：初期使用 PostgreSQL Outbox + 后台 Worker；规模扩大后引入 NATS/RabbitMQ。

选择理由见 `adr/0002-postgresql-and-content-snapshots.md`。

## 3. ID 策略

现有协议大量使用 `int Index`，不能立即替换。采用双 ID：

| ID | 用途 | 规则 |
| --- | --- | --- |
| `id bigint` | 控制面全局唯一 ID | Snowflake 或数据库序列，跨区唯一 |
| `local_index int` | 协议和现有客户端使用 | 每个内容版本/区服内唯一 |
| `zone_id` | 区服归属 | 玩家、公会、订单、邮件等带区服维度 |
| `content_key` | 内容实体稳定标识 | 例如 `item.1001`、`monster.2003` |

合区时通过 `merge_id_map` 把旧区服 ID 映射到新区服 ID；协议层只暴露映射后的 `local_index`。

## 4. 核心表设计

### 4.1 身份与角色

控制面管理端身份（M1 已实现）与游戏玩家账号分离，避免后台权限和游戏登录混用：

```text
control_account(id, username, username_normalized, email, display_name, password_hash,
                status, mfa_enabled, failed_login_count, locked_until, last_login_at)
control_role(id, code, name, description, is_system)
control_permission(id, code, description)
control_role_permission(role_id, permission_id)
control_account_role(account_id, role_id, assigned_at)
control_session(id uuid, account_id, refresh_token_hash, refresh_token_expires_at,
                client_ip, user_agent, revoked_at, revoked_reason)
```

密码使用 Argon2id；刷新令牌只保存 SHA-256 哈希。访问令牌是短时 JWT，并绑定 `control_session`，
会话撤销后访问令牌立即失效。

游戏侧账号与角色目标模型（尚未实现）：

```text
account(id, username, email, password_hash, status, created_at)
account_identity(account_id, provider, external_id)
character(id, account_id, zone_id, name, class, gender, level, data_jsonb)
guild(id, zone_id, name, leader_character_id, data_jsonb)
guild_member(guild_id, character_id, rank, joined_at)
```

角色高频变化的资产不放在大 JSON 中，按需拆表：

```text
character_item(id, character_id, item_key, slot, durability, data_jsonb)
character_magic(character_id, magic_key, level, experience)
character_quest(character_id, quest_key, state, progress_jsonb)
mail(id, zone_id, sender_id, receiver_id, subject, body, items_jsonb, gold, status)
auction(id, zone_id, seller_id, item_jsonb, price, status)
```

### 4.2 内容编辑

内容编辑使用规范化表或按类型分表，复杂结构用 `jsonb`：

```text
content_item(id, content_key, name, type, data_jsonb, schema_version, updated_at)
content_monster(id, content_key, name, ai_profile, data_jsonb, schema_version)
content_map(id, content_key, file_name, title, data_jsonb, schema_version)
content_npc(id, content_key, name, map_key, script_key, data_jsonb)
content_quest(id, content_key, title, data_jsonb, schema_version)
content_magic(id, content_key, name, data_jsonb, schema_version)
content_script(id, content_key, language, source, compiled_hash)
```

通用表结构（M1 已实现）：

```text
content_import_run(
  id bigint primary key,
  source text not null,
  source_hash text,
  legacy_version int,
  status text not null,
  entity_count int not null,
  summary jsonb not null,
  started_at timestamptz,
  finished_at timestamptz
)

content_entity(
  id bigint primary key,
  import_run_id bigint references content_import_run(id),
  entity_type text not null,
  local_index bigint not null,
  content_key text not null,
  schema_version int not null,
  data jsonb not null,
  created_at timestamptz,
  unique (import_run_id, entity_type, content_key),
  unique (import_run_id, entity_type, local_index)
)
```

一次二进制导入对应一个 `content_import_run`，批次不可变，便于回滚和历史对比。

可编辑草稿使用独立副本，不修改导入批次：

```text
content_draft(
  id bigint primary key,
  name text not null,
  base_import_run_id bigint references content_import_run(id),
  status text not null,              -- editing / validated / failed / archived
  created_by bigint,
  updated_by bigint,
  entity_count int not null,
  last_validation jsonb,
  created_at timestamptz,
  updated_at timestamptz
)

content_draft_entity(
  draft_id bigint references content_draft(id),
  entity_type text not null,
  local_index bigint not null,
  content_key text not null,
  schema_version int not null,
  data jsonb not null,
  revision bigint not null,
  updated_by bigint,
  updated_at timestamptz,
  unique (draft_id, entity_type, content_key),
  unique (draft_id, entity_type, local_index)
)

content_draft_revision(
  id bigint primary key,
  draft_id bigint references content_draft(id),
  revision_number bigint not null,
  action text not null,              -- create / update / delete
  entity_type text not null,
  local_index bigint not null,
  content_key text not null,
  before_data jsonb,
  after_data jsonb,
  actor_account_id bigint,
  reason text,
  occurred_at timestamptz,
  unique (draft_id, revision_number)
)
```

草稿创建时复制基础批次的全部实体。逐实体 `revision` 用于乐观并发；更新/删除携带
`expectedRevision`，不一致返回 409。每次变更追加不可修改的修订记录，保存操作者、原因、前后数据和时间。
校验结果持久化到 `content_draft.last_validation`，发布时从草稿当前实体复制到不可变发布版本。

专用表用于需要索引、关联约束和后台筛选的字段；`data jsonb` 保存完整实体。

文件型、启动生成和资源元数据也进入同一实体模型：M1 已支持把 `Envir/Drops` 导入为 `drop` 实体，内容键为
`drop.monster.<Index>`，保留掉落路径、相对文件名和解析后的条目；`Envir/Recipe` 导入为 `recipe` 实体，
保留产出、材料、工具、成功率、金币和解锁条件；启动时生成的 `BuffInfo` 导入为 `buff` 实体；`Envir/NPCs`
文本导入为 `script` 实体，保留规范化原文、页面、包含/插入/调用关系和 NPC 索引；`Maps` 与 `Envir/Previews`
文件导入为 `asset` 元数据，只保存路径、类型、大小和 SHA256，不复制二进制内容。这些实体都不参与旧二进制重建；
Control API 直接按 `jsonb` 关系提供地图拓扑、刷新点、传送门、NPC、NPC 脚本、怪物掉落和配方引用只读查询，
并通过通用内容接口查询资源元数据。脚本实体同时保存由 `NpcScriptParser` 生成的 AST、校验问题和稳定编译哈希；
地图资源由 `MapAssetParser` 解析格式、尺寸、可走/阻挡/门格。资源文件由配置的 `ControlApi:AssetRoot` 提供，
下载前校验路径不越界、文件大小和 SHA256，但二进制仍不进入快照或 PostgreSQL。图库/音效等其他资源服务仍需后续管线。

### 4.3 发布与资源

```text
content_release(
  id bigint primary key,
  import_run_id bigint,
  draft_id bigint,
  version text not null,
  status text not null,             -- draft / validating / compiled / signed / published / active / archived / failed / rolled_back
  schema_version int not null,
  format_version int not null,
  content_hash text not null,
  manifest_object_key text,
  created_by bigint,
  created_at timestamptz,
  published_at timestamptz,
  notes text,
  rolled_back_at timestamptz,
  rolled_back_by bigint,
  rollback_reason text,
  rollback_target_id bigint references content_release(id)
)

content_release_entity(release_id, entity_type, local_index, content_key, payload jsonb)

asset(
  id bigint primary key,
  sha256 text unique not null,
  object_key text not null,
  asset_type text not null,          -- lib / map / sound / image / patch
  size_bytes bigint,
  metadata jsonb,
  license text,
  source text
)

content_release_asset(release_id, asset_id, role)
```

M1 已实现发布 API：创建版本时必须从已完成的 `content_import_run` 或 `content_draft` 二选一复制实体，
生成不可变 `content_release_entity`；重建并校验快照后编译到 `ReleaseRoot/<version>/`，再使用
ECDSA P-256 签名并发布。状态流转使用条件更新，失败校验会进入 `failed` 且禁止继续编译/签名/发布；
重复发布保持幂等。发布事务复制实体后会回读并重算内容哈希，来源草稿并发变更时返回 409；
从草稿创建版本后，后续草稿修改不会影响已创建版本。

回滚只作用于当前 `published` 或 `active` 版本。事务内锁定当前版本、当前 active 版本和目标版本，
重新验签目标产物后把当前版本标记为 `rolled_back`，把目标版本标记为 `active`，并记录
`rolled_back_at`、`rolled_back_by`、`rollback_reason` 和 `rollback_target_id`。部分唯一索引
保证最多一个 `active` 版本。详见 `adr/0006-content-drafts-and-release-rollback.md`。

### 4.4 区服与运营

```text
zone(id, name, template_id, status, host, game_port, api_port,
     content_release_id, capacity, opened_at, closed_at)
zone_template(id, name, config jsonb)
operation_job(id, type, status, payload jsonb, result jsonb,
              created_by, started_at, finished_at)
merge_job(id, source_zone_id, target_zone_id, status, report jsonb)
```

### 4.5 交易与发货

```text
payment_order(id, zone_id, account_id, product_key, amount, currency,
              status, provider, provider_order_id, created_at, paid_at)
payment_transaction(id, order_id, provider_tx_id, amount, status, raw jsonb)
ledger_entry(id, account_id, order_id, direction, amount, balance_after,
             reason, created_at)
delivery_outbox(id, order_id, zone_id, payload jsonb, status, attempts,
                next_attempt_at, delivered_at)
```

账本只追加不修改；退款通过反向分录实现。

### 4.6 审计

```text
audit_event(
  id bigint primary key,
  occurred_at timestamptz,
  actor_account_id bigint,
  actor_label text,
  actor_ip inet,
  action text,
  resource_type text,
  resource_id text,
  before_hash text,
  after_hash text,
  request_id text,
  result text,                 -- success / failure / denied
  reason text,
  user_agent text,
  metadata jsonb
)
```

M1 当前记录登录、刷新、登出、账号创建和授权拒绝。数据库触发器禁止 UPDATE、DELETE 和 TRUNCATE；
后续归档到只读存储，并补充保留策略。

## 5. 内容生命周期

```text
draft -> validating -> compiled -> signed -> published -> active -> archived
                         |            |
                         +-> failed   +-> rolled_back
```

| 阶段 | 产物 | 是否可修改 |
| --- | --- | --- |
| draft | 导入批次或可编辑草稿的不可变候选 | 否 |
| validating | 校验报告 | 否 |
| compiled | 内容包 + manifest | 否 |
| signed | 签名 manifest | 否 |
| published | 可被区服拉取的 release | 否 |
| active | 平台当前生效版本；由回滚事务设置 | 否 |
| rolled_back | 已被回滚并保留目标和原因 | 否 |

`publish` 只把版本标记为可用，不直接设置 `active`。当前没有 active 版本时，Control.Web 展示最新发布版本，
但不会虚构区服灰度或热切换状态；真实区服激活属于 M3 Agent。

## 6. 内容包格式

内容包由 manifest、实体数据、脚本和资源引用组成：

```json
{
  "release_id": 10023,
  "version": "2026.11.09.1",
  "schema_version": 1,
  "protocol_version": 1,
  "content_hash": "sha256:...",
  "entities": {
    "item": ["items.json"],
    "monster": ["monsters.json"],
    "map": ["maps.json"],
    "npc": ["npcs.json"],
    "quest": ["quests.json"],
    "script": ["scripts.json"]
  },
  "assets": [
    { "path": "Data/Items.lib", "sha256": "...", "size": 123456 }
  ],
  "min_client_version": "0.1.0",
  "signature": {
    "algorithm": "ecdsa-p256-sha256",
    "keyId": "content-release-2026-01",
    "signedAt": "2026-11-11T00:00:00Z",
    "value": "base64-encoded-p1363-signature"
  }
}
```

当前签名实现使用 ECDSA P-256 + SHA-256；签名载荷是包含完整无签名 manifest 和签名元数据的 canonical JSON envelope。`contentHash` 不包含签名，签名失败或缺失时由发布/区服策略决定拒绝加载还是告警。详见 `adr/0005-content-signature-format.md`。

初期使用 JSON + zstd 压缩；实体数量变大后再评估 MessagePack。资源文件独立存放，manifest 只引用哈希。

### 6.1 已实现快照格式（M1）

M1 初版使用目录快照，不等待压缩和签名组件：

```text
manifest.json          版本、来源哈希、实体文件哈希、计数、contentHash
summary.json           导入摘要
entities/<type>.jsonl  每行一个实体，按 localIndex 排序
```

- 顶层字段 camelCase；`data` 保留旧实体字段名，降低迁移期字段映射风险。
- 实体之间的旧对象引用写成 `{"$ref":"item","index":1001,"key":"item.1001"}`。
- `contentHash` 只由实体类型、数量和实体文件 SHA256 计算，不受发布时间和输入顺序影响。
- 读取器校验文件哈希、字节数、计数和 `contentHash`。
- PostgreSQL 导入以 `content_import_run` 隔离批次，快照和批次都不可变。
- 发布编译使用同一构建器在内存中重建 manifest，写入独立的 `ReleaseRoot/<version>/`；先写临时目录，
  再原子改名，已有同版本目录必须内容哈希一致。
- `POST /api/v1/releases/{id}/validate` 是 dry-run 校验；`compile` 会再次校验并把错误状态持久化为
  `failed`，`sign` 只接受 `compiled`，`publish` 只接受验签通过的 `signed`。

实现和黄金样本结果见 `reference/content-import.md`。

## 7. 校验规则

发布前必须通过：

- Schema 校验：字段类型、必填、枚举、数值范围。
- 引用校验：物品/怪物/地图/技能/脚本引用存在。
- 协议校验：索引范围、字段长度、包体大小限制。
- 资源校验：图库、地图、音效、特效文件存在且哈希匹配。
- 脚本校验：语法、编译、API 白名单、执行预算。
- 经济校验：掉落期望、经验曲线、货币产出、回收比例。
- 兼容校验：客户端最低版本、内容 schema 版本、协议版本。
- 冲突校验：同名/同索引/同地图坐标冲突。

校验结果分为 `error`、`warning`、`info`；有 `error` 时禁止发布。

## 8. 资源管线

| 资源 | 当前格式 | 处理方式 |
| --- | --- | --- |
| 图库 | `.lib`（MLibrary/WTL/WeMade 等） | 服务端读取元数据、生成缩略图、按哈希存储 |
| 地图 | `.map` | 解析尺寸/格子/门点；编辑复用 `Crystal.MapEditor` |
| 音效 | `.wav` 等 | 提取元数据、试听、打包 |
| 特效 | 图库帧 + 参数 | 资源引用 + 客户端参数配置 |
| 补丁包 | 文件级压缩 | 内容寻址、差分、签名 |

资源必须记录来源和许可证。没有明确授权的素材不得进入发布包。

## 9. 脚本模型

分两层：

1. **兼容层**：现有 NPC DSL，解析为 AST，支持格式化、校验、可视化块编辑。
2. **扩展层**：Lua 沙箱，用于活动、AI、任务、自定义玩法；暴露稳定游戏 API。

脚本发布时编译/校验，记录 `source_hash` 和 `compiled_hash`；运行时只加载与内容包匹配的版本。

## 10. 从二进制数据库迁移

```text
读取 Server.MirDB（现有 BinaryReader 构造器）
-> 映射到领域对象
-> 写入 PostgreSQL 草稿
-> 生成内容包
-> 用旧数据库和新内容包分别启动只读/沙箱区服
-> 对比实体数量、索引、关键属性、掉落期望
-> 切换默认数据源
-> 保留 LegacyBinaryExporter 作为回滚路径
```

迁移必须保留版本映射：当前 `MinVersion=60`、`Version=117`。旧字段在新 schema 中用 `legacy` 扩展保存，避免丢失。

