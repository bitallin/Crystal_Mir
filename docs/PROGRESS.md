# 项目进度台账

> 最后更新：2026-11-19
> 当前阶段：M1、M2 已完成本轮范围；M3 服务端控制闭环、真实运行时指标、指标历史/健康评估、Control.Web 区服闭环和客户端签名补丁核心已完成本轮研究项目范围（Agent 注册/心跳/命令、发布包下载验签、staging、进程级 activate/rollback、ready/指标文件、失败恢复、CLI/systemd、真实区服总览/详情、趋势告警与五类命令、签名 manifest、文件级增量、事务应用与回滚）。浏览器直玩并行轨道已形成 W0 垂直闭环，W1/W2 的客户端、网关、资源管线、HUD 和核心业务链路已落地。远端 CI run 15 已全绿；已依次修复 `DOTNET` 命令名解析、Control API 镜像依赖、runner 缺少 `rg` 和 MinIO 镜像下架问题。二进制 delta、灰度/CDN、launcher 自更新、进程内热切换、玩家状态迁移、外部 staging/systemd 验证、Windows 冒烟、真实素材动画、Safari/真机/性能和 M0 外部阻塞仍未完成；授权、许可证和素材合规按要求本轮跳过

## 1. 当前状态

| 项目 | 状态 | 说明 |
| --- | --- | --- |
| 技术文档体系 | 已完成 | 架构、ADR、开发、路线图、进度台账已建立 |
| 架构决策 | 已完成初版 | ADR 0001～0018（0016 提议中；0017/0018 已接受） |
| 构建基线 | 已完成 | Linux 安装 .NET 8 SDK；旧服务端/客户端/内容库/API/工具/GameCore/Agent contracts/ZoneAgent 构建成功；安全回归、内容快照、区服指标和契约边界检查通过 |
| Linux 区服宿主 | 已完成初版 | `Crystal.ZoneServer` 控制台宿主、日志、SIGTERM 优雅退出、端口/线程环境变量覆盖、内容路径大小写与分隔符兼容；legacy Jev 内容加载 463 张地图，签名发布模式加载 463 地图、1,628 物品、555 怪物、375 NPC、154 任务并完成冒烟 |
| Ubuntu 部署 | 已完成初版 | 自包含 `linux-x64` ZoneServer 发布约 74 MB；提供 legacy `crystal-zone.service`、`crystal-zone-agent.service`、安装/发布脚本和两份运行手册；容器内未执行真实 systemd 安装和开机自启演练 |
| 外部资源 | 已完成真实客户端素材准备 | `Crystal.Database`、`Crystal.MapEditor` 已拉取；Jev `Client With Monk` 已解压到 `.external/jev-client/client/Client`，含 2,304 个 `.lib`、624 个音效文件和 479 张可覆盖地图；真实素材和生成物不入库 |
| 浏览器直玩 | W0 已闭环，W1/W2 部分完成 | 独立 `web-client/`（TypeScript/Vite/PixiJS v8/Preact）、`Crystal.Web.Gateway`、`Crystal.WebAssets`、WebAssetCompiler、协议样本和 Playwright 冒烟已建立；登录、选角、进图、移动、基础战斗、拾取、聊天、背包/装备、背包内物品移动、NPC/商店、角色创建/删除和重连已有实现；真实 479 张地图与 45 个图库已完成确定性编译，真实 V5/V1/V100 地图经严格资源加载断言通过浏览器三上下文冒烟，真实 ZoneServer + Gateway 的 `ClientVersion` 门禁已联调；临时真实账号已通过浏览器完成登录、创建角色和进入 `3` 号真实地图，但大图库加载期间的会话超时仍阻断浏览器移动验收；真实角色/怪物/NPC 动画、门/光照/音效、Safari/真机和性能验收未完成 |
| Web 资源管线 | 已完成初版，真实图库已回归 | `.lib` v2/v3 与 `.map` v0～v7/v100 解析、PNG 图集/遮罩、32 字节 `cells.bin`、`meta.json`/`manifest.json`、地图库映射、确定性哈希；编译器强制 `--build-hash`，缺失图库或重复 ID 直接失败；Jev 1,698 张真实地图全部解析，真实 479 张地图 + 45 个图库两次编译 manifest 与逐文件字节一致；真实音频转码、角色/怪物/NPC 图库动画仍未实现 |
| 数据迁移 | 已完成初版 | 二进制导入器、JSONL 快照、PostgreSQL schema、旧二进制导出、快照/PostgreSQL 到旧库重建完成，黄金样本逐字节一致；掉落、配方、NPC 脚本和地图/预览资源元数据可通过 `--content-root` 导入，启动时 Buff 定义始终导入；脚本已有 AST/编译校验，地图资源已解析格式、尺寸、可走/阻挡/门格；其他资源二进制待做 |
| 内容快照 | 已完成初版 | `Crystal.Content`（MIT）+ 9,793 个核心实体（含 59 个 Buff）+ 可选 `drop`/`recipe`/`script`/`asset` 实体 + 哈希校验 + ECDSA P-256 签名/验签 + 回归测试；测试内容根批次为 9,797 个实体，最终 `contentHash` 为 `1b93509c81d7c4839f009411d46c3e42f3596c9e7bab368cf711d2dd8ec22291` |
| 参考文档生成 | 已完成初版 | 25 个内容实体、432 个协议包、274 个配置项已生成 |
| CI 基线 | 已完成 | run 15（`59cd60e`）的 `build-test`、`control-api-integration` 和 `compose-stack` 全部 success；已修复 `DOTNET=dotnet` 解析、Control API 镜像漏拷 `Crystal.Agent.Contracts`、`rg` 缺失和 MinIO 镜像下架 |
| 控制面 | M2 完成，M3 区服闭环完成 | `Crystal.Control.Api` 内容查询暴露 15 类实体；10 类 Schema 编辑器、引用选择、两级校验、JSONL 导入导出、脚本校验、地图关系 CRUD 和地图预览已完成；发布 API 支持不可变版本、校验、编译、ECDSA 签名、发布和双版本回滚；新增 `zone`/`zone_command`、注册/心跳/命令/租约/结果和发布包 ZIP 下载；新增 `zone_metric_sample`、指标窗口查询、汇总和实时健康告警；`Crystal.Control.Web` 已接内容编辑、关系、预览、发布中心、账号、审计以及真实区服总览/详情、趋势告警、命令历史和五类生命周期操作 |
| 认证与审计 | 已完成初版 | Argon2id、短时 JWT + 刷新令牌、RBAC、账号创建与生命周期、会话撤销、最后管理员保护、只追加审计和管理端接入完成 |
| 热更新 | 服务端 Agent 闭环已完成，客户端核心闭环已完成 | ZoneServer 可验签并消费已发布快照、隔离重建旧库和物化内容；legacy 启动模式保留。`Crystal.ZoneAgent` 已实现注册、心跳、命令领取、下载验签、staging、进程级 activate/rollback/reload/stop、ready/指标文件读取和失败恢复。客户端新增 MIT `Crystal.Patcher`/`Crystal.PatcherTool` 和旧 WinForms Launcher 可选分支：ECDSA 签名 manifest、SHA256 文件级增量、Range 续传、事务应用、崩溃恢复和回滚；二进制 delta、灰度/CDN、launcher 自更新、进程内热切换和玩家状态迁移未实现 |
| 区服 Agent | 已完成本轮闭环 | 单区单 Agent 单 ZoneServer 子进程；命令结果本地持久化并可重报；意外退出按 2/5/10/30 秒退避重启；ZoneServer 每 100 ms 采样、60 秒窗口、每秒原子写指标文件，Agent 校验 schema/zoneId/PID/15 秒时效后上报真实玩家数、Tick P95、错误率和重启失败次数，失效时归零并标记来源；Control API 同事务幂等持久化心跳样本 |
| 运营能力 | 部分完成 | 开区/合区/支付尚未实现；指标历史、趋势查询和基础健康告警已完成 |
| 许可证清单 | 进行中 | 代码/外部资源清单已建立；授权、许可证和素材合规按要求本轮跳过，MapEditor 和素材授权待确认 |

## 2. 里程碑状态

| 里程碑 | 状态 | 完成度 | 备注 |
| --- | --- | --- | --- |
| M0 基线与合规 | 进行中 | 80% | 构建、资源、黄金样本完成；Windows 运行冒烟和素材授权待做 |
| M1 架构骨架与数据底座 | 已完成 | 100% | 内容快照/导入/重建、查询 API、账号/JWT/RBAC/审计、草稿/修订、发布/回滚、GameCore/Agent 契约、Compose 栈和 PG16 集成均已落地；真实区服热切换按计划留给 M3 |
| M2 内容后台 MVP | 已完成 | 100% | 10 类内容编辑器、引用选择、两级校验、JSONL 导入导出、脚本校验、地图关系 CRUD、地图预览、签名发布启动消费已完成；Agent 与进程级换版已由 M3 接手，授权/许可证和灰度仍按范围外处理 |
| M3 热更新与区服 Agent | 进行中 | 本轮服务端/Web/客户端核心范围完成 | 已完成 Agent 注册/心跳/命令、下载验签、staging、进程级 activate/rollback/reload/stop、ready/指标文件、失败恢复、CLI/systemd、PG16 E2E、真实指标、指标历史/健康评估、Control.Web 区服闭环，以及客户端签名 manifest、文件级增量、事务应用和回滚；二进制 delta、灰度/CDN、launcher 自更新、进程内热切换、玩家状态迁移、外部 staging/systemd 验证、告警通知、远端指标汇聚和 Windows 冒烟仍未完成；授权/许可证按要求跳过 |
| M4 运营 MVP | 未开始 | 0% | 依赖 M2/M3 |
| M5 生产加固 | 未开始 | 0% | 依赖 M4 |
| M6 通用化与生态 | 未开始 | 0% | 长期 |

