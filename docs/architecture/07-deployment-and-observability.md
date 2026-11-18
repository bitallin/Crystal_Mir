# 部署与可观测性

> 最后更新：2026-11-18
> 状态：生效

## 0. 当前实现（2026-11-10）

旧服务端库已可在 Ubuntu 上由 `src/Crystal.ZoneServer` 独立托管。当前实现包括：

- 自包含 `linux-x64` 发布产物，不要求目标机安装 .NET。
- systemd 单元 `deploy/systemd/crystal-zone.service` 和 `scripts/install-zone.sh`。
- 内容根目录与程序目录分离；数据库和 `Envir/` 等运行数据位于可写目录。
- 游戏端口、状态端口、最大用户数、线程数和保存间隔可由环境变量覆盖。
- 日志同时写控制台和 `Logs/zone.log`，SIGTERM 触发保存和优雅退出。
- 内容引用统一处理 Windows 反斜杠和大小写差异，适配 Linux 大小写敏感文件系统。

运维步骤见 `docs/runbooks/zone-server-linux.md` 和 `docs/runbooks/zone-agent-linux.md`。ZoneServer 已通过本地
指标文件向 Agent 提供真实玩家数、Tick P95 和错误率，再由现有心跳写入 Control API；Prometheus/OpenTelemetry
远端汇聚尚未接入。

### 0.1 ZoneServer 发布模式（M2）

已签名的发布版本可在启动期通过以下参数消费：

```bash
Crystal.ZoneServer \
  --content-root /srv/crystal/content \
  --content-release /srv/crystal/releases/2026.11.17.1 \
  --content-public-key /etc/crystal/content-public.pem \
  --content-key-id content-release-2026-01
```

也可使用 `CRYSTAL_ZONE_CONTENT_RELEASE`、`CRYSTAL_ZONE_CONTENT_PUBLIC_KEY` 和
`CRYSTAL_ZONE_CONTENT_KEY_ID`。启动时先校验 manifest/实体哈希和 ECDSA 签名，再在系统临时目录重建旧库并物化
`drop`、`recipe`、`script`、`quest`；地图、配置和其余运行文件从 `--content-root` 链接或复制。发布目录和源内容根
不被修改。签名缺失、快照篡改或物化失败会阻止启动和 ready。未传发布参数时继续使用 legacy 二进制库模式。

该模式本身不含 Agent、热切换、灰度或在线回滚；换版需要重启区服。冒烟覆盖 legacy、签名发布、未签名、快照篡改和缺失地图
五条路径，见 `scripts/zone-smoke.sh`。

### 0.1.1 Zone Agent 进程级换版（M3）

每个区服由一个 `Crystal.ZoneAgent` 监督一个 `Crystal.ZoneServer` 子进程：

```text
Crystal.ZoneAgent (MIT)
  -> Control API: register / heartbeat / claim / result
  -> Control API: release artifact ZIP
  -> releases/{releaseId}-{contentHash}
  -> SIGTERM old child
  -> new child: --content-release ... --ready-file state/ready.json --metrics-file state/metrics.json
  -> read/validate state/metrics.json on every heartbeat
```

- Agent 目录：`releases/`、`staging/`、`state/active.json`、`state/ready.json`、`state/metrics.json`、
  `state/command-results/`、`logs/`。
- 候选 ready 失败时自动恢复上一版本；ZoneServer 意外退出时按退避策略重启当前 active 版本。
- 允许短暂停机，不做双实例、玩家状态迁移、灰度或 CDN。
- Agent 不监听入站控制端口，主动访问 Control API；systemd 单元为 `deploy/systemd/crystal-zone-agent.service`。
- Agent 校验指标文件 schema、zoneId、PID 和 15 秒时效；无效/过期时上报 0 并标记 `unavailable`，
  子进程停止时标记 `process-stopped`，连续三次失效且子进程仍在运行则降级。
- 原有 `deploy/systemd/crystal-zone.service` 保留给 legacy/直接启动模式。

### 0.2 控制面 Compose 栈（2026-11-13）

根目录 `compose.yaml` 提供可在 Linux 容器中验证的 M1 控制面栈：

