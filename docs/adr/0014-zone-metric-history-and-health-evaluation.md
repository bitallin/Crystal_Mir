# 0014 区服指标历史与健康评估

- 状态：已接受
- 日期：2026-11-18

## 背景

0013 已经让 ZoneServer 写出运行时指标，并让 Agent 通过心跳把玩家数、Tick P95、错误率和重启失败次数上报到
Control API。当前 `zone` 表只保留最后一次心跳，Control.Web 只能看到瞬时值，无法判断趋势、无法回溯故障窗口，
也没有统一的健康判定规则。

本轮按研究项目口径继续补齐 M3 的可观测闭环，跳过授权、许可证和素材合规。目标是在不改变 Agent 协议、
ZoneServer 指标文件格式和现有 `ZoneDto` 的前提下，持久化心跳样本并在 Control API 中计算健康状态与告警。

## 决策

### 1. 心跳写入指标样本

新增 `zone_metric_sample` 表，字段包括：

- `zone_id`、`observed_at`、`recorded_at`、`status`。
- `active_release_id`、`active_content_hash`。
- `player_count`、`tick_p95_ms`、`error_rate`。
- 从 metadata 提取的 `metrics_source`、`restart_failures`、`zone_pid`。
- 原始 `metadata` JSONB，便于后续扩展而不改表。

Control API 处理心跳时，在同一条数据库事务中更新 `zone` 投影并插入样本。`(zone_id, observed_at)` 建立唯一索引，
重复心跳使用 `ON CONFLICT DO NOTHING` 幂等处理。`observed_at` 缺失的请求按 400 拒绝，不写入历史。

### 2. 新增只读指标查询接口

新增：

```text
GET /api/v1/zones/{zoneId}/metrics?from=<ISO8601>&to=<ISO8601>&limit=<1..2000>
```

- `to` 默认当前时间，`from` 默认 `to - 1 小时`。
- 查询窗口最大 7 天，默认返回窗口内最近 300 条样本，按 `observed_at` 升序输出。
- 响应包含 `items`、`summary` 和 `health`；未知区服返回 404，非法时间窗口或 limit 返回 400。
- 该接口沿用现有区服只读接口的匿名访问口径，不新增认证模型。

`summary` 只基于本次返回样本计算，至少包含样本数、首末时间、最新值、平均值、最大值；空样本返回零值而不是错误。

### 3. 健康状态实时计算

Control API 不持久化告警状态，而是每次查询时用最新 `zone` 投影、最新样本和配置阈值计算：

- `critical`：区服离线、Tick P95 达到 critical 阈值、错误率达到 critical 阈值。
- `warning`：区服 degraded、指标来源不是 `zone-runtime`、Tick P95 或错误率达到 warning 阈值、重启失败次数大于 0。
- `healthy`：没有上述告警。

告警代码固定为 `zone.offline`、`zone.degraded`、`metrics.unavailable`、`runtime.tick_p95`、
`runtime.error_rate`、`agent.restart_failures`。阈值通过 `ControlApi:ZoneTickP95WarningMs`、
`ControlApi:ZoneTickP95CriticalMs`、`ControlApi:ZoneErrorRateWarning`、`ControlApi:ZoneErrorRateCritical`
配置，默认分别为 50ms、100ms、5% 和 20%。

### 4. Web 展示趋势和告警

Control.Web 的区服详情页增加指标趋势与健康告警区域，读取新增 metrics 接口，不修改现有区服列表、命令接口或
`ZoneDto`。没有样本时显示空状态；指标加载失败不影响区服详情和命令操作。

### 5. 本轮不做的内容

- 不实现告警状态机、告警去重、通知渠道、值班和静默规则。
- 不把指标文件升级为远程签名数据源，也不改变 Agent 心跳协议。
- 不在本切片中实现 Prometheus/OpenTelemetry 汇聚、分区表或自动保留策略；样本表按时间索引，后续可接外部
  保留任务或分区迁移。

## 备选方案

1. 只保留最后一次心跳：实现最小，但无法展示趋势或回溯故障窗口，拒绝。
2. 让 Agent 直接写 PostgreSQL：破坏控制面/数据面边界，也扩大 Agent 权限，拒绝。
3. 在 ZoneServer 暴露 Prometheus 端点：需要新增监听、认证和网络规则，本轮不引入。
4. 把健康等级写回 `zone.status`：混淆 Agent 上报状态和平台判定，且会改变现有区服 DTO 语义，拒绝。

## 影响

- 区服详情可以看到真实指标趋势和统一告警，不依赖静态样例。
- 心跳写入多一次样本插入，但复用同一事务和现有连接；唯一索引保证重复上报幂等。
- `zone_metric_sample` 会持续增长，研究环境先依赖时间索引和外部清理；生产化前必须补保留策略或分区。
- 不改变客户端协议、Agent 协议、ZoneServer 指标文件和现有数据库表。

## 重新评估条件

- 需要接入 Prometheus、OpenTelemetry、告警通知或长期归档时。
- 样本量或查询延迟需要分区、降采样或预聚合时。
- 需要跨主机 Agent、mTLS 或指标签名时。
- 需要把平台健康状态与 Agent 上报状态分离为独立状态机时。
