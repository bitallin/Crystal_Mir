# Control API 参考

> 最后更新：2026-11-10
> 状态：生效（M1 认证、账号生命周期与只读初版）

## 1. 位置与边界

实现位于 `src/Crystal.Control.Api/`，目标框架 `net8.0`，使用 ASP.NET Core Minimal API + Npgsql。
它读取内容、控制面账号、角色、会话和审计表，不引用 `Server/`、`Server.Library` 或任何 GPL 游戏代码。

当前能力：

- 健康检查。
- 导入批次查询。
- 内容实体分页查询、按旧索引查询、按内容键查询。
- Argon2id 密码、短时 JWT、刷新令牌轮换和会话撤销。
- RBAC 权限校验、账号创建/停用/启用、密码修改/重置、角色调整和只读审计查询。
- API key 机器到机器认证、登录限流和全局限流。

尚未实现：MFA/OIDC、账号删除、解锁接口、写入/发布 API、内容编辑和真实世界关系 API。

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
  "entityCount": 9734,
  "startedAt": "2026-11-10T02:45:18Z",
  "finishedAt": "2026-11-10T02:45:18Z"
}
```

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
- `entityType`：`item`、`monster`、`map`、`npc`、`quest`、`magic`、`gameshop`、`conquest`、`dragon`、`respawn` 等。
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

### 3.5 账号、角色与审计

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

脚本会执行真实 PostgreSQL 导入、启动 API，验证登录、JWT、刷新令牌轮换、RBAC 403、登出撤销、
账号创建、停用/启用、管理员重置密码、自助改密、角色调整、最后管理员保护、审计查询、健康检查、
分页、详情、类型计数和非法参数，并在 CI 中通过 PostgreSQL 16 service container 运行。测试库应为
独立数据库；已有账号时不会再执行 bootstrap。

## 5. 当前限制

- 尚未实现 MFA/OIDC、账号删除、管理员解锁接口和高风险操作再认证。
- 审计目前同步写入 PostgreSQL，后续需要归档、保留策略和防篡改外部存储。
- 角色变更通过撤销会话使权限立即生效；后续引入 OIDC 时需要同步定义 token 失效策略。
- 计数和列表跨全部历史批次；调用方必须使用 `importRunId` 或未来活动发布限定数据。
- JSON 字段名保留旧实体命名，前端需要 schema 或字段映射层。
- 分页使用 offset，内容规模很大时需要评估 keyset pagination。
