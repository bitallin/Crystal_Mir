# Crystal.Control.Api

控制面 API，读取 PostgreSQL 中的内容、账号、角色和审计数据。内容接口目前只读并暴露 15 类实体；账号登录、
会话、RBAC、账号生命周期和审计已提供初版。

## 运行

```bash
export CRYSTAL_DB_CONNECTION='Host=127.0.0.1;Port=5432;Database=crystal;Username=crystal;Password=...'
export CRYSTAL_CONTROL_API_KEY='local-dev-key'
export CRYSTAL_CONTROL_JWT_SIGNING_KEY='local-dev-signing-key-at-least-32-bytes'
export CRYSTAL_CONTROL_BOOTSTRAP_PASSWORD='initial-admin-password'
export CRYSTAL_CONTROL_CORS_ORIGINS='http://127.0.0.1:4173'
export CRYSTAL_CONTROL_ASSET_ROOT='./.external/Crystal.Database/Jev'
export CRYSTAL_CONTROL_RELEASE_ROOT='./artifacts/releases'
export CRYSTAL_CONTROL_RELEASE_SIGNING_PRIVATE_KEY_PATH='/secure/content-signing-private.pem'
export CRYSTAL_CONTROL_RELEASE_SIGNING_PUBLIC_KEY_PATH='/secure/content-signing-public.pem'
export CRYSTAL_CONTROL_RELEASE_SIGNING_KEY_ID='content-release-2026-01'
dotnet run --project src/Crystal.Control.Api
```

`CRYSTAL_CONTROL_JWT_SIGNING_KEY` 必须至少 32 字节，Production 缺失会拒绝启动。开发环境缺失时会生成
临时密钥并记录警告。空库首次启动时可设置 `CRYSTAL_CONTROL_BOOTSTRAP_PASSWORD` 创建 `admin` 管理员；
账号创建后应移除该环境变量。

`CRYSTAL_CONTROL_API_KEY` 用于机器到机器调用，可选；管理端浏览器应使用账号登录后得到的 JWT。
健康检查、登录和刷新接口不需要认证。

`CRYSTAL_CONTROL_CORS_ORIGINS` 或 `ControlApi__CorsOrigins` 是逗号分隔的允许来源列表，供静态
`Crystal.Control.Web` 跨端口调用。Production 必须显式配置，不要使用通配符。开发配置默认允许
`http://127.0.0.1:4173` 和 `http://localhost:4173`。

`CRYSTAL_CONTROL_ASSET_ROOT` 或 `ControlApi:AssetRoot` 是资源文件根目录，快照中的 `relativePath`
相对该目录解析。未配置时内容元数据仍可查询，但 `/content/asset/{localIndex}/file` 会返回明确错误。

`CRYSTAL_CONTROL_RELEASE_ROOT` 或 `ControlApi:ReleaseRoot` 是不可变内容包的输出目录，默认是
`artifacts/releases`（相对 API 内容根）。每个发布版本写入独立的 `<version>/` 目录，包含 manifest、
summary 和按实体类型拆分的 JSONL 文件。

发布签名使用 ECDSA P-256 PEM 密钥。`ReleaseSigningPrivateKeyPath` 和 `ReleaseSigningKeyId` 未配置时
仍可创建、校验和编译发布，但 `sign`/`publish` 返回 503。只配置私钥时 API 会从私钥导出公钥用于验签；
生产环境建议同时配置公钥或改用 KMS/HSM 托管密钥。

