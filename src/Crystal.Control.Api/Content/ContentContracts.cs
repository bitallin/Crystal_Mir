using System.Text.Json;

namespace Crystal.Control.Api.Content;

public sealed record ImportRunDto(
    long Id,
    string Source,
    string? SourceHash,
    int? LegacyVersion,
    int SchemaVersion,
    string Status,
    int EntityCount,
    DateTime StartedAt,
    DateTime? FinishedAt);

public sealed record ContentEntityDto(
    long Id,
    long ImportRunId,
    string ContentKey,
    long LocalIndex,
    int SchemaVersion,
    JsonElement Data);

public sealed record ContentTypeCountDto(string EntityType, long Count);

public sealed record PagedResult<T>(
    long ImportRunId,
    string EntityType,
    int Page,
    int PageSize,
    long Total,
    IReadOnlyList<T> Items);
