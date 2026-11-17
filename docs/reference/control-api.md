# Control API 参考

> 最后更新：2026-11-17
> 状态：生效（M1 草稿/发布闭环 + M2 十类内容编辑器、引用、批量导入导出、地图关系与地图预览）

## 1. 位置与边界

实现位于 `src/Crystal.Control.Api/`，目标框架 `net8.0`，使用 ASP.NET Core Minimal API + Npgsql。
它读取内容、控制面账号、角色、会话和审计表，不引用 `Server/`、`Server.Library` 或任何 GPL 游戏代码。

当前能力：

- 健康检查。
- 导入批次查询。
- 内容实体分页查询、按旧索引查询、按内容键查询。
- 按活动草稿或导入批次搜索引用目标。
- 地图目录、地图拓扑、安全区、NPC、刷新点、传送门、怪物掉落、配方和 NPC 脚本关系查询。
- NPC 脚本 AST、静态编译校验和稳定编译哈希查询。
- 地图资源格式、尺寸、可走/阻挡/门格元数据、降采样预览，以及经路径/大小/SHA256 校验的资源文件下载。
- Argon2id 密码、短时 JWT、刷新令牌轮换和会话撤销。
- RBAC 权限校验、账号创建/停用/启用、密码修改/重置、角色调整和只读审计查询。
- API key 机器到机器认证、登录限流和全局限流。
- 从导入批次创建可编辑草稿、逐实体增改删、修订查询和草稿校验。
- 十类编辑器 Schema、服务端身份解析、保存前规范化、结构/完整性/引用校验和字段级错误。
- 草稿脚本源码校验，以及地图关系数组整体替换和刷新点/NPC 行级 CRUD。
- 草稿 JSONL dry-run、事务应用导入和稳定排序导出。
- 从导入批次或草稿创建不可变发布版本，校验、编译、ECDSA 签名、发布和回滚。

尚未实现：MFA/OIDC、账号删除、解锁接口、CSV 导入、NPC 语句块结构化编辑、`.lib` 图库解析、资源上传、
Zone Agent 服务和真实区服热切换。

## 2. 配置

| 配置 | 环境变量 | 说明 |
| --- | --- | --- |
| `ConnectionStrings:Crystal` | `CRYSTAL_DB_CONNECTION` / `CRYSTAL_DATABASE_URL` | PostgreSQL 连接串 |
| `ControlApi:ApiKey` | `CRYSTAL_CONTROL_API_KEY` | 可选的服务间 API key |
| `ControlApi:JwtSigningKey` | `CRYSTAL_CONTROL_JWT_SIGNING_KEY` | JWT 签名密钥，至少 32 字节，Production 必填 |
| `ControlApi:AccessTokenMinutes` | 同左 | 访问令牌有效期，默认 15 分钟 |
| `ControlApi:RefreshTokenDays` | 同左 | 刷新令牌有效期，默认 30 天 |
| `ControlApi:ApiRateLimitPermitLimit` | 同左 | 普通 API 每 IP 每分钟请求上限，默认 240 |
| `ControlApi:AuthRateLimitPermitLimit` | 同左 | 登录/刷新每 IP 每分钟请求上限，默认 10 |
| `ControlApi:BootstrapAdminPassword` | `CRYSTAL_CONTROL_BOOTSTRAP_PASSWORD` | 空库首次创建管理员的密码 |
| `ControlApi:AssetRoot` | `CRYSTAL_CONTROL_ASSET_ROOT` | 资源文件根目录；配置后 `asset` 下载端点才可用，路径相对于快照中的 `relativePath` |
| `ControlApi:ReleaseRoot` | `CRYSTAL_CONTROL_RELEASE_ROOT` | 编译产物根目录，默认 `artifacts/releases` |
| `ControlApi:ReleaseSigningPrivateKeyPath` | `CRYSTAL_CONTROL_RELEASE_SIGNING_PRIVATE_KEY_PATH` | ECDSA P-256 私钥 PEM；仅签名动作需要 |
| `ControlApi:ReleaseSigningPublicKeyPath` | `CRYSTAL_CONTROL_RELEASE_SIGNING_PUBLIC_KEY_PATH` | 公钥 PEM；发布和回滚验签需要 |
| `ControlApi:ReleaseSigningKeyId` | `CRYSTAL_CONTROL_RELEASE_SIGNING_KEY_ID` | 写入 manifest 的密钥标识 |
| `ASPNETCORE_URLS` | 同左 | 监听地址 |
| `ASPNETCORE_ENVIRONMENT` | 同左 | Production 强制要求 JWT 签名密钥 |

