# 项目进度台账

> 最后更新：2026-11-13
> 当前阶段：M1 架构骨架与数据底座（内容快照、导入器、快照/PostgreSQL 到旧二进制重建、PostgreSQL schema、内容快照签名、Control API、账号/JWT/RBAC/审计、NPC 脚本 AST/编译校验、地图格式解析与资源文件校验下载、Control.Web 全量地图目录与真实关系接入、Linux 区服宿主和 Ubuntu 部署已完成初版；从导入批次创建/校验/编译/签名/发布 API 已完成初版；可编辑内容草稿写入、M1 计划中的 GameCore/Agent/Compose 交付物与 PG16 staging 待做）

## 1. 当前状态

| 项目 | 状态 | 说明 |
| --- | --- | --- |
| 技术文档体系 | 已完成 | 架构、ADR、开发、路线图、进度台账已建立 |
| 架构决策 | 已完成初版 | ADR 0001～0005 |
| 构建基线 | 已完成 | Linux 安装 .NET 8 SDK；旧服务端/客户端/内容库/API/工具构建成功；358 项回归检查通过 |
| Linux 区服宿主 | 已完成初版 | `Crystal.ZoneServer` 控制台宿主、日志、SIGTERM 优雅退出、端口/线程环境变量覆盖、内容路径大小写与分隔符兼容；Jev 内容加载 463 张地图并完成冒烟 |
| Ubuntu 部署 | 已完成初版 | 自包含 `linux-x64` 发布约 74 MB；提供 systemd 单元、安装脚本、发布脚本和部署运行手册；容器内未执行 systemd 实际安装 |
| 外部资源 | 已完成初版 | `Crystal.Database`、`Crystal.MapEditor` 已拉取；客户端 `Data/Map/Sound` 仍缺 |
| 数据迁移 | 已完成初版 | 二进制导入器、JSONL 快照、PostgreSQL schema、旧二进制导出、快照/PostgreSQL 到旧库重建完成，黄金样本逐字节一致；掉落、配方、NPC 脚本和地图/预览资源元数据可通过 `--content-root` 导入，启动时 Buff 定义始终导入；脚本已有 AST/编译校验，地图资源已解析格式、尺寸、可走/阻挡/门格；其他资源二进制待做 |
| 内容快照 | 已完成初版 | `Crystal.Content`（MIT）+ 9,793 个核心实体（含 59 个 Buff）+ 可选 `drop`/`recipe`/`script`/`asset` 实体 + 哈希校验 + ECDSA P-256 签名/验签 + 回归测试；测试内容根批次为 9,797 个实体，最终 `contentHash` 为 `1b93509c81d7c4839f009411d46c3e42f3596c9e7bab368cf711d2dd8ec22291` |
| 参考文档生成 | 已完成初版 | 25 个内容实体、432 个协议包、274 个配置项已生成 |
| CI 基线 | 已完成初版 | 本地 `ci.sh` 通过；GitHub Actions 增加 PostgreSQL 16 Control API 集成 job |
| 控制面 | 进行中 | `Crystal.Control.Api` 内容只读接口暴露 15 类实体，含全量地图目录、地图拓扑/刷新点/传送门/NPC/NPC 脚本/怪物掉落/配方引用关系、NPC 脚本 AST/校验、地图资源元数据和经完整性校验的文件下载；发布 API 支持从导入批次创建不可变 `draft`、dry-run 校验、编译内容包、ECDSA 签名、验签发布、重复发布幂等和作者/审批人权限分离；账号生命周期、健康检查、可配置限流、CORS 和 PG 集成测试完成；`Crystal.Control.Web` 已接入真实登录、刷新/登出/自助改密、内容总览、导入批次、15 个内容域、全量地图目录与检索、真实关系面板与详情抽屉、脚本 AST 与编译校验、资源下载与浏览器端 SHA256 复核、Buff 列表、账号管理和审计导出；发布中心 Web 仍为原型，可编辑内容草稿写入待做 |
| 认证与审计 | 已完成初版 | Argon2id、短时 JWT + 刷新令牌、RBAC、账号创建与生命周期、会话撤销、最后管理员保护、只追加审计和管理端接入完成 |
| 热更新 | 未开始 | 新 manifest/启动器/Agent 尚未实现 |
| 运营能力 | 未开始 | 开区/合区/支付/监控尚未实现 |
| 许可证清单 | 进行中 | 代码/外部资源清单已建立；MapEditor 和素材授权待确认 |

## 2. 里程碑状态

| 里程碑 | 状态 | 完成度 | 备注 |
| --- | --- | --- | --- |
| M0 基线与合规 | 进行中 | 80% | 构建、资源、黄金样本完成；Windows 运行冒烟和素材授权待做 |
| M1 架构骨架与数据底座 | 进行中 | 98% | 数据链路、只读 API、地图目录与关系/掉落/配方/NPC 脚本 API、脚本 AST/编译校验、地图尺寸/阻挡解析、资源完整性下载、内容快照签名、发布校验/编译/签名/发布 API、账号/JWT/RBAC/审计、旧二进制导出与快照/PostgreSQL 重建、Linux 区服宿主和 Ubuntu 部署完成；`Crystal.GameCore`、`Crystal.Agent.Contracts`、Compose 交付物、可编辑内容草稿写入和 PG16 staging 待做 |
| M2 内容后台 MVP | 未开始 | 0% | 依赖 M1 |
| M3 热更新与区服 Agent | 未开始 | 0% | 依赖 M1/M2 |
| M4 运营 MVP | 未开始 | 0% | 依赖 M2/M3 |
| M5 生产加固 | 未开始 | 0% | 依赖 M4 |
| M6 通用化与生态 | 未开始 | 0% | 长期 |

## 3. 工作日志

### 2026-11-09

- 范围：建立完整技术文档体系和后续进度记录机制。
- 改动：新增 `AGENTS.md`、`docs/README.md`、`docs/PROGRESS.md`、架构文档 7 篇、ADR 4 篇、开发文档 3 篇、需求能力矩阵、路线图与里程碑、参考手册索引、运行手册索引。
- 关键结论：采用控制面/数据面分离；PostgreSQL + 不可变内容快照；保留现有客户端协议并引入边缘网关；控制面与 GPL 服务端代码隔离。
- 验证：检查文档目录、内部链接和文件存在性；未执行代码构建（当前环境无 .NET SDK）。
- 下一步：完成 M0 构建基线、外部资源清单和许可证/素材审查清单。

### 2026-11-09（M0 构建与资源基线）

- 范围：恢复构建、拉取外部资源、建立数据库检查和黄金样本。
- 环境：Ubuntu 20.04，安装 .NET SDK 8.0.427 到 `~/.dotnet`。
- 构建：`Shared`、`Server.Library`、`Server.MirForms`、`Client` 全部构建成功；Windows 项目使用 `-p:EnableWindowsTargeting=true` 交叉构建。
- 测试：`Tests/ItemSecurity` 通过 358 项回归检查。
- 工具：新增 `tools/Crystal.DbInspector`、`scripts/build.sh`、`scripts/test.sh`、`scripts/inspect-db.sh`、`scripts/inspect-all-db.sh`、`scripts/fetch-external.sh`、`scripts/ci.sh`、`.github/workflows/ci.yml`。
- 资源：拉取 `Crystal.Database`（3.4 GB，Unlicense）和 `Crystal.MapEditor`（33 MB，未发现明确许可证），放在 `.external/` 并加入忽略。
- 数据库：Jev v111、Daneo1989 v83、cjlaaa v105 可加载；NightWolf 头部版本 34，低于最低支持版本 60。
- 黄金样本：`tests/fixtures/golden/Server.MirDB`，SHA256 `37358b0d95eafe458b060c1db2ebc0524757fe524fb93125ccb98d9b4a8ef78b`。
- 文档：新增 `docs/reference/external-resources.md`、`db-inventory.md`、`license-inventory.md`、`tests/fixtures/README.md`，并更新本地环境和构建文档。
- 验证：`./scripts/inspect-db.sh` 成功；检查器使用临时目录，不在仓库根目录生成 `Configs/` 或 `Envir/`。
- 未验证：客户端和 WinForms 宿主的实际运行、登录和进服；当前环境是 Linux。
- 下一步：Windows 冒烟测试，随后进入 M1 PostgreSQL schema 和二进制导入器。

