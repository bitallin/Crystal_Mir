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

public sealed record PointDto(int X, int Y);

public sealed record MapSummaryDto(
    long LocalIndex,
    string ContentKey,
    int Index,
    string? FileName,
    string? Title);

public sealed record MapMovementRelationDto(
    int TargetMapIndex,
    MapSummaryDto? TargetMap,
    PointDto? Source,
    PointDto? Destination,
    bool NeedHole,
    bool NeedMove,
    bool ShowOnBigMap,
    int ConquestIndex,
    int Icon);

public sealed record MapSafeZoneRelationDto(
    PointDto? Location,
    int Size,
    bool StartPoint);

public sealed record MapNpcRelationDto(
    long LocalIndex,
    string ContentKey,
    string? Name,
    string? FileName,
    PointDto? Location,
    int? Image,
    bool ShowOnBigMap);

public sealed record MapRespawnRelationDto(
    long LocalIndex,
    string ContentKey,
    int MonsterIndex,
    string? MonsterContentKey,
    string? MonsterName,
    PointDto? Location,
    int Count,
    int Spread,
    int Delay,
    int Direction,
    int RandomDelay,
    string? RoutePath,
    int RespawnTicks,
    int? LegacyRespawnIndex);

public sealed record MapRespawnPageDto(
    int Page,
    int PageSize,
    long Total,
    IReadOnlyList<MapRespawnRelationDto> Items);

public sealed record MapRelationsDto(
    long ImportRunId,
    MapSummaryDto Map,
    IReadOnlyList<MapMovementRelationDto> Movements,
    IReadOnlyList<MapSafeZoneRelationDto> SafeZones,
    IReadOnlyList<MapNpcRelationDto> Npcs,
    MapRespawnPageDto Respawns);

public sealed record DropItemReferenceDto(
    long LocalIndex,
    string ContentKey,
    string? Name);

public sealed record DropRelationDto(
    int Chance,
    uint Gold,
    bool QuestRequired,
    int Type,
    DropItemReferenceDto? Item,
    DropGroupDto? Group);

public sealed record DropGroupDto(
    bool Random,
    bool First,
    IReadOnlyList<DropRelationDto> Items);

public sealed record MonsterDropsDto(
    long ImportRunId,
    long LocalIndex,
    string ContentKey,
    string? Name,
    string? DropPath,
    string? DropContentKey,
    IReadOnlyList<DropRelationDto> Drops);