本地运行：

```bash
export CRYSTAL_DB_CONNECTION='Host=127.0.0.1;Port=5432;Database=crystal;Username=crystal;Password=...'
export CRYSTAL_CONTROL_API_KEY='local-dev-key'
export CRYSTAL_CONTROL_JWT_SIGNING_KEY='local-dev-signing-key-at-least-32-bytes'
export CRYSTAL_CONTROL_BOOTSTRAP_PASSWORD='initial-admin-password'
dotnet run --project src/Crystal.Control.Api
```

## 3. 接口

### 3.1 健康检查

```text
GET /health/live
GET /health/ready
```

`live` 只表示进程存活。`ready` 检查内容、账号、会话和审计表是否存在，失败返回 503。

### 3.2 认证与会话

```text
POST /api/v1/auth/login
POST /api/v1/auth/refresh
POST /api/v1/auth/logout
POST /api/v1/auth/change-password
GET  /api/v1/auth/me
```

登录请求：

```json
{
  "username": "admin",
  "password": "initial-admin-password"
}
```

登录响应包含 `accessToken`、`refreshToken`、`expiresIn`、`sessionId` 和账号的 `roles`/`permissions`。
访问令牌默认 15 分钟；刷新令牌默认 30 天，每次刷新后轮换，旧刷新令牌立即失效。登出会撤销会话，
已签发的访问令牌在下一次请求时失效。

登录用户可以调用 `POST /auth/change-password` 修改自己的密码：

```json
{
  "currentPassword": "current-password",
  "newPassword": "at-least-12-characters"
}
```

修改成功后保留当前会话，并撤销同一账号的其他会话。API key 不能调用该接口。

管理端请求使用 `Authorization: Bearer <accessToken>`。机器到机器场景仍可使用
`X-Api-Key` 或 `Authorization: Bearer <apiKey>`；API key 拥有通配权限。

健康检查、登录和刷新接口不需要认证。其余内容、账号和审计接口按权限返回 401 或 403。

### 3.3 导入批次

```text
GET /api/v1/import-runs?limit=20
GET /api/v1/import-runs/{id}
GET /api/v1/import-runs/{id}/content-types
```

`limit` 范围 1～100，默认 20。批次响应：

```json
{
  "id": 1,
  "source": "/path/to/Server.MirDB",
  "sourceHash": "37358b0d...",
  "legacyVersion": 111,
  "schemaVersion": 1,
  "status": "imported",
  "entityCount": 9797,
  "startedAt": "2026-11-10T02:45:18Z",
  "finishedAt": "2026-11-10T02:45:18Z"
}
```

黄金库核心实体为 9,793 个（含 59 个启动时生成的 Buff）；使用 `--content-root` 导入掉落、配方、NPC 脚本和资源元数据后，
CI 测试批次为 9,797 个实体。

### 3.4 内容查询

```text
GET /api/v1/content/{entityType}
    ?importRunId=
    &page=1
    &pageSize=50
    &q=
    &sort=index|key|name
    &direction=asc|desc

GET /api/v1/content/{entityType}/{localIndex}
GET /api/v1/content/{entityType}/key/{contentKey}
GET /api/v1/content/{entityType}/options?draftId=&importRunId=&q=&limit=
```

- `importRunId` 省略时使用最新 `imported` 批次。
- `entityType`：`item`、`monster`、`map`、`npc`、`quest`、`magic`、`gameshop`、`conquest`、`dragon`、`respawn`、`drop`、`recipe`、`buff`、`script`、`asset` 等。
- `localIndex` 是旧数据库 `Index`，`contentKey` 例如 `item.1`。
- `q` 按 Schema 声明的标签和搜索字段做模糊匹配；未声明时回退到常见名称、标题、文件名和内容键字段。
- `options` 的 `draftId` 与 `importRunId` 最多提供一个；省略时使用最新 imported 批次。响应项包含
  `localIndex`、`contentKey`、`label` 和 `summary`，用于 Web 引用选择器。`limit` 默认 20，范围 1～100。