### 2026-11-09（M1 参考文档生成）

- 范围：从现有代码生成内容字段、协议包和配置项清单。
- 工具：新增 `tools/Crystal.FieldInventory` 和 `scripts/generate-reference.sh`。
- 产物：`docs/reference/content-schema.md`（25 个实体）、`packet-catalog.md`（432 个包）、`config-keys.md`（274 项）。
- 处理：区分公开字段、可写属性和只读计算属性；字段清单不包含继承成员。
- 验证：生成命令可重复执行；生成过程在临时目录运行，不会在仓库根目录创建 `Configs/` 或 `Envir/`。
- CI：`scripts/ci.sh` 端到端通过；测试和检查器均使用临时工作目录，工作区保持干净。
- 下一步：基于字段清单设计 PostgreSQL schema，并实现 `LegacyBinaryImporter`。

### 2026-11-09（M1 内容快照、二进制导入器与 PostgreSQL schema）

- 范围：打通 `Server.MirDB -> JSONL 快照 -> PostgreSQL` 的数据底座。
- 新增 `src/Crystal.Content`（MIT）：实体模型、canonical JSON、manifest、SHA256 文件校验、快照读写和计数校验。
- 新增 `tools/Crystal.LegacyImporter`（链接 GPL `Server.Library`）：读取旧数据库，反射映射实体，处理循环引用和实体引用，生成快照，可选写入 PostgreSQL。
- 新增 `db/migrations/0001_content_schema.sql`：`content_import_run`、`content_entity`、`content_release`、资源表和只读视图。
- 新增 `scripts/import-db.sh`、`scripts/verify-content.sh`，并接入 `build.sh`、`test.sh`、`ci.sh` 和 GitHub Actions。
- 映射：物品、怪物、地图、NPC、任务、技能、商城、攻城、龙、展平刷怪点；配方/Buff/掉落/脚本不在 `Server.MirDB`，已写入 manifest 限制说明。
- 黄金样本：9,734 个实体（物品 1,628、怪物 555、地图 463、NPC 375、任务 154、技能 111、商城 105、攻城 1、龙 1、刷怪点 6,341），`contentHash` 为 `7e7a6dd14c091b63eed9bb271ae5bbb6ec080d081669048beeefb6c46a373825`。
- 验证：`ContentSnapshot` 13 项检查通过；黄金导入两次输出逐字节一致；本地 PostgreSQL 12 上 schema 迁移、`COPY` 写入、重复导入和只读视图通过；`content_import_run` 记录完整。
- 未验证：PostgreSQL 16 staging、Windows 运行、资源/脚本导入、`LegacyBinaryExporter`。
- 下一步：搭建 `Crystal.Control.Api` 只读内容接口，再做 `LegacyBinaryExporter` 和资源/脚本导入。

### 2026-11-10（M1 只读 Control API）

- 范围：建立控制面只读 API 和真实 PostgreSQL 集成验证。
- 新增 `src/Crystal.Control.Api`（ASP.NET Core Minimal API + Npgsql，不引用 GPL 代码）。
- 接口：`/health/live`、`/health/ready`、导入批次、批次类型计数、内容分页查询、按旧索引/内容键查询。
- 安全：API key（`X-Api-Key`/Bearer）、Production 缺密钥拒绝启动、240 次/分钟 IP 限流、`no-store`、ProblemDetails、隐藏 Kestrel Server 头。
- 查询：1-based 分页、`pageSize` 1～200、`sort` 白名单、`q` 参数化模糊搜索并转义通配符、默认最新 `imported` 批次。
- 新增 `scripts/test-control-api.sh`：真实 schema 迁移、黄金导入、启动 API、验证 401、健康检查、分页、详情、类型计数和非法参数。
- CI：新增 PostgreSQL 16 service container job，运行完整 Control API 集成脚本。
- 验证：本地 PostgreSQL 12 上 `test-control-api.sh` 通过；API 返回物品 1,628、怪物 555 等 10 类计数，详情和按内容键查询一致；带数据库运行 `./scripts/ci.sh` 时 API 集成也通过。
- 未验证：GitHub Actions 实际运行、PostgreSQL 16 本地、管理员账号/RBAC/审计、浏览器 Web 后台。
- 下一步：补齐账号/RBAC/审计 schema 与认证端点，再搭建 `Crystal.Control.Web` 只读内容浏览，随后实现 `LegacyBinaryExporter`。

### 2026-11-10（M1 认证、RBAC 与审计）

- 范围：为控制面补上管理端账号、会话、授权和审计骨架。
- schema：新增 `db/migrations/0002_control_identity_audit_schema.sql`，包含 `control_account`、角色/权限、账号角色、会话和 `audit_event`；内置平台管理员、版本作者、发布审核、GM、财务和审计六类角色。
- 密码与会话：密码使用 Argon2id；访问令牌为短时 JWT，刷新令牌随机生成、仅存 SHA-256 哈希，并在刷新时轮换；JWT 每次请求会检查数据库会话，登出后立即失效。
- 授权：权限按 `resource:action` 校验，内容、账号、角色和审计接口分别要求对应权限；API key 保留为机器到机器/兼容凭证并拥有通配权限。
- 接口：新增 `/api/v1/auth/login`、`/auth/refresh`、`/auth/logout`、`/auth/me`、`/accounts`、`/roles`、`/audit/events`。
- 审计：登录成功/失败、刷新、登出、账号创建和授权拒绝写入 `audit_event`；数据库触发器禁止 UPDATE、DELETE 和 TRUNCATE。
- 启动：支持通过 `CRYSTAL_CONTROL_BOOTSTRAP_PASSWORD` 在空库创建初始管理员；Production 强制配置 `CRYSTAL_CONTROL_JWT_SIGNING_KEY`。
- 迁移：`Crystal.LegacyImporter` 改为按文件名顺序应用 `db/migrations/*.sql`，不再硬编码单个 migration。
- 验证：`./scripts/ci.sh` 通过；358 项回归、13 项内容快照检查、黄金导入和 PostgreSQL 12 Control API 集成通过；集成覆盖登录、JWT 访问、短时/刷新令牌轮换、旧刷新令牌重放拒绝、RBAC 403、登出即时撤销和审计查询；手工验证审计 UPDATE/DELETE 均被拒绝。
- 未验证：MFA/OIDC、账号禁用/改密/重置/角色调整、日志归档、PostgreSQL 16 staging、GitHub Actions 实际运行和浏览器 Web 后台。
- 下一步：搭建 `Crystal.Control.Web` 最小只读内容浏览器并接入账号登录，随后补齐账号生命周期与 `LegacyBinaryExporter`。

### 2026-11-10（Crystal.Control.Web 高保真原型）

