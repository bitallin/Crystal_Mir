using System.Text.Json;

namespace Crystal.Agent.Contracts;

public static class AgentContractVersion
{
    public const int Current = 1;
}

public static class AgentStatuses
{
    public const string Online = "online";
    public const string Degraded = "degraded";
    public const string Offline = "offline";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Rejected = "rejected";
}

public static class AgentCommandStatuses
{
    public const string Queued = "queued";
    public const string Delivered = "delivered";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Rejected = "rejected";
    public const string Expired = "expired";

    public static readonly IReadOnlyList<string> Final =
    [
        Succeeded,
        Failed,
        Rejected,
        Expired
    ];
}

public static class AgentCommandTypes
{
    public const string StageContent = "content.stage";
    public const string ActivateContent = "content.activate";
    public const string RollbackContent = "content.rollback";
    public const string ReloadContent = "content.reload";
    public const string StopZone = "zone.stop";
}

public static class AgentApiRoutes
{
    public const string Register = "/api/v1/zones/register";
    public const string Zones = "/api/v1/zones";
    public const string Zone = "/api/v1/zones/{zoneId}";
    public const string Heartbeat = "/api/v1/zones/{zoneId}/heartbeat";
    public const string Commands = "/api/v1/zones/{zoneId}/commands";
    public const string CommandResult = "/api/v1/zones/{zoneId}/commands/{commandId}/result";
    public const string ReleaseManifest = "/api/v1/releases/{releaseId}/manifest";
    public const string ReleaseArtifact = "/api/v1/releases/{releaseId}/artifact";
}

public sealed record ZoneDto(
    string ZoneId,
    string ZoneName,
    string AgentId,
    string AgentVersion,
    int ProtocolVersion,
    int ContentSchemaVersion,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? LastHeartbeatAt,
    long? ActiveReleaseId,
    string? ActiveReleaseVersion,
    string? ActiveContentHash,
    long? StagedReleaseId,
    string? StagedReleaseVersion,
    string? StagedContentHash,
    int PlayerCount,
    double TickP95Ms,
    double ErrorRate,
    IReadOnlyList<string> Capabilities,
    JsonElement? Metadata,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ZoneListResponse(IReadOnlyList<ZoneDto> Items);

public sealed record CreateZoneCommandRequest(
    string Type,
    JsonElement? Payload,
    DateTimeOffset? ExpiresAt);

public sealed record ReleaseCommandPayload(long ReleaseId);

public sealed record AgentCommandListResponse(IReadOnlyList<AgentCommand> Items);

public sealed record ZoneCommandDto(
    string CommandId,
    string ZoneId,
    string Type,
    string Status,
    long? ReleaseId,
    JsonElement? Payload,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    int Attempts,
    DateTimeOffset? ClaimedAt,
    DateTimeOffset? CompletedAt,
    string? Error,
    JsonElement? ResultPayload);

public sealed record ZoneCommandListResponse(IReadOnlyList<ZoneCommandDto> Items);

public sealed record AgentRegistrationRequest(
    string ZoneId,
    string ZoneName,
    string AgentId,
    string AgentVersion,
    int ProtocolVersion,
    int ContentSchemaVersion,
    DateTimeOffset StartedAt,
    IReadOnlyList<string> Capabilities);

public sealed record AgentHeartbeatRequest(
    string ZoneId,
    string AgentId,
    DateTimeOffset ObservedAt,
    string Status,
    long? ActiveReleaseId,
    string? ActiveContentHash,
    int PlayerCount,
    double TickP95Ms,
    double ErrorRate,
    IReadOnlyDictionary<string, string>? Metadata);

public sealed record ContentReleaseDescriptor(
    long ReleaseId,
    string Version,
    string ContentHash,
    int SchemaVersion,
    int ProtocolVersion,
    string ManifestUrl,
    string SignatureAlgorithm,
    string KeyId,
    DateTimeOffset SignedAt);

public sealed record AgentCommand(
    string CommandId,
    string ZoneId,
    string Type,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    JsonElement? Payload);

public sealed record AgentCommandResult(
    string CommandId,
    string ZoneId,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string? Error,
    JsonElement? Payload);

public static class AgentContractJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}