列表响应：

```json
{
  "importRunId": 1,
  "entityType": "item",
  "page": 1,
  "pageSize": 5,
  "total": 1628,
  "items": [
    {
      "id": 1,
      "importRunId": 1,
      "contentKey": "item.1",
      "localIndex": 1,
      "schemaVersion": 1,
      "data": {
        "Index": 1,
        "Name": "SpiritBlade"
      }
    }
  ]
}
```

### 3.4.1 内容编辑器 Schema 与类型化创建

```text
GET /api/v1/editor-schemas/{entityType}
POST /api/v1/drafts/{id}/content/{entityType}
```

`GET /editor-schemas/{entityType}` 要求 `content:read`，从 Control API 的 `entityType` 注册表返回分组字段定义；
当前注册 `item`、`monster`、`map`、`npc`、`script`、`drop`、`magic`、`quest`、`gameshop` 和 `recipe`，
均为 `schemaVersion=1`，未注册类型返回 404。Schema 由 Control API 手工维护，不引用 GPL 覆盖的
`Shared`/`Server` 程序集。字段包含 `path`、`label`、`kind`、`required`、`readOnly`、`defaultValue`、
`min`、`max`、`options` 和复合字段定义；M2 新增 `referenceType`、`nullable`、`multiline`、`minItems`、
`maxItems`，均为可选字段，旧客户端可忽略。字段种类支持 `number`、`array`、`reference`、`code`、`color`
和 `readonly`。

- `item` 覆盖 33 个顶层字段、`Stats.Values` 动态属性表，以及 `RandomStats` 下的 70 个 0～255 字节字段。
- `monster` 覆盖 `identity`、`basics`、`combat`、`behavior`、`stats` 五组共 20 个字段：
  `Index`、`Name`、`Image`、`DropPath`、`AI`、`Effect`、`Level`、`ViewRange`、`CoolEye`、`Light`、
  `AttackSpeed`、`MoveSpeed`、`Experience`、`CanTame`、`CanPush`、`AutoRev`、`Undead`、`CanRecall`、
  `IsBoss`、`Stats`。
- 怪物 `Image` 是手工维护在 MIT 控制面侧的字符串枚举。`Stats.Values` 只允许 `MinAC`、`MaxAC`、`MinMAC`、
  `MaxMAC`、`MinDC`、`MaxDC`、`MinMC`、`MaxMC`、`MinSC`、`MaxSC`、`HP`、`Accuracy`、`Agility`，
  值为 32 位整数。
- `Drops`、`HasSpawnScript`、`HasDieScript` 是运行时派生数据，不是怪物 Schema 字段；掉落通过独立 `drop` 编辑器维护。
- `map` 覆盖 `identity`、`presentation`、`access`、`combat`、`population`、`territory` 六组共 44 个顶层标量字段；
  `Index` 和 `WeatherParticles` 只读。`Light` 使用 `Normal`、`Dawn`、`Day`、`Evening`、`Night` 枚举；
  16 位字段限制为 `0..65535`，8 位字段限制为 `0..255`，伤害、组队人数和防火墙数量必须是非负 32 位整数。
- 地图的 `Movements`、`SafeZones`、`Respawns`、`NPCs`、`MineZones`、`ActiveCoords` 不属于标量 Schema；
  通过 3.7.1 的关系接口写入。`WeatherParticles` 保持字符串以兼容旧位掩码值。
- `npc` 覆盖地图、脚本、任务引用和坐标；`script` 覆盖源码与只读派生 AST/页面/问题；`drop` 覆盖物品、
  金币、嵌套组、概率和任务限定；`magic` 覆盖全部技能参数；`quest` 覆盖目标、奖励和引用；`gameshop`
  覆盖商品、价格、库存、上下架和购买方式；`recipe` 覆盖产出、材料、工具、条件、成功率和金币。