- 范围：完成管理后台的完整信息架构、高保真版式和可点击交互原型，用于确认内容生产、发布、区服运营、玩家交易、权限安全和平台治理的页面边界。
- 新增 `src/Crystal.Control.Web`：首版 36 个后台业务页面和 1 个登录页，共 37 个页面入口/43 个可访问路由；同日扩展为 38 个后台业务页面 + 登录页，共 39 个页面入口/45 个可访问路由（见下一条）；13 类内容域、全局搜索、内容详情抽屉、审批/高风险确认弹窗、登录模拟和移动端导航。
- 新增 `docs/product/control-web-prototype.md`：记录一级/二级信息架构、页面职责、统一版式、首版 6 条关键业务闭环（同日扩展为 7 条，见下一条）、高风险交互和视觉规范。
- 新增 `scripts/preview-control-web.sh`：无构建依赖的本地预览入口，默认监听 `127.0.0.1:4173`。
- 首版验证：全部 JavaScript 通过 `node --check`；Playwright 逐页打开 43 个可访问路由（37 个页面入口 + 6 个区服详情）无控制台错误；桌面 1440×1000 和移动 390×844 无横向溢出；内容详情抽屉、全局搜索、审批弹窗和移动端侧栏交互通过；扩展后的复验见下一条。
- 未验证：真实 Control API 接入、真实 JWT/RBAC、生产前端框架、构建链和浏览器兼容矩阵。
- 下一步：将登录、导入批次、内容只读查询和审计接入 Control API，替换演示数据，再补齐账号生命周期与 `LegacyBinaryExporter`。

### 2026-11-10（世界关系、刷怪掉落与地图坐标）

- 范围：围绕热血传奇的运行时玩法，把地图层、入口/出口坐标、刷怪点、怪物、刷新时间、掉落组、装备产出、NPC 坐标和在线人物坐标组织成可校验关系。
- 新增 `src/Crystal.Control.Web/assets/world-data.js`：15 个地图节点、20 个传送节点、10 个刷怪点、7 个 NPC 坐标、6 个掉落组、4 条典型玩法链和 5 个在线人物坐标。
- 新增 `#/world-map` 地图拓扑与坐标页：地图组层级链路、0,0 到地图尺寸的坐标画布、安全区、刷怪点、Boss、NPC、传送门、在线人物、入口/出口坐标表和刷新时间表。
- 新增 `#/content-relations` 世界关系图谱：地图 → 刷怪点 → 怪物 → 掉落组 → 装备正查，装备 → 来源怪物 → 地图坐标反查，并展示关系完整性检查和发布门禁。
- 内容总览新增“世界关系工作台”快捷入口；新增本地 SVG favicon，消除资源 404。
- 升级内容详情抽屉：物品显示来源怪物/地图坐标/爆率，怪物显示刷新点/刷新间隔/掉落组，地图显示入口出口、刷怪点和 NPC，NPC/掉落组/刷新点显示对应世界关系。
- 补充演示内容：屠龙、骨玉权杖、龙纹剑、法神披风等装备，沃玛教主、祖玛卫士等怪物，祖玛一层/二层/教主大厅、沃玛二层、盟重省等地图；GM 工作台增加在线人物当前地图、坐标和朝向，并支持地图定位。
- 文档：更新 `src/Crystal.Control.Web/README.md` 和 `docs/product/control-web-prototype.md`，补充世界关系模型、坐标字段、发布门禁、页面口径和验证方式。
- 验证：全部 JavaScript 通过 `node --check`；Playwright 逐页打开 45 个可访问路由（39 个页面入口 + 6 个区服详情动态路由）无控制台错误；桌面 1440×1000 和移动 390×844 无横向溢出；地图切换、关系链切换、详情抽屉和内容总览快捷入口通过。
- 未验证：真实 Control API、真实运行时坐标、真实 MirDB 掉落/NPC 脚本映射和关系 schema；当前世界关系仍为演示数据。
- 下一步：把地图拓扑、刷新点、传送门和掉落组落成正式只读关系 API，再将原型校验替换为服务端结果。

### 2026-11-10（地图级刷怪数量与怪物分布）

- 范围：明确一张地图可以配置多个刷怪点、多种怪物，每个刷怪点有独立坐标、数量、半径和刷新时间；地图怪物总量按所有刷怪点数量加总。
- 地图页新增“本图刷怪总览”：展示刷怪点数、怪物种类、怪物总量、Boss 点数，并按怪物种类汇总数量、占比、刷新点数和刷新间隔。
- 地图页保留并强化“刷怪点明细”：逐点展示怪物、坐标、数量、固定延迟和随机延迟。
- 地图选择器和同一地图组链路增加每张地图的怪物总量与种类数，例如祖玛寺庙三层显示 22 只 / 2 种。
- 世界关系图谱的刷新点节点和刷怪链路表增加怪物数量，地图节点增加本图总量与种类数；地图和怪物详情抽屉增加汇总口径。
- 文档：更新 `src/Crystal.Control.Web/README.md` 和 `docs/product/control-web-prototype.md`，补充地图刷怪汇总、容量门禁和验证口径。
- 验证：全部 JavaScript 通过 `node --check`；Playwright 45 条可访问路由无失败，桌面和移动端无横向溢出，世界关系与地图页无控制台错误。
- 未验证：真实 MirDB 刷怪点全量数据、地图容量/性能预算和真实运行时刷新状态；当前仍为演示样本。
- 决策：本次只是原型派生视图，未修改正式 schema、协议或发布模型，因此不新增 ADR。

### 2026-11-10（Control.Web 只读 API 接入）

- 范围：把管理后台的登录、会话、导入批次、内容只读查询和审计从原型数据切到真实 Control API，同时保留 API 不可用时的演示回退。
- 新增 `src/Crystal.Control.Web/assets/config.js` 和 `assets/api.js`：API 地址可通过 `?api=` 指定；JWT 保存在 `sessionStorage`；访问令牌到期前自动刷新并轮换刷新令牌；401 清理会话并回到登录页。
- 登录页接入 `POST /api/v1/auth/login`、`/auth/refresh`、`/auth/logout` 和 `GET /auth/me`，展示真实用户名、角色、MFA 状态和会话模式；错误密码、限流和 API 不可达分别提示。
- 导入批次接入 `GET /api/v1/import-runs` 和批次实体计数，支持切换批次摘要；内容库接入 `GET /api/v1/content/{entityType}`，物品、怪物、地图、NPC、任务、技能、商店、攻城、龙族和刷新点共 10 类使用真实分页、搜索和旧实体字段映射；审计接入 `GET /api/v1/audit/events` 并按 `audit:read` 控制。
- 页面显示“实时 API/演示数据/API 降级”徽标；真实查询失败时回退样本并明确提示；内容搜索保留焦点；顶栏和侧栏显示真实账号。
- 修复移动端 grid 子项最小内容宽度导致的横向溢出：内容库宽表格改为容器内横向滚动，单列布局使用 `minmax(0, 1fr)`。
- Control API 新增 `ControlApi:CorsOrigins`/`CRYSTAL_CONTROL_CORS_ORIGINS` 受控 CORS，开发默认允许 `127.0.0.1:4173` 和 `localhost:4173`，Production 要求显式配置。
- 测试：`scripts/test-control-api.sh` 增加 CORS 预检；新增 `scripts/test-control-web.sh`，用 Playwright 在真实 PostgreSQL 和 API 上验证错误/正确登录、导入批次、物品分页与搜索、搜索焦点、访问令牌刷新、审计查询、登出、CORS 和控制台错误。`scripts/ci.sh` 增加 Control.Web JavaScript 语法检查。
- 验证：`test-control-api.sh` 在 `crystal_web_test` 通过（黄金导入 9,734 实体，10 类计数）；`test-control-web.sh` 通过真实 API 全链路；`./scripts/ci.sh` 通过；演示模式 35 条路由无控制台错误；桌面 1440×1000 正常；移动端 390×844 抽查内容库、导入批次、审计、地图拓扑和关系图谱无横向溢出。
- 未验证：导航级 RBAC 与 403 空状态、账号/角色页面真实数据、内容总览 KPI 替换、掉落/配方/Buff 导入、真实地图关系 API、生产前端构建链、CSP、浏览器兼容矩阵和 PostgreSQL 16 staging。
- 决策：CORS 仅增加受控来源配置，未改变协议、数据模型、发布模型或许可证边界，因此不新增 ADR。