## 3. 工作日志

### 2026-11-19（真实账号链路探测、心跳与配置加载修复）

- 范围：在真实 Jev `Server.MirDB`、真实 ZoneServer、真实 Gateway、真实浏览器和真实图库上推进登录/选角/建角/进图/移动闭环；真实素材只落在 `.external/`，不提交生成物。
- 启动修复：`Settings` 原先在静态初始化时固定打开 `./Configs/Setup.ini`，会早于 ZoneServer 切换 `--content-root`，导致区服配置可能回退默认值。改为 `Load()` 时按当前内容根重新打开配置，`Save()` 在未加载时补建 reader。
- 心跳修复：浏览器 `KeepAlive` 由 60 秒改为 5 秒，低于 legacy 区服默认 10 秒空闲超时；该修复已通过类型检查、109 项 Web 单测和新构建。
- 真实链路：用临时内容副本把测试出生点从缺 `WemadeMir3/Snow/Object1c` 的 `0` 号图移到有完整图库的 `3` 号图，未修改源素材或源数据库。临时账号经 Gateway 创建后，浏览器完成登录、创建角色和进入 `3` 号真实地图；该地图使用 26 个真实图库、约 1.1 GiB 资源。
- 阻断：大图库加载后会话仍以 `Connection timed out` 结束，浏览器移动指令发出但服务端会话已断开。当前判断为图库哈希/纹理准备期间主线程停顿压住 5 秒心跳，或 legacy 超时配置仍存在未定位路径；移动闭环不算通过。
- 严格回归：用新构建哈希 `sha256:96004c96cfc6bd7b5479da94ed75d32927ccd2a0745aa0462401351263a434e4` 重新编译 `3`、`HKR`、`0100`、`CL00` 与 26 个真实图库，四张地图均通过桌面/移动横屏/竖屏严格资源加载和 fallback toast 拒绝断言。
- 自动化：`npm run typecheck`、`npm test`（5 个文件、109 项）、`npm run build`、`scripts/test-browser-client.sh`、`scripts/test.sh`（358 项物品安全、39 项内容快照、80 项内容编辑器、Web assets/gateway 等）和 `scripts/check-boundaries.sh` 通过；`git diff --check` 通过。`test.sh` 仍只有 NuGet 漏洞源不可达的既有 `NU1900` 告警。
- 未验证：真实账号浏览器移动、真实角色/怪物/NPC 动画、门/光照/音效、Safari/真机、帧率和 30 分钟内存。
- 下一步：把大图库哈希/准备移出主线程或改为可让出事件循环的增量方案，并补充长加载期间的会话存活回归；通过后再继续真实移动、战斗和 NPC 链路。

### 2026-11-19（真实客户端图库全量编译与严格浏览器回归）

- 范围：把浏览器资源验收从真实 `.map` 推进到真实 `Client/Data` 图库，优先完成资源编译、确定性验证和真实地图加载回归；不改动 `Envir`/`Map` 游戏逻辑，不引入客户端权威，不提交真实素材或生成物。
- 素材：下载 Jev `Client With Monk`（7,543,876,769 字节，SHA256 `9534eb3c…6ae98`）并解压到 `.external/jev-client/client/Client`；`Data/` 约 8.2 GB、2,304 个 `.lib`，`Sound/` 145 MB、624 个文件。按 Jev 活跃地图引用补齐 61 个地图库，最终支持 479 张地图；排除 17 张缺私有/不完整图库的地图，其中 `0`、`2` 缺 `WemadeMir3/Snow/Object1c`（ID `257`），`n0` 缺 `138`。
- 兼容修复：新增 `ClientAssetPath`，为 `--lib`、`--map` 和地图库解析提供大小写不敏感且在无精确匹配时唯一回退的路径解析，拒绝 `..` 路径穿越；修复 Linux 下 `SmTiles.Lib`/`Smtiles.lib`、`Objects27.Lib`/`Objects27.lib` 等真实文件差异。`LibraryReader` 增加 GDI 行/列对齐像素裁剪，修复真实 `SmTiles.Lib` 第 117 帧 4×1 图像被解压为 64 字节填充数据的问题。
- 编译：用当前 `clientBuildHash=sha256:83d8e0e…8c10e2` 对 479 张地图执行 `--all-maps --map-libraries`，生成 45 个地图库、1,506 个文件、2,311,541,814 字节（约 2.15 GiB），格式分布 `1:466`、`5:5`、`100:8`，共 20,895,455 格；无图库缺失告警，`manifest.json` SHA256 为 `b14beec1…cb9e6`。`maplib-0` 单库约 451 MB，是当前最大的真实纹理负载。
- 确定性：用同一 479 张地图、同一 `assetVersion` 和构建哈希重新编译一次，manifest SHA256 相同，`diff -qr` 退出码 0，1,506 个文件逐字节一致。
- 浏览器：新增 `CRYSTAL_BROWSER_REQUIRE_MAP_LIBRARIES=1` 严格模式，等待当前地图全部 `frames.json`、图集 PNG 和遮罩页响应完成并拒绝失败请求；真实 `HKR`（V5）、`0100`（V1）、`CL00`（V100）均在桌面 `1440×900`、移动横屏 `844×390`、竖屏 `390×844` 三上下文通过，且 `maplib-0` 的 451 MB 负载完成加载。
- 版本门禁：`scripts/test-web-version-gate.sh` 使用真实 Jev 内容目录启动 ZoneServer 和 Gateway，当前 Web 构建哈希被接受、错误哈希被拒绝，集成通过。
- 验证：`Tests/WebAssets`、`scripts/test.sh`（358 项物品安全、39 项内容快照、80 项内容编辑器、区服指标/健康/版本、客户端更新、Web assets/gateway 等）、`scripts/check-boundaries.sh`、`npm run typecheck`、`npm test`（5 个文件、109 项）、`npm run build`、`scripts/test-browser-client.sh`、真实资源确定性编译、三张真实地图严格浏览器冒烟和真实区服版本门禁均通过；`git diff --check` 通过。`scripts/test.sh` 仅出现 NuGet 漏洞源不可达的既有网络告警，不影响退出码。
- 未验证：完整真实账号登录/选角/建角/进图/移动、真实角色/怪物/NPC 图集动画、门/混合图块/光照/音效、Safari/iPhone/Android 真机、帧率和 30 分钟内存；剩余 17 张地图未纳入，不能宣称 496 张活跃地图全量视觉覆盖。
- 下一步：用真实账号完成登录/选角/建角/进图/移动回归，再补业务图集动画、音效和 W3 设备/性能/内存验收。

### 2026-11-19（真实地图资源回归与区服 Web 版本门禁）

- 范围：执行浏览器路线图下一步的真实地图资源回归和区服 `clientBuildHash` 门禁联调；不触碰 `Envir`/`Map` 游戏逻辑，不引入客户端权威，不提交真实素材或生成物。
- 真实地图：用 `.external/Crystal.Database/Jev/Maps` 扫描 1,698 张地图，全部解析成功，格式分布 `0:237`、`1:1441`、`3:1`、`5:6`、`100:13`，共 61,255,923 格；发现并修复 Web V1 地图按 17 字节/格读取的错误，真实格式为 15 字节/格，并补 `NormalizeBackBits` 与带文件名的错误上下文。
- 资源编译：编译 Jev 顶层 496 张活跃地图（排除 `Unused` 同名地图），两次构建均为 730 MB、993 个文件、manifest 与逐文件哈希完全一致；`manifest.json` SHA256 为 `141d88aa…abae031`，格式分布 `1:478`、`3:1`、`5:5`、`100:12`，共 23,790,791 格。256 条告警均为真实 `.lib` 图库缺失，浏览器按设计使用地形占位。
- 大小写修复：Linux 下 `*.map` 枚举区分大小写，漏掉 `LABYBUY01.MAP`、`LABYBUY02.MAP`、`SNAKE.MAP`；新增大小写不敏感的 `MapSourceDiscovery`，编译器改用该入口，并补混合大小写与重复 ID 测试。
- 浏览器回归：新增 `scripts/test-web-real-maps.sh`，自动编译真实地图并分别用真实 V1 地图 `0100` 和真实 V100 地图 `0` 跑桌面/移动横屏/竖屏 Playwright 冒烟，全部通过；`scripts/test-browser-client.py` 支持 `CRYSTAL_BROWSER_MAP` 和 `CRYSTAL_BROWSER_ASSET_MANIFEST_URL`，默认真实 fixture 路径不变。
- 版本门禁：新增 `scripts/test-web-version-gate.sh`，用真实 ZoneServer（`CheckVersion=True`、白名单写入 Web 构建哈希）+ 真实 Gateway + WebSocket 验证：正确哈希返回 `ClientVersion.Result=1`，错误哈希返回 `0` 并由区服记录 `Wrong Client Version`；测试需等待区服同 IP 5 秒建连冷却。
- 验证：`Tests/WebAssets`、`Tests/WebGateway`、`npm run typecheck`、`npm test`（5 个文件、109 项）、`npm run build`、`scripts/test-browser-client.sh`、`scripts/test-web-real-maps.sh`、`scripts/test-web-version-gate.sh` 全部通过；`clientBuildHash` 仍为 `sha256:83d8e0e11e9d1adb34fbd332aeaeecc611c111cba0231342ea876e5ebf8c10e2`。
- 未验证：真实 `Client/Data/*.lib`、`Client/Sound`、`--map-libraries` 全量编译、真实角色/怪物/NPC 图集动画、门/混合图块/光照/音效、完整真实账号登录/选角/创建角色/进图/移动、Safari、iPhone/Android 真机、帧率和 30 分钟内存。
- 下一步：准备真实 `Client/Data` 与 `Client/Sound`，用当前构建哈希执行 `--all-maps --map-libraries`，再补完整登录选角进图与动画/门/光照/音效；之后进入 W3 真机与性能验收。

