using System.Text.Json;

namespace Crystal.Content;

public sealed class ContentSnapshotManifest
{
    public int FormatVersion { get; init; } = ContentSnapshotFormat.FormatVersion;

    public int SchemaVersion { get; init; } = ContentSnapshotFormat.SchemaVersion;

    public required string ReleaseVersion { get; init; }

    public required string Source { get; init; }

    public string? SourceHash { get; init; }

    public required string Generator { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public required string ContentHash { get; init; }

    public required SortedDictionary<string, ContentSnapshotFile> Entities { get; init; }

    public required SortedDictionary<string, int> Counts { get; init; }

    public JsonElement? Legacy { get; init; }
}

public sealed class ContentSnapshotFile
{
    public required string File { get; init; }

    public required string Sha256 { get; init; }

    public required long SizeBytes { get; init; }

    public required int Count { get; init; }
}

public sealed class ContentSnapshot
{
    public required ContentSnapshotManifest Manifest { get; init; }

    public required IReadOnlyList<ContentEntity> Entities { get; init; }
}
