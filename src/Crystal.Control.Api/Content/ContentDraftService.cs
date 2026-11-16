using System.Text.Json;
using System.Text.RegularExpressions;
using Crystal.Content;

namespace Crystal.Control.Api.Content;

public sealed partial class ContentDraftService
{
    private readonly ContentDraftStore _store;
    private readonly ContentQueryStore _queryStore;

    public ContentDraftService(ContentDraftStore store, ContentQueryStore queryStore)
    {
        _store = store;
        _queryStore = queryStore;
    }

    public async Task<DraftDto> CreateAsync(
        long baseImportRunId,
        string name,
        long? createdBy,
        CancellationToken cancellationToken)
    {
        var run = await _queryStore.GetImportRunAsync(baseImportRunId, cancellationToken)
                  ?? throw new DraftNotFoundException($"Import run {baseImportRunId} does not exist.");
        if (!string.Equals(run.Status, "imported", StringComparison.Ordinal))
        {
            throw new DraftConflictException(
                $"Import run {baseImportRunId} is '{run.Status}', only imported runs can create drafts.");
        }

        return await _store.CreateAsync(
            baseImportRunId,
            name,
            createdBy,
            DateTimeOffset.UtcNow,
            cancellationToken);
    }

    public Task<IReadOnlyList<DraftDto>> ListAsync(
        int limit,
        string? status,
        CancellationToken cancellationToken)
    {
        return _store.ListAsync(limit, status, cancellationToken);
    }

    public async Task<DraftDto> GetAsync(long id, CancellationToken cancellationToken)
    {
        return await _store.GetAsync(id, cancellationToken)
               ?? throw new DraftNotFoundException($"Draft {id} does not exist.");
    }

    public async Task<(IReadOnlyList<DraftEntityDto> Items, long Total)> QueryEntitiesAsync(
        long draftId,
        string entityType,
        int page,
        int pageSize,
        string? query,
        string sort,
        string direction,
        CancellationToken cancellationToken)
    {
        _ = await GetAsync(draftId, cancellationToken);
        ValidateEntityType(entityType);
        return await _store.QueryEntitiesAsync(
            draftId,
            entityType,
            page,
            pageSize,
            query,
            sort,
            direction,
            cancellationToken);
    }

    public async Task<DraftEntityDto> GetEntityAsync(
        long draftId,
        string entityType,
        long localIndex,
        CancellationToken cancellationToken)
    {
        _ = await GetAsync(draftId, cancellationToken);
        ValidateEntityType(entityType);
        if (localIndex < 0)
        {
            throw new DraftConflictException("localIndex must be zero or greater.");
        }

        return await _store.GetEntityAsync(draftId, entityType, localIndex, cancellationToken)
               ?? throw new DraftNotFoundException(
                   $"Draft entity {entityType}/{localIndex} does not exist in draft {draftId}.");
    }