### 2026-11-10（Control.Web 只读闭环与导航级 RBAC）

- 范围：完成 Control.Web 剩余只读接入，并把权限判定覆盖到导航、直达路由和页面动作。
- 内容总览：实时模式使用当前 `content_import_run` 的 `entityCount`、来源哈希、开始时间和 10 类实体计数；内容域卡片对 MirDB 域显示批次快照，对未导入域明确标注原型样本；草稿、校验问题和发布状态没有读 API，实时模式显示 `—` 而不伪造数据。
- 账号页：接入 `GET /api/v1/accounts?limit=200`，展示真实显示名、用户名、邮箱、角色、状态、MFA、最近登录和账号 ID；接口未暴露 IP，实时模式不再展示伪造来源 IP；KPI 改为账号数、MFA 覆盖率、启用数和停用/锁定数。
- 角色页：接入 `GET /api/v1/roles`，用真实 `resource:action` 权限点生成权限矩阵，支持选择角色查看权限；成员数没有聚合接口，KPI 改为可写角色数并明确标注。
- 导航级 RBAC：新增路由权限映射，实时模式过滤侧栏、全局搜索和区服入口；直达无权限路由统一渲染 403 空状态，展示所需权限、当前角色和账号，不回退原型数据。登录后不再预先拉取 `content:read` 数据，避免财务等角色因无关权限缺失被误报。
- 页面动作：账号创建按 `account:write`、角色保存按 `role:write`、权限审计入口按 `audit:read` 控制显隐；内容、账号、角色和审计的真实页面不再回退样本。
- 审计导出：新增“导出当前查询结果”，在前端把已加载审计行导出为 UTF-8 BOM CSV；这不是服务端导出任务，范围仅限当前最多 100 条查询结果，并在界面文案中说明。
- 测试：`scripts/test-control-web.sh` 增加实时内容总览、账号列表、角色矩阵、审计 CSV 下载，以及通过 API 新建财务账号后验证导航隐藏、`content:read`/`account:read` 直达 403、无越权内容/账号请求和浏览器控制台错误检查。
- 验证：`test-control-web.sh` 通过真实 PostgreSQL + Control API + Chromium 全链路；`./scripts/ci.sh` 通过；桌面 1440×1000 检查账号、角色页面，移动端 390×844 检查角色矩阵无横向溢出（页面宽度 390，视口 390）。
- 未验证：账号生命周期写接口、服务端审计导出任务、字段级权限、自定义角色 CRUD、内容写入/发布、掉落/配方/Buff 导入、真实地图关系 API、生产前端构建链、CSP、浏览器兼容矩阵和 PostgreSQL 16 staging。
- 决策：仅完成前端只读接线和权限显隐，服务端仍以 JWT 权限策略为最终授权边界；未改变协议、数据模型、发布模型或许可证边界，因此不新增 ADR。

### 2026-11-10（M1 账号生命周期与限流配置）

- 范围：补齐控制面账号生命周期，并把账号页从只读/模拟写操作接入真实 API。
- API：新增 `POST /api/v1/auth/change-password`、`POST /api/v1/accounts/{id}/status`、`POST /api/v1/accounts/{id}/password-reset`、`PUT /api/v1/accounts/{id}/roles`。
- 安全语义：停用、管理员重置密码和角色调整会撤销目标账号相关会话；自助改密保留当前会话并撤销其他会话；启用账号清除失败登录计数和锁定。
- 保护规则：禁止停用当前请求账号；禁止停用或移除最后一个启用平台管理员的 `platform_admin` 角色；管理员重置自己的密码必须走自助改密接口。
- 审计：新增 `account.status`、`account.password.reset`、`account.roles.update` 和 `auth.password.change`，元数据不记录密码或密码哈希。
- Web：账号页接入真实创建、停用/启用、重置密码和角色调整；用户菜单接入自助改密；写操作继续受 `account:write` 控制。
- 配置：新增 `ControlApi:ApiRateLimitPermitLimit`（默认 240）和 `ControlApi:AuthRateLimitPermitLimit`（默认 10），测试脚本按需放宽。
- 测试：`scripts/test-control-api.sh` 覆盖停用/启用、会话撤销、管理员重置、自助改密、角色变更后权限生效、最后管理员保护和未知/非法参数；`scripts/test-control-web.sh` 覆盖账号创建、停用/启用、重置密码、角色调整和审计动作。
- 验证：API 集成在全新 PostgreSQL 测试库通过；Control Web 在真实 PostgreSQL + Control API + Chromium 全链路通过；Node.js 语法检查通过。
- 未验证：账号删除、管理员解锁、MFA/OIDC、字段级权限、服务端审计导出任务、自定义角色 CRUD、PostgreSQL 16 staging、生产前端构建链和 CSP。
- 决策：未改变协议、数据模型、发布模型或许可证边界，因此不新增 ADR。

### 2026-11-10（M1 LegacyBinaryExporter v1）

- 范围：实现旧 `Server.MirDB` 的确定性二进制导出，并验证重新加载和回滚制品路径。
- 工具：新增 `tools/Crystal.LegacyImporter/LegacyBinaryExporter.cs` 和 `--export <path>`、`--no-verify`；新增 `scripts/export-db.sh`。
- 写入：严格按 `Envir.SaveDB()` 顺序输出头部、8 个索引、地图/物品/怪物/NPC/任务/龙/技能/商城/攻城、刷新计时器和 GTMap 列表，版本为 117。
- 安全性：默认拒绝覆盖；先写同目录临时文件，flush 后原子替换；默认通过 `LoadDB()` 冷回读并比较实体语义哈希、索引和集合计数。
- 会话隔离：加载前清理旧 `Settings.RandomItemStatsList` 的进程内累加状态，避免同一验证进程第二次加载产生派生字段差异。
- 验证：黄金库导出两次均为 548,567 字节且 SHA256 为 `3775771c893d95f490bbb54b868650c91f7200e8d268d356a9c22d0a7e11284a`；两次字节一致，冷回读语义哈希一致。
- 内容等价：源库与导出库分别生成的实体快照逐字节一致，`contentHash` 均为 `7e7a6dd14c091b63eed9bb271ae5bbb6ec080d081669048beeefb6c46a373825`。
- CI：`scripts/verify-content.sh` 增加导出、冷回读和源/导出实体快照逐字节比较。
- 未验证：从 JSONL 快照或 PostgreSQL 对象图重建旧实体后导出；完整 Windows 旧服务端启动。
- 决策：保持现有二进制格式和版本协商，不改变协议、数据模型、发布模型或许可证边界，因此不新增 ADR。

### 2026-11-10（Linux 服务端宿主可行性验证）

