# 0006 内容草稿、修订与发布回滚模型

- 状态：已接受
- 日期：2026-11-13

## 背景

M1 已能把不可变导入批次编译为内容版本，并完成校验、签名和发布，但版本作者无法在不影响已发布版本的前提下修改内容，
也无法把已发布版本切回上一个稳定版本。若直接在 `content_release` 或 `content_entity` 上修改，会破坏不可变快照、
审计和旧二进制重建的可复现性。

同时，客户端协议和现有 `Envir`/`Map` 行为不能在本轮改变，草稿和回滚只能先落在控制面数据模型与 API 闭环，
不能把编辑状态写入运行中的区服内存，也不能把 GPL 游戏逻辑引入控制面。

## 决策

### 1. 草稿是独立的可编辑副本

新增以下表：

```text
content_draft(id, name, base_import_run_id, status, created_by, updated_by,
              entity_count, last_validation, created_at, updated_at)
content_draft_entity(draft_id, entity_type, local_index, content_key, schema_version,
                     data, revision, updated_by, updated_at)
content_draft_revision(id, draft_id, revision_number, action, entity_type, local_index,
                       content_key, before_data, after_data, actor_account_id, reason, occurred_at)
```

- 创建草稿时在单个事务内复制指定已导入批次的全部实体；草稿与基础批次分离，后续编辑不修改导入批次。
- 实体键为 `(draft_id, entity_type, content_key)`，并额外保证 `(draft_id, entity_type, local_index)` 唯一。
- 草稿状态为 `editing`、`validated`、`failed`、`archived`。校验报告写入 `last_validation`，供后台和后续发布读取。

### 2. 写入使用逐实体乐观并发和只追加修订

- 每个草稿实体带单调递增的 `revision`，从 1 开始。
- `PUT` 新建要求 `expectedRevision=0`；更新要求与当前 `revision` 完全一致，否则返回 409。
- `DELETE` 要求匹配当前 `revision`，否则返回 409。
- 每次新增、修改、删除都在同一事务内追加 `content_draft_revision`，保存操作者、原因、前后 JSON 数据和时间。
- `contentKey` 创建后不可修改；需要改内容键时删除后重建，避免引用关系在无审计的情况下漂移。

### 3. 发布版本在创建时冻结来源

- `POST /api/v1/releases` 的 `importRunId` 与 `draftId` 必须恰好提供一个。
- 从导入批次发布时复制 `content_entity`；从草稿发布时复制当前 `content_draft_entity`。
- 两者都会写入独立的 `content_release_entity`，因此后续草稿修改不会影响已创建版本。
- 发布事务复制实体后回读 `content_release_entity` 并重算内容哈希；若来源在创建请求读取后被并发修改，则返回 409，避免哈希与冻结实体不一致。
- `content_release.draft_id` 记录草稿来源；`import_run_id` 仍记录基础批次。

### 4. 回滚是发布记录状态切换，不是删除

新增发布记录元数据：

```text
content_release.draft_id
content_release.rolled_back_at
content_release.rolled_back_by
content_release.rollback_reason
content_release.rollback_target_id
```

- 只允许对 `published` 或 `active` 版本执行回滚。
- 默认目标为最新的其他 `published` 版本；也可以显式指定 `targetReleaseId`。
- 目标必须是 `published`，且不能是当前版本。
- 在事务内锁定当前版本、当前 active 版本和目标版本；把当前版本标记为 `rolled_back`，把目标版本标记为 `active`。
- 回滚前重新校验目标产物的 manifest、`contentHash` 和 ECDSA 签名；失败则拒绝状态切换。
- 以部分唯一索引 `ux_content_release_single_active` 保证最多一个 `active` 版本。
- 回滚原因长度 8～512 字符，并写入 `audit_event`。

### 5. 发布与生效分离

- `publish` 只把版本标记为可用（`published`），不直接设置 `active`。
- `active` 只由回滚事务设置。Web 在没有 `active` 时展示最新 `published`，但不虚构区服灰度状态。
- 区服 Agent 的 `content.stage`、`content.activate`、`content.rollback` 命令只定义在 MIT 的
  `Crystal.Agent.Contracts` 中；M3 才实现 Agent 服务和真实热切换。

## 备选方案

1. 直接在导入批次上编辑：破坏批次不可变性和历史重建能力，拒绝。
2. 每个实体单独建草稿表：字段 schema 高度异构，M1 无法在不重写游戏模型的情况下完成，拒绝。
3. 回滚时复制出新的发布版本：审计上更复杂，且会放大存储；M1 选择保留原版本并切换状态。
4. 回滚直接改 `active_zone` 或运行时内存：控制面没有区服连接，且违反控制面/数据面分离，拒绝。

## 影响

- 草稿和发布版本都会增加存储；草稿修订表需要后续保留策略和归档。
- `content_release_entity` 仍是发布版本的唯一不可变实体副本；修复草稿不会自动修复已创建版本。
- 回滚只改变平台发布状态，不会自行通知区服；真实热切换依赖 M3 Agent。
- Web 的版本状态、操作按钮和错误提示必须与 `release:*` 权限和 409 语义保持一致。

## 重新评估条件

- 需要多人实时协作编辑同一草稿。
- 草稿规模或修订量需要独立的编辑数据库或事件溯源存储。
- M3 Agent 引入多区服滚动回滚，需要区服级 active 状态而非平台级单 active。
- 内容发布改为对象存储或外部发布服务，需要重新定义事务边界。