- 其他顶层未知字段原样保留，避免旧数据或未来字段在编辑保存时丢失。

类型化创建要求 `content:write`，仅对注册类型开放。物品请求体示例：

```json
{
  "schemaVersion": 1,
  "reason": "新增测试物品",
  "data": {
    "Name": "DraftAddedItem",
    "Price": 1
  }
}
```

服务端在单个事务内解析身份并覆盖相应身份字段，写入实体、`create` 修订并返回 201。客户端不能自行指定已有内容键。
身份规则如下：

| 类型 | 身份规则 |
| --- | --- |
| `item` / `monster` / `map` / `npc` / `quest` | `data.Index = localIndex`，键为 `<type>.<localIndex>` |
| `gameshop` | `data.GIndex = localIndex`，键为 `gameshop.<GIndex>` |
| `magic` | `localIndex` 使用 `Spell` 枚举值，键为 `magic.<Spell>` |
| `recipe` | 自动分配 `localIndex`，键为 `recipe.<产出物品名>` |
| `script` | 自动分配 `localIndex`，键为 `script.<规范化文件路径>` |
| `drop` | `localIndex = monsterIndex`，键为 `drop.monster.<monsterIndex>` |

旧内容键保持原样；新建配方、脚本等使用安全键段，已有历史键在保存和发布校验时仍被接受。

所有注册类型采用两级校验：

- `PUT`/类型化创建阻断结构错误并返回 400 字段错误，包括字段类型、整数范围、枚举、flags、布尔值、
  `Index`/`contentKey` 一致性、`Stats.Values` 键和值范围。怪物属性值必须是 32 位整数。允许保存字段不完整的草稿。
- 草稿/发布校验报告 `<entityType>-completeness`、`<entityType>-content-key`、`reference-missing`、
  `<type>-duplicate` 等问题，例如缺少顶层字段、空名称、引用缺失、重复身份或内容键不一致。
  旧物品内容缺少整个 `RandomStats` 表示全零随机属性，视为合法。
- 地图 `FileName` 在草稿和发布集合内按 `Trim()` 后大小写不敏感唯一；冲突报告 `map-file-name-duplicate`。
- 掉落目标重复报告 `drop-duplicate`，同一物品被多个配方产出报告 `recipe-duplicate`，脚本文本重复报告
  `script-duplicate`，脚本编译错误报告 `script-validation-error`。
- 未知字段原样保留，避免旧数据或未来字段在编辑保存时丢失。

### 3.5 地图目录与内容关系

```text
GET /api/v1/content/map/catalog?importRunId=&page=1&pageSize=500&q=
GET /api/v1/content/map/{localIndex}/relations?importRunId=&respawnPage=1&respawnPageSize=200
GET /api/v1/content/monster/{localIndex}/drops?importRunId=
GET /api/v1/content/recipe/{localIndex}/relations?importRunId=
GET /api/v1/content/npc/{localIndex}/script?importRunId=
GET /api/v1/content/script/{localIndex}/validation?importRunId=
GET /api/v1/content/asset/{localIndex}/file?importRunId=
GET /api/v1/content/asset/{localIndex}/preview?importRunId=&maxSize=256
```

地图目录只返回 `MapSummaryDto`，不携带完整地图 JSON，供 Web 全量地图选择器使用。
`pageSize` 范围 1～500，默认 500；`q` 对内容键、`Title`、`FileName` 做模糊匹配，并对完整
本地索引做精确匹配。黄金样本 `/content/map/catalog` 返回 463 张地图。

地图关系接口返回地图摘要、`Movements` 传送/入口出口、目标地图摘要、`SafeZones`、按
`NPCInfo.MapIndex` 关联的 NPC，以及分页刷新点。NPC 会按大小写不敏感的 `FileName` 关联 `script` 实体，
存在时返回脚本内容键、路径、行数和页面数，不存在时 `script` 为 `null`。刷新点会解析 `MonsterIndex`
并补上怪物内容键和名称。`asset` 字段返回对应地图资源的内容键、相对路径、大小、SHA256 和解析后的
格式、宽高、格子数、可走/阻挡/门格；没有导入地图资源时为 `null`。`respawnPageSize` 范围 1～500，默认 200。

