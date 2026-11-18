using Crystal.GameCore;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void CheckNear(double actual, double expected, string message)
{
    if (Math.Abs(actual - expected) > 0.0001)
    {
        throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}");
    }
}

DateTimeOffset start = new(2026, 11, 18, 0, 0, 0, TimeSpan.Zero);

var p95Sampler = new RuntimeMetricsSampler(TimeSpan.FromSeconds(60));
for (int i = 1; i <= 100; i++)
{
    p95Sampler.Record(start.AddMilliseconds(i * 100), i, i, 0);
}
RuntimeMetricsSnapshot p95 = p95Sampler.Snapshot(7, 100, 0, start.AddSeconds(10));
CheckNear(p95.TickP95Ms, 95, "P95 uses nearest-rank");
Check(p95.PlayerCount == 7, "Player count is preserved");
Check(p95.SampleCount == 100, "All samples are retained inside the window");

var dedupeSampler = new RuntimeMetricsSampler(TimeSpan.FromSeconds(60));
dedupeSampler.Record(start, 80, 1, 0);
dedupeSampler.Record(start.AddMilliseconds(100), 80, 1, 0);
dedupeSampler.Record(start.AddMilliseconds(200), 80, 1, 2);
RuntimeMetricsSnapshot deduped = dedupeSampler.Snapshot(0, 1, 2, start.AddMilliseconds(300));
Check(deduped.SampleCount == 1, $"Completed tick deduplication expected 1 sample, actual {deduped.SampleCount}");
CheckNear(deduped.ErrorRate, 1, "Error-only counter updates do not duplicate tick samples");

var windowSampler = new RuntimeMetricsSampler(TimeSpan.FromSeconds(10));
for (int i = 0; i < 20; i++)
{
    windowSampler.Record(start.AddSeconds(i), 10 + i, i + 1, 0);
}
RuntimeMetricsSnapshot window = windowSampler.Snapshot(0, 20, 0, start.AddSeconds(19));
Check(window.SampleCount == 11, $"Window pruning expected 11 samples, actual {window.SampleCount}");

var errorSampler = new RuntimeMetricsSampler(TimeSpan.FromSeconds(60));
errorSampler.Record(start, 10, 0, 0);
errorSampler.Record(start.AddSeconds(1), 10, 10, 2);
RuntimeMetricsSnapshot errors = errorSampler.Snapshot(0, 10, 2, start.AddSeconds(2));
CheckNear(errors.ErrorRate, 0.2, "Error rate uses counter deltas");

var cappedSampler = new RuntimeMetricsSampler(TimeSpan.FromSeconds(60));
cappedSampler.Record(start, 10, 0, 0);
cappedSampler.Record(start.AddSeconds(1), 10, 10, 20);
RuntimeMetricsSnapshot capped = cappedSampler.Snapshot(0, 10, 20, start.AddSeconds(2));
CheckNear(capped.ErrorRate, 1, "Error rate is capped at one");

var noTickSampler = new RuntimeMetricsSampler(TimeSpan.FromSeconds(60));
noTickSampler.Record(start, 10, 0, 0);
noTickSampler.Record(start.AddSeconds(1), 10, 0, 3);
RuntimeMetricsSnapshot noTicks = noTickSampler.Snapshot(0, 0, 3, start.AddSeconds(2));
CheckNear(noTicks.ErrorRate, 1, "Errors without ticks produce a fully failed window");

var emptySampler = new RuntimeMetricsSampler(TimeSpan.FromSeconds(60));
RuntimeMetricsSnapshot empty = emptySampler.Snapshot(0, 0, 0, start);
CheckNear(empty.TickP95Ms, 0, "Empty sampler has no tick metric");
CheckNear(empty.ErrorRate, 0, "Empty sampler has no error metric");

var metrics = new ZoneRuntimeMetrics(
    ZoneRuntimeMetricsJson.CurrentSchemaVersion,
    "zone-a",
    123,
    start,
    start.AddSeconds(1),
    42,
    18.5,
    0.02,
    100,
    2,
    10,
    60);
string json = ZoneRuntimeMetricsJson.Serialize(metrics);
Check(ZoneRuntimeMetricsJson.TryParse(
    json,
    "zone-a",
    123,
    TimeSpan.FromSeconds(15),
    start.AddSeconds(2),
    out ZoneRuntimeMetrics? parsed,
    out string? parseError), $"Valid metrics were rejected: {parseError}");
Check(parsed == metrics, "Metrics JSON round-trip changed the payload");

Check(!ZoneRuntimeMetricsJson.TryParse(
    json,
    "zone-b",
    123,
    TimeSpan.FromSeconds(15),
    start.AddSeconds(2),
    out _,
    out string? zoneError), "Zone mismatch was accepted");
Check(zoneError?.Contains("zone id", StringComparison.OrdinalIgnoreCase) == true, "Zone mismatch error is missing");

Check(!ZoneRuntimeMetricsJson.TryParse(
    json,
    "zone-a",
    999,
    TimeSpan.FromSeconds(15),
    start.AddSeconds(2),
    out _,
    out string? pidError), "PID mismatch was accepted");
Check(pidError?.Contains("pid", StringComparison.OrdinalIgnoreCase) == true, "PID mismatch error is missing");

Check(!ZoneRuntimeMetricsJson.TryParse(
    json,
    "zone-a",
    123,
    TimeSpan.FromSeconds(15),
    start.AddSeconds(20),
    out _,
    out string? staleError), "Stale metrics were accepted");
Check(staleError?.Contains("stale", StringComparison.OrdinalIgnoreCase) == true, "Stale error is missing");

Check(!ZoneRuntimeMetricsJson.TryParse(
    "{",
    "zone-a",
    123,
    TimeSpan.FromSeconds(15),
    start.AddSeconds(2),
    out _,
    out string? jsonError), "Malformed JSON was accepted");
Check(jsonError?.Contains("invalid", StringComparison.OrdinalIgnoreCase) == true, "Malformed JSON error is missing");

Console.WriteLine("Zone metrics checks passed.");