- 范围：确认旧服务端是否必须运行在 Windows，并验证 `Server.Library` 在 Ubuntu 上的实际启动能力。
- 环境：Ubuntu 20.04、.NET SDK 8.0.427、`linux-x64`；`Server.Library` Release 构建 0 警告、0 错误。
- 验证：在临时目录用最小控制台宿主引用 `Server.Library`，加载 `Jev` 数据库副本并调用 `Envir.Start()`；运行 15 秒后服务端保持 `running=True`，加载 400 张地图和 57,584 个对象，游戏端口 7000 正常初始化。
- 结论：当前可直接运行的旧服务端宿主 `Server.MirForms` 仍是 Windows/WinForms 专用；但 `Server/` 核心库是 `net8.0`，未发现 Win32、注册表、WinForms 或 `System.Drawing.Common` 依赖，可在 Linux 控制台宿主中运行。文档中的“Zone Server Linux 优先”目标与实测一致。
- 兼容问题：Linux 启动日志出现部分地图和任务文件加载失败，原因是内容引用的大小写与磁盘文件名不一致（如 `D2001` 对 `d2001.map`）以及任务路径使用 Windows 反斜杠；属于内容路径规范化问题，不是 .NET 运行时平台限制。
- 未验证：真实 Windows 客户端连接、登录/选角/进服和完整玩法链路；带 UI 的 `Server.MirForms` 在 Linux 运行；生产级 Linux 服务、进程守护和日志采集。
- 下一步：实现正式的 `Crystal.ZoneServer` 控制台宿主，统一内容路径大小写与分隔符，并用黄金数据库和真实客户端完成 Linux 区服冒烟。
- 决策：未改变协议、数据模型、发布模型或许可证边界，因此不新增 ADR。

### 2026-11-10（Crystal.ZoneServer 与 Ubuntu 部署）

- 范围：把可行性验证落成正式 Linux 区服宿主，并补齐当前 Ubuntu 环境可直接部署的产物。
- 新增 `src/Crystal.ZoneServer`：加载 `Settings` 和旧二进制数据库，启动 `Envir`，写控制台/滚动日志，支持 SIGTERM/SIGINT 优雅退出；提供 `--content-root`、`--check`、启动超时和环境变量覆盖。
- 核心调整：`Envir` 增加 `Ready` 状态；状态端口改为 `Settings.StatusPort`，默认仍为 `3000`；新增 `ContentPath` 对内容路径做分隔符规范化和大小写无关解析，覆盖地图、路线、任务、配方、掉落、NPC 脚本/include、变量和名单文件。
- 部署：新增 `scripts/publish-zone.sh`、`scripts/zone-smoke.sh`、`scripts/install-zone.sh`、`deploy/systemd/crystal-zone.service` 和 `zone.env.example`；自包含 `linux-x64` 产物约 74 MB。
- 文档：新增 `docs/runbooks/zone-server-linux.md`，更新本地环境、构建发布、运行时模块、部署架构和文档索引。
- 验证：`--check` 通过，463 张地图、375 个 NPC 脚本、154 个任务引用全部解析；完整启动加载 463 张地图、1,628 个物品、555 个怪物，`Zone ready` 后 SIGTERM 退出码为 0；`scripts/zone-smoke.sh` 通过；自包含发布产物独立启动并正常退出；`./scripts/ci.sh` 全部通过；shell 脚本通过 `bash -n`。
- 未验证：systemd 实际安装和开机启动（当前容器未运行 systemd）；真实 Windows 客户端登录、选角、进服和完整玩法链路；长时间运行和压力测试。
- 已知内容告警：Jev 内容的 `HiGreatGhoul` 掉落行无法解析，不影响区服启动，属于内容数据问题。
- 决策：未改变协议、数据模型、发布模型或许可证边界，因此不新增 ADR。

### 2026-11-10（GitHub 仓库初始化与首次发布）

- 范围：将当前工作区纳入 Git，并发布到 `https://github.com/bitallin/Crystal_Mir`。
- 改动：初始化 `main` 分支，配置 SSH 远程 `git@github.com:bitallin/Crystal_Mir.git`，首次提交当前源码、文档、脚本和必要资源文件。
- 排除：`.external/`、`artifacts/`、`Build/`、各项目 `bin/`/`obj/` 与误生成的 `--help` 发布目录不纳入提交。
- 验证：首次提交前检查待提交文件数量、体积、敏感信息和忽略规则；远程为空仓库，SSH 认证可用。
- 未验证：GitHub Actions 首次运行结果待推送后确认；外部资源仍按 `scripts/fetch-external.sh` 单独准备。
- 决策：仅发布现有代码和方法，不改变架构、协议、数据模型、发布模型或许可证边界，因此不新增 ADR。

### 2026-11-10（M1 快照/PostgreSQL 旧库重建）

- 范围：完成从活动 JSONL 快照或 PostgreSQL 最新 `imported` 批次重建旧实体，并导出可被当前 `LoadDB()` 读取的 `Server.MirDB`，覆盖纯控制面回滚路径。
- 新增 `LegacyContentRebuilder`：通过反射恢复 `ItemInfo`、`MonsterInfo`、`MapInfo`、`NPCInfo`、`QuestInfo`、`MagicInfo`、`GameShopItem`、`ConquestInfo` 和 `DragonInfo`，解析 `$ref` 引用并校验 `respawn` 投影。
- 快照元数据：`manifest.legacy` 增加实体列表顺序、全局索引、`RespawnTick` 和 `GTMap` 字段，保证重建结果不只语义一致，还能保持原始二进制顺序。
- CLI：新增 `--snapshot <dir> --export <path>` 和 `--from-postgres <connection> --export <path>`；新增 `scripts/rebuild-db.sh`，支持快照和 PostgreSQL 两种来源。
- PostgreSQL 读取：新增最新导入批次查询和 `content_entity` 读取，恢复完整 manifest、实体 JSON 和批次 ID；重建仍执行冷回读语义校验。
- 测试：`scripts/verify-content.sh` 增加快照重建、重建库再导入和逐字节比较；`scripts/test-control-api.sh` 增加从 PostgreSQL 导入批次重建并直接导出比较。
- 验证：黄金样本快照重建文件与直接导出文件逐字节一致，大小 548,567 字节，SHA256 均为 `3775771c893d95f490bbb54b868650c91f7200e8d268d356a9c22d0a7e11284a`；本地 PostgreSQL 12 的 `--from-postgres` 重建结果同样逐字节一致；`./scripts/verify-content.sh` 和 `./scripts/test-control-api.sh` 均通过。
- 未验证：真实 Windows 旧服务端启动重建库、PostgreSQL 16 staging、资源/脚本导入、超大内容集性能和并发导出。
- 决策：保持现有二进制格式、内容快照格式和发布模型，不新增 ADR。

### 2026-11-10（M1 地图关系与掉落只读 API）

- 范围：把 `Envir/Drops` 文本接入内容快照，并为地图拓扑、刷新点、传送门、NPC 和怪物掉落建立只读关系 API。
- 导入器：新增 `--content-root <dir>`，按怪物 `DropPath`（为空时使用怪物名称）读取 `Envir/Drops/*.txt`；新增 `drop` 实体，内容键为 `drop.monster.<Index>`，保存怪物摘要、文件路径和解析后的 `DropInfo`，物品引用继续使用 `$ref`。
- 兼容性：`drop` 不写回旧二进制数据库；快照重建和导出验证会过滤掉 `drop` 等文件型内容，黄金库三条重建路径仍逐字节一致。
- Control API：新增 `GET /api/v1/content/map/{localIndex}/relations`，返回地图摘要、传送/入口出口、目标地图、安全区、NPC 和分页刷新点；新增 `GET /api/v1/content/monster/{localIndex}/drops`，返回掉落路径、掉落内容键、物品引用和物品名称。
- 查询边界：地图关系按 `data->>'mapIndex'` 读取刷新点、按 `data->>'MapIndex'` 读取 NPC，分页参数 `respawnPage`/`respawnPageSize` 校验为 1～500；关系查询仍直接解析 `jsonb`，尚未建立规范化投影表。
- 测试：新增 `tests/fixtures/content/Envir/Drops/Misc/ArcherGuard.txt`；`scripts/verify-content.sh` 覆盖掉落导入、含掉落快照重建和二进制一致性；`scripts/test-control-api.sh` 覆盖地图 1 的 72 条传送、6 个安全区、46 个 NPC、360 个刷新点分页、怪物 1 的 GreenPoison 掉落和非法分页参数。
- 验证：`./scripts/verify-content.sh` 通过；Control API 在临时 PostgreSQL 12 数据库上完整集成通过（run 1，11 类内容，含 `drop`）；API 与导入器 Release 构建通过。
- 未验证：全量 Jev 掉落导入的条目性能、分组掉落样本、NPC 脚本/配方关系、PostgreSQL 16 staging；Control.Web 真实关系页接入已在 2026-11-11 完成。
- 决策：复用现有不可变快照、通用 `content_entity` 和只读 API 模型，未改变数据库 schema、协议或发布边界，因此不新增 ADR。