掉落接口返回怪物摘要、`DropPath`、`drop.monster.<Index>` 内容键和已解析掉落条目。普通物品掉落和嵌套
分组掉落都会补上 `item.<Index>` 内容键与物品名称；没有导入掉落文本时返回空数组，而不是 404。

配方关系接口返回产出、材料、工具、成功率、金币和解锁条件。产出与材料/工具中的物品索引会解析为
`item.<Index>` 内容键和物品名称；未解析到的物品保留原始索引和配方文件中的名称。

NPC 脚本接口在 NPC 存在时始终返回 200。`found=false` 表示该 NPC 没有对应脚本或脚本目录未导入；
`found=true` 时返回脚本内容键、相对路径、语言、SHA256、大小、行数、页面、包含/插入/调用关系和来源文本。
NPC 本身不存在时返回 404。

脚本校验接口返回 `available`、脚本摘要、`ast`、`validation` 和 `issues`。`validation.valid` 表示静态校验
是否通过，`errorCount`/`warningCount` 给出问题数，`compiledSha256` 是 AST 的稳定摘要。脚本实体不存在时
返回 404；实体存在但没有 AST 时 `available=false`。

资源文件接口要求 `asset:read`，按 `localIndex` 解析当前批次（或指定 `importRunId`）的 `asset` 实体。
服务端要求配置 `ControlApi:AssetRoot`，并校验相对路径不越界、文件存在、大小与快照一致、SHA256 与快照一致；
成功时返回文件流、`Content-Type`、ETag 和 `private, max-age=300` 缓存头。未配置、缺失文件、路径非法或
完整性不一致时返回 4xx/5xx 错误，不返回未经校验的文件。

资源预览接口同样要求 `asset:read`，仅支持地图资产。服务端按需重新读取并解析地图文件，将可走、阻挡和门格
降采样到 `maxSize` 指定的最大边长，返回原始尺寸、采样网格、图例计数和地图摘要；`maxSize` 默认 256，
范围 8～512。该接口不解析客户端 `.lib` 图库，也不提供物品/怪物图标缩略图。

示例（节选）：

```json
{
  "importRunId": 1,
  "map": {
    "localIndex": 1,
    "contentKey": "map.1",
    "index": 1,
    "fileName": "0",
    "title": "BichonProvince"
  },
  "movements": [
    {
      "targetMapIndex": 2,
      "targetMap": {"localIndex": 2, "contentKey": "map.2", "fileName": "1"},
      "source": {"x": 347, "y": 188},
      "destination": {"x": 13, "y": 40}
    }
  ],
  "npcs": [{
    "localIndex": 1,
    "contentKey": "npc.1",
    "name": "Teleport_Gilbert",
    "fileName": "BichonProvince/BorderVillage/Transport",
    "script": {
      "localIndex": 1,
      "contentKey": "script.BichonProvince.BorderVillage.Transport",
      "relativePath": "Envir/NPCs/BichonProvince/BorderVillage/Transport.txt",
      "lineCount": 95,
      "pageCount": 11
    }
  }],
  "respawns": {
    "page": 1,
    "pageSize": 200,
    "total": 360,
    "items": [{"contentKey": "respawn.map1.1", "monsterName": "Hen", "count": 5}]
  }
}
```

配方关系示例（节选）：

```json
{
  "importRunId": 1,
  "localIndex": 1,
  "contentKey": "recipe.GreenPoison",
  "name": "GreenPoison",
  "fileName": "Envir/Recipe/GreenPoison.txt",
  "output": {"localIndex": 710, "contentKey": "item.710", "itemIndex": 710, "name": "GreenPoison", "count": 2},
  "ingredients": [{"contentKey": "item.710", "name": "GreenPoison", "count": 3}],
  "tools": [{"contentKey": "item.710", "name": "GreenPoison", "count": 1}],
  "chance": 75,
  "gold": 123,
  "requiredLevel": 22,
  "requiredGender": "Female",
  "requiredClasses": ["Taoist"],
  "requiredFlags": [1],
  "requiredQuests": [2]
}
```

