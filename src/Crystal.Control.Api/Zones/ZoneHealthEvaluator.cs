using System.Globalization;
using System.Text.Json;
using Crystal.Agent.Contracts;

namespace Crystal.Control.Api.Zones;

public static class ZoneHealthLevels
{
    public const string Healthy = "healthy";
    public const string Warning = "warning";
    public const string Critical = "critical";
}

public static class ZoneAlertSeverities
{
    public const string Warning = "warning";
    public const string Critical = "critical";
}

public static class ZoneHealthEvaluator
{
    public static ZoneHealthDto Evaluate(
        ZoneDto zone,
        IReadOnlyList<ZoneMetricSampleDto> samples,
        DateTimeOffset now,
        ZoneHealthThresholds thresholds)
    {
        var alerts = new List<ZoneAlertDto>();
        var lastSample = samples.Count > 0 ? samples[^1] : null;
        var metricsSource = ReadMetadataString(zone.Metadata, "metricsSource") ?? lastSample?.MetricsSource;
        var restartFailures = ReadMetadataInt(zone.Metadata, "restartFailures") ?? lastSample?.RestartFailures ?? 0;
        var observedAt = zone.LastHeartbeatAt ?? lastSample?.ObservedAt;

        if (zone.Status == AgentStatuses.Offline)
        {
            alerts.Add(new ZoneAlertDto(
                "zone.offline",
                ZoneAlertSeverities.Critical,
                zone.LastHeartbeatAt is null
                    ? "区服尚未上报心跳。"
                    : "区服心跳已超过 30 秒，Control API 已投影为离线。",
                observedAt,
                null));
        }
        else if (zone.Status == AgentStatuses.Degraded)
        {
            alerts.Add(new ZoneAlertDto(
                "zone.degraded",
                ZoneAlertSeverities.Warning,
                "Agent 上报区服为降级状态。",
                observedAt,
                null));
        }

        if (!string.Equals(metricsSource, "zone-runtime", StringComparison.Ordinal))
        {
            alerts.Add(new ZoneAlertDto(
                "metrics.unavailable",
                ZoneAlertSeverities.Warning,
                metricsSource == "process-stopped"
                    ? "ZoneServer 进程已停止，运行时指标不可用。"
                    : "最近心跳没有可用的 ZoneServer 运行时指标。",
                observedAt,
                metricsSource));
        }

        AddThresholdAlert(
            alerts,
            "runtime.tick_p95",
            zone.TickP95Ms,
            thresholds.TickP95WarningMs,
            thresholds.TickP95CriticalMs,
            observedAt,
            value => $"Tick P95 达到 {value.ToString("0.0", CultureInfo.InvariantCulture)} ms，超过阈值。",
            "ms");
        AddThresholdAlert(
            alerts,
            "runtime.error_rate",
            zone.ErrorRate,
            thresholds.ErrorRateWarning,
            thresholds.ErrorRateCritical,
            observedAt,
            value => $"错误率达到 {(value * 100).ToString("0.00", CultureInfo.InvariantCulture)}%，超过阈值。",
            "ratio");

        if (restartFailures > 0)
        {
            alerts.Add(new ZoneAlertDto(
                "agent.restart_failures",
                ZoneAlertSeverities.Warning,
                $"Agent 记录到 {restartFailures} 次重启失败。",
                observedAt,
                restartFailures.ToString(CultureInfo.InvariantCulture)));
        }

        var level = alerts.Any(alert => alert.Severity == ZoneAlertSeverities.Critical)
            ? ZoneHealthLevels.Critical
            : alerts.Count > 0
                ? ZoneHealthLevels.Warning
                : ZoneHealthLevels.Healthy;

        return new ZoneHealthDto(
            zone.ZoneId,
            level,
            alerts
                .OrderByDescending(alert => alert.Severity == ZoneAlertSeverities.Critical)
                .ThenBy(alert => alert.Code, StringComparer.Ordinal)
                .ToArray(),
            now);
    }

    private static void AddThresholdAlert(
        ICollection<ZoneAlertDto> alerts,
        string code,
        double value,
        double warningThreshold,
        double criticalThreshold,
        DateTimeOffset? observedAt,
        Func<double, string> message,
        string unit)
    {
        if (value < warningThreshold) return;
        alerts.Add(new ZoneAlertDto(
            code,
            value >= criticalThreshold ? ZoneAlertSeverities.Critical : ZoneAlertSeverities.Warning,
            message(value),
            observedAt,
            $"{value.ToString(CultureInfo.InvariantCulture)} {unit}".Trim()));
    }

    private static string? ReadMetadataString(JsonElement? metadata, string name)
    {
        if (metadata is not { ValueKind: JsonValueKind.Object } value) return null;
        return value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static int? ReadMetadataInt(JsonElement? metadata, string name)
    {
        if (metadata is not { ValueKind: JsonValueKind.Object } value) return null;
        if (!value.TryGetProperty(name, out var property)) return null;
        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number)) return number;
        return property.ValueKind == JsonValueKind.String &&
               int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
            ? number
            : null;
    }
}