### 2026-11-11（M1 Control.Web 真实地图/掉落关系接入）

- 范围：把现有地图关系和怪物掉落只读 API 接入 `Crystal.Control.Web`，并在实时模式中抑制按旧索引猜测的演示关系。
- API 客户端：新增 `getMapRelations` 和 `getMonsterDrops`，支持指定批次、刷新点页码和页大小；关系数据按批次与地图索引缓存，切换批次或地图时失效。
- 地图拓扑/世界关系：新增“真实批次关系”区，加载当前批次前 200 张地图，支持按地图本地索引切换；展示地图摘要、传送/入口出口、安全区、NPC、分页刷新点和怪物名称，点击刷新点可按需读取 `monster/{localIndex}/drops` 并打开掉落明细。
- 内容详情：实时模式下地图和怪物抽屉分别读取真实关系与掉落；地图抽屉展示传送、安全区、NPC、刷新点摘要，怪物抽屉展示掉落路径、顶层条目和分组/物品明细，不再展示演示关系。
- 查询边界：刷新点按 50 条分页；地图关系页未做全量地图搜索，坐标画布和运行时在线人物仍使用原型数据；关系响应仍直接消费 `jsonb` 查询结果，没有新增投影表。
- 测试：`scripts/test-control-web.sh` 增加地图 1 关系抽屉（360 个刷新点）、怪物 1 的 `GreenPoison` 掉落、世界地图真实关系的 72 条传送 / 6 个安全区 / 46 个 NPC / 360 个刷新点，以及按需掉落弹窗断言。
- 验证：带临时 PostgreSQL 12 库运行 `./scripts/ci.sh` 通过（全量构建、358 项旧服务端回归、内容快照/二进制往返、区服冒烟、Control API）；`./scripts/test-control-web.sh` 最终版本通过完整 API + Playwright 集成；全部 JavaScript 通过 `node --check`，`git diff --check` 通过。
- 未验证：全量 463 张地图选择、地图尺寸/阻挡资源接入、分组掉落和大批量掉落的性能、PostgreSQL 16 staging。
- 决策：仅消费已有只读契约并补充前端状态与测试，未改变数据库 schema、协议或发布模型，因此不新增 ADR。

### 2026-11-11（M1 全量地图目录与检索）

- 范围：补齐 Control.Web 只能选择前 200 张地图的缺口，让实时地图关系区覆盖当前批次全部 463 张地图。
- Control API：新增 `GET /api/v1/content/map/catalog?importRunId=&page=1&pageSize=500&q=`，只返回地图摘要，不携带完整地图 JSON；`pageSize` 范围 1～500，`q` 支持内容键、`Title`、`FileName` 模糊匹配和本地索引精确匹配。
- Control.Web：关系区改为通过地图目录加载全部摘要，支持名称、文件名、内容键和索引检索；选择 200 号以后地图可正常加载关系。切换地图时会丢弃过期请求结果，目录读取失败时提供重试入口。
- 测试：`scripts/test-control-api.sh` 校验目录总数 463、首项为 `map.1`、搜索命中 `map.1` 和非法 `pageSize=501` 返回 400；`scripts/test-control-web.sh` 校验 463 个选项、选择 `map.300`、按 `BichonProvince` 过滤地图以及原有关系/掉落面板。
- 验证：Control API 集成测试在临时 PostgreSQL 12 库通过；Control Web Playwright 集成通过；JavaScript `node --check`、相关 Shell `bash -n` 和 `git diff --check` 通过。
- 未验证：地图尺寸、阻挡资源和真实坐标画布；大于 500 张地图时前端顺序分页加载的延迟；PostgreSQL 16 staging。
- 决策：新增只读目录端点，不改变 `content_entity` schema、协议或发布模型，因此不新增 ADR。

### 2026-11-11（M1 配方与 Buff 内容链路）

- 范围：补齐 M1 中配方文本和启动时 Buff 定义未进入快照/PostgreSQL 的缺口，并打通控制面只读关系。
- 导入器：新增 `RecipeContentImporter` 和 `BuffContentImporter`；`--content-root` 在导入 `Envir/Drops` 后按文件名顺序导入 `Envir/Recipe/*.txt`，Buff 从 `BuffInfo.Load()` 始终导入。配方保存产出、材料、工具、成功率、金币和等级/职业/性别/Flag/Quest 条件。
- 快照：黄金样本新增 59 个 Buff；使用测试内容根时新增 1 个掉落和 1 个配方，测试批次从 9,735 增至 9,795 个实体。配方和 Buff 不参与旧二进制重建，导出与冷回读语义校验仍保持逐字节一致。
- Control API：新增 `GET /api/v1/content/recipe/{localIndex}/relations`，解析产出、材料、工具到 `item.<Index>` 内容键和名称；通用内容接口现可直接查询 `recipe` 和 `buff`。
- Control.Web：配方与 Buff 页面切换到当前真实批次；配方详情抽屉读取真实产出/材料/工具和条件，Buff 展示类型、叠加、属性、图标与可见性。
- 测试：`verify-content.sh` 校验 Buff/配方计数和字段；`test-control-api.sh` 校验 9,795 实体、13 类内容、Buff 列表和配方关系；`test-control-web.sh` 校验真实配方/Buff 页面及配方抽屉。
- 验证：`CRYSTAL_DATABASE_URL=... ./scripts/ci.sh` 通过（全量构建、358 项旧服务端回归、内容快照/二进制往返、区服冒烟、Control API）；`test-control-web.sh` Playwright 集成通过；收尾阶段再次确认 Control.Web README、Control API 未实现项和文档日期一致，并重跑 `verify-content.sh`、`test-control-web.sh`（含 Control API 13 类内容）、JavaScript `node --check`、Shell `bash -n` 和 `git diff --check`，全部通过。
- 未验证：全量 Jev 79 个配方在 PostgreSQL 16 staging 的导入性能；配方条件与运行时 `CanCraft` 的端到端行为；NPC 脚本和资源导入。
- 决策：复用现有 `content_entity` 和只读契约，仅新增关系端点，未改变数据库 schema、协议或发布模型，因此不新增 ADR。

### 2026-11-11（M1 NPC 脚本与资源元数据导入）

