using System.Text.Json;

namespace Crystal.Content;

public sealed class ContentEntity
{
    public required string EntityType { get; init; }

    public required long LocalIndex { get; init; }

    public required string ContentKey { get; init; }

    public int SchemaVersion { get; init; } = ContentSnapshotFormat.SchemaVersion;

    public required JsonElement Data { get; init; }
}
