# 0009 地图元数据编辑器与嵌套关系只读边界

- 状态：已接受
- 日期：2026-11-16

## 背景

ADR 0008 已把内容编辑器泛化为按 `entityType` 注册的 Schema、类型化创建和两级校验框架。地图是第三个适合验证该框架的内容域：旧 `MapInfo` 同时包含稳定的顶层标量元数据、客户端表现参数，以及传送、安全区、刷新点、NPC、矿区和激活坐标等嵌套集合。地图文件本身由 `Crystal.MapEditor` 管理，控制面不应在本轮复制地图编辑或运行时 `Map` 行为。

已有地图数据中的 `WeatherParticles` 可能包含无法命名的旧位掩码字符串，例如 `"3"`。如果本轮把它建模为有限枚举或 flags 编辑器，会破坏旧数据保存和发布。与此同时，地图 `FileName` 是客户端资源定位标识，重复文件名会导致发布内容不可判定。

## 决策

### 1. 注册 `map` 编辑器 Schema v1

- 在 `ContentEditorSchemas` 注册 `map`，`schemaVersion=1`；新增 `GET /api/v1/editor-schemas/map`，要求 `content:read`。
- Schema 分为 `identity`、`presentation`、`access`、`combat`、`population`、`territory` 六组，共 44 个顶层字段。
- 身份字段为只读 `Index` 和必填非空的 `FileName`、`Title`。
- 客户端表现字段为 `MiniMap`、`BigMap`、`Music`、`Light`、`MapDarkLight`、`WeatherParticles`。
- 访问控制、战斗与环境、人口与限制、矿区与攻城字段按旧 `MapInfo` 标量语义建模。
- `Light` 使用 MIT 控制面手工维护的 `Normal`、`Dawn`、`Day`、`Evening`、`Night` 枚举，不引用 GPL 程序集。
- 16 位字段限制为 `0..65535`，8 位字段限制为 `0..255`，伤害、组队人数和防火墙数量限制为非负 32 位整数。

### 2. 天气字段保持只读字符串

- `WeatherParticles` 在 Schema 中是必填、只读的字符串，默认值为 `"None"`。
- 不把旧位掩码强制解析为已知枚举，避免把 `"3"` 等无法命名的值改写或拒绝。
- 本轮不引入新的天气 flags 编辑协议；后续如需编辑，先单独定义可逆的位掩码契约和迁移规则。

### 3. 嵌套集合只读并由未知字段透传保留

- `Movements`、`SafeZones`、`Respawns`、`NPCs`、`MineZones`、`ActiveCoords` 不进入 Schema，不在本轮提供嵌套 CRUD。
- 保存地图标量字段时，Control API 继续对 Schema 之外的字段做未知字段透传，保证已导入集合原样保留。
- 地图编辑器只读展示传送、安全区、NPC、刷新点数量和首批明细；没有基础批次关系时显示空状态，不把 404 当作故障。
- 新建地图不主动生成嵌套集合；缺失集合按旧模型的反序列化默认空集合处理。

### 4. 类型化创建和两级校验沿用统一框架

- 复用 `POST/PUT/DELETE /api/v1/drafts/{id}/content/map...`，不新增地图专用写接口。
- 服务端为新建地图分配下一个 `localIndex`、覆盖 `Index` 并生成 `map.<localIndex>`。
- 保存阻断整数类型/范围、字符串、布尔、枚举、`Index == localIndex` 和 `contentKey == map.<localIndex>` 等结构错误。
- 草稿/发布校验生成 `map-completeness`、`map-content-key`，并在发布集合内检查地图 `FileName` 去空格、大小写不敏感的唯一性，冲突生成 `map-file-name-duplicate`。

### 5. Web 复用类型化编辑器状态机

- `map` 加入可编辑内容类型，地图列表在活动草稿下读取草稿数据，可写用户点击进入地图编辑抽屉。
- 地图编辑器复用字段差异、保存、删除、校验、400 字段错误和 409 revision 冲突恢复。
- 只读账号不显示地图新建或编辑入口；地图关系摘要只读展示，不能从编辑器修改嵌套集合。

## 备选方案

1. 同时实现传送、安全区、刷新点、NPC 和矿区 CRUD：会把本轮垂直切片扩大为地图关系编辑器，并需要先确定引用选择器、坐标编辑和并发语义，拒绝。
2. 把 `WeatherParticles` 建成有限枚举或 flags：会破坏无法命名的旧位掩码值，拒绝。
3. 在控制面嵌入或重写 `Crystal.MapEditor`：跨越资源编辑边界，并增加 GPL/客户端兼容风险，拒绝。
4. 只在 Web 硬编码 44 个字段：会与 API Schema、校验和问题码漂移，拒绝。

## 影响

- 地图顶层标量元数据可在草稿内安全新建、编辑、删除并进入现有校验、编译、签名和发布链路。
- 地图关系继续由现有查询接口提供只读视图；嵌套集合的写入需要后续 ADR 或明确扩展。
- 地图 `FileName` 被视为稳定资源标识；草稿和发布集合内必须唯一，但本轮不新增数据库唯一约束或迁移。
- 不新增数据库迁移，不改变内容快照格式、发布 manifest、客户端协议或 GPL 边界。
- 天气字段只读是兼容旧数据的刻意限制，不代表最终地图编辑能力。

## 重新评估条件

- 需要编辑传送、安全区、刷新点、NPC、矿区或激活坐标时。
- 需要定义可逆的 `WeatherParticles` 位掩码编辑协议时。
- 需要地图文件上传、资源预览或与 `Crystal.MapEditor` 深度集成时。
- 地图 `FileName` 需要跨草稿、跨版本或全局唯一约束时。
