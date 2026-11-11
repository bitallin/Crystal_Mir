# Control API 参考

> 最后更新：2026-11-11
> 状态：生效（M1 认证、账号生命周期与只读初版）

## 1. 位置与边界

实现位于 `src/Crystal.Control.Api/`，目标框架 `net8.0`，使用 ASP.NET Core Minimal API + Npgsql。
它读取内容、控制面账号、角色、会话和审计表，不引用 `Server/`、`Server.Library` 或任何 GPL 游戏代码。

当前能力：

- 健康检查。
- 导入批次查询。
- 内容实体分页查询、按旧索引查询、按内容键查询。
- 地图目录、地图拓扑、安全区、NPC、刷新点、传送门、怪物掉落、配方和 NPC 脚本关系查询。
- NPC 脚本 AST、静态编译校验和稳定编译哈希查询。
- 地图资源格式、尺寸、可走/阻挡/门格元数据，以及经路径/大小/SHA256 校验的资源文件下载。
- Argon2id 密码、短时 JWT、刷新令牌轮换和会话撤销。
- RBAC 权限校验、账号创建/停用/启用、密码修改/重置、角色调整和只读审计查询。
- API key 机器到机器认证、登录限流和全局限流。

尚未实现：MFA/OIDC、账号删除、解锁接口、写入/发布 API、内容编辑和资源上传。

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
```

- `importRunId` 省略时使用最新 `imported` 批次。
- `entityType`：`item`、`monster`、`map`、`npc`、`quest`、`magic`、`gameshop`、`conquest`、`dragon`、`respawn`、`drop`、`recipe`、`buff`、`script`、`asset` 等。
- `localIndex` 是旧数据库 `Index`，`contentKey` 例如 `item.1`。
- `q` 对 `data.Name`、`data.Title`、`data.FileName` 和内容键做模糊匹配。

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

### 3.5 地图目录与内容关系

```text
GET /api/v1/content/map/catalog?importRunId=&page=1&pageSize=500&q=
GET /api/v1/content/map/{localIndex}/relations?importRunId=&respawnPage=1&respawnPageSize=200
GET /api/v1/content/monster/{localIndex}/drops?importRunId=
GET /api/v1/content/recipe/{localIndex}/relations?importRunId=
GET /api/v1/content/npc/{localIndex}/script?importRunId=
GET /api/v1/content/script/{localIndex}/validation?importRunId=
GET /api/v1/content/asset/{localIndex}/file?importRunId=
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

## 4. 集成验证

```bash
CRYSTAL_DATABASE_URL='Host=127.0.0.1;Port=5432;Database=crystal;Username=crystal;Password=...' \
  ./scripts/test-control-api.sh
```

脚本会执行真实 PostgreSQL 导入（含掉落、配方、脚本和资源元数据）、启动 API，验证登录、JWT、刷新令牌轮换、
RBAC 403、登出撤销、账号创建、停用/启用、管理员重置密码、自助改密、角色调整、最后管理员保护、审计查询、健康检查、
分页、详情、类型计数、地图关系、怪物掉落、配方关系、NPC 脚本关系与 AST、资源元数据、资源文件字节/SHA256
和非法参数，并在 CI 中通过 PostgreSQL 16 service container 运行。测试库应为独立数据库；已有账号时不会再执行 bootstrap。

## 5. 当前限制

- 尚未实现 MFA/OIDC、账号删除、管理员解锁接口和高风险操作再认证。
- 审计目前同步写入 PostgreSQL，后续需要归档、保留策略和防篡改外部存储。
- 角色变更通过撤销会话使权限立即生效；后续引入 OIDC 时需要同步定义 token 失效策略。
- 计数和列表跨全部历史批次；调用方必须使用 `importRunId` 或未来活动发布限定数据。
- JSON 字段名保留旧实体命名，前端需要 schema 或字段映射层。
- 关系查询直接解析 `data jsonb`，尚未建立规范化外键；大规模关系检索前需要评估专用索引或投影表。
- 资源下载要求 API 进程能直接访问 `ControlApi:AssetRoot`；对象存储/CDN、资源上传和签名分发尚未实现。
- 分页使用 offset，内容规模很大时需要评估 keyset pagination。