## 接口

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| GET | `/health/live` | 进程存活，不访问数据库 |
| GET | `/health/ready` | 数据库连接和 schema 就绪 |
| GET | `/api/v1/import-runs?limit=20` | 导入批次 |
| GET | `/api/v1/import-runs/{id}` | 单个批次 |
| GET | `/api/v1/import-runs/{id}/content-types` | 批次实体类型计数 |
| GET | `/api/v1/content/{entityType}?importRunId=&page=1&pageSize=50&q=&sort=index&direction=asc` | 分页查询 |
| GET | `/api/v1/content/{entityType}/{localIndex}` | 按旧 `Index` 查询 |
| GET | `/api/v1/content/{entityType}/key/{contentKey}` | 按内容键查询 |
| GET | `/api/v1/content/map/{localIndex}/relations` | 地图拓扑、NPC/NPC 脚本、刷新点和传送关系 |
| GET | `/api/v1/content/map/catalog?q=&page=1&pageSize=500` | 全量地图摘要目录与检索 |
| GET | `/api/v1/content/monster/{localIndex}/drops` | 怪物掉落关系 |
| GET | `/api/v1/content/recipe/{localIndex}/relations` | 配方产出、材料、工具和条件 |
| GET | `/api/v1/content/npc/{localIndex}/script` | NPC 对应脚本或 `found=false` |
| GET | `/api/v1/content/script/{localIndex}/validation` | 脚本 AST、静态校验和编译哈希 |
| GET | `/api/v1/content/asset/{localIndex}/file` | 经路径/大小/SHA256 校验的资源文件下载 |
| GET | `/api/v1/releases?limit=50&status=` | 发布列表与实体计数 |
| GET | `/api/v1/releases/{id}` | 发布详情 |
| GET | `/api/v1/releases/{id}/manifest` | 已编译或已签名 manifest |
| POST | `/api/v1/releases` | 从导入批次创建不可变 `draft` 发布 |
| POST | `/api/v1/releases/{id}/validate` | 重建快照并校验哈希、实体和脚本/地图元数据 |
| POST | `/api/v1/releases/{id}/compile` | 生成不可变内容包和 manifest，状态进入 `compiled` |
| POST | `/api/v1/releases/{id}/sign` | 使用 ECDSA P-256 私钥签名，状态进入 `signed` |
| POST | `/api/v1/releases/{id}/publish` | 验签后发布，状态进入 `published` |
| POST | `/api/v1/auth/login` | 用户名密码登录 |
| POST | `/api/v1/auth/refresh` | 轮换刷新令牌并签发新访问令牌 |
| POST | `/api/v1/auth/logout` | 撤销当前会话 |
| POST | `/api/v1/auth/change-password` | 修改当前账号密码，保留当前会话并撤销其他会话 |
| GET | `/api/v1/auth/me` | 当前身份、角色和权限 |
| GET | `/api/v1/accounts?limit=50` | 账号列表 |
| POST | `/api/v1/accounts` | 创建账号并分配角色 |
| POST | `/api/v1/accounts/{id}/status` | 启用或停用账号，停用会撤销会话 |
| POST | `/api/v1/accounts/{id}/password-reset` | 管理员重置密码并撤销目标会话 |
| PUT | `/api/v1/accounts/{id}/roles` | 调整账号角色并撤销目标会话 |
| GET | `/api/v1/roles` | 角色和权限定义 |
| GET | `/api/v1/audit/events?limit=50&action=&result=&actorAccountId=` | 审计事件 |

`entityType` 使用 `item`、`monster`、`map`、`npc`、`quest`、`magic`、`gameshop`、`conquest`、`dragon`、`respawn`、`drop`、`recipe`、`buff`、`script`、`asset`，共 15 类。
`importRunId` 省略时使用最新 `imported` 批次。`sort` 只允许 `index`、`key`、`name`。

内容接口要求 `content:read`，账号管理要求 `account:read`/`account:write`，审计查询要求
`audit:read`，资源文件下载要求 `asset:read`。发布读取、创建/校验/编译/签名和发布分别要求
`release:read`、`release:write` 和 `release:publish`。API key 使用 `X-Api-Key: <key>` 或
`Authorization: Bearer <key>`；JWT 使用 `Authorization: Bearer <accessToken>`。

账号生命周期操作不能停用当前请求账号，也不能停用或移除最后一个启用平台管理员的
`platform_admin` 角色。登录/刷新和普通 API 的每分钟每 IP 限额可分别通过
`ControlApi:AuthRateLimitPermitLimit` 和 `ControlApi:ApiRateLimitPermitLimit` 调整。

## 集成验证

```bash
CRYSTAL_DATABASE_URL="$CRYSTAL_DB_CONNECTION" ./scripts/test-control-api.sh
CRYSTAL_DATABASE_URL="$CRYSTAL_DB_CONNECTION" ./scripts/test-control-web.sh
```

`test-control-api.sh` 验证 schema、导入、认证、RBAC、账号生命周期、审计、CORS 预检，以及
从导入批次创建发布、校验、编译、签名、作者/审批人权限分离和重复发布幂等性。脚本会临时生成
ECDSA P-256 签名密钥，并用 PostgreSQL 的 `content_release` 表验证完整状态流转。
`test-control-web.sh` 在真实 API 上启动静态后台，用 Playwright 验证登录失败/成功、内容总览、
导入批次、内容分页检索、搜索焦点、访问令牌刷新、账号创建/停用/启用/重置密码/角色调整、
脚本 AST/校验、地图尺寸/阻挡、资源下载与 SHA256、角色权限矩阵、审计查询与 CSV 导出、登出、
财务账号导航/403 和控制台错误；需要 Python Playwright。Web 集成脚本启动 API 时必须设置
`ControlApi__AssetRoot` 指向测试内容根。