NPC 脚本关系示例（节选）：

```json
{
  "importRunId": 1,
  "npcLocalIndex": 1,
  "npcContentKey": "npc.1",
  "name": "Teleport_Gilbert",
  "fileName": "BichonProvince/BorderVillage/Transport",
  "found": true,
  "scriptLocalIndex": 1,
  "scriptContentKey": "script.BichonProvince.BorderVillage.Transport",
  "scriptPath": "Envir/NPCs/BichonProvince/BorderVillage/Transport.txt",
  "language": "CrystalNpc",
  "sha256": "4411fb16709796db17e16da504b393f6663bda0d5f636854356db255af35694f",
  "sizeBytes": 1195,
  "lineCount": 95,
  "pages": ["@MAIN", "@Main-1", "@tele"],
  "includes": [],
  "inserts": [],
  "calls": [],
  "source": "[@MAIN]\n..."
}
```

### 3.6 账号、角色与审计

```text
GET  /api/v1/accounts?limit=50
POST /api/v1/accounts
POST /api/v1/accounts/{id}/status
POST /api/v1/accounts/{id}/password-reset
PUT  /api/v1/accounts/{id}/roles
GET  /api/v1/roles
GET  /api/v1/audit/events?limit=50&action=&result=&actorAccountId=
```

创建账号请求：

```json
{
  "username": "content-author-01",
  "displayName": "Content Author 01",
  "password": "at-least-12-characters",
  "roles": ["content_author"]
}
```

内置角色：`platform_admin`、`content_author`、`release_approver`、`gm`、`finance`、`auditor`。
审计事件包含操作者、IP、动作、资源、请求 ID、结果、原因和 JSON 元数据。`audit_event` 由数据库触发器
保护为只追加。

账号生命周期接口均要求 `account:write`：

- `status` 只接受 `active` 或 `disabled`。停用会撤销目标账号全部会话；启用会清除失败登录计数和锁定。
- `password-reset` 要求 `newPassword` 为 12～128 字符，成功后撤销目标账号全部会话。
- `roles` 要求至少一个角色，保存后撤销目标账号全部会话，使新权限立即生效。
- 请求者不能停用自己的账号；最后一个启用状态的 `platform_admin` 不能被停用或移除 `platform_admin` 角色。
- 管理员重置自己的密码应使用 `/auth/change-password`，账号重置接口会拒绝该请求。
- 审计动作：`account.create`、`account.status`、`account.password.reset`、`account.roles.update`、`auth.password.change`。
  审计元数据不记录密码或密码哈希。

### 3.7 内容草稿

```text
GET    /api/v1/drafts?limit=50&status=
POST   /api/v1/drafts
GET    /api/v1/drafts/{id}
GET    /api/v1/drafts/{id}/content?entityType=&page=1&pageSize=50&q=&sort=index|key|name&direction=asc|desc
GET    /api/v1/drafts/{id}/content/{entityType}/{localIndex}
POST   /api/v1/drafts/{id}/content/{entityType}
PUT    /api/v1/drafts/{id}/content/{entityType}/{localIndex}
DELETE /api/v1/drafts/{id}/content/{entityType}/{localIndex}
POST   /api/v1/drafts/{id}/content/{entityType}/import?dryRun=true|false&reason=
GET    /api/v1/drafts/{id}/content/{entityType}/export?importRunId=
POST   /api/v1/drafts/{id}/content/script/{localIndex}/validate
GET    /api/v1/drafts/{id}/content/map/{mapIndex}/relations
PUT    /api/v1/drafts/{id}/content/map/{mapIndex}/movements|safezones|minezones|activecoords
POST   /api/v1/drafts/{id}/content/map/{mapIndex}/respawns
PUT    /api/v1/drafts/{id}/content/map/{mapIndex}/respawns/{respawnIndex}
DELETE /api/v1/drafts/{id}/content/map/{mapIndex}/respawns/{respawnIndex}
POST   /api/v1/drafts/{id}/content/map/{mapIndex}/npcs
PUT    /api/v1/drafts/{id}/content/map/{mapIndex}/npcs/{npcIndex}
DELETE /api/v1/drafts/{id}/content/map/{mapIndex}/npcs/{npcIndex}
POST   /api/v1/drafts/{id}/validate
GET    /api/v1/drafts/{id}/revisions?entityType=&contentKey=&limit=100
```

