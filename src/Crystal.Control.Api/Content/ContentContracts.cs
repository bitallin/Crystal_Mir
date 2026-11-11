using System.Text.Json;
using Crystal.Content;

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

public sealed record MapAssetReferenceDto(
    long LocalIndex,
    string ContentKey,
    string? RelativePath,
    long SizeBytes,
    string? Sha256,
    MapAssetMetadata? Map);

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

public sealed record MapNpcScriptReferenceDto(
    long LocalIndex,
    string ContentKey,
    string? FileName,
    string? RelativePath,
    int? LineCount,
    int? PageCount);

public sealed record MapNpcRelationDto(
    long LocalIndex,
    string ContentKey,
    string? Name,
    string? FileName,
    PointDto? Location,
    int? Image,
    bool ShowOnBigMap,
    MapNpcScriptReferenceDto? Script);

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
    MapAssetReferenceDto? Asset,
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

public sealed record RecipeItemReferenceDto(
    long? LocalIndex,
    string? ContentKey,
    int ItemIndex,
    string? Name,
    int Count,
    int CurrentDura,
    int MaxDura);

public sealed record NpcScriptIncludeDto(string Path, string Page);

public sealed record NpcScriptRelationsDto(
    long ImportRunId,
    long NpcLocalIndex,
    string NpcContentKey,
    string? Name,
    string? FileName,
    bool Found,
    long? ScriptLocalIndex,
    string? ScriptContentKey,
    string? ScriptPath,
    string? Language,
    string? Sha256,
    int? SizeBytes,
    int? LineCount,
    IReadOnlyList<string> Pages,
    IReadOnlyList<NpcScriptIncludeDto> Includes,
    IReadOnlyList<string> Inserts,
    IReadOnlyList<string> Calls,
    NpcScriptValidationSummaryDto? Validation,
    string? Source);

public sealed record NpcScriptValidationSummaryDto(
    bool Valid,
    int ErrorCount,
    int WarningCount,
    string? CompiledSha256);

public sealed record NpcScriptValidationDto(
    bool Available,
    long ImportRunId,
    long LocalIndex,
    string ContentKey,
    string? FileName,
    string? RelativePath,
    string? Sha256,
    NpcScriptValidationSummaryDto Validation,
    IReadOnlyList<NpcScriptValidationIssueDto> Issues,
    NpcScriptAst? Ast);

public sealed record NpcScriptValidationIssueDto(
    string Code,
    string Severity,
    string Message,
    int? Line,
    string? Page);

public sealed record RecipeRelationsDto(
    long ImportRunId,
    long LocalIndex,
    string ContentKey,
    string? Name,
    string? FileName,
    RecipeItemReferenceDto? Output,
    IReadOnlyList<RecipeItemReferenceDto> Ingredients,
    IReadOnlyList<RecipeItemReferenceDto> Tools,
    int Chance,
    uint Gold,
    int? RequiredLevel,
    string? RequiredGender,
    IReadOnlyList<string> RequiredClasses,
    IReadOnlyList<int> RequiredFlags,
    IReadOnlyList<int> RequiredQuests);
