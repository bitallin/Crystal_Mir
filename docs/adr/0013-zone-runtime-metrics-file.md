# 0013 区服运行时指标文件桥接

- 状态：已接受
- 日期：2026-11-18

## 背景

Control API 已通过 `zone` 投影保存玩家数、Tick P95 和错误率，Control.Web 也需要展示区服健康状态。但
`Crystal.ZoneServer` 位于 GPL 边界内，`Crystal.ZoneAgent` 是 MIT 控制面组件，不能链接 ZoneServer 或
`Server/` 程序集。直接在 Agent 中读取游戏主循环既不满足依赖边界，也无法覆盖独立进程的采样窗口。

本轮需要在不改变游戏主循环行为、客户端协议、Control API DTO 和数据库 schema 的前提下，采集真实运行指标，
并让 Agent 以现有心跳字段和 metadata 上报。授权、许可证和素材合规不在本轮范围。

## 决策

### 1. 指标采样与编解码放在 GameCore

新增 `src/Crystal.GameCore/RuntimeMetrics.cs`，只依赖 BCL 和 `Crystal.Shared`：

- `RuntimeMetricsSampler` 使用 60 秒滑动窗口，ZoneServer 每 100 ms 采样一次。
- 世界循环样本按 `completedTicks` 去重；同一已完成 Tick 不重复计入 P95。
- Tick P95 使用最近秩法：排序升序后取 `ceil(n * 0.95) - 1`。
- 错误率按窗口首尾计数增量计算：`min(1, deltaErrorCount / deltaCompletedTicks)`；没有 Tick 增量但出现异常时取 1，
  没有样本或没有异常时取 0。
- `ZoneRuntimeMetricsJson` 提供版本化 JSON 序列化和校验。

### 2. ZoneServer 只增加计数和本地文件输出

`Envir` 只增加已完成世界循环计数，在当前一轮对象遍历重新开始时递增；`MessageQueue.Enqueue(Exception)` 只增加异常计数，
不改变消息入队、日志或异常处理逻辑。

ZoneServer 新增 `--metrics-file <path>` 和 `CRYSTAL_ZONE_METRICS_FILE`。ready 后由独立异步循环每秒原子写入指标文件，
退出时删除。写入失败只记录日志，不终止游戏主循环或区服进程。

指标文件字段固定为：

```json
{
  "schemaVersion": 1,
  "zoneId": "zone-1",
  "pid": 1234,
  "processStartedAt": "2026-11-18T00:00:00Z",
  "observedAt": "2026-11-18T00:00:10Z",
  "playerCount": 37,
  "tickP95Ms": 42.5,
  "errorRate": 0.03,
  "completedTicks": 12345,
  "errorCount": 7,
  "sampleCount": 600,
  "windowSeconds": 60
}
```

### 3. Agent 读取并校验本地文件

Agent 将 `state/metrics.json` 传给 ZoneServer，并在每次心跳时读取。只有同时满足以下条件才采用文件值：

- `schemaVersion` 等于当前支持版本。
- `zoneId` 与 Agent 配置一致。
- `pid` 与当前 ZoneServer 子进程一致。
- `observedAt` 不超过 15 秒，且不在未来超过 5 秒。
- `playerCount`、`tickP95Ms`、`errorRate`、计数和窗口在合法范围内。

无效或过期时，心跳的玩家数、Tick P95 和错误率上报 0，并设置 `metricsSource=unavailable`。子进程已停止时设置
`metricsSource=process-stopped`。连续三次读取失败且子进程仍在运行时，Agent 心跳降级为 `degraded`。心跳 metadata
继续保留 `restartFailures`、`zonePid` 和发布目录字段，并新增：

- `metricsSource=zone-runtime|unavailable|process-stopped`
- `metricsObservedAt`
- `metricsWindowSeconds`
- `metricsSampleCount`
- `metricsError`（仅失败时）

### 4. 控制面与数据库保持现状

Control API 不新增路由，不修改 `AgentHeartbeatRequest`、`ZoneDto` 或数据库 schema。Control.Web 只使用现有
`GET /zones`、`GET /zones/{id}`、`GET/POST /zones/{id}/commands` 和发布列表接口。

## 备选方案

1. 在 ZoneServer 内暴露 Prometheus/HTTP 端点：需要新增监听、认证和网络边界，本轮不引入。
2. Agent 解析 ZoneServer 日志：格式脆弱、采样不准确，且容易误判异常，拒绝。
3. Agent 直接引用 ZoneServer 或 `Server/`：破坏 GPL/MIT 边界，拒绝。
4. 共享内存或命名管道：需要跨进程协议、权限和清理策略，当前收益不足，拒绝。
5. 修改 Control API DTO 或数据库 schema：无必要，现有心跳字段和 metadata 已能表达指标来源，拒绝。

## 影响

- 控制面可以看到真实玩家数、Tick P95、错误率和重启失败次数，而不是固定 0。
- 指标文件是同一主机上的非权威缓存；Agent 必须校验 PID、zoneId 和时效，不能信任过期文件。
- 100 ms 采样能反映研究环境的趋势，但不保证覆盖每个内部循环；错误率是窗口近似值。
- 无客户端连接时玩家数只能验证为 0；非零玩家数和真实客户端错误仍需 Windows 冒烟验证。
- 指标写入失败不影响区服运行，但 Control.Web 会显示 `unavailable` 或降级状态。
- 不改变客户端协议、Control API DTO、数据库 schema、进程级换版模型或授权边界。

## 重新评估条件

- 需要生产级 Prometheus、OpenTelemetry 或远端指标汇聚时。
- 需要精确覆盖率、分位数或按地图/线程细分指标时。
- 需要多 Agent HA、跨主机指标聚合或指标签名时。
- 需要把指标写入权限纳入正式安全模型时。