### 2026-11-19（AGENTS 浏览器客户端开发规范，精简版）

- 范围：在浏览器直玩实现基础上，将仓库级 `AGENTS.md` 从详细清单压缩为 52 行执行约束，细节回引 `docs/`，避免规范与文档重复。
- 改动：保留项目定位与工作流程、控制面/GPL/不可变内容边界、原生 Web 客户端 + 字节网关 + 权威区服路线、服务端权威、协议逐字节测试、资源哈希与懒加载、`clientBuildHash` 一致、安全/性能基线和完成定义；删除可由架构、开发、契约文档承载的实现细节。
- 验证：`git diff --check` 通过；人工核对 `AGENTS.md` 与 `docs/product/browser-client-requirements.md`、`docs/architecture/09-browser-client.md`、`docs/development/04-browser-client.md`、`docs/reference/web-client-contracts.md`、ADR 0017/0018 的口径一致。
- 未验证：本次只修改文档，未重新执行构建、浏览器或真机测试；后续实现仍按本台账的浏览器未验证项推进。
- 下一步：按新规范先用真实 `Client/Data`、`Client/Map`、`Client/Sound` 完成资源编译和真实测试区服版本门禁联调，再补角色/怪物/NPC 动画、门/光照/音效与 W3 设备矩阵。

### 2026-11-19（浏览器直玩：Web 客户端、网关、资源管线与版本门禁）

- 范围：按 `docs/roadmap/browser-client.md` 实施浏览器直玩并行轨道，保留旧 TCP 客户端、旧协议和权威区服；不重写 `Envir`/`Map`，不引入客户端权威或云游戏串流。
- Web 客户端：新增 `web-client/`，TypeScript + Vite + PixiJS v8 负责地图/实体/摄像机/伤害数字/聊天气泡，Preact + CSS 负责登录、选角、聊天、背包、装备、NPC、商店和响应式 HUD；桌面支持点击寻路/键盘，手机横屏支持虚拟摇杆和动作按钮，竖屏显示旋转门禁。
- 协议与业务：显式 `PacketCodec` 覆盖登录、选角、进图、移动、攻击、拾取、聊天、物品信息/使用/装备/移除/丢弃/背包移动、NPC 对话/商店、生命/死亡/复活、地图切换和重连；新增 C# 协议样本工具，生成 86 个 fixture，TypeScript 逐字节回放。
- 网关：新增 `Crystal.Web.Gateway`，ASP.NET Core Minimal API + WebSocket/TCP 字节桥；只接受 `crystal-v1` 子协议和二进制帧，默认拒绝缺失/不匹配 Origin，执行 256 KiB 帧上限、180 帧/秒、4 MiB/秒、256 连接、30 秒空闲关闭、背压和结构化指标；Production 启动时拒绝 `sha256:dev`、`dev` 资源版本和通配 Origin。
- 资源：新增 `Crystal.WebAssets` 与 `Crystal.WebAssetCompiler`；严格解析 `.lib` v2/v3 和 `.map` v2/v3，生成 PNG 图集/遮罩、`frames.json`、`meta.json`、32 字节/格 `cells.bin`、manifest 和地图库映射；输出使用显式小端长度编码保证确定性。编译器必须接收 Web `clientBuildHash`，缺失图库、重复 sanitized ID 或缺失构建身份直接失败。
- 版本门禁：Web 构建生成 `dist/client-build.json`；客户端把 `clientBuildHash` 按 UTF-8 写入 `ClientVersion.VersionHash`。区服 `Settings` 保留 `VersionPath` 文件 MD5 白名单，新增 `[General] VersionHashValues` 和 `CRYSTAL_ZONE_VERSION_HASH_VALUES` 字面量白名单；新增 `Tests/ZoneVersionGate` 验证旧 MD5 与新 Web 哈希并存。
- 构建与部署：新增 `scripts/build-web-client.sh`、`scripts/test-browser-client.sh`、Python Playwright 冒烟和 Chromium CI job；Compose 增加 `web-gateway`/`web-client` profile，提供网关/静态站 Dockerfile、Nginx WSS 反代、安全头和不可变缓存配置；本地 Compose 默认 `Development`，生产显式设置 `CRYSTAL_WEB_ENVIRONMENT=Production`；Docker 构建上下文排除 `web-client/node_modules`、`dist` 和本地缓存。
- 本地开发：Vite 开发服务器新增 `/assets/` 静态资源中间件，默认从 `../artifacts/web-assets` 提供编译器产物，支持 `CRYSTAL_WEB_ASSET_ROOT` 覆盖；生产仍由 Nginx/CDN 提供，不改变部署边界。
- 文档：新增/重写 `docs/product/browser-client-requirements.md`、`docs/architecture/09-browser-client.md`、`docs/roadmap/browser-client.md`、`docs/development/04-browser-client.md`、`docs/reference/web-client-contracts.md`、ADR 0017/0018，并同步架构/产品/路线图/参考索引、需求矩阵、里程碑、ZoneServer README、systemd 示例和本台账。
- 验证：`npm run typecheck`、`npm test`（5 个测试文件、109 项，含图库哈希篡改拒绝和 16 位包长边界）、`npm run build` 通过；构建 hash 为 `sha256:83d8e0e11e9d1adb34fbd332aeaeecc611c111cba0231342ea876e5ebf8c10e2`，应用壳 gzip 约 `0.45 kB HTML + 7.96 kB CSS + 5.54 kB UI JS + 31.34 kB 主 JS + 174.39 kB Pixi JS`。
- 验证：`./scripts/build.sh`、`./scripts/test.sh`、`./scripts/zone-smoke.sh` 通过；包含 358 项物品安全回归、39 项内容快照、80 项内容编辑器、区服指标/健康、客户端更新、`Tests/WebAssets`、`Tests/WebGateway`、`Tests/ZoneVersionGate`，以及 legacy/签名发布/缺签名/篡改快照/缺地图区服冒烟；区服失败用例日志初始化竞态已修正。`scripts/test-browser-client.sh` 通过桌面 `1440×900`、移动横屏 `844×390` 和竖屏 `390×844` 冒烟；Vite 开发服务器 `/assets/` 产物读取已用临时编译地图验证；`docker compose --profile web config -q` 通过。
- 未验证：真实 `Client/Data`、`Client/Map`、`Client/Sound` 素材和真实 `.lib` 遮罩/地图 V3 字段；真实区服版本门禁联调；真实角色/怪物/NPC 图集动画、门动画、混合图块、光照和音效；iPhone/Android 真机、iOS Safari 16.4+、Firefox/Safari 矩阵、30 分钟内存和帧率验收。
- 下一步：用真实素材跑全量资源编译和地图回归；在测试区服配置 Web 构建哈希做真实登录/进图；补角色/怪物/NPC 图集动画、门/光照/音效；再进入真机与性能验收。

### 2026-11-19（容器内控制面部署与控制台启动）

- 范围：在当前工作容器内启动控制台，Web 监听 `0.0.0.0:80`，由宿主机映射到 `8580`；不修改业务代码、协议或数据模型。
- 部署：使用共享当前容器网络命名空间的 `crystal-pg-local`、`crystal-api-local` 和 `crystal-web-local` 容器；PostgreSQL 监听 `127.0.0.1:5432`，Control API 监听 `127.0.0.1:5080`，Nginx Web 监听 `0.0.0.0:80` 并同源代理 `/api`、`/health`。
- 内容：复用本地 PostgreSQL 数据卷中的黄金库导入结果，包含 1,628 物品、555 怪物、463 地图等实体；运行配置和发布私钥位于已忽略的 `artifacts/control-stack/`、`artifacts/control-signing/`。
- 验证：容器内 `http://127.0.0.1/health/ready` 和外层 `http://10.30.0.25:8580/health/ready` 均返回 ready；`admin` 登录成功；控制台首页标题正常。
- 未验证：生产域名/TLS、systemd 区服、Windows 客户端和外部对象存储；本次仅为容器内联调实例。
- 下一步：生产化仍按 `docs/architecture/07-deployment-and-observability.md` 接入反向代理/TLS、持久化 PostgreSQL/MinIO、密钥管理和区服 Agent。

### 2026-11-19（VERA20K 架构逆向与 Crystal 对标）

