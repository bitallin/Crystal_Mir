# 里程碑

> 最后更新：2026-11-18
> 状态：生效

## 0. 估算假设

- 以现有 Crystal 游戏逻辑为基础，不重写核心战斗/地图/AI。
- 团队 3～4 人，包含后端、前端、游戏服务端和运维能力。
- 外部数据库、地图和素材可以获取并合法使用。
- 估算用于排期，不是交付承诺；每个里程碑结束重新评估。

## M0 基线与合规

**目标**：让当前项目可重复构建、可运行、可验证，并明确许可证和资源边界。

**交付物**

- Windows 构建脚本和最小 CI。
- 标准 `Server.MirDB`、地图、客户端资源清单。
- 服务端/客户端冒烟测试清单。
- 许可证和素材来源清单。
- ADR 0001～0004 评审通过。

**验收标准**

- 干净环境可以构建 `Server`、`Server.MirForms`、`Client`。
- 服务端可加载标准数据库并启动。
- 客户端可登录、选角、进入游戏。
- 黄金样本数据库和关键截图/日志归档。

**依赖/风险**：Windows 构建环境；外部资源获取；GPL/素材合规。

**估算**：2～4 周。

## M1 架构骨架与数据底座

**状态**：已完成（2026-11-13，代码、Linux 构建、PostgreSQL 16 集成和 Compose 冒烟口径）。
Windows 客户端登录进服、真实 Ubuntu/systemd 安装、MapEditor 与素材授权仍作为 M0/外部阻塞跟踪。

**目标**：建立控制面/数据面骨架，打通二进制数据库到 PostgreSQL 和内容快照的迁移路径。

**交付物**

- `Crystal.Shared`、`Crystal.Content`、`Crystal.GameCore`、`Crystal.ZoneServer`、`Crystal.Agent.Contracts`、`Crystal.Control.Api`、`Crystal.Control.Web` 项目骨架。
- PostgreSQL schema 和迁移工具。
- `LegacyBinaryImporter` / `LegacyBinaryExporter`。
- 内容包格式、manifest、哈希和基本签名。
- Docker Compose：PostgreSQL、Redis、S3 兼容存储（本地/CI 使用 S3Mock）、API、Web。
- 登录、RBAC、审计日志骨架。

**验收标准**

- 导入标准 `Server.MirDB`，实体数量、索引和关键字段与旧实现一致。
- 能导出旧二进制数据库并启动旧服务端。
- API 可只读查询物品/怪物/地图。
- 后台可登录并看到内容列表。
- 可从导入批次创建草稿，逐实体修改并处理 revision 冲突。
- 可从草稿创建不可变版本，完成校验、编译、签名、发布和回滚。
- `docker compose` 可启动 PostgreSQL 16、Redis 7、S3Mock、API 和 Nginx Web，并通过同源 `/api` 登录和查询内容。

**已知边界**：M1 不包含类型化编辑器、Zone Agent 服务、真实区服热切换、Windows 客户端冒烟和素材授权。

**依赖/风险**：旧数据库字段映射；`Envir` 耦合；PostgreSQL schema 设计。

**估算**：4～6 周。

## M2 内容后台 MVP

**状态**：已完成（2026-11-17，完成本轮内容后台范围）。

**目标**：让版本作者在 Web 后台完成核心内容编辑、校验和发布。

**完成情况**：Control API 注册 `item`、`monster`、`map`、`npc`、`script`、`drop`、`magic`、`quest`、
`gameshop` 和 `recipe` 十类编辑器 Schema，完成服务端身份解析、保存前规范化、引用规则、标签/搜索字段、
结构校验和完整性校验。Web 支持全部十类内容的草稿内新建、编辑、删除、字段差异、引用选择器、脚本源码与只读
AST、嵌套掉落组编辑、400 字段错误和 409 revision 冲突恢复。草稿 JSONL 导入默认 dry-run，应用模式单事务
按内容键 upsert；导出与快照 JSONL 行格式一致。地图传送、安全区、矿区、激活坐标支持整体替换，刷新点和 NPC
支持行级 CRUD 并同步地图投影；地图预览返回降采样可走/阻挡/门格。`Crystal.ZoneServer` 支持启动期验证签名
发布、重建旧库并物化内容，legacy 启动模式保持兼容。

**交付物**

- 物品、怪物、地图元数据、NPC、掉落、技能、任务、商城、配方 CRUD。
- Schema 驱动表单、搜索、引用选择器、批量导入导出。
- NPC DSL 解析器、AST、校验、文本编辑器和基础可视化块。
- 内容草稿、校验、编译、发布、回滚。
- 区服加载已发布内容包的只读路径。

**验收标准**

- 在后台创建/修改十类核心内容，包括物品、怪物、地图、NPC、脚本、掉落、技能、任务、商城和配方。
- 发布一个新内容版本，区服加载后行为正确。
- 出现错误时禁止发布；发布失败可回滚。
- 旧二进制导入内容可在后台正常编辑。