读取要求 `content:read`，创建、写入、删除和校验要求 `content:write`。创建请求：

```json
{
  "baseImportRunId": 1,
  "name": "2026-11 平衡性调整"
}
```

创建时在单个事务内复制该导入批次的全部实体。草稿实体更新请求：

```json
{
  "contentKey": "item.1",
  "schemaVersion": 1,
  "expectedRevision": 1,
  "reason": "调整价格用于测试",
  "data": {
    "Index": 1,
    "Name": "SpiritBlade",
    "Price": 12345
  }
}
```

- 新建实体使用 `expectedRevision: 0` 并提供 `contentKey`。
- 更新必须匹配当前实体 `revision`；删除请求体为 `{ "expectedRevision": 1, "reason": "..." }`。
- 版本不一致返回 409，响应为 `application/problem+json`。
- `contentKey` 创建后不可修改。每次变更追加修订记录，包含操作者、原因、前后数据和时间。
- 十类注册编辑器推荐使用类型化创建接口解析身份；通用 `PUT` 仍可用于显式索引和旧内容迁移。
- `validate` 返回并持久化 `contentHash`、实体数、`valid` 和问题列表；结果同时写入 `content_draft.last_validation`。
- 保存前会执行 Schema 声明的规范化，例如脚本派生字段、商城 `Info`/`ItemIndex` 和旧内容键兼容；未知字段保留。

### 3.7.1 脚本校验、JSONL 与地图关系

脚本校验请求体：

```json
{
  "source": "[@MAIN]\nSAY Hello\n",
  "language": "CrystalNpc"
}
```

服务端不信任客户端提交的 AST，始终重新解析源码，返回 `ast`、页面、静态引用、`validation`、`issues`、
源码 SHA256 和 `compiledSha256`。脚本实体不存在返回 404；存在但没有可用解析器时返回 `available=false`。

JSONL 导入使用与内容快照 `entities/<type>.jsonl` 一致的实体行，单次请求上限 16 MiB。示例：

```json
{"entityType":"item","localIndex":9100001,"contentKey":"item.9100001","schemaVersion":1,"data":{"Index":9100001,"Name":"Example"}}
```

- `dryRun` 省略或为 `true` 时只校验，不写草稿；响应列出总行数、有效行、逐行错误和内容键。
- `dryRun=false` 时在单事务内按 `contentKey` upsert；成功写修订和 `draft.import` 审计，冲突返回 409，
  校验失败返回 400。
- 导出按 `localIndex` 稳定排序，实体行与快照格式一致；可从活动草稿或指定 `importRunId` 生成。

地图数组替换请求示例：

```json
{
  "expectedRevision": 3,
  "items": [{"Location": {"x": 10, "y": 12}, "Size": 4, "StartPoint": false}],
  "reason": "调整安全区"
}
```

`movements`、`safezones`、`minezones`、`activecoords` 每次整体替换。刷新点和 NPC 行级请求体为
`{ "data": {...}, "expectedRevision": 3, "reason": "..." }`；创建时服务端解析独立实体 `localIndex`，
更新/删除要求匹配对应实体 revision。所有关系写入都在同一事务内同步地图 `Respawns`/`NPCs` 投影。
坐标必须非负且在地图资源尺寸内，目标地图、怪物和 NPC 脚本必须存在；revision 不一致返回 409。

### 3.8 发布、回滚与 Manifest

```text
GET  /api/v1/releases?limit=50&status=
GET  /api/v1/releases/{id}
GET  /api/v1/releases/{id}/manifest
POST /api/v1/releases
POST /api/v1/releases/{id}/validate
POST /api/v1/releases/{id}/compile
POST /api/v1/releases/{id}/sign
POST /api/v1/releases/{id}/publish
POST /api/v1/releases/{id}/rollback
```

创建版本要求 `release:write`，且 `importRunId` 与 `draftId` 恰好提供一个：