- 范围：对 `https://www.ra20k.com/` 的公开前端、下载通道和 `YuriPlanet/vera20k` 公开仓库做架构级逆向；只做公开 `GET` 观察，不登录、不写入目标站点。
- 观察：目标站是 `VERA20K 红警2-尤里的复仇联机平台`；`/lobby` 为 Vue 3 + Vite SPA，前端使用 Nakama 兼容 `/v2` 契约和自定义 `/vera/*` 接口；公开节点为 `sh2-vera20k.libisky.com`，节点注册表为 `/nodes.json`。
- 运行时：目标引擎为 Rust workspace，拆分 `sim`/`render`/`app`/`net`，使用定点数、固定随机流和状态 checksum；在线为 HTTPS lobby + 认证 UDP relay，客户端保留 RTS simulation authority。
- 交付：目标站地图/Mod 使用 SHA-256 内容寻址、上传/审核/版本/举报/下架；安装包和引擎更新分离，公开下载指针带签名；另有节点健康、公共统计、排位赛季和回放。
- 结论：不把 Crystal 改成 VERA20K 的客户端权威 lockstep；保留服务端权威 MMO，吸收会话边缘、多维版本门禁、command journal/回放、UGC 供应链、节点目录和 ClientCore 分层。
- 文档：新增 `docs/architecture/08-vera20k-benchmark-and-refactor.md` 和提议中的 `docs/adr/0016-vera20k-benchmark-boundary.md`，并更新架构索引、ADR 索引和本文档。
- 验证：公开页面、`/nodes.json`、`/vera/config`、`/vera/ready`、`/vera/stats`、下载 `latest.json` 和公开仓库文档均已读取核对；未执行目标站登录、上传或写入。
- 未验证：目标站后台技术栈、Nakama 是否为官方部署、审核工作流、反作弊、支付/订阅和容量指标；仓库自述的 30 人/20,000 单位未做性能复现。
- 下一步：若认可 ADR 0016，先做 P0 预会话握手原型、玩家 config/ready/stats 只读接口、节点目录和版本兼容矩阵；不先改游戏主循环。

### 2026-11-18（CI runner 兼容性与 S3Mock 替换）

- 范围：继续处理 GitHub Actions run 12/13/14 暴露的 runner 差异；授权、许可证和素材合规不纳入。
- run 12：`build-test` 的构建、回归测试、黄金库和内容校验全部通过；`check-boundaries` 因 runner 未安装 `rg` 被误判，改为 `grep` 后本地通过。`compose-stack` 仍在数秒内失败。
- run 13：`build-test` 与 `control-api-integration` 通过；`compose-stack` 注解定位到 `docker compose up`，失败原因为 `minio/mc:RELEASE.2024-12-18T13-15-44Z` 已从 Docker Hub 下架。
- 替换：Compose 的对象存储测试替身从 MinIO 改为 Apache-2.0 的 `adobe/s3mock:5.2.3`，启动时自动创建 `crystal-content` 桶；移除 `minio-init`、MinIO 凭据和相关环境变量，保留 S3 兼容接口和生产 MinIO/S3 选型。
- 诊断增强：`test-compose.sh` 将 Compose 启动日志写入文件，失败时通过 `::error::` 注解输出最后 12 行，并检查 Docker Compose v2 可用性。
- 验证：`grep` 版边界检查通过；`docker compose config -q` 通过；本地 `scripts/test-compose.sh` 完成 PostgreSQL 16、Redis、S3Mock、迁移、API/Web 代理、登录和内容查询全链路。
- 验证（远端）：run 15（`59cd60e`，run id `44292294225`）的 `build-test`、`control-api-integration` 和 `compose-stack` 全部 success。
- 未验证：Windows 客户端、launcher 和外部 staging/systemd 仍不可在当前 Linux 容器验证。
- 下一步：进入 WP1，补 Agent mTLS、节点绑定、结果签名和重放保护。
- 决策：S3Mock 仅作为 Compose 本地/CI 测试替身，生产仍使用 MinIO/S3；不改变控制面协议、数据模型或发布模型，因此不新增 ADR。

### 2026-11-18（远端 CI 失败定位与修复）

- 范围：核对 GitHub Actions run 11（提交 `4461591`）的三个 job，修复可在本地复现的 CI 阻断；不涉及授权、许可证和素材合规。
- 远端结果：`control-api-integration` 成功；`build-test` 在 `Restore and build`、`compose-stack` 在 `Compose deployment smoke test` 均于启动后数秒失败。
- 根因一：工作流设置 `DOTNET=dotnet`，7 个脚本使用 `[ ! -x "$DOTNET" ]` 把命令名当成文件路径，立即退出 1。改为 `command -v "$DOTNET"`，同时接受 PATH 命令名和绝对路径。
- 根因二：`deploy/docker/control-api.Dockerfile` 未复制 M3 新增的 `src/Crystal.Agent.Contracts`，镜像内 Control API 编译缺少 Agent 契约类型；补齐 csproj 和源码复制。
- 验证：以 `PATH=/root/.dotnet:$PATH DOTNET=dotnet` 复跑 `scripts/build.sh`、`scripts/test.sh`、`scripts/inspect-db.sh`、`scripts/verify-content.sh`、`scripts/check-boundaries.sh` 和 Control Web `node --check` 全部通过；`scripts/test-compose.sh` 完成镜像构建、PostgreSQL 16/Redis/MinIO、迁移、API/Web 代理、登录和内容查询验证。
- 未验证：未提交或推送，因此远端 run 12 尚未产生；Windows 客户端、launcher 和外部 staging/systemd 仍不可在当前 Linux 容器验证。
- 下一步：提交并推送 CI 修复，确认三个 job 全绿；随后按 `docs/roadmap/milestones.md` 的 WP1 补 Agent mTLS、节点绑定、结果签名和重放保护。
- 决策：仅修复 CI 可执行性，不改变协议、数据模型、发布模型或许可证边界，因此不新增 ADR。

### 2026-11-18（客户端签名补丁与事务更新）

- 范围：执行 M3 客户端更新链路的最小完整闭环，新增签名 manifest、本地 SHA256 扫描、文件级增量、Range 续传、事务应用、崩溃恢复、回滚和启动器版本门禁；保留旧 `PList.gz` 路径。授权、许可证、素材合规、二进制 delta、灰度/CDN、launcher 自更新和 Windows 运行冒烟不在本轮。
- 新增 `src/Crystal.Patcher`（MIT、`net8.0`、无项目依赖）：manifest 模型、camelCase canonical JSON、ECDSA P-256/SHA-256 签名、keyId、渠道/最低 launcher-client 版本/协议/content schema/有效期/降级门禁、路径穿越和大小写重复路径拒绝。
- 更新流程：扫描本地文件 → 生成新增/替换/删除计划 → 下载到 `.crystal-update/staging` → 校验大小和 SHA256 → 写 `pending.json` → 备份旧文件 → 移入新文件 → 写 `state.json` → 删除 pending。失败自动恢复；进程崩溃后下次更新或回滚读取 pending 恢复；未知本地文件不删除。
- 新增 `tools/Crystal.PatcherTool`：`pack`、`sign`、`verify`、`plan`、`apply`、`rollback`、`state`，支持本地路径和 HTTP(S) manifest；新增 `docs/reference/client-patch-manifest.md`。
- 旧 WinForms Launcher 增加 `Launcher:ManifestUrl` 分支：配置后使用签名 manifest，读取公钥、keyId、channel、协议/schema 版本和可选版本覆盖；未配置时旧 `PList.gz` 流程不变，签名更新失败不静默回退。
- 新增 `Tests/ClientUpdates` 和 `scripts/test-client-updates.sh`，并接入 `build.sh`、`test.sh`、`ci.sh` 和依赖边界检查。
- 文档：新增 ADR 0015；同步 ADR 索引、文档地图、架构热更新、开发构建、能力矩阵、里程碑、参考手册索引和本台账。
- 验证：`Tests/ClientUpdates` 通过签名/篡改/过期/版本门禁/路径安全/扫描/应用/无变化/防降级/哈希失败/崩溃恢复/回滚/JSON round-trip；`scripts/test-client-updates.sh` 通过 `pack -> sign -> verify -> apply -> state -> rollback`；`Client.csproj` 在 Linux 上交叉构建成功；`check-boundaries.sh`、`bash -n` 通过。
- 未验证/范围外：Windows 启动器实际运行、launcher 自更新、二进制 delta、并发限速、磁盘空间预检、灰度/CDN、登录握手版本门禁和真实客户端资源更新；未处理授权、许可证和素材合规。
- 阻塞/风险：本轮回滚只覆盖最近一次已应用事务；manifest 应排除运行中的 launcher；Windows 上文件占用和权限行为仍需真实环境验证。
- 下一步：在 Windows 环境用签名 manifest 跑启动器和客户端冒烟；补 launcher 自更新、二进制 delta、渠道/灰度和登录握手门禁。

### 2026-11-18（区服指标历史、趋势与健康告警）

- 范围：在已完成的真实指标心跳基础上，增加区服指标持久化、窗口查询、汇总、健康等级和 Control.Web 趋势/告警展示；不改变 Agent 心跳 DTO、ZoneServer 指标文件、客户端协议或游戏主循环，授权、许可证和素材合规继续跳过。
- 数据：新增 `db/migrations/0005_zone_metric_history.sql` 和 `zone_metric_sample`，保存区服、observedAt、状态、active release、玩家数、Tick P95、错误率、metricsSource、重启失败、PID 和原始 metadata；`(zone_id, observed_at)` 唯一，按区服/时间和 recordedAt 建索引。
- Control API：心跳事务内更新 `zone` 投影并幂等插入样本；`observedAt` 缺失返回 400；新增 `GET /api/v1/zones/{zoneId}/metrics`，支持 from/to/limit、默认 1 小时、最大 7 天、最多 2000 条，返回样本、汇总和 health。新增 ADR 0014。
- 健康评估：按最新投影和阈值实时给出 `healthy|warning|critical`；覆盖离线、降级、指标不可用、Tick P95、错误率和重启失败告警；阈值通过 `ControlApi:ZoneTickP95WarningMs`、`ZoneTickP95CriticalMs`、`ZoneErrorRateWarning`、`ZoneErrorRateCritical` 配置，默认 50/100ms 和 5%/20%。
- Control.Web：区服详情新增健康评估卡片，以及玩家数、Tick P95、错误率三条 SVG 趋势；使用新增 metrics API，指标读取失败不阻断区服详情和命令操作。
- 测试：新增 `Tests/ZoneHealth`，覆盖健康、warning、critical、degraded 和 metadata 告警；Control API PG16 集成覆盖同 observedAt 去重、窗口/limit/未知区服边界、汇总和 critical 告警；Control.Web Playwright 覆盖趋势 SVG、健康告警文本；Zone Agent E2E 继续通过。
- 文档：新增并登记 ADR 0014；同步文档地图、架构 02/03/04/07、Control API 参考、配置键、能力矩阵、里程碑和本台账。
- 验证：`Tests/ZoneHealth` 通过；在独立 PostgreSQL 16 数据库上运行 `test-control-api.sh`、`test-control-web.sh`、`test-zone-agent.sh` 全部通过；Node `--check`、Shell `bash -n` 和 `git diff --check` 通过。
- 未验证/范围外：告警通知渠道、告警状态机/去重、Prometheus/OpenTelemetry 远端汇聚、样本自动保留/分区/降采样、Windows 客户端冒烟、授权、许可证、KMS 和素材合规不在本轮。
- 阻塞/风险：样本按心跳持续增长，研究环境暂不自动清理；健康阈值是平台配置而非内容签名事实；指标仍来自同机非权威缓存。
- 下一步：补告警通知与保留/降采样策略；继续推进版本门禁、客户端启动器和零停机换版研究。

