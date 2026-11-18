using System.Text.Json;
using Crystal.Agent.Contracts;
using Crystal.Control.Api.Zones;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

DateTimeOffset now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
var thresholds = new ZoneHealthThresholds(50, 100, 0.05, 0.20);

ZoneDto Zone(
    string status = AgentStatuses.Online,
    double tick = 20,
    double errorRate = 0.01,
    IReadOnlyDictionary<string, string>? metadata = null)
{
    metadata ??= new Dictionary<string, string> { ["metricsSource"] = "zone-runtime" };
    return new ZoneDto(
        "zone-health",
        "Health Zone",
        "agent-1",
        "0.1.0",
        AgentContractVersion.Current,
        AgentContractVersion.Current,
        status,
        now.AddHours(-1),
        now.AddSeconds(-1),
        10,
        "release-10",
        "hash-10",
        null,
        null,
        null,
        12,
        tick,
        errorRate,
        Array.Empty<string>(),
        metadata is null
            ? null
            : JsonSerializer.SerializeToElement(metadata, AgentContractJson.Options),
        now.AddHours(-2),
        now);
}

var healthy = ZoneHealthEvaluator.Evaluate(Zone(), Array.Empty<ZoneMetricSampleDto>(), now, thresholds);
Check(healthy.Level == ZoneHealthLevels.Healthy, healthy.Level);
Check(healthy.Alerts.Count == 0, $"Healthy zone should not have alerts: {healthy.Alerts.Count}");

var warning = ZoneHealthEvaluator.Evaluate(
    Zone(tick: 75, metadata: new Dictionary<string, string>
    {
        ["metricsSource"] = "zone-runtime",
        ["restartFailures"] = "2"
    }),
    Array.Empty<ZoneMetricSampleDto>(),
    now,
    thresholds);
Check(warning.Level == ZoneHealthLevels.Warning, warning.Level);
Check(warning.Alerts.Any(alert => alert.Code == "runtime.tick_p95" && alert.Severity == ZoneAlertSeverities.Warning),
    "Tick warning alert is missing.");
Check(warning.Alerts.Any(alert => alert.Code == "agent.restart_failures"), "Restart failure alert is missing.");
Check(!warning.Alerts.Any(alert => alert.Code == "metrics.unavailable"), "Valid metrics source must not be unavailable.");

var critical = ZoneHealthEvaluator.Evaluate(
    Zone(
        status: AgentStatuses.Offline,
        tick: 140,
        errorRate: 0.30,
        metadata: new Dictionary<string, string>
        {
            ["metricsSource"] = "unavailable",
            ["restartFailures"] = "3"
        }),
    Array.Empty<ZoneMetricSampleDto>(),
    now,
    thresholds);
Check(critical.Level == ZoneHealthLevels.Critical, critical.Level);
Check(critical.Alerts[0].Severity == ZoneAlertSeverities.Critical, "Critical alerts must sort first.");
Check(new[] { "zone.offline", "runtime.tick_p95", "runtime.error_rate", "metrics.unavailable", "agent.restart_failures" }
    .All(code => critical.Alerts.Any(alert => alert.Code == code)), "Expected critical and warning alert codes are missing.");

var degraded = ZoneHealthEvaluator.Evaluate(
    Zone(
        status: AgentStatuses.Degraded,
        metadata: new Dictionary<string, string>
        {
            ["metricsSource"] = "process-stopped"
        }),
    Array.Empty<ZoneMetricSampleDto>(),
    now,
    thresholds);
Check(degraded.Level == ZoneHealthLevels.Warning, degraded.Level);
Check(degraded.Alerts.Any(alert => alert.Code == "zone.degraded"), "Degraded alert is missing.");
Check(degraded.Alerts.Any(alert => alert.Code == "metrics.unavailable" && alert.Value == "process-stopped"),
    "process-stopped must be reported as unavailable metrics.");

Console.WriteLine("Zone health evaluation verified: healthy, warning, critical, degraded and metadata-derived alerts.");