```json
{
  "draftId": 7,
  "version": "2026.11.13.1",
  "notes": "从草稿创建，包含价格调整"
}
```

创建时把来源实体复制到不可变 `content_release_entity`，后续草稿修改不会影响该版本。`ReleaseDto`
包含 `importRunId`、`draftId`、`status`、`contentHash`、`counts`，以及回滚时的
`rolledBackAt`、`rollbackReason` 和 `rollbackTargetId`。

回滚要求 `release:rollback`，请求体：

```json
{
  "reason": "新版本出现物品价格异常，切回上一个稳定版本",
  "targetReleaseId": 12
}
```

- `reason` 长度 8～512 字符，写入 `release.rollback` 审计事件。
- `targetReleaseId` 可省略，默认选择最新的其他 `published` 版本。
- 只允许回滚 `published` 或 `active` 版本；目标必须是 `published` 且不同于当前版本。
- 事务内锁定记录，重新校验目标 artifact、`contentHash` 和 ECDSA 签名；成功后当前版本变为
  `rolled_back`，目标版本变为 `active`。
- 同一时刻最多一个 `active` 版本，由部分唯一索引保证。重复回滚或状态已变化返回 409。
- `publish` 只把签名版本标记为可用的 `published`，不会自行设置 `active`。

## 4. 集成验证

```bash
CRYSTAL_DATABASE_URL='Host=127.0.0.1;Port=5432;Database=crystal;Username=crystal;Password=...' \
  ./scripts/test-control-api.sh
```

脚本会执行真实 PostgreSQL 导入（含掉落、配方、脚本和资源元数据）、启动 API，验证登录、JWT、刷新令牌轮换、
RBAC 403、登出撤销、账号生命周期、最后管理员保护、审计、健康检查、分页、详情、类型计数、地图关系、怪物掉落、
配方关系、NPC 脚本关系与 AST、资源元数据和文件字节/SHA256。M2 覆盖 10 类 Schema 的 200/401、类型化 CRUD、
结构 400、陈旧 revision 409、引用缺失、JSONL dry-run/apply/export、脚本校验、地图关系 CRUD/清理、发布链路、
默认和显式双版本回滚。测试在 PostgreSQL 16 service container 中运行。
测试库应为独立数据库；已有账号时不会再执行 bootstrap。

Compose 栈验证使用：

```bash
./scripts/test-compose.sh
```

脚本会启动 PostgreSQL 16、Redis 7、MinIO、API 和 Nginx Web，等待健康检查，由宿主机运行
`Crystal.LegacyImporter` 导入黄金样本，再通过 Web 同源代理登录、查询内容和打开后台。

## 5. 当前限制

- 尚未实现 MFA/OIDC、账号删除、管理员解锁接口和高风险操作再认证。
- 审计目前同步写入 PostgreSQL，后续需要归档、保留策略和防篡改外部存储。
- 角色变更通过撤销会话使权限立即生效；后续引入 OIDC 时需要同步定义 token 失效策略。
- 计数和列表跨全部历史批次；调用方必须使用 `importRunId` 或未来活动发布限定数据。
- JSON 字段名保留旧实体命名，前端需要 schema 或字段映射层。
- 关系查询直接解析 `data jsonb`，尚未建立规范化外键；大规模关系检索前需要评估专用索引或投影表。
- 资源下载要求 API 进程能直接访问 `ControlApi:AssetRoot`；对象存储/CDN、资源上传和签名分发尚未实现。
- 分页使用 offset，内容规模很大时需要评估 keyset pagination。
- 当前只提供 JSONL 批量导入导出；CSV、服务端异步大文件导入和跨草稿复制未实现。
- 资源预览只支持地图网格；客户端 `.lib` 图库、物品/怪物图标缩略图和资源上传未实现。
- 脚本编辑器不提供语句块拖拽、增删排序或脚本执行；完整行为仍由文本源码和服务端解析结果决定。
- Agent 契约已定义，但 Control API 不注册 Agent 路由。ZoneServer 只支持启动期读取签名发布，不自动热切换 `active` 版本。
- 授权、许可证和素材合规不在 M2 本轮范围。