    public async Task<DraftEntityDto> UpsertEntityAsync(
        long draftId,
        string entityType,
        long localIndex,
        UpsertDraftEntityRequest request,
        long? actorAccountId,
        CancellationToken cancellationToken)
    {
        _ = await GetAsync(draftId, cancellationToken);
        ValidateEntityType(entityType);
        if (localIndex < 0)
        {
            throw new DraftConflictException("localIndex must be zero or greater.");
        }

        if (request.ExpectedRevision < 0)
        {
            throw new DraftConflictException("expectedRevision must be zero or greater.");
        }

        if (request.Data is null ||
            request.Data.Value.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
        {
            throw new DraftConflictException("data must be a JSON object or array.");
        }

        var contentKey = string.IsNullOrWhiteSpace(request.ContentKey) ? null : request.ContentKey.Trim();
        if (contentKey is not null &&
            (contentKey.Length > 256 || !ContentKeyPattern().IsMatch(contentKey)))
        {
            throw new DraftConflictException(
                "contentKey must be 1-256 characters using letters, digits, dot, underscore or hyphen.");
        }

        var schemaVersion = request.SchemaVersion ?? 1;
        if (schemaVersion <= 0)
        {
            throw new DraftConflictException("schemaVersion must be a positive integer.");
        }

        ValidateStructure(entityType, localIndex, contentKey, request.Data.Value);

        var reason = NormalizeReason(request.Reason);
        return await _store.UpsertEntityAsync(
            draftId,
            entityType,
            localIndex,
            request,
            request.Data.Value,
            contentKey,
            schemaVersion,
            actorAccountId,
            reason,
            DateTimeOffset.UtcNow,
            cancellationToken);
    }

    public async Task<DraftEntityDto> CreateEntityAsync(
        long draftId,
        string entityType,
        CreateDraftEntityRequest request,
        long? actorAccountId,
        CancellationToken cancellationToken)
    {
        _ = await GetAsync(draftId, cancellationToken);
        ValidateEntityType(entityType);
        if (!ContentEditorSchemas.TryGet(entityType, out var editorSchema))
        {
            throw new DraftConflictException(
                $"Typed creation is not available for entity type '{entityType}'.");
        }

        if (request.Data is null || request.Data.Value.ValueKind != JsonValueKind.Object)
        {
            throw new DraftConflictException("data must be a JSON object.");
        }

        var schemaVersion = request.SchemaVersion ?? editorSchema.SchemaVersion;
        if (schemaVersion != editorSchema.SchemaVersion)
        {
            throw new DraftConflictException(
                $"Unsupported {entityType} schemaVersion {schemaVersion}; expected {editorSchema.SchemaVersion}.");
        }

        return await _store.CreateEntityAsync(
            draftId,
            entityType,
            request.Data.Value,
            schemaVersion,
            actorAccountId,
            NormalizeReason(request.Reason),
            DateTimeOffset.UtcNow,
            cancellationToken);
    }

    public async Task DeleteEntityAsync(
        long draftId,
        string entityType,
        long localIndex,
        long expectedRevision,
        string? reason,
        long? actorAccountId,
        CancellationToken cancellationToken)
    {
        _ = await GetAsync(draftId, cancellationToken);
        ValidateEntityType(entityType);
        if (localIndex < 0)
        {
            throw new DraftConflictException("localIndex must be zero or greater.");
        }

        if (expectedRevision <= 0)
        {
            throw new DraftConflictException("expectedRevision must be a positive integer.");
        }

        await _store.DeleteEntityAsync(
            draftId,
            entityType,
            localIndex,
            expectedRevision,
            NormalizeReason(reason),
            actorAccountId,
            DateTimeOffset.UtcNow,
            cancellationToken);
    }

    public async Task<DraftValidationReportDto> ValidateAsync(
        long draftId,
        CancellationToken cancellationToken)
    {
        var draft = await GetAsync(draftId, cancellationToken);
        if (draft.Status == "archived")
        {
            throw new DraftConflictException($"Draft {draftId} is archived and cannot be validated.");
        }

        var entities = await _store.GetEntitiesAsync(draftId, cancellationToken);
        var issues = new List<ReleaseValidationIssueDto>();
        ContentSnapshotBuild? build = null;
        if (entities.Count == 0)
        {
            issues.Add(new ReleaseValidationIssueDto(
                "empty-draft",
                "Draft contains no entities.",
                null));
        }

        try
        {
            build = ContentSnapshotWriter.Build(new ContentSnapshotWriteOptions
            {
                OutputDirectory = Path.Combine(Path.GetTempPath(), $"crystal-draft-{draftId}"),
                ReleaseVersion = $"draft-{draftId}",
                Source = $"content_draft:{draftId}",
                CreatedAt = DateTimeOffset.UtcNow
            }, entities);
        }
        catch (InvalidDataException exception)
        {
            issues.Add(new ReleaseValidationIssueDto("snapshot-build-failed", exception.Message, null));
        }

        if (build is not null)
        {
            issues.AddRange(ContentSnapshotValidator.Validate(new ContentSnapshot
            {
                Manifest = build.Manifest,
                Entities = entities
            }).Select(issue => new ReleaseValidationIssueDto(
                issue.Code,
                issue.Message,
                issue.ContentKey)));
            issues.AddRange(ContentValidationRules.ValidateEntitySemantics(entities));
        }

        var report = new DraftValidationReportDto(
            draftId,
            draft.Name,
            build?.Manifest.ContentHash ?? string.Empty,
            entities.Count,
            issues.Count == 0,
            issues);
        await _store.SaveValidationAsync(draftId, report, DateTimeOffset.UtcNow, cancellationToken);
        return report;
    }

    public async Task<IReadOnlyList<DraftRevisionDto>> ListRevisionsAsync(
        long draftId,
        string? entityType,
        string? contentKey,
        int limit,
        CancellationToken cancellationToken)
    {
        _ = await GetAsync(draftId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(entityType)) ValidateEntityType(entityType);
        return await _store.ListRevisionsAsync(
            draftId,
            string.IsNullOrWhiteSpace(entityType) ? null : entityType,
            string.IsNullOrWhiteSpace(contentKey) ? null : contentKey.Trim(),
            limit,
            cancellationToken);
    }

    public static IReadOnlyList<string> EntityTypes => ContentEntityTypes.All;

    public static void ValidateEntityType(string entityType)
    {
        if (!ContentEntityTypes.All.Contains(entityType, StringComparer.Ordinal))
        {
            throw new DraftConflictException($"Unknown entity type '{entityType}'.");
        }
    }

    private static void ValidateStructure(
        string entityType,
        long localIndex,
        string? contentKey,
        JsonElement data)
    {
        if (!ContentEditorSchemas.TryGet(entityType, out var editorSchema))
        {
            return;
        }

        var errors = editorSchema.ValidateStructure(data, localIndex, contentKey);
        if (errors.Count > 0)
        {
            throw new DraftInputValidationException(errors);
        }
    }

    private static string? NormalizeReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return null;
        var normalized = reason.Trim();
        if (normalized.Length > 512)
        {
            throw new DraftConflictException("reason must be 512 characters or less.");
        }

        return normalized;
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,255}$", RegexOptions.CultureInvariant)]
    private static partial Regex ContentKeyPattern();
}
