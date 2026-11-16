# 0008 多内容编辑器注册表与两级校验

- 状态：已接受
- 日期：2026-11-16

## 背景

ADR 0007 为物品建立了类型化 Schema、类型化创建和两级校验，但实现仍以 `item` 为硬编码中心。
继续复制这套逻辑到怪物、地图、NPC 和掉落，会让注册、Schema 查询、服务端索引分配、校验问题码和 Web 状态机
各自出现分叉，也会把 GPL 覆盖的旧实体类型进一步泄漏到控制面契约。

怪物是第二个适合验证泛化的内容域：字段数量可控，包含手工枚举、整数范围、布尔开关和 `Stats.Values` 动态表，
同时存在 `Drops`、`HasSpawnScript`、`HasDieScript` 等运行时派生数据。它可以在不改变快照格式、数据库 schema
或客户端协议的前提下，验证多内容编辑器框架是否可复用。

## 决策

### 1. 按 `entityType` 注册编辑器定义

- 新增 `ContentEditorSchemas` 注册表，按 `entityType` 提供 Schema、`schemaVersion`、结构校验和完整性校验。
- `GET /api/v1/editor-schemas/{entityType}` 只查询注册表；未注册类型返回 404。
- 当前注册 `item` 和 `monster`，两者均保持 `schemaVersion=1`。物品的字段、分组、响应结构和问题码不变。
- 类型化创建只对已注册类型开放；未注册类型继续使用通用草稿写入，不能用类型化接口创建。
- 注册表是 Control API 内手工维护的 MIT 契约，不引用 `Shared`、`Server` 或其他 GPL 程序集。

### 2. 怪物 Schema v1 的字段边界

- 分组为 `identity`、`basics`、`combat`、`behavior`、`stats`，共 20 个顶层可编辑字段：
  `Index`、`Name`、`Image`、`DropPath`、`AI`、`Effect`、`Level`、`ViewRange`、`CoolEye`、`Light`、
  `AttackSpeed`、`MoveSpeed`、`Experience`、`CanTame`、`CanPush`、`AutoRev`、`Undead`、`CanRecall`、
  `IsBoss`、`Stats`。
- `Image` 使用 Control API 手工维护的 MIT 侧字符串枚举，覆盖当前内容中的怪物图像名称，不链接 GPL 枚举类型。
- `Stats.Values` 只允许 `MinAC`、`MaxAC`、`MinMAC`、`MaxMAC`、`MinDC`、`MaxDC`、`MinMC`、`MaxMC`、
  `MinSC`、`MaxSC`、`HP`、`Accuracy`、`Agility`，值必须是 32 位整数。
- `Drops`、`HasSpawnScript`、`HasDieScript` 是运行时派生数据，不纳入可编辑字段；编辑器只提供只读掉落关系入口。
- 其他顶层未知字段原样透传，避免旧内容或未来字段在保存时丢失。
- 默认值沿用现有模型：`ViewRange=7`、`AttackSpeed=2500`、`MoveSpeed=1800`、
  `CanTame/CanPush/AutoRev=true`，其余布尔值为 `false`。

### 3. 类型化创建由服务端分配身份

- `POST /api/v1/drafts/{id}/content/{entityType}` 对注册类型生效。
- 服务端在单个事务内锁定草稿、分配该类型下一个 `localIndex`、覆盖 `data.Index`，并生成
  `<entityType>.<localIndex>` 内容键。
- 同一事务写入 `create` 修订；客户端不能自行决定最终索引或内容键。
- 不新增数据库迁移，继续复用 ADR 0006 的草稿实体表和 revision 模型。

### 4. 保存阻断结构错误，完整性留给草稿/发布校验

- 保存只阻断类型、整数范围、枚举、布尔值、`Index == localIndex`、`contentKey == <entityType>.<localIndex>`、
  未知 `Stats.Values` 键和非 32 位属性值等结构错误，返回 400 字段错误。
- 草稿/发布校验统一生成 `<entityType>-completeness` 和 `<entityType>-content-key` 问题码。
- 怪物允许保存未完成字段或缺少 `Stats.Values` 的草稿；发布前校验会报告缺失字段和空名称。
- 物品问题码继续为 `item-completeness`、`item-content-key`，不因注册表改造而变化。

### 5. Web 状态机按内容类型泛化

- 可编辑内容域由 `item`、`monster` 集合控制，Schema 缓存按 `entityType` 隔离。
- 编辑抽屉、字段差异、保存、删除、校验、400 字段错误和 409 revision 冲突恢复使用同一套 `entityType` 驱动状态。
- 物品和怪物列表在存在活动草稿时读取草稿查询接口；可写用户点击进入编辑器，只读用户继续进入原有详情抽屉。
- 怪物编辑器展示只读掉落关系；草稿内新建怪物没有导入批次掉落记录时显示空关系，不把 404 当作读取故障。
- 新建怪物由服务端分配 `localIndex`，前端只展示“自动分配”状态。

## 备选方案

1. 继续复制物品编辑器代码：短期快，但会让后续地图、NPC、掉落编辑器产生多套创建、校验和冲突恢复逻辑，拒绝。
2. 直接在 Web 硬编码怪物字段：会与 API 校验和问题码漂移，也无法供其他客户端复用，拒绝。
3. 使用 GPL 类型或反射生成 Schema：违反控制面边界，且把运行时模型变成公共契约，拒绝。
4. 把掉落写入、脚本派生字段和素材预览并入本轮：超出垂直切片范围，且需要先确定引用和资源模型，拒绝。

## 影响

- 后续内容域接入编辑器时，应优先增加注册表定义和字段契约，而不是在前端复制物品/怪物专用分支。
- Schema 是手工契约；字段、默认值和枚举变化必须同步 API 校验、Web 测试和参考文档。
- 保存成功不等于可发布；作者必须运行草稿校验，发布校验会再次执行完整性规则。
- 本轮不新增数据库迁移，不改变内容快照格式、发布格式、客户端协议或旧服务端行为。
- 授权、许可证、素材来源审查、掉落 CRUD、地图资源预览和真实区服消费仍不在本轮范围。

## 重新评估条件

- 内容字段需要从单一可隔离的 MIT 源自动生成，而不是手工维护。
- 需要多人同时编辑同一实体或引入字段级协作锁。
- 需要 Schema 多版本并存并支持客户端协议迁移窗口。
- 引用选择器、批量操作或资源预览成为下一个跨内容域通用能力。