```text
control-web (Nginx, 同源 /api 与 /health 代理)
  -> control-api (.NET 8, 内部 8080)
       -> postgres 16
       -> db-migrate（一次性，按顺序执行 db/migrations/*.sql）
       -> release-artifacts 卷
       -> signing key 只读挂载
redis 7
minio + minio-init
```

- `deploy/docker/control-api.Dockerfile` 使用 .NET 8 多阶段构建，运行用户为非 root 的 `app`。
- `deploy/docker/control-web.Dockerfile` 用 Nginx 托管静态后台；`/api/` 和 `/health/` 反向代理到
  `control-api:8080`，浏览器默认使用同源 API，不再需要 CORS。
- PostgreSQL 数据、Redis 数据、MinIO 数据和发布产物分别使用独立命名卷；资源目录以只读 bind mount 提供。
- JWT、数据库、MinIO 和 bootstrap 密码通过 `.env` 注入；`.env` 不提交仓库。示例见 `.env.example`。
- ECDSA 发布私钥只通过运行时只读目录挂载（`CRYSTAL_CONTROL_SIGNING_DIR`），不进入镜像或数据库。
  可用 `scripts/generate-release-keys.sh` 生成 `private.pem` 和 `public.pem`。
- Compose 不启动 GPL 的 `Crystal.LegacyImporter`。示例内容由宿主机导入，测试脚本 `scripts/test-compose.sh`
  会启动栈、等待健康检查、在宿主机导入黄金样本，再通过 Web 同源代理登录并查询内容。
- 默认 Compose 网络为 `172.28.0.0/16`，可用 `CRYSTAL_COMPOSE_SUBNET` 覆盖。若运行环境无法使用
  主机端口映射，测试脚本会改用 Nginx 容器内同源代理，并用一次性运行时容器在 Compose 网络内执行
  宿主机已构建的导入器；这不会把 GPL 代码加入控制面镜像。
- 启动时 `db-migrate` 先于 API 完成；迁移脚本是幂等的，导入器仍可按校验和重复应用。

常用命令：

```bash
cp .env.example .env
./scripts/generate-release-keys.sh
docker compose up -d --build
docker compose ps
docker compose logs -f control-api
```

Compose 是开发/集成环境基线，不等同于生产编排；生产仍需外部负载均衡、TLS、对象存储、
密钥管理、数据库备份和区服 Agent。

## 1. 环境

| 环境 | 用途 | 数据 |
| --- | --- | --- |
| `local` | 开发、单机调试 | 本地数据库/对象存储 |
| `dev` | 联调、自动部署 | 可重置 |
| `staging` | 发布前验证、迁移演练 | 脱敏数据 |
| `prod` | 正式运行 | 生产数据 |

环境之间网络、数据库、密钥和对象存储隔离。禁止从生产直接复制未脱敏数据到开发环境。

## 2. 部署拓扑

### 2.1 开发环境

```text
docker compose
├── postgres
├── redis
├── minio
├── control-api
├── control-web
└── zone-server（可选，本机运行）
```

### 2.2 生产环境

```text
负载均衡 / WAF
├── Admin Web（静态资源 + CDN）
├── Control API（2+ 实例）
├── Zone Agent / Zone Server（每个区服一个实例）
├── PostgreSQL（主备 + PITR）
├── Redis（高可用）
├── MinIO/S3（多副本）
└── 监控栈（Prometheus/Grafana/Loki/Tempo）
```

区服是有状态服务，按区服独立部署和隔离；控制面无状态，可水平扩展。

## 3. 端口与网络

| 服务 | 端口 | 暴露范围 |
| --- | --- | --- |
| Admin Web | 443 | 公网或办公网 |
| Control API | 443/8443 | 管理网 + Web |
| Zone Agent -> Control API | 443/8443 出站 HTTP(S) | 私有控制网；Agent 不监听入站控制端口 |
| Zone Game | TCP 7000+ | 公网/加速网络 |
| Status | 3000+ | 私有监控网 |
| PostgreSQL | 5432 | 私有数据网 |
| Redis | 6379 | 私有数据网 |
| MinIO/S3 | 9000/443 | 私有或 CDN 回源 |