### 2026-11-18（真实区服指标与 Control.Web 区服闭环）

- 范围：把 ZoneServer 的真实玩家数、世界 Tick P95、错误率和 Agent 重启次数接入现有心跳，并将 Control.Web 区服总览/详情从静态样例切换到真实 API，开放 stage、activate、rollback、reload、stop 五类命令；不修改游戏主循环行为、客户端协议、Control API DTO 或数据库 schema，授权、许可证和素材合规继续跳过。
- GameCore：新增 `RuntimeMetricsSampler` 和版本化 JSON 编解码；60 秒窗口、100 ms 采样、最近秩 P95，按 `completedTicks` 去重世界循环样本；错误率按窗口首尾异常/Tick 增量计算并封顶 1。新增 `Tests/ZoneMetrics`，覆盖 P95、窗口淘汰、错误率上限、无 Tick 样本、计数去重、非法 JSON 和 zoneId/PID/时效校验。
- ZoneServer：`Envir` 增加已完成世界循环计数，`MessageQueue.Enqueue(Exception)` 只增加异常计数；新增 `--metrics-file` / `CRYSTAL_ZONE_METRICS_FILE`，ready 后独立循环每秒原子写入指标文件，进程启动时间在 `Main` 入口捕获，写入失败只记录日志且不终止区服，退出时删除文件。
- Agent：启动 ZoneServer 时传入 `state/metrics.json`；每次心跳校验 schema、zoneId、PID、15 秒时效和数值范围，有效时上报真实指标，无效或过期时上报 0 并标记 `metricsSource=unavailable`，子进程停止后标记 `process-stopped`；连续三次失效且进程仍运行时降级。保留 `restartFailures`、`zonePid` 和发布目录字段，新增 `metricsObservedAt`、`metricsWindowSeconds`、`metricsSampleCount`、`metricsError`。
- Control.Web：接入现有 `GET /zones`、`GET /zones/{id}`、`GET/POST /zones/{id}/commands` 和发布列表接口；区服列表/详情展示真实在线数、Tick P95、错误率、重启次数、Agent/进程状态、active/staged 版本、内容哈希、最后心跳和命令历史。五类命令要求 `zone:write`、二次确认和发布版本选择；离线或在途命令时禁用，409 显示明确冲突提示；列表每 5 秒、详情每 2 秒刷新并在离开路由后清理定时器。`process-stopped` 显示零值而非“指标不可用”。
- 文档：新增 ADR 0013 并登记 ADR 索引；同步文档地图、架构 02/03/04/07、Control API 参考、ZoneServer/Agent 运行手册、能力矩阵、里程碑、Control.Web README 和本台账；ADR 0012 标记为被 0013 扩展。
- 验证：`./scripts/build.sh`、`./scripts/test.sh`、`./scripts/check-boundaries.sh` 全部通过；在全新 PostgreSQL 16.16 数据库上分别运行 `./scripts/test-control-api.sh`、`./scripts/test-zone-agent.sh`、`./scripts/test-control-web.sh` 全部通过。Control API 覆盖非零玩家数/Tick P95/错误率投影、metadata 完整保留和越界拒绝；Zone Agent 覆盖真实指标文件、`zone-runtime` 心跳、正 Tick P95、重启后新 PID 和 stop 后归零/`process-stopped`；Control Web 覆盖真实列表/详情、五类命令请求体与历史、409 冲突、离线禁用和只读账号。Node `--check`、Shell `bash -n` 和 `git diff --check` 通过。
- 未验证/范围外：无客户端连接时只验证玩家数为 0；非零玩家数和真实客户端错误需 Windows 冒烟。真实 Ubuntu systemd 安装、客户端启动器、灰度/CDN、进程内热切换、玩家状态迁移、多 Agent HA、Prometheus/OpenTelemetry 汇聚、授权、许可证、KMS 和素材合规不在本轮。
- 阻塞/风险：100 ms 采样不保证覆盖每个内部循环，指标适合研究环境趋势；指标文件是同机非权威缓存，依赖 PID/zoneId/时效校验；生产化前需补远端汇聚、告警和 Windows 验证。
- 下一步：完成 Windows 客户端连接与真实玩家数/错误率冒烟；接入长期指标存储和告警；推进客户端启动器、版本门禁和零停机换版研究。

### 2026-11-17（M3 Zone Agent 控制闭环与进程级换版）

- 范围：按研究项目口径完成 M3 服务端端到端最小闭环，跳过授权、许可证、Windows 冒烟和客户端启动器；换版允许短暂停机，不做进程内热切换、灰度/CDN、Control.Web 区服页面和玩家状态迁移。
- 契约：`Crystal.Agent.Contracts` 增加 Zone DTO、命令状态、创建命令请求、命令历史、发布包路由和固定 payload；现有注册/心跳/命令字段保持兼容。
- schema：新增 `db/migrations/0004_zone_agent_control.sql`，创建 `zone` 和 `zone_command`，包含区服投影、命令状态/类型约束、轮询/历史/租约索引和单生命周期部分唯一索引；不修改现有内容表，区服激活不改变 `content_release.status`。
- Control API：新增 `ZoneStore`/`ZoneEndpoints`，支持注册、心跳、列表/详情、命令入队/领取/历史、租约过期重投、超时 `expired`、单生命周期冲突、结果幂等/冲突和 `zone:write` 权限；新增 `GET /api/v1/releases/{id}/artifact` 流式 ZIP 下载，只允许已签名 `published`/`active` 发布。
- Zone Agent：新增 `Crystal.ZoneAgent`，仅引用 MIT 的 `Crystal.Agent.Contracts` 与 `Crystal.Content`；实现注册、心跳、命令轮询、结果本地持久化/重报、ZIP 路径穿越拒绝、manifest/实体/内容哈希和 ECDSA 验签、原子 staging、SIGTERM、ready PID 检查、候选失败恢复上一版本、意外退出退避重启和 active 状态恢复。
- ZoneServer：新增 `--ready-file <path>` 和 `CRYSTAL_ZONE_READY_FILE`，ready 后原子写入 JSON（含 PID、内容根、计数），退出删除；未指定时 legacy 启动模式和客户端协议不变。
- 工具与部署：新增 `scripts/zone-control.sh`、`scripts/test-zone-agent.sh`、`deploy/systemd/crystal-zone-agent.service` 和 `deploy/systemd/zone-agent.env.example`；导入器新增 `--drop-manifest` 供最小 E2E 夹具生成缺失掉落路径。
- 验证：在一次性 PostgreSQL 16 数据库上从零应用 0001～0004，`test-zone-agent.sh` 通过注册、CLI 区服列表、stage、两次 activate、rollback、篡改 ZIP 实体字节拒绝、候选启动失败恢复、重复结果幂等、Agent 重启恢复和 stop；`test-control-api.sh` 通过 Zone API 未知区服、非法心跳、payload 校验、单生命周期 409、租约重投、历史超时 `expired`、结果幂等/冲突和 stop 投影；`scripts/build.sh`、`scripts/test.sh`（358 项物品安全、39 项内容快照、契约边界、80 项内容编辑）、`scripts/check-boundaries.sh`、`scripts/zone-smoke.sh`（legacy/签名发布/未签名/篡改/缺失地图）和 `git diff --check` 通过；在全新 PostgreSQL 16 库上运行完整 `./scripts/ci.sh` 通过（含 Control API 与 Zone Agent 两套集成）。
- 文档：新增 ADR 0012；同步 ADR 索引、文档地图、系统概览、运行时与模块、数据与内容、热更新、部署与可观测性、安全与合规、构建测试、Control API 参考、Linux ZoneServer/Agent runbook、里程碑和本台账。
- 未验证：真实 Ubuntu systemd 安装/开机自启/日志轮转、真实玩家会话下的换版、进程内热切换、玩家状态迁移、真实主循环指标、Control.Web 区服页面、Windows 客户端/宿主运行、远端 GitHub Actions PG16/Compose job、授权/许可证/KMS/素材授权。
- 阻塞/风险：进程级换版仍有短暂停机；同一区服只支持单 Agent，无 HA；Agent 面向端点当前依赖可信私有网络和现有 API key，生产化前需要 mTLS/节点绑定/结果签名；指标为占位值。
- 下一步：在 staging 主机实际安装 Agent systemd 单元并演练重启/日志/防火墙；接入真实指标；再做客户端启动器、版本门禁、灰度/CDN 和更高等级的高可用换版。

### 2026-11-17（M2 内容后台 MVP 完成）

