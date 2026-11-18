using System.Text.Json;

namespace Crystal.GameCore;

public sealed record RuntimeMetricsSnapshot(
    int PlayerCount,
    double TickP95Ms,
    double ErrorRate,
    long CompletedTicks,
    long ErrorCount,
    int SampleCount,
    int WindowSeconds);

public sealed record ZoneRuntimeMetrics(
    int SchemaVersion,
    string ZoneId,
    int Pid,
    DateTimeOffset ProcessStartedAt,
    DateTimeOffset ObservedAt,
    int PlayerCount,
    double TickP95Ms,
    double ErrorRate,
    long CompletedTicks,
    long ErrorCount,
    int SampleCount,
    int WindowSeconds);

public sealed class RuntimeMetricsSampler
{
    private readonly TimeSpan _window;
    private readonly List<TickSample> _tickSamples = [];
    private readonly List<CounterSample> _counterSamples = [];
    private long _lastCompletedTicks = -1;
    private long _lastErrorCount = -1;

    public RuntimeMetricsSampler(TimeSpan? window = null)
    {
        _window = window ?? TimeSpan.FromSeconds(60);
        if (_window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(window), "Metrics window must be positive.");
        }
    }

    public void Record(
        DateTimeOffset observedAt,
        long tickDurationMs,
        long completedTicks,
        long errorCount)
    {
        if (completedTicks < 0 || errorCount < 0 || tickDurationMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(completedTicks), "Metrics counters and durations must be non-negative.");
        }

        bool tickChanged = completedTicks != _lastCompletedTicks;
        bool errorCountChanged = errorCount != _lastErrorCount;
        if (!tickChanged && !errorCountChanged) return;

        if (tickChanged)
        {
            _tickSamples.Add(new TickSample(observedAt, tickDurationMs));
        }
        if (tickChanged || errorCountChanged)
        {
            _counterSamples.Add(new CounterSample(observedAt, completedTicks, errorCount));
        }

        _lastCompletedTicks = completedTicks;
        _lastErrorCount = errorCount;
        Prune(observedAt);
    }

    public RuntimeMetricsSnapshot Snapshot(
        int playerCount,
        long completedTicks,
        long errorCount,
        DateTimeOffset observedAt)
    {
        if (playerCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(playerCount), "Player count must be non-negative.");
        }

        Prune(observedAt);

        double tickP95Ms = 0;
        if (_tickSamples.Count > 0)
        {
            long[] durations = _tickSamples.Select(sample => sample.TickDurationMs).Order().ToArray();
            int index = Math.Max(0, (int)Math.Ceiling(durations.Length * 0.95) - 1);
            tickP95Ms = durations[index];
        }

        double errorRate = 0;
        if (_counterSamples.Count > 1)
        {
            CounterSample first = _counterSamples[0];
            CounterSample last = _counterSamples[^1];
            long tickDelta = Math.Max(0, last.CompletedTicks - first.CompletedTicks);
            long errorDelta = Math.Max(0, last.ErrorCount - first.ErrorCount);
            errorRate = tickDelta > 0
                ? Math.Min(1, errorDelta / (double)tickDelta)
                : errorDelta > 0 ? 1 : 0;
        }

        return new RuntimeMetricsSnapshot(
            playerCount,
            Math.Round(tickP95Ms, 3),
            Math.Round(errorRate, 6),
            completedTicks,
            errorCount,
            _tickSamples.Count,
            (int)Math.Round(_window.TotalSeconds));
    }

    private void Prune(DateTimeOffset observedAt)
    {
        DateTimeOffset cutoff = observedAt - _window;
        int removeCount = 0;
        while (removeCount < _tickSamples.Count && _tickSamples[removeCount].ObservedAt < cutoff)
        {
            removeCount++;
        }

        if (removeCount > 0) _tickSamples.RemoveRange(0, removeCount);

        removeCount = 0;
        while (removeCount < _counterSamples.Count && _counterSamples[removeCount].ObservedAt < cutoff)
        {
            removeCount++;
        }

        if (removeCount > 0) _counterSamples.RemoveRange(0, removeCount);
    }

    private sealed record TickSample(
        DateTimeOffset ObservedAt,
        long TickDurationMs);

    private sealed record CounterSample(
        DateTimeOffset ObservedAt,
        long CompletedTicks,
        long ErrorCount);
}

public static class ZoneRuntimeMetricsJson
{
    public const int CurrentSchemaVersion = 1;
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(ZoneRuntimeMetrics metrics)
    {
        return JsonSerializer.Serialize(metrics, Options) + "\n";
    }

    public static bool TryParse(
        string json,
        string expectedZoneId,
        int expectedPid,
        TimeSpan maxAge,
        DateTimeOffset now,
        out ZoneRuntimeMetrics? metrics,
        out string? error)
    {
        metrics = null;
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "Metrics document is empty.";
            return false;
        }

        try
        {
            metrics = JsonSerializer.Deserialize<ZoneRuntimeMetrics>(json, Options);
        }
        catch (JsonException exception)
        {
            error = $"Metrics JSON is invalid: {exception.Message}";
            return false;
        }

        if (metrics is null)
        {
            error = "Metrics document is empty.";
            return false;
        }

        if (metrics.SchemaVersion != CurrentSchemaVersion)
        {
            error = $"Unsupported metrics schema version {metrics.SchemaVersion}.";
            return false;
        }

        if (!string.Equals(metrics.ZoneId, expectedZoneId, StringComparison.Ordinal))
        {
            error = $"Metrics zone id mismatch: expected {expectedZoneId}, actual {metrics.ZoneId}.";
            return false;
        }

        if (metrics.Pid <= 0 || metrics.Pid != expectedPid)
        {
            error = $"Metrics pid mismatch: expected {expectedPid}, actual {metrics.Pid}.";
            return false;
        }

        if (metrics.ObservedAt == default || metrics.ProcessStartedAt == default)
        {
            error = "Metrics timestamps are missing.";
            return false;
        }

        if (metrics.ObservedAt > now.AddSeconds(5))
        {
            error = "Metrics timestamp is in the future.";
            return false;
        }

        if (now - metrics.ObservedAt > maxAge)
        {
            error = $"Metrics are stale by {(now - metrics.ObservedAt).TotalSeconds:0.0} seconds.";
            return false;
        }

        if (metrics.PlayerCount < 0 ||
            metrics.TickP95Ms < 0 ||
            metrics.ErrorRate is < 0 or > 1 ||
            metrics.CompletedTicks < 0 ||
            metrics.ErrorCount < 0 ||
            metrics.SampleCount < 0 ||
            metrics.WindowSeconds <= 0)
        {
            error = "Metrics values are outside their valid ranges.";
            return false;
        }

        return true;
    }
}
