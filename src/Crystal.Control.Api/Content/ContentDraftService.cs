using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text;
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

    public async Task<IReadOnlyList<ContentReferenceOptionDto>> QueryOptionsAsync(
        string entityType,
        long? draftId,
        long? importRunId,
        string? query,
        int limit,
        CancellationToken cancellationToken)
    {
        ValidateEntityType(entityType);
        if (draftId is not null)
        {
            _ = await GetAsync(draftId.Value, cancellationToken);
            return await _store.QueryOptionsAsync(draftId.Value, entityType, query, limit, cancellationToken);
        }

        var runId = await _queryStore.ResolveImportRunIdAsync(importRunId, cancellationToken);
        if (runId is null)
        {
            throw new DraftNotFoundException(
                importRunId is null
                    ? "No imported content run is available."
                    : $"Import run {importRunId} does not exist.");
        }

        return await _queryStore.QueryOptionsAsync(runId.Value, entityType, query, limit, cancellationToken);
    }

    public async Task<ContentImportResultDto> ImportJsonLinesAsync(
        long draftId,
        string entityType,
        string jsonLines,
        bool dryRun,
        long? actorAccountId,
        string? reason,
        CancellationToken cancellationToken)
    {
        _ = await GetAsync(draftId, cancellationToken);
        ValidateEntityType(entityType);
        if (!ContentEditorSchemas.TryGet(entityType, out var schema))
        {
            throw new DraftConflictException($"Typed import is not available for entity type '{entityType}'.");
        }

        var errors = new List<ContentImportLineIssue>();
        var entities = new List<(int Line, ContentEntity Entity)>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var lineNumber = 0;
        var totalLines = 0;
        using var reader = new StringReader(jsonLines);
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            totalLines++;

            ContentEntity entity;
            try
            {
                entity = JsonSerializer.Deserialize<ContentEntity>(line, ContentJson.CompactOptions)
                         ?? throw new JsonException("Entity row is empty.");
            }
            catch (JsonException exception)
            {
                errors.Add(new ContentImportLineIssue(lineNumber, "jsonl-invalid", exception.Message));
                continue;
            }

            if (!string.Equals(entity.EntityType, entityType, StringComparison.Ordinal))
            {
                errors.Add(new ContentImportLineIssue(
                    lineNumber,
                    "entity-type",
                    $"Entity type must be '{entityType}', got '{entity.EntityType}'."));
                continue;
            }

            if (entity.SchemaVersion != schema.SchemaVersion)
            {
                errors.Add(new ContentImportLineIssue(
                    lineNumber,
                    "schema-version",
                    $"Unsupported schemaVersion {entity.SchemaVersion}; expected {schema.SchemaVersion}."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(entity.ContentKey) ||
                entity.ContentKey.Length > 256 ||
                !ContentKeyPattern().IsMatch(entity.ContentKey))
            {
                errors.Add(new ContentImportLineIssue(
                    lineNumber,
                    "content-key",
                    "contentKey is invalid for JSONL import."));
                continue;
            }

            if (!keys.Add(entity.ContentKey))
            {
                errors.Add(new ContentImportLineIssue(
                    lineNumber,
                    $"{entityType}-duplicate",
                    $"Duplicate contentKey '{entity.ContentKey}' in import batch."));
                continue;
            }

            var rawStructureErrors = ContentEditorSchemas.ValidateRawStructure(entityType, entity.Data);
            if (rawStructureErrors.Count > 0)
            {
                foreach (var (path, messages) in rawStructureErrors)
                {
                    foreach (var message in messages)
                    {
                        errors.Add(new ContentImportLineIssue(lineNumber, path, message));
                    }
                }

                continue;
            }

            JsonElement normalized;
            try
            {
                normalized = ContentEditorSchemas.Normalize(entityType, entity.Data);
            }
            catch (JsonException exception)
            {
                errors.Add(new ContentImportLineIssue(lineNumber, "jsonl-invalid", exception.Message));
                continue;
            }

            var identity = ContentEditorSchemas.ResolveIdentity(entityType, normalized, entity.LocalIndex);
            if (identity.LocalIndex != entity.LocalIndex)
            {
                errors.Add(new ContentImportLineIssue(
                    lineNumber,
                    $"{entityType}-content-key",
                    $"localIndex {entity.LocalIndex} does not match resolved identity {identity.LocalIndex}."));
                continue;
            }

            normalized = ContentEditorSchemas.ApplyIdentity(entityType, normalized, identity);
            if (!ContentEditorSchemas.IsContentKeyValid(
                    entityType,
                    normalized,
                    identity.LocalIndex,
                    entity.ContentKey))
            {
                errors.Add(new ContentImportLineIssue(
                    lineNumber,
                    $"{entityType}-content-key",
                    $"contentKey must be '{identity.ContentKey}'."));
                continue;
            }

            var structureErrors = schema.ValidateStructure(normalized, identity.LocalIndex, entity.ContentKey);
            if (structureErrors.Count > 0)
            {
                foreach (var (path, messages) in structureErrors)
                {
                    foreach (var message in messages)
                    {
                        errors.Add(new ContentImportLineIssue(lineNumber, path, message));
                    }
                }

                continue;
            }

            entities.Add((lineNumber, new ContentEntity
            {
                EntityType = entityType,
                LocalIndex = identity.LocalIndex,
                ContentKey = entity.ContentKey,
                SchemaVersion = entity.SchemaVersion,
                Data = normalized
            }));
        }

        var applied = 0;
        if (!dryRun && errors.Count == 0 && entities.Count > 0)
        {
            applied = await _store.ImportEntitiesAsync(
                draftId,
                entityType,
                entities.Select(item => item.Entity).ToArray(),
                actorAccountId,
                NormalizeReason(reason),
                DateTimeOffset.UtcNow,
                cancellationToken);
        }

        return new ContentImportResultDto(
            draftId,
            entityType,
            dryRun,
            totalLines,
            entities.Count,
            applied,
            errors.Count == 0,
            errors,
            entities.Select(item => item.Entity.ContentKey).ToArray());
    }

    public async Task<byte[]> ExportJsonLinesAsync(
        long draftId,
        string entityType,
        long? importRunId,
        CancellationToken cancellationToken)
    {
        ValidateEntityType(entityType);
        IReadOnlyList<ContentEntity> entities;
        if (importRunId is null)
        {
            _ = await GetAsync(draftId, cancellationToken);
            entities = (await _store.GetEntitiesAsync(draftId, cancellationToken))
                .Where(entity => string.Equals(entity.EntityType, entityType, StringComparison.Ordinal))
                .ToArray();
        }
        else
        {
            var run = await _queryStore.GetImportRunAsync(importRunId.Value, cancellationToken)
                      ?? throw new DraftNotFoundException($"Import run {importRunId} does not exist.");
            _ = run;
            entities = await _queryStore.GetEntitiesAsync(importRunId.Value, entityType, cancellationToken);
        }

        var ordered = entities
            .OrderBy(entity => entity.LocalIndex)
            .ThenBy(entity => entity.ContentKey, StringComparer.Ordinal)
            .Select(entity => new ContentEntity
            {
                EntityType = entity.EntityType,
                LocalIndex = entity.LocalIndex,
                ContentKey = entity.ContentKey,
                SchemaVersion = entity.SchemaVersion,
                Data = ContentJson.Canonicalize(entity.Data)
            });
        var builder = new StringBuilder();
        foreach (var entity in ordered)
        {
            builder.Append(JsonSerializer.Serialize(entity, ContentJson.CompactOptions)).Append('\n');
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    public static ContentScriptValidationResponse ValidateScript(string source, string? language)
    {
        var normalizedLanguage = string.IsNullOrWhiteSpace(language) ? "CrystalNpc" : language.Trim();
        var normalizedSource = source ?? string.Empty;
        var parsed = NpcScriptParser.Parse(normalizedSource, normalizedLanguage);
        return new ContentScriptValidationResponse(
            parsed.IsValid,
            ContentJson.Sha256Hex(normalizedSource),
            Encoding.UTF8.GetByteCount(normalizedSource),
            normalizedSource.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n').Length,
            parsed.CompiledSha256,
            parsed.PageKeys,
            parsed.Issues.Select(issue => new NpcScriptValidationIssueDto(
                issue.Code,
                issue.Severity,
                issue.Message,
                issue.Line,
                issue.Page)).ToArray(),
            parsed.Ast);
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

        ValidateRawStructure(entityType, request.Data.Value);
        var normalizedData = ContentEditorSchemas.Normalize(entityType, request.Data.Value);
        ValidateStructure(entityType, localIndex, contentKey, normalizedData);

        var reason = NormalizeReason(request.Reason);
        return await _store.UpsertEntityAsync(
            draftId,
            entityType,
            localIndex,
            request,
            normalizedData,
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

    private static void ValidateRawStructure(string entityType, JsonElement data)
    {
        var errors = ContentEditorSchemas.ValidateRawStructure(entityType, data);
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

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._()-]{0,255}$", RegexOptions.CultureInvariant)]
    private static partial Regex ContentKeyPattern();
}
