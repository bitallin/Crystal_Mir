namespace Crystal.Control.Api.Zones;

public sealed record ZoneMetricSampleDto(
    string ZoneId,
    DateTimeOffset ObservedAt,
    DateTimeOffset RecordedAt,
    string Status,
    long? ActiveReleaseId,
    string? ActiveContentHash,
    int PlayerCount,
    double TickP95Ms,
    double ErrorRate,
    string? MetricsSource,
    int? RestartFailures,
    int? ZonePid);

public sealed record ZoneMetricsSummaryDto(
    int SampleCount,
    DateTimeOffset? FirstObservedAt,
    DateTimeOffset? LastObservedAt,
    int LatestPlayerCount,
    double LatestTickP95Ms,
    double LatestErrorRate,
    double AveragePlayerCount,
    double AverageTickP95Ms,
    double MaxTickP95Ms,
    double AverageErrorRate,
    double MaxErrorRate);

public sealed record ZoneAlertDto(
    string Code,
    string Severity,
    string Message,
    DateTimeOffset? ObservedAt,
    string? Value);

public sealed record ZoneHealthDto(
    string ZoneId,
    string Level,
    IReadOnlyList<ZoneAlertDto> Alerts,
    DateTimeOffset EvaluatedAt);

public sealed record ZoneMetricsResponse(
    string ZoneId,
    DateTimeOffset From,
    DateTimeOffset To,
    ZoneMetricsSummaryDto Summary,
    ZoneHealthDto Health,
    IReadOnlyList<ZoneMetricSampleDto> Items);

public sealed record ZoneHealthThresholds(
    double TickP95WarningMs,
    double TickP95CriticalMs,
    double ErrorRateWarning,
    double ErrorRateCritical);