- 范围：完成 M2 本轮内容后台能力，包括 10 类 Schema 驱动编辑、引用选择、两级校验、JSONL 批量导入导出、脚本源码校验、地图嵌套关系 CRUD、地图网格预览，以及 ZoneServer 启动期只读消费签名发布。授权、许可证、素材合规、M3 Agent、热切换和灰度不在本轮。
- 编辑框架：Schema 字段类型扩展 `number`、`array`、`reference`、`code`、`color`、`readonly`，字段 DTO 增加 `referenceType`、`nullable`、`multiline`、`minItems`、`maxItems` 等可选属性；编辑器定义增加服务端身份、规范化、引用、标签和搜索钩子，统一结构/完整性/内容键/引用校验和问题码。
- 内容类型：注册 `item`、`monster`、`map`、`npc`、`script`、`drop`、`magic`、`quest`、`gameshop`、`recipe`。身份规则覆盖自动 `localIndex`、`GIndex`、`Spell`、产出物品名、规范化脚本路径和 `monsterIndex`；旧历史内容键保留，新建键使用安全段，客户端不能改写已有内容键。
- 专用能力：NPC 支持地图/脚本/任务引用和坐标校验；脚本由服务端重算 AST、页面、引用、问题、行数、源码 SHA256 和编译哈希；掉落支持金币、物品、嵌套组、概率、任务限定和引用；技能、任务、商城、配方覆盖专用字段、交叉引用和保存规范化。
- API：新增 `/content/{entityType}/options`、草稿 JSONL import/export、脚本 validate、地图 relations/数组替换/刷新点与 NPC 行级 CRUD、`/content/asset/{localIndex}/preview`。关系写入使用 revision 乐观并发，刷新点/NPC 写入与地图投影同事务；地图预览返回降采样可走/阻挡/门格。
- Web：新增十类编辑器、数字/数组/引用/颜色/代码/只读控件和引用选择器；新增脚本源码与只读 AST、嵌套掉落组、地图关系表格与分页、JSONL dry-run/apply/export、地图网格预览和资源抽屉内联预览；只读账号不显示新建或保存入口。
- ZoneServer：新增 `--content-release`、`--content-public-key`、`--content-key-id` 及对应环境变量；启动前校验 manifest/实体哈希和 ECDSA 签名，在隔离临时目录重建 `Server.MirDB`、物化 `drop`/`recipe`/`script`/`quest`，再走现有 `Settings.Load()`/`LoadDB()`/`Envir.Start()`。未传发布参数时 legacy 模式不变；签名缺失、快照篡改和地图/脚本/配方物化失败会阻止 ready。
- 边界：不新增数据库迁移，不改变客户端协议、快照文件格式、发布 manifest 或本地索引语义；`Crystal.Control.Api` 不引用 `Server/`、`Shared/` 或 `Crystal.LegacyRuntime`；发布模式不修改运行内容，不做热切换。
- 验证：`scripts/ci.sh` 在全新 PostgreSQL 16 库上通过，覆盖全量构建、358 项物品安全回归、39 项内容快照检查、80 项 ContentEditor 检查、依赖边界、黄金导入/签名/重建/导出、Zone 冒烟和 Control API 集成；`scripts/zone-smoke.sh` 通过 legacy、Jev 签名发布（463 地图/1,628 物品/555 怪物/375 NPC/154 任务）、未签名、篡改快照和缺失地图五条路径；`scripts/test-control-web.sh` 在另一全新库上通过真实 API + Chromium 的十类编辑器、引用、关系 CRUD/409 恢复、JSONL、AST、地图预览、资源下载、发布/回滚和只读权限链路；`bash -n scripts/*.sh`、Web JavaScript `node --check` 和 `git diff --check` 通过。
- 文档：新增 ADR 0010（多内容编辑、引用、批量导入导出与地图关系）和 ADR 0011（ZoneServer 启动期只读消费签名内容）；同步 ADR 索引、文档地图、架构决策摘要、数据与内容、热更新、部署与可观测性、里程碑、能力矩阵、Control API/Web/ZoneServer README、Control API 参考、内容导入参考和本台账。
- 未验证：Windows 客户端加载修改后的内容、systemd 实际安装、真实生产发布灰度、M3 Agent/热切换、CSV/超大文件异步导入、`.lib` 图库解析、资源上传、授权与许可证合规。
- 下一步：进入 M3，设计 Zone Agent 轮询/命令、staging 快照、启动器和原子切换/回滚；M0 的 Windows 冒烟、systemd 实装和素材授权继续作为外部阻塞。

### 2026-11-16（M2 地图元数据编辑器垂直切片）

- 范围：把 `map` 注册为第三种 Schema 驱动编辑器，复用现有草稿创建、更新、删除、revision 冲突、校验和发布链路；只编辑地图顶层标量元数据，不新增数据库迁移，不改变内容快照格式、客户端协议或 GPL 边界。
- 后端：新增 `ContentMapEditorSchema.cs`，注册到 `ContentEditorSchemas`；`GET /api/v1/editor-schemas/map` 返回 6 个分组、44 个字段及整数范围、`Light` 枚举和只读信息。`Index`、`WeatherParticles` 只读，后者默认 `"None"`，避免改写旧位掩码字符串。
- 字段边界：覆盖身份、客户端表现、访问控制、战斗与环境、人口与限制、矿区与攻城；`Movements`、`SafeZones`、`Respawns`、`NPCs`、`MineZones`、`ActiveCoords` 不进入 Schema，保存时通过未知字段透传原样保留。
- 校验：保存阶段阻断整数类型/范围、字符串、布尔、枚举、`Index == localIndex` 和 `contentKey == map.<localIndex>` 错误；草稿/发布校验生成 `map-completeness`、`map-content-key`，并新增地图 `FileName` 去空格、大小写不敏感的唯一性检查，冲突生成 `map-file-name-duplicate`。
- 类型化生命周期：服务端继续为新建地图分配下一个 `localIndex`、覆盖 `Index` 并生成 `map.<index>`；新建地图不主动生成嵌套集合，缺失集合按旧模型默认空集合处理。地图更新、删除和包含地图修改的草稿发布继续走既有发布链路。
- 前端：`map` 加入可编辑类型和编辑器元数据；地图列表在活动草稿下读取草稿数据，点击进入地图编辑抽屉。抽屉增加只读“地图关系”摘要，展示传送、安全区、NPC、刷新点数量与首批明细；新草稿地图或基础批次无关系时显示空状态，不把 404 当作错误。
- API 集成：`scripts/test-control-api.sh` 覆盖地图 Schema 的 200/44 字段/枚举/只读、标题/小地图/光照/布尔/伤害修改、嵌套集合数量保持、非法范围/枚举/类型/`Index`/`contentKey` 400、stale revision 409、`map-completeness` 与 `map-file-name-duplicate`，以及地图创建/更新/删除和发布闭环。
- Web 集成：`scripts/test-control-web.sh` 覆盖地图列表、编辑抽屉、字段差异、保存、400 字段错误、409 载入最新 revision、新建/删除、真实关系计数、空关系状态和只读账号无编辑入口；同时修正测试中搜索框跨查询未清空导致的临时记录过滤问题。
- 验证：`DOTNET=/root/.dotnet/dotnet ./scripts/test-control-api.sh` 在临时 PostgreSQL 16 上通过；随后以 `SKIP_API_SETUP=1` 运行 `./scripts/test-control-web.sh` 通过；带 PostgreSQL 16 的完整 `./scripts/ci.sh` 通过（构建、358 项安全回归、39 项内容快照检查、边界检查、黄金导入/导出、Zone 冒烟和 Control API 集成）；JavaScript/Shell 语法检查和 `git diff --check` 通过。
- 文档：新增 ADR 0009；同步 ADR 索引、文档地图、架构决策摘要、内容字段清单、Control API 参考与 README、Web README、能力矩阵、里程碑和本台账。
- 未验证：地图嵌套关系 CRUD、地图文件上传/资源预览、天气 flags 编辑协议、Windows 客户端实际加载修改后的地图元数据、生产环境灰度发布。
- 下一步：继续 M2 的 NPC 脚本/掉落等专用编辑器，并评估地图关系编辑所需的坐标、引用选择和并发语义；M0 外部阻塞保持不变。

### 2026-11-16（M2 怪物编辑器垂直切片与多内容编辑框架）