当前服务端默认游戏端口 7000、状态端口 3000，见 `Server/Settings.cs`；正式环境必须改为可配置并纳入区服编排。

Agent 的命令轮询默认 2 秒、心跳 5 秒、租约 60 秒。Control API 命令默认 10 分钟过期，最长 24 小时；
同一区服同时只允许一个未完成的 activate/rollback/reload/stop 命令。

## 4. 配置管理

- 配置分层：默认值、环境配置、区服配置、运行时覆盖。
- 敏感配置由密钥管理系统注入。
- 配置变更有版本、审计和回滚。
- 区服配置以结构化 JSON/YAML 保存，不直接编辑运行中的文件。
- 环境变量命名统一，例如 `CRYSTAL_DB_CONNECTION`、`CRYSTAL_REDIS_URL`、`CRYSTAL_S3_ENDPOINT`。

## 5. CI/CD

```text
PR -> 构建 -> 单元/集成测试 -> 安全扫描 -> 制品
-> 部署 dev -> 冒烟测试 -> 部署 staging
-> 发布审批 -> 生产灰度 -> 全量 -> 观察 -> 回滚
```

要求：

- 构建可复现，产物带提交哈希和版本号。
- 数据迁移与应用发布分离，先迁移后发布或使用兼容迁移。
- 生产发布支持灰度、暂停和回滚。
- 数据库变更使用迁移工具，禁止手工改生产表。

## 6. 可观测性

### 6.1 日志

- 结构化 JSON 日志，统一字段：`timestamp`、`level`、`service`、`zone_id`、`trace_id`、`request_id`、`message`。
- 敏感字段脱敏。
- 日志分级和采样，避免 Tick 日志刷屏。

### 6.2 指标

- 当前链路：ZoneServer 每 100 ms 采样、60 秒窗口、每秒原子写 `state/metrics.json`；Agent 心跳上报
  `playerCount`、`tickP95Ms`、`errorRate`，metadata 带 `metricsSource`、`metricsObservedAt`、
  `metricsWindowSeconds`、`metricsSampleCount` 和 `restartFailures`。
- Tick P95 使用最近秩法；错误率为窗口异常增量/Tick 增量并封顶 1。`restartFailures` 单独展示，不混入错误率。
- Control.Web 区服总览每 5 秒刷新，详情和命令历史每 2 秒刷新，离开路由后清理定时器。
- 后续接入 Prometheus/OpenTelemetry 时沿用 `zone_tick_duration_ms`、`zone_players`、`zone_connections`、
  `content_reload_duration_ms`、`release_failures_total`、`payment_delivery_latency_ms` 等命名。
- 每个区服带 `zone_id` 标签，避免高基数标签滥用。

### 6.3 追踪

- Control API、Agent、Zone 命令、发布、支付使用 OpenTelemetry。
- Trace 贯穿“后台操作 -> 任务 -> Agent -> 区服 -> 结果”。
- 采样策略按环境和错误率调整。

### 6.4 仪表盘

- 平台总览：区服数、在线、发布、支付、告警。
- 区服详情：Tick、对象、地图、连接、错误。
- 发布看板：版本分布、成功率、回滚。
- 支付看板：订单、成功率、发货延迟、对账差异。
- 安全看板：登录失败、封禁、异常包、WAF。

## 7. 容量规划

- 按区服玩家上限、地图数、怪物数、连接数压测。
- 控制面按管理操作并发和发布包大小规划。
- 对象存储按图库/地图/补丁历史增长规划，设置生命周期策略。
- PostgreSQL 按角色数据和审计日志增长规划分区/归档。
- 预留 30% 以上容量余量。

## 8. 备份与恢复

- 数据库：全量 + WAL，定期恢复演练。
- 对象存储：版本控制，关键发布包保留多个版本。
- 区服配置和内容 manifest 可重建。
- 记录 RPO/RTO，并在文档中维护恢复步骤。

## 9. 运行手册入口

后续在 `docs/runbooks/` 增加（索引见 `docs/runbooks/README.md`）：

- 区服无法启动。
- 内容发布失败。
- 热重载失败回滚。
- 支付回调异常。
- 合区失败恢复。
- 数据库故障切换。
- 客户端补丁事故。

