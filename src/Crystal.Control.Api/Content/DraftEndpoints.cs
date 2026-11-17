using Crystal.Control.Api.Audit;
using Crystal.Control.Api.Auth;
using System.Text.Json;

namespace Crystal.Control.Api.Content;

public static class DraftEndpoints
{
    private static readonly HashSet<string> DraftStatuses = new(
        ["editing", "validated", "failed", "archived"],
        StringComparer.Ordinal);

    public static RouteGroupBuilder MapDraftEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/drafts", ListDraftsAsync)
            .RequireAuthorization(Permissions.ContentRead);
        api.MapGet("/drafts/{id:long}", GetDraftAsync)
            .RequireAuthorization(Permissions.ContentRead);
        api.MapPost("/drafts", CreateDraftAsync)
            .RequireAuthorization(Permissions.ContentWrite);
        api.MapPost("/drafts/{id:long}/content/{entityType}", CreateDraftEntityAsync)
            .RequireAuthorization(Permissions.ContentWrite);
        api.MapPost("/drafts/{id:long}/content/{entityType}/import", ImportDraftContentAsync)
            .RequireAuthorization(Permissions.ContentWrite);
        api.MapGet("/drafts/{id:long}/content/{entityType}/export", ExportDraftContentAsync)
            .RequireAuthorization(Permissions.ContentRead);
        api.MapPost("/drafts/{id:long}/content/script/{localIndex:long}/validate", ValidateDraftScriptAsync)
            .RequireAuthorization(Permissions.ContentRead);
        api.MapGet("/drafts/{id:long}/content/map/{mapIndex:long}/relations", GetDraftMapRelationsAsync)
            .RequireAuthorization(Permissions.ContentRead);
        api.MapPut("/drafts/{id:long}/content/map/{mapIndex:long}/movements", ReplaceDraftMapMovementsAsync)
            .RequireAuthorization(Permissions.ContentWrite);
        api.MapPut("/drafts/{id:long}/content/map/{mapIndex:long}/safezones", ReplaceDraftMapSafeZonesAsync)
            .RequireAuthorization(Permissions.ContentWrite);
        api.MapPut("/drafts/{id:long}/content/map/{mapIndex:long}/minezones", ReplaceDraftMapMineZonesAsync)
            .RequireAuthorization(Permissions.ContentWrite);
        api.MapPut("/drafts/{id:long}/content/map/{mapIndex:long}/activecoords", ReplaceDraftMapActiveCoordsAsync)
            .RequireAuthorization(Permissions.ContentWrite);
        api.MapPost("/drafts/{id:long}/content/map/{mapIndex:long}/respawns", CreateDraftMapRespawnAsync)
            .RequireAuthorization(Permissions.ContentWrite);
        api.MapPut("/drafts/{id:long}/content/map/{mapIndex:long}/respawns/{respawnIndex:long}", UpdateDraftMapRespawnAsync)
            .RequireAuthorization(Permissions.ContentWrite);
        api.MapDelete("/drafts/{id:long}/content/map/{mapIndex:long}/respawns/{respawnIndex:long}", DeleteDraftMapRespawnAsync)
            .RequireAuthorization(Permissions.ContentWrite);
        api.MapPost("/drafts/{id:long}/content/map/{mapIndex:long}/npcs", CreateDraftMapNpcAsync)
            .RequireAuthorization(Permissions.ContentWrite);
        api.MapPut("/drafts/{id:long}/content/map/{mapIndex:long}/npcs/{npcIndex:long}", UpdateDraftMapNpcAsync)
            .RequireAuthorization(Permissions.ContentWrite);
        api.MapDelete("/drafts/{id:long}/content/map/{mapIndex:long}/npcs/{npcIndex:long}", DeleteDraftMapNpcAsync)
            .RequireAuthorization(Permissions.ContentWrite);
        api.MapGet("/drafts/{id:long}/content", QueryDraftContentAsync)
            .RequireAuthorization(Permissions.ContentRead);
        api.MapGet("/drafts/{id:long}/content/{entityType}/{localIndex:long}", GetDraftEntityAsync)
            .RequireAuthorization(Permissions.ContentRead);
        api.MapPut("/drafts/{id:long}/content/{entityType}/{localIndex:long}", UpsertDraftEntityAsync)
            .RequireAuthorization(Permissions.ContentWrite);
        api.MapDelete("/drafts/{id:long}/content/{entityType}/{localIndex:long}", DeleteDraftEntityAsync)
            .RequireAuthorization(Permissions.ContentWrite);
        api.MapPost("/drafts/{id:long}/validate", ValidateDraftAsync)
            .RequireAuthorization(Permissions.ContentWrite);
        api.MapGet("/drafts/{id:long}/revisions", ListDraftRevisionsAsync)
            .RequireAuthorization(Permissions.ContentRead);
        return api;
    }

    private static async Task<IResult> ListDraftsAsync(
        ContentDraftService service,
        int? limit,
        string? status,
        CancellationToken cancellationToken)
    {
        var take = limit ?? 50;
        var errors = new Dictionary<string, string[]>();
        if (take is < 1 or > 200) errors["limit"] = ["limit must be between 1 and 200."];
        var normalizedStatus = string.IsNullOrWhiteSpace(status) ? null : status.Trim();
        if (normalizedStatus is not null && !DraftStatuses.Contains(normalizedStatus))
        {
            errors["status"] = [$"Unknown draft status '{normalizedStatus}'."];
        }

        if (errors.Count > 0) return Results.ValidationProblem(errors);
        var drafts = await service.ListAsync(take, normalizedStatus, cancellationToken);
        return Results.Ok(new { limit = take, items = drafts });
    }

    private static async Task<IResult> GetDraftAsync(
        long id,
        ContentDraftService service,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await service.GetAsync(id, cancellationToken));
        }
        catch (DraftNotFoundException exception)
        {
            return NotFound(exception.Message);
        }
    }

    private static async Task<IResult> CreateDraftAsync(
        CreateDraftRequest request,
        ContentDraftService service,
        AuditStore auditStore,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.BaseImportRunId <= 0)
        {
            errors["baseImportRunId"] = ["baseImportRunId must be a positive integer."];
        }

        var name = string.IsNullOrWhiteSpace(request.Name)
            ? $"Draft from run {request.BaseImportRunId}"
            : request.Name.Trim();
        if (name.Length > 128)
        {
            errors["name"] = ["name must be 128 characters or less."];
        }

        if (errors.Count > 0) return Results.ValidationProblem(errors);

        try
        {
            var draft = await service.CreateAsync(
                request.BaseImportRunId,
                name,
                RequestMetadata.AccountId(context.User),
                cancellationToken);
            await WriteAuditAsync(auditStore, context, "draft.create", draft, null, cancellationToken);
            return Results.Created($"/api/v1/drafts/{draft.Id}", draft);
        }
        catch (DraftNotFoundException exception)
        {
            return NotFound(exception.Message);
        }
        catch (DraftConflictException exception)
        {
            return Conflict(exception.Message);
        }
    }

    private static async Task<IResult> QueryDraftContentAsync(
        long id,
        string entityType,
        int? page,
        int? pageSize,
        string? q,
        string? sort,
        string? direction,
        ContentDraftService service,
        CancellationToken cancellationToken)
    {
        var take = pageSize ?? 50;
        var pageNumber = page ?? 1;
        var normalizedSort = string.IsNullOrWhiteSpace(sort) ? "index" : sort.Trim();
        var normalizedDirection = string.IsNullOrWhiteSpace(direction) ? "asc" : direction.Trim();
        var errors = new Dictionary<string, string[]>();
        if (pageNumber < 1) errors["page"] = ["page must be at least 1."];
        if (take is < 1 or > 200) errors["pageSize"] = ["pageSize must be between 1 and 200."];
        if (q is { Length: > 100 }) errors["q"] = ["q must be 100 characters or less."];
        if (normalizedSort is not ("index" or "key" or "name"))
        {
            errors["sort"] = ["sort must be index, key or name."];
        }

        if (normalizedDirection is not ("asc" or "desc"))
        {
            errors["direction"] = ["direction must be asc or desc."];
        }

        if (!ContentDraftService.EntityTypes.Contains(entityType, StringComparer.Ordinal))
        {
            errors["entityType"] = [$"Unknown entity type '{entityType}'."];
        }

        if (errors.Count > 0) return Results.ValidationProblem(errors);

        try
        {
            var (items, total) = await service.QueryEntitiesAsync(
                id,
                entityType,
                pageNumber,
                take,
                q,
                normalizedSort,
                normalizedDirection,
                cancellationToken);
            return Results.Ok(new
            {
                draftId = id,
                entityType,
                page = pageNumber,
                pageSize = take,
                total,
                items
            });
        }
        catch (DraftNotFoundException exception)
        {
            return NotFound(exception.Message);
        }
        catch (DraftConflictException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    private static async Task<IResult> GetDraftEntityAsync(
        long id,
        string entityType,
        long localIndex,
        ContentDraftService service,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await service.GetEntityAsync(id, entityType, localIndex, cancellationToken));
        }
        catch (DraftNotFoundException exception)
        {
            return NotFound(exception.Message);
        }
        catch (DraftConflictException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    private static async Task<IResult> UpsertDraftEntityAsync(
        long id,
        string entityType,
        long localIndex,
        UpsertDraftEntityRequest request,
        ContentDraftService service,
        AuditStore auditStore,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var entity = await service.UpsertEntityAsync(
                id,
                entityType,
                localIndex,
                request,
                RequestMetadata.AccountId(context.User),
                cancellationToken);
            await auditStore.WriteAsync(new AuditEventWrite(
                RequestMetadata.AccountId(context.User),
                context.User.Identity?.Name,
                RequestMetadata.ClientIp(context),
                "draft.update",
                "content_draft",
                id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                null,
                entity.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
                RequestMetadata.RequestId(context),
                "success",
                request.Reason,
                RequestMetadata.UserAgent(context),
                new { entity.EntityType, entity.LocalIndex, entity.ContentKey, entity.Revision }), cancellationToken);
            return Results.Ok(entity);
        }
        catch (DraftNotFoundException exception)
        {
            return NotFound(exception.Message);
        }
        catch (DraftConflictException exception)
        {
            return Conflict(exception.Message);
        }
        catch (DraftInputValidationException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>(exception.Errors));
        }
    }

    private static async Task<IResult> CreateDraftEntityAsync(
        long id,
        string entityType,
        CreateDraftEntityRequest request,
        ContentDraftService service,
        AuditStore auditStore,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var entity = await service.CreateEntityAsync(
                id,
                entityType,
                request,
                RequestMetadata.AccountId(context.User),
                cancellationToken);
            await auditStore.WriteAsync(new AuditEventWrite(
                RequestMetadata.AccountId(context.User),
                context.User.Identity?.Name,
                RequestMetadata.ClientIp(context),
                "draft.entity.create",
                "content_draft",
                id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                null,
                entity.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
                RequestMetadata.RequestId(context),
                "success",
                request.Reason,
                RequestMetadata.UserAgent(context),
                new { entity.EntityType, entity.LocalIndex, entity.ContentKey, entity.Revision }), cancellationToken);
            return Results.Created(
                $"/api/v1/drafts/{id}/content/{entityType}/{entity.LocalIndex}",
                entity);
        }
        catch (DraftNotFoundException exception)
        {
            return NotFound(exception.Message);
        }
        catch (DraftConflictException exception)
        {
            return Conflict(exception.Message);
        }
        catch (DraftInputValidationException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>(exception.Errors));
        }
    }

    private static async Task<IResult> ImportDraftContentAsync(
        long id,
        string entityType,
        bool? dryRun,
        string? reason,
        ContentDraftService service,
        AuditStore auditStore,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        const int maxBytes = 16 * 1024 * 1024;
        using var body = new MemoryStream();
        await context.Request.Body.CopyToAsync(body, cancellationToken);
        if (body.Length > maxBytes)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status413PayloadTooLarge,
                title: "Import too large",
                detail: "JSONL import must be 16 MiB or smaller.");
        }

        var content = System.Text.Encoding.UTF8.GetString(body.ToArray());
        var apply = dryRun == false;
        try
        {
            var result = await service.ImportJsonLinesAsync(
                id,
                entityType,
                content,
                !apply,
                RequestMetadata.AccountId(context.User),
                reason,
                cancellationToken);
            if (apply && result.Valid)
            {
                await auditStore.WriteAsync(new AuditEventWrite(
                    RequestMetadata.AccountId(context.User),
                    context.User.Identity?.Name,
                    RequestMetadata.ClientIp(context),
                    "draft.import",
                    "content_draft",
                    id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    null,
                    result.AppliedLines.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    RequestMetadata.RequestId(context),
                    "success",
                    reason,
                    RequestMetadata.UserAgent(context),
                    new { entityType, result.TotalLines, result.AppliedLines }), cancellationToken);
            }

            return apply && !result.Valid
                ? Results.Json(result, statusCode: StatusCodes.Status400BadRequest)
                : Results.Ok(result);
        }
        catch (DraftNotFoundException exception)
        {
            return NotFound(exception.Message);
        }
        catch (DraftConflictException exception)
        {
            return Conflict(exception.Message);
        }
    }

    private static async Task<IResult> ExportDraftContentAsync(
        long id,
        string entityType,
        ContentDraftService service,
        long? importRunId,
        CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await service.ExportJsonLinesAsync(id, entityType, importRunId, cancellationToken);
            return Results.File(bytes, "application/x-ndjson; charset=utf-8", $"{entityType}.jsonl");
        }
        catch (DraftNotFoundException exception)
        {
            return NotFound(exception.Message);
        }
        catch (DraftConflictException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    private static async Task<IResult> ValidateDraftScriptAsync(
        long id,
        long localIndex,
        ContentDraftService service,
        HttpContext context,
        string? language,
        CancellationToken cancellationToken)
    {
        try
        {
            _ = await service.GetAsync(id, cancellationToken);
            using var body = new MemoryStream();
            await context.Request.Body.CopyToAsync(body, cancellationToken);
            var content = System.Text.Encoding.UTF8.GetString(body.ToArray());
            var source = content;
            if (context.Request.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
            {
                using var document = JsonDocument.Parse(content);
                if (document.RootElement.ValueKind != JsonValueKind.Object ||
                    !document.RootElement.TryGetProperty("source", out var sourceElement) ||
                    sourceElement.ValueKind != JsonValueKind.String)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["source"] = ["source is required and must be a string."]
                    });
                }

                source = sourceElement.GetString() ?? string.Empty;
                if (document.RootElement.TryGetProperty("language", out var languageElement) &&
                    languageElement.ValueKind == JsonValueKind.String)
                {
                    language = languageElement.GetString();
                }
            }

            if (string.IsNullOrWhiteSpace(language))
            {
                var existing = await service.GetEntityAsync(id, "script", localIndex, cancellationToken);
                language = ContentEditorValidation.ReadString(existing.Data, "language");
            }

            return Results.Ok(ContentDraftService.ValidateScript(source, language));
        }
        catch (DraftNotFoundException exception)
        {
            return NotFound(exception.Message);
        }
        catch (DraftConflictException exception)
        {
            return BadRequest(exception.Message);
        }
        catch (JsonException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["source"] = [exception.Message]
            });
        }
    }

    private static async Task<IResult> GetDraftMapRelationsAsync(
        long id,
        long mapIndex,
        ContentDraftStore store,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await store.GetMapRelationsAsync(id, mapIndex, cancellationToken));
        }
        catch (DraftNotFoundException exception)
        {
            return NotFound(exception.Message);
        }
    }

    private static Task<IResult> ReplaceDraftMapMovementsAsync(
        long id,
        long mapIndex,
        long? expectedRevision,
        string? reason,
        ContentDraftStore store,
        AuditStore auditStore,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        return ReplaceDraftMapArrayAsync(id, mapIndex, "Movements", expectedRevision, reason, store,
            auditStore, context, cancellationToken);
    }

    private static Task<IResult> ReplaceDraftMapSafeZonesAsync(
        long id,
        long mapIndex,
        long? expectedRevision,
        string? reason,
        ContentDraftStore store,
        AuditStore auditStore,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        return ReplaceDraftMapArrayAsync(id, mapIndex, "SafeZones", expectedRevision, reason, store,
            auditStore, context, cancellationToken);
    }

    private static Task<IResult> ReplaceDraftMapMineZonesAsync(
        long id,
        long mapIndex,
        long? expectedRevision,
        string? reason,
        ContentDraftStore store,
        AuditStore auditStore,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        return ReplaceDraftMapArrayAsync(id, mapIndex, "MineZones", expectedRevision, reason, store,
            auditStore, context, cancellationToken);
    }

    private static Task<IResult> ReplaceDraftMapActiveCoordsAsync(
        long id,
        long mapIndex,
        long? expectedRevision,
        string? reason,
        ContentDraftStore store,
        AuditStore auditStore,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        return ReplaceDraftMapArrayAsync(id, mapIndex, "ActiveCoords", expectedRevision, reason, store,
            auditStore, context, cancellationToken);
    }

    private static async Task<IResult> ReplaceDraftMapArrayAsync(
        long id,
        long mapIndex,
        string property,
        long? expectedRevision,
        string? reason,
        ContentDraftStore store,
        AuditStore auditStore,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            using var body = await ReadJsonBodyAsync(context.Request, cancellationToken);
            if (body is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["body"] = ["A JSON array or relation request object is required."]
                });
            }

            var root = body.RootElement;
            var revision = expectedRevision;
            JsonElement items;
            string? requestReason = reason;
            if (root.ValueKind == JsonValueKind.Array)
            {
                items = root;
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("expectedRevision", out var revisionElement) &&
                    revisionElement.TryGetInt64(out var parsedRevision))
                {
                    revision = parsedRevision;
                }

                if (root.TryGetProperty("reason", out var reasonElement) &&
                    reasonElement.ValueKind == JsonValueKind.String)
                {
                    requestReason = reasonElement.GetString();
                }

                if (!root.TryGetProperty("items", out items) &&
                    !root.TryGetProperty(property, out items) &&
                    !root.TryGetProperty(property.ToLowerInvariant(), out items))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["items"] = ["items array is required."]
                    });
                }
            }
            else
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["body"] = ["A JSON array or relation request object is required."]
                });
            }

            if (revision is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["expectedRevision"] = ["expectedRevision is required."]
                });
            }

            var entity = await store.ReplaceMapArrayAsync(
                id,
                mapIndex,
                property,
                items,
                revision.Value,
                requestReason,
                RequestMetadata.AccountId(context.User),
                cancellationToken);
            await auditStore.WriteAsync(new AuditEventWrite(
                RequestMetadata.AccountId(context.User),
                context.User.Identity?.Name,
                RequestMetadata.ClientIp(context),
                "draft.map.relations.update",
                "content_draft",
                id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                null,
                entity.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
                RequestMetadata.RequestId(context),
                "success",
                requestReason,
                RequestMetadata.UserAgent(context),
                new { mapIndex, property, entity.Revision }), cancellationToken);
            return Results.Ok(entity);
        }
        catch (DraftNotFoundException exception)
        {
            return NotFound(exception.Message);
        }
        catch (DraftConflictException exception)
        {
            return Conflict(exception.Message);
        }
        catch (DraftInputValidationException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>(exception.Errors));
        }
        catch (JsonException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["body"] = [exception.Message]
            });
        }
    }

    private static Task<IResult> CreateDraftMapRespawnAsync(
        long id,
        long mapIndex,
        long? expectedRevision,
        string? reason,
        ContentDraftStore store,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        return SaveDraftMapRelationEntityAsync(id, mapIndex, "respawn", null, expectedRevision, reason,
            store, context, cancellationToken);
    }

    private static Task<IResult> UpdateDraftMapRespawnAsync(
        long id,
        long mapIndex,
        long respawnIndex,
        long? expectedRevision,
        string? reason,
        ContentDraftStore store,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        return SaveDraftMapRelationEntityAsync(id, mapIndex, "respawn", respawnIndex, expectedRevision, reason,
            store, context, cancellationToken);
    }

    private static Task<IResult> CreateDraftMapNpcAsync(
        long id,
        long mapIndex,
        long? expectedRevision,
        string? reason,
        ContentDraftStore store,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        return SaveDraftMapRelationEntityAsync(id, mapIndex, "npc", null, expectedRevision, reason,
            store, context, cancellationToken);
    }

    private static Task<IResult> UpdateDraftMapNpcAsync(
        long id,
        long mapIndex,
        long npcIndex,
        long? expectedRevision,
        string? reason,
        ContentDraftStore store,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        return SaveDraftMapRelationEntityAsync(id, mapIndex, "npc", npcIndex, expectedRevision, reason,
            store, context, cancellationToken);
    }

    private static async Task<IResult> SaveDraftMapRelationEntityAsync(
        long id,
        long mapIndex,
        string entityType,
        long? localIndex,
        long? expectedRevision,
        string? reason,
        ContentDraftStore store,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            using var body = await ReadJsonBodyAsync(context.Request, cancellationToken);
            if (body is null || body.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["body"] = ["A JSON object is required."]
                });
            }

            var root = body.RootElement;
            JsonElement data = root;
            string? requestReason = reason;
            if (root.TryGetProperty("data", out var wrappedData) && wrappedData.ValueKind == JsonValueKind.Object)
            {
                data = wrappedData;
            }

            if (root.TryGetProperty("expectedRevision", out var revisionElement) &&
                revisionElement.TryGetInt64(out var parsedRevision))
            {
                expectedRevision = parsedRevision;
            }

            if (root.TryGetProperty("reason", out var reasonElement) &&
                reasonElement.ValueKind == JsonValueKind.String)
            {
                requestReason = reasonElement.GetString();
            }

            if (localIndex is not null && expectedRevision is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["expectedRevision"] = ["expectedRevision is required for updates."]
                });
            }

            var entity = await store.SaveMapRelationEntityAsync(
                id,
                mapIndex,
                entityType,
                localIndex,
                expectedRevision,
                data,
                requestReason,
                RequestMetadata.AccountId(context.User),
                cancellationToken);
            return localIndex is null
                ? Results.Created($"/api/v1/drafts/{id}/content/{entityType}/{entity.LocalIndex}", entity)
                : Results.Ok(entity);
        }
        catch (DraftNotFoundException exception)
        {
            return NotFound(exception.Message);
        }
        catch (DraftConflictException exception)
        {
            return Conflict(exception.Message);
        }
        catch (DraftInputValidationException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>(exception.Errors));
        }
        catch (JsonException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["body"] = [exception.Message]
            });
        }
    }

    private static Task<IResult> DeleteDraftMapRespawnAsync(
        long id,
        long mapIndex,
        long respawnIndex,
        long? expectedRevision,
        string? reason,
        ContentDraftStore store,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        return DeleteDraftMapRelationEntityAsync(id, mapIndex, "respawn", respawnIndex, expectedRevision,
            reason, store, context, cancellationToken);
    }

    private static Task<IResult> DeleteDraftMapNpcAsync(
        long id,
        long mapIndex,
        long npcIndex,
        long? expectedRevision,
        string? reason,
        ContentDraftStore store,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        return DeleteDraftMapRelationEntityAsync(id, mapIndex, "npc", npcIndex, expectedRevision,
            reason, store, context, cancellationToken);
    }

    private static async Task<IResult> DeleteDraftMapRelationEntityAsync(
        long id,
        long mapIndex,
        string entityType,
        long localIndex,
        long? expectedRevision,
        string? reason,
        ContentDraftStore store,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            string? requestReason = reason;
            using var body = await ReadJsonBodyAsync(context.Request, cancellationToken);
            if (body is not null && body.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (body.RootElement.TryGetProperty("expectedRevision", out var revisionElement) &&
                    revisionElement.TryGetInt64(out var parsedRevision))
                {
                    expectedRevision = parsedRevision;
                }

                if (body.RootElement.TryGetProperty("reason", out var reasonElement) &&
                    reasonElement.ValueKind == JsonValueKind.String)
                {
                    requestReason = reasonElement.GetString();
                }
            }

            if (expectedRevision is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["expectedRevision"] = ["expectedRevision is required."]
                });
            }

            await store.DeleteMapRelationEntityAsync(
                id,
                mapIndex,
                entityType,
                localIndex,
                expectedRevision.Value,
                requestReason,
                RequestMetadata.AccountId(context.User),
                cancellationToken);
            return Results.NoContent();
        }
        catch (DraftNotFoundException exception)
        {
            return NotFound(exception.Message);
        }
        catch (DraftConflictException exception)
        {
            return Conflict(exception.Message);
        }
        catch (JsonException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["body"] = [exception.Message]
            });
        }
    }

    private static async Task<JsonDocument?> ReadJsonBodyAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        await request.Body.CopyToAsync(stream, cancellationToken);
        if (stream.Length == 0) return null;
        if (stream.Length > 8 * 1024 * 1024)
        {
            throw new JsonException("Relation request body must be 8 MiB or smaller.");
        }

        return JsonDocument.Parse(stream.ToArray());
    }

    private static async Task<IResult> DeleteDraftEntityAsync(
        long id,
        string entityType,
        long localIndex,
        [Microsoft.AspNetCore.Mvc.FromBody] DeleteDraftEntityRequest request,
        ContentDraftService service,
        AuditStore auditStore,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            await service.DeleteEntityAsync(
                id,
                entityType,
                localIndex,
                request.ExpectedRevision,
                request.Reason,
                RequestMetadata.AccountId(context.User),
                cancellationToken);
            await auditStore.WriteAsync(new AuditEventWrite(
                RequestMetadata.AccountId(context.User),
                context.User.Identity?.Name,
                RequestMetadata.ClientIp(context),
                "draft.delete",
                "content_draft",
                id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                request.ExpectedRevision.ToString(System.Globalization.CultureInfo.InvariantCulture),
                null,
                RequestMetadata.RequestId(context),
                "success",
                request.Reason,
                RequestMetadata.UserAgent(context),
                new { EntityType = entityType, LocalIndex = localIndex }), cancellationToken);
            return Results.NoContent();
        }
        catch (DraftNotFoundException exception)
        {
            return NotFound(exception.Message);
        }
        catch (DraftConflictException exception)
        {
            return Conflict(exception.Message);
        }
    }

    private static async Task<IResult> ValidateDraftAsync(
        long id,
        ContentDraftService service,
        AuditStore auditStore,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var report = await service.ValidateAsync(id, cancellationToken);
            await auditStore.WriteAsync(new AuditEventWrite(
                RequestMetadata.AccountId(context.User),
                context.User.Identity?.Name,
                RequestMetadata.ClientIp(context),
                "draft.validate",
                "content_draft",
                id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                null,
                report.ContentHash,
                RequestMetadata.RequestId(context),
                report.Valid ? "success" : "failure",
                null,
                RequestMetadata.UserAgent(context),
                new { report.EntityCount, issueCount = report.Issues.Count, report.Valid }), cancellationToken);
            return Results.Ok(report);
        }
        catch (DraftNotFoundException exception)
        {
            return NotFound(exception.Message);
        }
        catch (DraftConflictException exception)
        {
            return Conflict(exception.Message);
        }
    }

    private static async Task<IResult> ListDraftRevisionsAsync(
        long id,
        string? entityType,
        string? contentKey,
        int? limit,
        ContentDraftService service,
        CancellationToken cancellationToken)
    {
        var take = limit ?? 100;
        if (take is < 1 or > 500) return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["limit"] = ["limit must be between 1 and 500."]
        });

        try
        {
            var revisions = await service.ListRevisionsAsync(
                id,
                entityType,
                contentKey,
                take,
                cancellationToken);
            return Results.Ok(new { draftId = id, limit = take, items = revisions });
        }
        catch (DraftNotFoundException exception)
        {
            return NotFound(exception.Message);
        }
        catch (DraftConflictException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    private static Task WriteAuditAsync(
        AuditStore auditStore,
        HttpContext context,
        string action,
        DraftDto draft,
        string? reason,
        CancellationToken cancellationToken)
    {
        return auditStore.WriteAsync(new AuditEventWrite(
            RequestMetadata.AccountId(context.User),
            context.User.Identity?.Name,
            RequestMetadata.ClientIp(context),
            action,
            "content_draft",
            draft.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            null,
            null,
            RequestMetadata.RequestId(context),
            "success",
            reason,
            RequestMetadata.UserAgent(context),
            new { draft.Name, draft.BaseImportRunId, draft.EntityCount }), cancellationToken);
    }

    private static IResult NotFound(string detail) => Results.Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "Draft not found",
        detail: detail);

    private static IResult Conflict(string detail) => Results.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "Draft conflict",
        detail: detail);

    private static IResult BadRequest(string detail) => Results.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Invalid draft request",
        detail: detail);
}
