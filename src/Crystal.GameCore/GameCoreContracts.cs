namespace Crystal.GameCore;

public sealed record ContentSnapshotHandle<TSnapshot>(
    long? ReleaseId,
    string Version,
    string ContentHash,
    int SchemaVersion,
    TSnapshot Snapshot)
    where TSnapshot : class;

public interface IContentSnapshotProvider<TSnapshot>
    where TSnapshot : class
{
    ContentSnapshotHandle<TSnapshot> Current { get; }
}

public interface IPlayerRepository<TSnapshot>
    where TSnapshot : class
{
    ValueTask<TSnapshot?> LoadAsync(string characterKey, CancellationToken cancellationToken);

    ValueTask SaveAsync(string characterKey, TSnapshot snapshot, CancellationToken cancellationToken);
}

public interface IWorldClock
{
    DateTimeOffset UtcNow { get; }

    ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed record ZoneRuntimeEvent(
    string Name,
    DateTimeOffset OccurredAt,
    IReadOnlyDictionary<string, string>? Dimensions = null,
    double? Value = null);

public interface IZoneEventSink
{
    ValueTask WriteAsync(ZoneRuntimeEvent runtimeEvent, CancellationToken cancellationToken);
}

public interface IChatTransport
{
    ValueTask SendAsync(string characterKey, string message, CancellationToken cancellationToken);

    ValueTask BroadcastAsync(string message, CancellationToken cancellationToken);
}