- 范围：在 `item` 垂直切片基础上先消除类型硬编码，建立按 `entityType` 注册 Schema、结构校验和完整性校验的多内容编辑框架，并以 `monster` 作为第二个垂直切片；不新增数据库迁移，不改变快照/发布格式、客户端协议或旧服务端行为。
- 后端：新增 `ContentEditorSchemas.cs` 和 `ContentMonsterEditorSchema.cs`；`GET /api/v1/editor-schemas/{entityType}` 查询注册表，未注册类型返回 404；`item` 保持 `schemaVersion=1`、字段、响应和问题码不变。
- 怪物 Schema：5 个分组、20 个可编辑字段，覆盖身份、基础行为、战斗参数、布尔开关和 `Stats.Values`；图像枚举由 MIT 控制面手工维护，`Stats.Values` 仅允许 13 个固定键且值为 32 位整数。`Drops`、`HasSpawnScript`、`HasDieScript` 保持运行时派生，不进入编辑字段。
- 写入：类型化创建对所有已注册类型生效，服务端事务内分配下一个 `localIndex`、覆盖 `Index`、生成 `<entityType>.<localIndex>` 并写 `create` 修订；保存只阻断类型、范围、枚举、布尔、`Index`、`contentKey` 和 `Stats.Values` 等结构错误，未知顶层字段原样透传。
- 校验：草稿/发布校验统一生成 `<entityType>-completeness` 和 `<entityType>-content-key`；怪物允许保存不完整草稿，校验会报告缺失字段、空名称和缺少 `Stats.Values`。物品问题码保持 `item-completeness`、`item-content-key`。
- 前端：编辑状态、Schema 缓存、抽屉、差异、保存、删除、校验、400 映射和 409 恢复改为通用 `entityType` 驱动，启用 `item`、`monster`；草稿激活时物品/怪物列表读取草稿数据，可写用户点击进入编辑器，只读用户保留详情抽屉。怪物编辑器展示只读掉落关系；草稿新建怪物无导入掉落时显示空关系而非读取失败。
- 文档：新增 ADR 0008，并同步 ADR 索引、文档地图、Control API/Web README、Control API 参考、内容 schema 参考、能力矩阵、里程碑和本进度台账。
- 验证：`DOTNET=/root/.dotnet/dotnet ./scripts/ci.sh` 通过，包含全量构建、358 项服务端安全回归、39 项内容快照检查、边界检查、黄金内容导入/签名/往返和区服冒烟（463 张地图、1628 件物品、555 个怪物）；在全新 PostgreSQL 16.16 数据库上运行 `scripts/test-control-api.sh` 通过，覆盖怪物 Schema、合法更新、未知字段透传、类型/范围/枚举/布尔/Stats/Index/contentKey 400、stale revision 409、自动索引创建、不完整草稿问题码、删除、修订和从草稿发布；随后运行 `SKIP_API_SETUP=1 scripts/test-control-web.sh` 通过，覆盖物品回归、怪物草稿列表/编辑/差异/保存/校验/409 恢复、新建/删除和只读抽屉。全部 Web JavaScript `node --check`、Shell `bash -n` 和 `git diff --check` 通过。
- 未验证/范围外：地图、NPC、掉落、技能、任务等专用编辑器；掉落 CRUD、资源预览、批量导入导出、真实区服消费；Windows 客户端冒烟。授权、许可证和素材合规按本轮范围未处理。
- 下一步：复用注册表接入地图元数据或 NPC 脚本编辑器，并补引用选择器和资源预览；掉落继续保留只读关系，直到确定独立掉落模型和概率校验。

### 2026-11-16（M2 物品编辑器垂直切片）

- 范围：在不新增数据库迁移、不改客户端协议和旧服务端行为的前提下，为 `item` 打通 Schema、草稿编辑、校验、差异和发布前闭环。
- 后端：新增 `ContentEditorModels.cs` 和 `ContentItemEditorSchema.cs`；`GET /api/v1/editor-schemas/item` 返回 `schemaVersion=1` 的 6 个分组，覆盖 33 个顶层字段、`Stats.Values` 和 70 个随机属性字段。
- 写入：新增 `POST /api/v1/drafts/{id}/content/item`，事务内分配草稿内下一个 `localIndex`、覆盖 `Index`、生成 `item.<index>` 并写 `create` 修订；现有 `PUT` 增加 integer/string/boolean/enum/flags/`Stats.Values`/`RandomStats`/`Index`/`contentKey` 结构校验。
- 校验：保存只阻断结构错误，允许保存未完成草稿；草稿/发布校验报告 `item-completeness` 和 `item-content-key`。旧内容省略整个 `RandomStats` 表示全零随机属性，视为合法；`Stats` 存在时要求 `Stats.Values`；未知字段原样透传。
- 前端：新增 `content-editor.js`，接入草稿列表/激活/创建、物品列表草稿查询、全字段编辑抽屉、动态属性行、70 个随机属性、字段差异、保存/删除/校验、400 字段错误映射、409 冲突保留输入和载入最新 revision；只读用户隐藏写动作。
- 文档：新增 ADR 0007，并更新架构索引、Control API、数据与内容、产品原型、能力矩阵、路线图和文档索引。
- 验证：`scripts/build.sh`、`scripts/test.sh`、`scripts/check-boundaries.sh` 通过；全新 PostgreSQL 测试库上 `scripts/ci.sh` 全链路通过；`scripts/test-control-api.sh` 与 `scripts/test-control-web.sh` 通过；Node.js 语法检查、Shell 语法检查和 `git diff --check` 通过。
- 集成细节：API 测试覆盖 Schema 401/未知类型 404、只读账号读 Schema 200/写 403、非法字段 400、自动索引与 `item.<index>`、不完整草稿校验、删除、stale revision 409、完整草稿发布；Web 测试覆盖草稿切换、全字段保存刷新、差异、校验、400/409 恢复、新建/删除和只读权限。
- 未验证：其他内容类型的类型化编辑器、资源预览、批量导入导出、真实区服消费新内容版本（M3）。
- 阻塞：无新增技术阻塞；授权、许可证和素材合规按本轮范围未处理。
- 下一步：把同一 Schema + 两级校验模式推广到怪物、地图、NPC、掉落等 M2 内容域，并补充引用选择和资源预览。

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

### 2026-11-13（M1 收口：骨架、草稿/回滚闭环与可部署栈）

- 范围：按 M1 收口计划补齐 `Crystal.GameCore`、`Crystal.Agent.Contracts`、依赖边界检查、可编辑内容草稿、发布回滚、Control.Web 真实发布中心和 Compose 可部署栈；不迁移 `Envir`/`Map` 游戏逻辑，不改变客户端协议。
- GameCore：新增 `IContentSnapshotProvider<TSnapshot>`、`IPlayerRepository<TSnapshot>`、`IWorldClock`、`IZoneEventSink`、`IChatTransport` 和运行时事件类型；`Crystal.ZoneServer` 只增加项目引用。
- Agent 契约：新增 MIT `Crystal.Agent.Contracts`，定义注册、心跳、命令、命令结果、内容版本描述、稳定 JSON 字段和命令字符串 `content.stage`/`content.activate`/`content.rollback`/`content.reload`/`zone.stop`；本轮不注册 Control API Agent 路由。
- 边界：新增 `Tests/ContractBoundary`、`scripts/check-boundaries.sh` 并接入 `scripts/build.sh`、`scripts/test.sh` 和 `scripts/ci.sh`；检查 Control API 不引用 GPL 项目，GameCore 只依赖 Shared，Agent 契约无项目依赖。
- schema：新增 `db/migrations/0003_content_draft_schema.sql`，创建 `content_draft`、`content_draft_entity`、`content_draft_revision`，为 `content_release` 增加 `draft_id`、回滚时间/操作者/原因/目标，并用 `ux_content_release_single_active` 保证最多一个 `active` 版本。
- 草稿 API：支持从已导入批次复制实体、分页查询、按实体读取、PUT 新增/更新、DELETE 删除、校验和修订查询；逐实体 `revision` 乐观并发，冲突返回 409；变更追加前后数据、操作者、原因和时间，校验结果持久化到 `last_validation`。
- 发布/回滚：创建版本时 `importRunId` 与 `draftId` 恰好提供一个，创建时复制实体到不可变版本，并在同一事务内回读实体重算内容哈希，来源草稿被并发修改时返回 409；回滚只作用于 `published`/`active`，目标须为 `published`，事务锁定并重新验签，当前版本标记 `rolled_back`、目标版本标记 `active`，原因写入审计。
- Control.Web：发布中心改为真实 API 数据，展示发布列表、KPI、状态链路、实体计数、manifest/签名、校验报告和回滚元数据；支持从导入批次/草稿创建版本、校验、编译、签名、发布和回滚；动作按 `release:*` 权限显隐，不再展示虚构区服灰度和 Ed25519。
- 部署：新增根目录 `compose.yaml`、`.env.example`、`deploy/docker/control-api.Dockerfile`、`deploy/docker/control-web.Dockerfile`、`deploy/nginx/control-web.conf` 和 `scripts/test-compose.sh`。Compose 包含 PostgreSQL 16、Redis 7、MinIO、MinIO init、db-migrate、Control API 和 Nginx Web；Web 同源代理 `/api`/`/health`，数据、Redis、MinIO 和发布产物使用独立卷，签名私钥只运行时挂载，GPL 导入器不进入 Compose。
- 验证：`./scripts/build.sh` 通过；`./scripts/test.sh` 通过 358 项物品安全回归、39 项内容快照检查和 Agent/GameCore 契约边界测试；`./scripts/check-boundaries.sh`、`./scripts/verify-content.sh`、`./scripts/inspect-db.sh`、`./scripts/zone-smoke.sh` 通过。
- 验证：在一次性 PostgreSQL 16 数据库上运行 `./scripts/test-control-api.sh`，覆盖草稿复制/增改删、陈旧 revision 409、校验失败/成功、从草稿创建发布、完整发布链路、显式/默认双版本回滚、403、短原因 400、重复回滚 409 和审计事件，通过。
- 验证：Node Playwright（playwright-core + Chromium）对真实 API/Web 运行发布中心冒烟，覆盖草稿发布、校验、编译、签名、manifest、权限动作和回滚；分别验证了存在 active 与无 active 的两条回滚分支。Python Playwright 在当前环境无法安装，`scripts/test-control-web.sh` 已同步修正回滚源/目标逻辑但未在本机执行。
- 验证：`./scripts/test-compose.sh` 通过。当前沙箱无法使用 Docker 主机端口映射，脚本自动改用 Nginx 容器内同源代理验证健康、登录和内容查询，并用一次性 SDK/运行时容器在 Compose 网络内运行宿主机已构建的 GPL 导入器；CI 环境仍使用宿主机导入路径。
- 验证：Shell `bash -n`、全部 Control.Web JavaScript `node --check`、`docker compose config` 和 `git diff --check` 通过。
- 未验证：GitHub Actions 远端 PG16/Compose job、Windows 客户端登录/选角/进服、Windows 宿主编译运行、Ubuntu systemd 实际安装、真实 Zone Agent 热切换、KMS/HSM 密钥和素材授权。
- 决策：新增 ADR 0006，记录草稿复制、逐实体 revision、发布时冻结实体、平台级单 active 和回滚状态切换模型。

