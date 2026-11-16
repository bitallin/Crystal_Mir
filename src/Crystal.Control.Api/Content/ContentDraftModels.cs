using System.Text.Json;

namespace Crystal.Control.Api.Content;

public sealed record CreateDraftRequest(long BaseImportRunId, string? Name);

public sealed record DraftDto(
    long Id,
    string Name,
    long BaseImportRunId,
    string Status,
    long? CreatedBy,
    long? UpdatedBy,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int EntityCount,
    JsonElement? LastValidation,
    IReadOnlyList<ContentTypeCountDto> Counts);

public sealed record DraftEntityDto(
    long DraftId,
    string EntityType,
    long LocalIndex,
    string ContentKey,
    int SchemaVersion,
    JsonElement Data,
    long Revision,
    long? UpdatedBy,
    DateTime UpdatedAt);

public sealed record UpsertDraftEntityRequest(
    string? ContentKey,
    int? SchemaVersion,
    JsonElement? Data,
    long ExpectedRevision,
    string? Reason);

public sealed record DeleteDraftEntityRequest(long ExpectedRevision, string? Reason);

public sealed record DraftRevisionDto(
    long RevisionNumber,
    string Action,
    string EntityType,
    long LocalIndex,
    string ContentKey,
    JsonElement? BeforeData,
    JsonElement? AfterData,
    long? ActorAccountId,
    string? Reason,
    DateTime OccurredAt);

public sealed record DraftValidationReportDto(
    long DraftId,
    string Name,
    string ContentHash,
    int EntityCount,
    bool Valid,
    IReadOnlyList<ReleaseValidationIssueDto> Issues);

public sealed class DraftNotFoundException : Exception
{
    public DraftNotFoundException(string message) : base(message)
    {
    }
}

public sealed class DraftConflictException : Exception
{
    public DraftConflictException(string message) : base(message)
    {
    }
}

public sealed class DraftValidationException : Exception
{
    public DraftValidationException(DraftValidationReportDto report)
        : base($"Draft {report.DraftId} has {report.Issues.Count} validation issue(s).")
    {
        Report = report;
    }

    public DraftValidationReportDto Report { get; }
}
