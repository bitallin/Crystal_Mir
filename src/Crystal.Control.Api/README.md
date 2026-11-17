# Crystal.Control.Api

控制面 API，读取 PostgreSQL 中的内容、账号、角色和审计数据。内容查询暴露 15 类实体；草稿接口支持复制、
修订、类型化编辑和发布校验。当前注册 10 类 Schema 驱动编辑器，支持引用选择、JSONL 导入导出、脚本源码校验、
地图嵌套关系写入和地图预览。账号登录、会话、RBAC、账号生命周期和审计已提供初版。

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
| GET | `/api/v1/content/{entityType}/options?draftId=&importRunId=&q=&limit=` | 引用选择目标搜索 |
| GET | `/api/v1/content/map/{localIndex}/relations` | 地图拓扑、NPC/NPC 脚本、刷新点和传送关系 |
| GET | `/api/v1/content/map/catalog?q=&page=1&pageSize=500` | 全量地图摘要目录与检索 |
| GET | `/api/v1/content/monster/{localIndex}/drops` | 怪物掉落关系 |
| GET | `/api/v1/content/recipe/{localIndex}/relations` | 配方产出、材料、工具和条件 |
| GET | `/api/v1/content/npc/{localIndex}/script` | NPC 对应脚本或 `found=false` |
| GET | `/api/v1/content/script/{localIndex}/validation` | 脚本 AST、静态校验和编译哈希 |
| GET | `/api/v1/content/asset/{localIndex}/file` | 经路径/大小/SHA256 校验的资源文件下载 |
| GET | `/api/v1/content/asset/{localIndex}/preview?maxSize=256` | 地图可走/阻挡/门格降采样网格 |
| GET | `/api/v1/editor-schemas/{entityType}` | 已注册内容域的编辑器 Schema |
| GET | `/api/v1/drafts?limit=50&status=` | 草稿列表 |
| POST | `/api/v1/drafts` | 从导入批次创建草稿 |
| GET | `/api/v1/drafts/{id}/content` | 草稿内容分页查询 |
| GET | `/api/v1/drafts/{id}/content/{entityType}/{localIndex}` | 读取草稿实体 |
| POST | `/api/v1/drafts/{id}/content/{entityType}` | 类型化创建并自动分配本地索引 |
| PUT | `/api/v1/drafts/{id}/content/{entityType}/{localIndex}` | 更新草稿实体并校验 revision |
| DELETE | `/api/v1/drafts/{id}/content/{entityType}/{localIndex}` | 删除草稿实体 |
| POST | `/api/v1/drafts/{id}/content/{entityType}/import?dryRun=&reason=` | JSONL 导入，默认 dry-run |
| GET | `/api/v1/drafts/{id}/content/{entityType}/export?importRunId=` | 导出稳定排序 JSONL |
| POST | `/api/v1/drafts/{id}/content/script/{localIndex}/validate` | 用提交源码重算 AST、问题和哈希 |
| GET | `/api/v1/drafts/{id}/content/map/{mapIndex}/relations` | 读取草稿地图关系 |
| PUT | `/api/v1/drafts/{id}/content/map/{mapIndex}/{movements|safezones|minezones|activecoords}` | 整体替换关系数组 |
| POST/PUT/DELETE | `/api/v1/drafts/{id}/content/map/{mapIndex}/respawns[/{index}]` | 刷新点行级 CRUD 并同步投影 |
| POST/PUT/DELETE | `/api/v1/drafts/{id}/content/map/{mapIndex}/npcs[/{index}]` | NPC 行级 CRUD 并同步投影 |
| POST | `/api/v1/drafts/{id}/validate` | 运行草稿完整性校验 |
| GET | `/api/v1/drafts/{id}/revisions` | 草稿修订记录 |
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

当前注册 `item`、`monster`、`map`、`npc`、`script`、`drop`、`magic`、`quest`、`gameshop` 和 `recipe`
十种编辑器 Schema，均为 `schemaVersion=1`。字段类型支持 `number`、`array`、`reference`、`code`、`color`
和 `readonly`，并可用 `referenceType`、`nullable`、`multiline`、`minItems`、`maxItems` 描述编辑约束。

类型化创建仅对注册类型开放，客户端不能指定已有内容键。服务端身份规则为：

| 类型 | 身份规则 |
| --- | --- |
| `item` / `monster` / `map` / `npc` / `quest` | 自动分配 `localIndex`，键为 `<type>.<localIndex>` |
| `gameshop` | 自动分配 `GIndex`，键为 `gameshop.<GIndex>` |
| `magic` | `localIndex` 使用 `Spell`，键为 `magic.<Spell>` |
| `recipe` | 自动分配 `localIndex`，键为 `recipe.<产出物品名>` |
| `script` | 自动分配 `localIndex`，键为 `script.<规范化文件路径>` |
| `drop` | `localIndex = monsterIndex`，键为 `drop.monster.<monsterIndex>` |

保存阻断类型、范围、枚举、布尔值、引用形状、身份字段/内容键一致性等结构错误；字段缺失、空名称、引用缺失、
重复身份和内容键不一致由草稿/发布校验报告为 `<entityType>-completeness`、`<entityType>-content-key`、
`reference-missing`、`drop-duplicate`、`recipe-duplicate`、`script-duplicate` 等。未知字段在保存时原样保留。

地图编辑器覆盖 44 个顶层标量字段，分为身份、客户端表现、访问控制、战斗与环境、人口与限制、矿区与攻城六组。
传送、安全区、矿区和激活坐标通过草稿关系接口整体替换；刷新点和 NPC 由独立实体行级写入，并在同一事务内同步
地图投影。关系写入使用 `expectedRevision`，坐标越界或引用缺失会被拒绝。`WeatherParticles` 保留为只读字符串以兼容
旧位掩码。地图 `FileName` 在草稿和发布集合内按去空格、大小写不敏感唯一，冲突报告 `map-file-name-duplicate`。

JSONL 导入与快照实体行格式一致，单次上限 16 MiB，默认 `dryRun=true`；应用模式在单事务内按 `contentKey` upsert，
冲突返回 409，并记录草稿修订与 `draft.import` 审计。导出按 `localIndex` 稳定排序，可从活动草稿或指定导入批次生成。
脚本校验接口不信任客户端 AST，始终用提交源码重新计算页面、静态引用、问题、源码 SHA256 和编译哈希。

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

`test-control-api.sh` 验证 10 类 Schema 的 200/401、类型化 CRUD、结构 400、stale revision 409、引用缺失、
JSONL dry-run/apply/export、脚本 AST、地图关系 CRUD/清理、认证/RBAC/账号生命周期/审计/CORS，以及发布创建、
校验、编译、签名、作者/审批人权限分离和重复发布幂等性。脚本会临时生成 ECDSA P-256 签名密钥。
`test-control-web.sh` 在真实 API 上启动静态后台，用 Playwright 验证登录、内容总览、导入批次、10 类编辑器、
引用选择、脚本源码与只读 AST、嵌套掉落组、地图关系 CRUD 和 409 恢复、JSONL dry-run/apply/export、地图预览、
资源下载与 SHA256、发布闭环、只读权限、账号/角色/审计、令牌刷新和登出；需要 Python Playwright。Web 集成脚本
启动 API 时会设置 `ControlApi__AssetRoot` 指向测试内容根。