## 4. 决策记录

| 日期 | 决策 | ADR |
| --- | --- | --- |
| 2026-11-09 | 控制面与数据面分离 | `docs/adr/0001-control-plane-data-plane.md` |
| 2026-11-09 | PostgreSQL 与内容快照 | `docs/adr/0002-postgresql-and-content-snapshots.md` |
| 2026-11-09 | 保留现有协议并引入边缘网关 | `docs/adr/0003-legacy-protocol-with-gateway.md` |
| 2026-11-09 | GPL 边界与代码隔离 | `docs/adr/0004-gpl-boundary.md` |
| 2026-11-11 | 内容快照签名格式 | `docs/adr/0005-content-signature-format.md` |
| 2026-11-13 | 内容草稿、修订与发布回滚模型 | `docs/adr/0006-content-drafts-and-release-rollback.md` |
| 2026-11-16 | 物品编辑器 schema 与校验 | `docs/adr/0007-item-editor-schema-and-validation.md` |
| 2026-11-16 | 多内容编辑器注册与引用模型 | `docs/adr/0008-multi-content-editor-registry.md` |
| 2026-11-17 | 地图元数据编辑器与嵌套关系边界 | `docs/adr/0009-map-metadata-editor.md` |
| 2026-11-17 | M2 多内容编辑、引用、导入导出与地图关系 | `docs/adr/0010-m2-multi-content-editing-references-import-export-and-map-relations.md` |
| 2026-11-17 | ZoneServer 启动期只读消费签名发布 | `docs/adr/0011-zone-server-read-only-signed-content-release.md` |
| 2026-11-17 | Zone Agent 控制闭环与进程级内容换版 | `docs/adr/0012-zone-agent-process-level-content-switching.md` |
| 2026-11-18 | 区服运行时指标文件桥接 | `docs/adr/0013-zone-runtime-metrics-file.md` |
| 2026-11-18 | 区服指标历史与健康评估 | `docs/adr/0014-zone-metric-history-and-health-evaluation.md` |
| 2026-11-18 | 客户端签名补丁清单与事务更新 | `docs/adr/0015-client-signed-patch-manifest-and-transactional-update.md` |
| 2026-11-19 | 以 VERA20K 为参照的会话边缘与可验证性边界（提议） | `docs/adr/0016-vera20k-benchmark-boundary.md` |
| 2026-11-19 | 原生浏览器客户端与 WebSocket 字节网关 | `docs/adr/0017-browser-client-and-websocket-gateway.md` |
| 2026-11-19 | Web 资源离线编译与版本化 | `docs/adr/0018-web-asset-pipeline.md` |

## 5. 阻塞与风险

| 类型 | 内容 | 影响 | 处理 |
| --- | --- | --- | --- |
| 环境 | 当前环境是 Linux，无法运行 Windows 客户端和 WinForms 宿主 | 只能完成交叉构建，不能做客户端运行冒烟 | 准备 Windows 环境或 Windows CI runner |
| 资源 | 17 张 Jev 地图依赖未覆盖或不完整的私有图库 | 无法宣称 496 张活跃地图全量覆盖；`0`、`2` 缺 `257`，`n0` 缺 `138` | 保留明确排除清单；补齐真实图库后再纳入，不猜测替代文件 |
| 资源 | 真实地图图库已进入 Web 回归，但角色/怪物/NPC 图库、门/光照和音效仍未接入 | 真实动画、混合图块和音效视觉正确性未知；`maplib-0` 单库约 451 MB，纹理内存仍需真机测量 | 继续补业务图库与动画，再做设备内存/性能采样 |
| 版本 | Web 构建哈希已支持区服字面量白名单，但尚未在真实测试区服联调 | 开发环境可用；生产门禁不能仅凭单测宣称闭环 | 在测试区服设置 `VersionHashValues`/`CRYSTAL_ZONE_VERSION_HASH_VALUES`，用真实 Web 构建登录进图 |
| 设备 | 尚无 iPhone/iPad/Android 真机和 Safari 验收 | 触控、WebGL2、DPR、内存和自动播放策略可能暴露问题 | 按 W3 建立设备矩阵和性能/内存记录 |
| 平台 | 客户端和 WinForms 宿主运行依赖 Windows | Linux 已验证区服运行，但客户端登录和进服仍需 Windows | 拆分构建矩阵，Windows runner 做客户端运行验证 |
| CI | 已解决 | run 15 三个 job 全部通过 | 保留 run 15 日志；后续 CI 变更继续以三个 job 为门禁 |
| 运行 | 当前环境是 Linux，未执行 Windows 客户端登录/进服和 WinForms 宿主冒烟 | M0 客户端验收仍不能闭环 | 准备 Windows runner 或测试机 |
| 许可 | `Crystal.MapEditor` 未发现明确许可证 | 不能随发布包分发 | 联系维护者、确认许可证或替换工具 |
| 许可 | 服务端 GPL-3.0-only、素材版权 | 分发和商业化受限 | M0 完成合规审查并保留源码归档流程 |
| 技术 | `Envir`/`Map` 耦合高 | 迁移和热重载风险 | 绞杀者模式 + 黄金样本测试 |
| 技术 | 当前换版为进程级重启 | 激活/回滚有短暂停机，玩家会话会中断 | 使用维护窗口；后续评估状态迁移/双实例 |
| 技术 | 单区只支持一个 Agent 和一个 ZoneServer | 无 Agent HA，租约过期重投依赖单实例假设 | 研究环境接受；生产化前补多实例和幂等执行日志 |
| 安全 | Agent 注册/心跳/领取/结果端点匿名 | 可信内网外暴露会被伪造或接管区服 | 保持内网隔离；生产前补 mTLS、节点绑定、结果签名和授权 |
| 指标 | 指标来自同机 ZoneServer 本地文件，100 ms 采样不保证覆盖每个内部循环；无客户端时只能验证玩家数为 0 | 研究环境趋势可用，但不能替代生产级 Prometheus/OpenTelemetry；真实客户端错误仍需 Windows 冒烟 | 保留 `metricsSource`/`metricsError` 降级标记；后续接远端汇聚、告警和 Windows 冒烟 |
| 客户端更新 | 当前为文件级增量，二进制 delta、并发限速、磁盘空间预检和 launcher 自更新未实现 | 大资源包流量和 launcher 自身升级仍依赖后续工作 | 先用签名 manifest 和事务回滚保证正确性；后续补 delta 与自更新 |
| 平台 | 签名 manifest 和 Launcher 分支只完成 Linux 交叉构建，未在 Windows 运行 | 文件占用、权限、杀软和 WebView2 行为未验证 | 准备 Windows runner 或测试机，用真实 manifest 跑更新冒烟 |
| 许可 | 授权/许可证本轮跳过 | M3 研究闭环不代表可分发结论 | 后续单独合规评审，保留 GPL 源码归档和素材来源清单 |

## 6. 下一步

1. 按 WP1 为 Agent 补充生产化安全边界：mTLS/客户端证书、`agent_id` 与 `zone_id` 节点绑定、结果签名/重放保护和审计。
2. 在 Ubuntu staging 主机实际安装 `crystal-zone-agent.service`，验证开机自启、日志轮转、端口防火墙、SIGTERM、异常退出重启和失败换版恢复。
3. 在 Windows 环境用签名 manifest 跑 `Launcher -> Client` 冒烟，验证文件占用、权限、回滚和 WebView2 行为。
4. 补 launcher 两阶段自更新、二进制 delta、并发限速、磁盘空间预检和渠道/灰度分发。
5. 先新增协议版本协商 ADR，再实现登录握手协议/内容/客户端版本门禁，把启动器 manifest 门禁扩展到服务端。
6. 评估零停机/双实例换版所需玩家状态迁移、会话粘滞和停写协议；当前继续使用维护窗口。
7. 接入 Prometheus/OpenTelemetry 指标汇聚、长期存储和告警；补充 Windows 客户端连接后的非零玩家数与真实错误率验证。
8. 在 Windows 环境启动 `Client`，连接 Linux 区服完成登录、选角、进服、移动、打怪冒烟测试。
9. 授权、许可证和素材合规不纳入当前研究项目计划；Windows 客户端运行资源仍按外部依赖准备。
10. 若 ADR 0016 通过评审，按 `docs/architecture/08-vera20k-benchmark-and-refactor.md` 的 P0 先补预会话握手、玩家只读接口、节点目录和版本兼容矩阵。
11. 浏览器轨道：真实 479 张地图 + 45 个地图库已确定性编译，V5/V1/V100 严格资源加载冒烟和真实 ZoneServer `ClientVersion` 门禁已通过；下一步用真实账号完成登录/选角/建角/进图/移动回归，并补齐剩余 17 张地图的真实图库或明确保持排除。
12. 补真实角色/怪物/NPC 图集动画、地图门/混合图块/光照和音效，再执行 W3 的 Safari、iPhone、Android、帧率、30 分钟内存和视觉回归。

## 7. 更新规则

- 每次实质性工作结束，在“工作日志”追加一条记录。
- 更新“当前状态”和“里程碑状态”，不要只写“进行中”。
- 新决策写入“决策记录”并新增 ADR。
- 阻塞项解决后移到历史记录或标注已解决。
- 记录验证方式和未验证项，不能把“未测试”写成“已完成”。

