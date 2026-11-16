using Crystal.Control.Api.Audit;
using Crystal.Control.Api.Auth;

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