**本轮边界**：授权、许可证、素材合规、客户端 `.lib` 图库解析、资源上传、Windows 客户端冒烟、systemd 实际安装、
Zone Agent、热切换和灰度发布不在本轮范围。

**依赖/风险**：内容字段建模；脚本 DSL 复杂度；资源预览。

**估算**：8～12 周。

## M3 热更新与区服 Agent

**状态**：进行中（2026-11-18，服务端控制闭环、真实运行时指标、指标历史/健康评估、Control.Web 区服闭环和客户端签名补丁核心完成本轮研究范围；整个 M3 尚未收口）。
二进制 delta、灰度/CDN、进程内热切换、玩家状态迁移、外部 staging 验证、launcher 自更新和 Windows 运行冒烟仍需后续完成。

**目标**：客户端和服务端内容可安全、可回滚地更新。

**本轮已完成**

- `Crystal.ZoneAgent` 独立进程：注册、心跳、命令领取、结果持久化和断线重报。
- Control API `zone`/`zone_command` schema，区服投影、命令租约、过期、单生命周期互斥和结果幂等。
- 签名发布 ZIP 下载，路径穿越拒绝，manifest/实体/内容哈希和 ECDSA 验签，原子本地 staging。
- 进程级 `stage`、`activate`、`rollback`、`reload`、`stop`；SIGTERM、ready 文件、候选失败恢复上一版本。
- ZoneServer `--ready-file`/`CRYSTAL_ZONE_READY_FILE`，legacy 启动模式和客户端协议保持兼容。
- ZoneServer `--metrics-file`/`CRYSTAL_ZONE_METRICS_FILE` 与 GameCore 60 秒指标采样器；Agent 校验 schema/zoneId/PID/时效后
  通过现有心跳上报真实玩家数、Tick P95、错误率和指标来源，stop 后归零并标记 `process-stopped`。
- Control API 在同一心跳事务幂等写入 `zone_metric_sample`，新增窗口化 metrics 查询、汇总和
  `healthy|warning|critical` 实时健康评估；Control.Web 展示三类趋势和告警代码。
- Control.Web 区服总览/详情接入真实 API，展示指标、active/staged 版本、内容哈希、Agent/进程状态、最后心跳和命令历史；
  `zone:write` 用户可执行 stage、activate、rollback、reload、stop，离线或生命周期命令在途时禁用并显示 409 冲突。
- `scripts/zone-control.sh`、`crystal-zone-agent.service` 和 Linux 运行手册。
- PostgreSQL 16 集成测试与 Zone Agent 端到端测试，覆盖篡改包、启动失败恢复、重复结果、Agent 重启恢复、
  真实指标文件、旧 PID 指标拒绝和 stop 归零；Control.Web Playwright 覆盖真实区服页、五类命令、只读和 409。
- MIT `Crystal.Patcher`：ECDSA P-256 签名 manifest、canonical JSON、keyId、渠道/版本/有效期/降级门禁、路径穿越拒绝。
- SHA256 文件级增量：只下载新增或变化文件，HTTP Range 续传；`.crystal-update` staging、备份、pending 事务、
  失败恢复和最近一次更新回滚；未知本地文件不删除。
- `Crystal.PatcherTool` 与 `scripts/test-client-updates.sh`：覆盖 `pack -> sign -> verify -> apply -> state -> rollback`。
- 旧 WinForms Launcher 在配置 `Launcher:ManifestUrl` 后接入签名更新；未配置时 `PList.gz` 路径保持兼容。

**仍待完成**

- 二进制 delta、并发限速、磁盘空间预检和 launcher 两阶段自更新。
- 发布服务：渠道、灰度、CDN、版本分布统计。
- 进程内热切换、玩家状态迁移、零停机双实例和多 Agent HA。
- 登录协议/内容/客户端版本握手和服务端旧客户端门禁（当前仅启动器 manifest 门禁）。
- Ubuntu staging 上的实际 systemd 安装、开机自启、日志轮转和故障演练。
- Prometheus/OpenTelemetry 远端指标汇聚、告警通知、长期归档和样本保留策略。
- 授权、许可证和素材合规（按研究项目口径本轮跳过）。

**剩余执行计划（研究项目口径，授权、许可证和素材合规不纳入）**