- 范围：补齐 M1 中 NPC 脚本和地图/预览资源未进入快照/PostgreSQL 的缺口，并打通 NPC→脚本只读关系。
- 导入器：新增 `ScriptContentImporter` 和 `AssetContentImporter`。`Envir/NPCs/**/*.txt` 按相对路径排序导入，文本换行统一为 LF，保存 SHA256、大小、行数、页面、`#INCLUDE`/`#INSERT`/`CALL` 和匹配的 NPC 索引；`Maps/**/*.map` 与 `Envir/Previews` 图片只保存路径、类型、大小和 SHA256 元数据，不复制二进制资源。
- 快照：测试内容根新增 1 个脚本和 1 个资源元数据，批次从 9,795 增至 9,797 个实体，`contentHash` 为 `e3542007913c96e04bd0a238e13f9f0484519fa9b51f27ae5643001ba4e3404e`；黄金样本仍为 9,793 个实体和 `1c6b6134...`。脚本、资源、掉落、配方和 Buff 均不参与旧二进制重建，导出与冷回读仍逐字节一致。
- Control API：通用内容接口现暴露 15 类实体；地图 NPC 关系增加可选 `script` 引用，新增 `GET /api/v1/content/npc/{localIndex}/script`，NPC 不存在返回 404，NPC 存在但无脚本返回 `found=false`。
- Control.Web：新增 `npc-scripts` 内容域，地图 NPC 表展示脚本键、路径、行数并可打开脚本抽屉；Web 共 14 个内容域，其中 12 类读取真实批次，`asset` 仅通过通用 API 查询、暂无独立 Web 域。
- 测试：`scripts/verify-content.sh` 校验脚本/资源计数、脚本字段和哈希；`scripts/test-control-api.sh` 校验 9,797 实体、15 类计数、脚本关系与资源查询；`scripts/test-control-web.sh` 校验脚本列表、脚本抽屉和地图 NPC 脚本关系。
- 验证：`CRYSTAL_DATABASE_URL=... ./scripts/ci.sh` 通过（全量构建、358 项旧服务端回归、内容快照/二进制往返、区服冒烟、Control API 15 类实体集成）；`SKIP_API_SETUP=1 ./scripts/test-control-web.sh` Playwright 集成通过；Shell `bash -n`、全部 JavaScript `node --check` 和 `git diff --check` 通过。
- 未验证：脚本 DSL 的 AST/编译校验和运行时执行；图库、音效、特效、客户端补丁等其他资源二进制；地图尺寸/阻挡资源；PostgreSQL 16 staging 和 Windows 客户端/区服运行冒烟。
- 决策：复用现有 `content_entity`、只读契约和资源元数据模型，未改变数据库 schema、协议或发布模型，因此不新增 ADR。

### 2026-11-11（M1 脚本 AST、地图解析与资源完整性下载）

- 范围：补齐脚本 AST/编译校验、地图尺寸/阻挡/门格解析和资源文件服务，让 Control API 与 Control.Web 能展示并下载经过完整性校验的真实资源。
- 新增 `NpcScriptParser`/`NpcScriptAst`：解析页面、块、命令、按钮和跳转引用，校验重复页面、缺失跳转目标、未知指令和空页面；脚本实体新增 `ast` 与 `validation`，测试脚本为 11 页、26 条命令、0 error、1 warning，`compiledSha256` 为 `c0e3f43676a6d5bd978d57c2586636efa404e17fd84014102a3d7d377ecdf8ff`。
- 新增 `MapAssetParser`/`MapAssetMetadata`：支持旧地图格式 0/1/2/3/4/5/6/7/100，校验尺寸和文件长度并统计可走、阻挡和门格；用 Jev 全量 1,698 张地图验证，`bad=0`，格式分布为 `0:237`、`1:1441`、`3:1`、`5:6`、`100:13`。
- 资源样本：删除文本假地图，新增确定性二进制 `tests/fixtures/content/Maps/0.map`（10×10 v0、1,252 字节、99 可走、1 阻挡、1 门格、SHA256 `3ab00f38…237f`）。
- Control API：新增 `GET /content/script/{localIndex}/validation` 和需要 `asset:read` 的 `GET /content/asset/{localIndex}/file`；资源下载校验配置根目录、路径穿越、文件大小和 SHA256，返回 Content-Type/ETag/缓存头；地图关系响应新增 `asset` 元数据。
- Control.Web：新增 `resources` 内容域，地图/资源列表展示格式、尺寸和阻挡统计；脚本抽屉展示 AST、错误/警告和编译哈希；资源抽屉可下载文件并在浏览器端复核 SHA256。
- 配置：`ControlApi:AssetRoot` / `CRYSTAL_CONTROL_ASSET_ROOT` 指定资源根目录；未配置时下载接口返回明确错误，不影响其他只读内容。
- 快照：测试内容根仍为 9,797 个实体，最终 `contentHash` 为 `1b93509c81d7c4839f009411d46c3e42f3596c9e7bab368cf711d2dd8ec22291`；脚本、资源、掉落、配方和 Buff 均不参与旧二进制重建。
- 测试：`scripts/verify-content.sh` 校验脚本 AST、编译哈希、地图元数据和资源哈希；`scripts/test-control-api.sh` 校验脚本校验端点、地图资源元数据和下载字节；`scripts/test-control-web.sh` 校验脚本 AST 抽屉、地图尺寸/阻挡信息和资源下载/哈希复核。
- 验证：`./scripts/build.sh`、`./scripts/test.sh`（358 项安全回归 + 30 项内容快照检查）、`./scripts/verify-content.sh`、`./scripts/inspect-db.sh`、PostgreSQL 12 上的 `./scripts/test-control-api.sh` 和 `SKIP_API_SETUP=1 ./scripts/test-control-web.sh` 全部通过；Shell `bash -n`、全部 JavaScript `node --check` 和 `git diff --check` 通过。
- 未验证：PostgreSQL 16 staging（当前环境仅 12.22）、Windows 客户端/区服运行冒烟、图库/音效/特效/客户端补丁等其他资源二进制，以及脚本 DSL 在旧运行时中的端到端执行。
- 决策：复用现有 `asset`/`script` 实体和只读契约，未改变数据库 schema、协议或发布模型，因此不新增 ADR。

### 2026-11-11（M1 进展评估与回归验证）

- 范围：核对当前工作区、M1 里程碑交付物和文档口径，并重跑本地可执行验证。
- 验证：`scripts/build.sh`、`scripts/test.sh`（358 项安全回归 + 30 项内容快照检查）、`scripts/verify-content.sh`、`scripts/inspect-db.sh`、PostgreSQL 12 上的 `scripts/test-control-api.sh`（15 类实体）和 `SKIP_API_SETUP=1 scripts/test-control-web.sh`、`scripts/zone-smoke.sh` 均通过；Shell `bash -n`、JavaScript `node --check` 和 `git diff --check` 通过。客户端交叉构建仅有既有 `WindowsBase` 版本冲突警告。
- 结论：内容导入、快照往返、只读 API、Control.Web、Linux 区服和本地回归已形成闭环，M1 功能链路接近收口；但当前 29 个修改文件和 12 个未跟踪文件尚未提交，远端 `main` 仍停在 `eedb980`。
- 发现（评估时）：`.github/workflows/ci.yml` 只监听 `master` 分支的 push，而当前主分支为 `main`，主分支直接推送不会触发 CI；M1 计划中的 `Crystal.GameCore`、`Crystal.Agent.Contracts`、Compose（PostgreSQL/Redis/MinIO/API/Web）和内容包签名仍未落地，因此 97% 仅代表内容功能链路完成度，不代表 M1 交付物全部完成。CI 触发与内容包基本签名已在后续条目修复。
- 未验证：PostgreSQL 16 staging、Windows 客户端登录/选角/进服、systemd 实际安装与开机启动、全量 Jev 掉落/配方/脚本/资源导入性能。
- 下一步：提交并推送当前工作区、修正 CI 分支触发，再推进 PG16 全量导入与 M2 内容写入/发布闭环。

### 2026-11-11（M1 内容快照签名与 CI 修复）

