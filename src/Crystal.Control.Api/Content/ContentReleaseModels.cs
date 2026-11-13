namespace Crystal.Control.Api.Content;

public sealed record CreateReleaseRequest(long? ImportRunId, string? Version, string? Notes);

public sealed record ReleaseDto(
    long Id,
    long? ImportRunId,
    string Version,
    string Status,
    int SchemaVersion,
    int FormatVersion,
    string ContentHash,
    string? ManifestObjectKey,
    long? CreatedBy,
    DateTime CreatedAt,
    DateTime? PublishedAt,
    string? Notes,
    IReadOnlyList<ContentTypeCountDto> Counts);

public sealed record ReleaseValidationIssueDto(string Code, string Message, string? ContentKey);

public sealed record ReleaseValidationReportDto(
    long ReleaseId,
    string Version,
    string ContentHash,
    int EntityCount,
    bool Valid,
    IReadOnlyList<ReleaseValidationIssueDto> Issues);

public sealed class ReleaseNotFoundException : Exception
{
    public ReleaseNotFoundException(string message) : base(message)
    {
    }
}

public sealed class ReleaseConflictException : Exception
{
    public ReleaseConflictException(string message) : base(message)
    {
    }
}

public sealed class ReleaseValidationException : Exception
{
    public ReleaseValidationException(ReleaseValidationReportDto report)
        : base($"Release {report.ReleaseId} has {report.Issues.Count} validation issue(s).")
    {
        Report = report;
    }

    public ReleaseValidationReportDto Report { get; }
}

public sealed class ReleaseSigningNotConfiguredException : Exception
{
    public ReleaseSigningNotConfiguredException(string message) : base(message)
    {
    }
}