| 顺序 | 工作包 | 范围 | 验收/验证 | 依赖 |
| --- | --- | --- | --- | --- |
| WP0 | CI 全绿 | 修复 `DOTNET` 命令名解析和 Control API 镜像缺少 `Crystal.Agent.Contracts`；推送后复跑三个 job | `build-test`、`control-api-integration`、`compose-stack` 全部 success | 提交并推送 |
| WP1 | Agent 生产安全边界 | mTLS/客户端证书、`agent_id` 与 `zone_id` 节点绑定、结果签名、重放保护和审计 | 伪造注册、跨区领取、重复结果和篡改结果均被拒绝，并保留成功路径 E2E | WP0 |
| WP2 | 客户端更新加固 | 二进制 delta、并发限速、磁盘空间预检、launcher 两阶段自更新 | Linux CLI 覆盖失败回滚；Windows 验证文件占用、权限和 launcher 自升级 | WP0；Windows 主机用于运行验证 |
| WP3 | 登录版本握手 | 协议、内容 schema、客户端版本和渠道握手；服务端旧客户端门禁 | 旧客户端被拒绝，新客户端正常登录、选角和进服 | 先新增协议版本协商 ADR |
| WP4 | 外部 staging 与 systemd | 真实安装 `crystal-zone-agent.service`，验证开机自启、日志轮转、防火墙、SIGTERM、异常重启和换版恢复 | staging 主机演练记录和日志证据 | Ubuntu staging 主机 |
| WP5 | Windows 端到端 | Launcher 签名更新后启动 Client，完成登录、选角、进服、移动和打怪 | Windows runner/测试机冒烟记录 | 客户端 `Data/Map/Sound` 等运行资源 |
| WP6 | 可观测性与告警 | Prometheus/OpenTelemetry 汇聚、告警通知、长期归档和样本保留策略 | 指标可远端查询，告警可触发并可恢复 | WP4 |
| WP7 | 零停机与高可用 | 玩家状态迁移、双实例切换、会话粘滞、停写协议和多 Agent HA | 维护窗口外完成换版且会话可恢复 | WP1、WP4 |

WP4、WP5 受外部主机和资源阻塞，不阻止 WP1、WP2、WP3、WP6 在本地继续研究和验证。

**验收标准**

- 修改图库或地图资源后，客户端可通过启动器更新。
- 发布内容包后，区服无需重装即可加载新内容。
- 模拟下载失败、签名失败、加载失败，均能回滚。
- 旧客户端被版本门禁正确拦截。

**依赖/风险**：现有补丁格式兼容；进程级换版的停机窗口；客户端启动器改造；状态迁移和外部部署验证。

**估算**：6～10 周。

## M4 运营 MVP

**目标**：支持开区、GM、支付和基础合区。

**交付物**

- 区服模板、开区/关区/维护编排、健康门禁。
- GM 工具：公告、踢人、封禁、邮件、发物品、传送、刷怪、重载。
- 支付订单、回调验签、账本、Outbox 发货、对账。
- 合区预检、ID 映射、名称冲突、冻结/备份/校验/切换/回滚。
- 监控仪表盘和告警。

**验收标准**

- 通过后台开出一个新区服并完成冒烟测试。
- 测试支付订单可幂等发货，重复回调不重复发放。
- 两个测试区完成合区演练，校验报告无 error。
- GM 操作全部有审计记录。

**依赖/风险**：区服有状态部署；合区 ID 冲突；支付合规和对账。

**估算**：8～12 周。

## M5 生产加固

**目标**：达到可持续运行的生产标准。

**交付物**

- 控制面多实例、数据库主备/PITR、对象存储版本化。
- 压测、浸泡、故障注入和恢复演练。
- 安全加固：MFA、RBAC、WAF、限流、脚本沙箱、依赖和许可证扫描。
- 日志/指标/追踪/告警全覆盖。
- 运行手册和回滚手册。
- GPL 源码归档和第三方 notice 流程。

**验收标准**

- 通过目标并发压测和 72 小时浸泡测试。
- 完成数据库故障切换、区服故障、补丁事故演练。
- 安全扫描无阻断级问题。
- 备份可在目标 RTO 内恢复。

**依赖/风险**：生产环境资源；安全评审；运维流程。

**估算**：8～12 周。

## M6 通用化与生态

**目标**：第三方可以基于模板创建自己的版本，减少改代码。

**交付物**

- Lua 脚本 VM 和稳定游戏 API。
- 事件/活动/AI/任务扩展点。
- 插件机制和内容类型扩展。
- 版本模板、CLI、SDK、示例版本。
- 多版本/多租户（可选）。

**验收标准**

- 不修改引擎代码，通过后台和脚本创建一个可玩的定制版本。
- 第三方可按文档完成本地开发和部署。
- 插件升级不影响核心区服。

**依赖/风险**：脚本安全；API 稳定性；文档和生态投入。

**估算**：持续迭代，首版 12～20 周。

## MVP 边界

浏览器直玩是独立并行轨道，不重编号 M0～M6；范围、验收和估算见 `docs/roadmap/browser-client.md` 与 ADR 0017/0018。

MVP 包含 M0～M3，加上 M4 中的 GM 和基础监控。暂不做：

- 多租户计费。
- 旧 Windows 客户端之外的全功能跨平台客户端；浏览器首版只覆盖需求文档中的可玩 MVP。
- 任意 UI 拖拽布局。
- 全自动合区（先做半自动 + 人工确认）。
- 完整插件市场。