- 范围：补齐内容包基本签名/验签能力，并修复主分支 push 不触发 CI 的问题。
- 签名：新增 `ContentSnapshotSignature` 和 `ContentSnapshotSigner`。使用 ECDSA P-256 + SHA-256、IEEE P1363 固定宽度签名；签名覆盖 canonical manifest 和 `algorithm`/`keyId`/`signedAt` 元数据，`contentHash` 与旧二进制重建不受影响。
- CLI：新增 MIT 工具 `tools/Crystal.ContentTool`，提供 `sign`/`verify`，只依赖 `Crystal.Content`，可用于生成签名快照并在发布前校验实体哈希、内容哈希和签名。
- CI：`.github/workflows/ci.yml` 的 push 触发改为 `main`/`master`，并增加 `workflow_dispatch`；内容签名已加入 `scripts/verify-content.sh`，包含正确密钥、错误 key id、错误公钥和 manifest 篡改场景。
- 测试：`./scripts/ci.sh` 通过（全量构建、358 项安全回归、39 项内容快照检查、内容签名/快照重建/二进制往返、区服冒烟、Control API 15 类实体）；`SKIP_API_SETUP=1 ./scripts/test-control-web.sh` Playwright 集成通过。
- 未验证：KMS/HSM 托管私钥、Control API 发布审批与签名编排、ZoneServer/Agent 强制验签、PostgreSQL 16 staging 和 Windows 客户端运行冒烟。
- 决策：新增 ADR 0005，记录 ECDSA P-256 当前算法、canonical 签名载荷和未来 Ed25519/KMS 迁移条件。

### 2026-11-13（M1 内容发布生命周期 API）

- 范围：补齐从不可变导入批次到内容发布的 Control API 闭环，复用现有 `content_release`/`content_release_entity` schema 和 `Crystal.Content` 快照/签名能力，不新增数据模型。
- 快照：`ContentSnapshotWriter` 增加内存构建和 `WriteBuild` 能力，发布创建时即可计算稳定的 `contentHash`；目录写入仍保持原子发布，旧导入/重建路径复用同一构建逻辑。
- API：新增 `GET /releases`、`GET /releases/{id}`、`GET /releases/{id}/manifest`、`POST /releases`、`/validate`、`/compile`、`/sign`、`/publish`。`draft -> compiled -> signed -> published` 使用条件更新；校验失败持久化为 `failed`，签名前重新校验产物哈希，发布前强制验签，重复发布幂等。
- 权限与审计：创建/校验/编译/签名要求 `release:write`，发布要求 `release:publish`，读取要求 `release:read`；发布创建、校验、编译、签名和发布均写入审计事件。
- 配置：新增 `ControlApi:ReleaseRoot`、`ReleaseSigningPrivateKeyPath`、`ReleaseSigningPublicKeyPath` 和 `ReleaseSigningKeyId`，签名私钥未配置时 `sign`/`publish` 返回 503。
- 测试：`scripts/build.sh` 全量构建通过（客户端/旧宿主仅有既有警告）；`scripts/test.sh` 通过 358 项安全回归和 39 项内容快照检查；`scripts/verify-content.sh` 黄金内容导入、签名、快照重建和旧二进制往返通过；`scripts/test-control-api.sh` 在 PostgreSQL 18.3 上通过 15 类实体、认证/RBAC/审计、作者创建/校验/编译/签名、审批人发布、越权 403、重复版本 409、发布前未签名 409 和重复发布幂等验证。
- 未验证：Control.Web 发布中心尚未接入真实发布 API；可编辑内容草稿/修订模型、发布回滚、KMS/HSM 托管密钥、Zone Agent 强制验签和 PostgreSQL 16 CI job 待做；本轮未运行 Playwright Web 集成和区服冒烟。
- 决策：复用既有发布表和 ADR 0005 签名格式，未改变协议或数据模型，因此不新增 ADR。

## 4. 决策记录

| 日期 | 决策 | ADR |
| --- | --- | --- |
| 2026-11-09 | 控制面与数据面分离 | `docs/adr/0001-control-plane-data-plane.md` |
| 2026-11-09 | PostgreSQL 与内容快照 | `docs/adr/0002-postgresql-and-content-snapshots.md` |
| 2026-11-09 | 保留现有协议并引入边缘网关 | `docs/adr/0003-legacy-protocol-with-gateway.md` |
| 2026-11-09 | GPL 边界与代码隔离 | `docs/adr/0004-gpl-boundary.md` |
| 2026-11-11 | 内容快照签名格式 | `docs/adr/0005-content-signature-format.md` |

## 5. 阻塞与风险

| 类型 | 内容 | 影响 | 处理 |
| --- | --- | --- | --- |
| 环境 | 当前环境是 Linux，无法运行 Windows 客户端和 WinForms 宿主 | 只能完成交叉构建，不能做客户端运行冒烟 | 准备 Windows 环境或 Windows CI runner |
| 资源 | 客户端 `Data/Map/Sound` 和 `.lib` 图库缺失 | 无法跑通完整客户端 | 确认合法来源并加入外部资源清单 |
| 平台 | 客户端和 WinForms 宿主运行依赖 Windows | Linux 已验证区服运行，但客户端登录和进服仍需 Windows | 拆分构建矩阵，Windows runner 做客户端运行验证 |
| CI | CI 已覆盖 `main`/`master` push，但 PostgreSQL 16 job 尚未在远端实际运行验证 | 无法确认 GitHub Actions 环境与本地结果一致 | 推送后检查 Actions 结果并保留日志 |
| 交付口径 | M1 计划中的 `Crystal.GameCore`、`Crystal.Agent.Contracts` 骨架和 Compose（PostgreSQL/Redis/MinIO/API/Web）尚未落地 | 内容链路虽接近完成，但按里程碑交付物口径仍不能判定 M1 完成 | 作为 M1/M2 收口项排期，并在进度表中区分功能链路与交付物完成度 |
| 许可 | `Crystal.MapEditor` 未发现明确许可证 | 不能随发布包分发 | 联系维护者、确认许可证或替换工具 |
| 许可 | 服务端 GPL-3.0-only、素材版权 | 分发和商业化受限 | M0 完成合规审查并保留源码归档流程 |
| 技术 | `Envir`/`Map` 耦合高 | 迁移和热重载风险 | 绞杀者模式 + 黄金样本测试 |

## 6. 下一步

1. 补齐 M1 交付物口径：`Crystal.GameCore`、`Crystal.Agent.Contracts` 骨架和 PostgreSQL/Redis/MinIO/API/Web Compose。
2. 将 Control.Web 发布中心接入真实 `/releases` API，补发布列表、校验报告、manifest/签名详情和审批操作。
3. 为 M2 设计可编辑内容草稿/修订模型和写入 API，明确草稿与已导入不可变批次的边界；涉及数据模型变更，先补 ADR。
4. 在 PostgreSQL 16 staging 验证全量导入与发布链路，确认 GitHub Actions PG16 job 稳定，并补发布回滚路径。
5. 在 Ubuntu staging 主机实际安装 systemd 服务，验证开机启动、日志轮转、端口防火墙和进程恢复。
6. 在 Windows 环境启动 `Client`，连接 Linux 区服完成登录、选角、进服、移动、打怪冒烟测试。
7. 清点客户端 `Data/Map/Sound` 的合法来源，补齐外部资源清单。

## 7. 更新规则

- 每次实质性工作结束，在“工作日志”追加一条记录。
- 更新“当前状态”和“里程碑状态”，不要只写“进行中”。
- 新决策写入“决策记录”并新增 ADR。
- 阻塞项解决后移到历史记录或标注已解决。
- 记录验证方式和未验证项，不能把“未测试”写成“已完成”。

