using Crystal.Content;
using Crystal.Control.Api.Auth;
using Microsoft.AspNetCore.Mvc;

namespace Crystal.Control.Api.Content;

public static class ContentEndpoints
{
    private static readonly HashSet<string> EntityTypes =
        new(ContentEntityTypes.All, StringComparer.Ordinal);

    public static RouteGroupBuilder MapContentEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/import-runs", GetImportRunsAsync)
            .RequireAuthorization(Permissions.ContentRead);
        api.MapGet("/import-runs/{id:long}", GetImportRunAsync)
            .RequireAuthorization(Permissions.ContentRead);
        api.MapGet("/import-runs/{id:long}/content-types", GetContentTypeCountsAsync)
            .RequireAuthorization(Permissions.ContentRead);
        api.MapGet("/content/{entityType}", QueryContentAsync)
            .RequireAuthorization(Permissions.ContentRead);
        api.MapGet("/content/{entityType}/{localIndex:long}", GetContentByIndexAsync)
            .RequireAuthorization(Permissions.ContentRead);
        api.MapGet("/content/{entityType}/key/{contentKey}", GetContentByKeyAsync)
            .RequireAuthorization(Permissions.ContentRead);
        return api;
    }

    private static async Task<IResult> GetImportRunsAsync(
        ContentQueryStore store,
        int? limit,
        CancellationToken cancellationToken)
    {
        var take = limit ?? 20;
        if (take is < 1 or > 100)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["limit"] = ["limit must be between 1 and 100."]
            });
        }

        var runs = await store.GetImportRunsAsync(take, cancellationToken);
        return Results.Ok(runs);
    }

    private static async Task<IResult> GetImportRunAsync(
        long id,
        ContentQueryStore store,
        CancellationToken cancellationToken)
    {
        var run = await store.GetImportRunAsync(id, cancellationToken);
        return run is null
            ? Results.Problem(statusCode: 404, title: "Import run not found", detail: $"Import run {id} does not exist.")
            : Results.Ok(run);
    }

    private static async Task<IResult> GetContentTypeCountsAsync(
        long id,
        ContentQueryStore store,
        CancellationToken cancellationToken)
    {
        var run = await store.GetImportRunAsync(id, cancellationToken);
        if (run is null)
        {
            return Results.Problem(
                statusCode: 404,
                title: "Import run not found",
                detail: $"Import run {id} does not exist.");
        }

        var counts = await store.GetContentTypeCountsAsync(id, cancellationToken);
        return Results.Ok(new { importRunId = id, counts });
    }

    private static async Task<IResult> QueryContentAsync(
        string entityType,
        ContentQueryStore store,
        long? importRunId,
        int? page,
        int? pageSize,
        string? q,
        string? sort,
        string? direction,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (!EntityTypes.Contains(entityType))
        {
            errors["entityType"] = [$"Unknown entity type '{entityType}'."];
        }

        var pageNumber = page ?? 1;
        var size = pageSize ?? 50;
        if (pageNumber < 1)
        {
            errors["page"] = ["page must be at least 1."];
        }

        if (size is < 1 or > 200)
        {
            errors["pageSize"] = ["pageSize must be between 1 and 200."];
        }

        var query = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        if (query is { Length: > 100 })
        {
            errors["q"] = ["q must be 100 characters or less."];
        }

        var order = (sort ?? "index").ToLowerInvariant();
        if (order is not ("index" or "key" or "name"))
        {
            errors["sort"] = ["sort must be one of: index, key, name."];
        }

        var orderDirection = (direction ?? "asc").ToLowerInvariant();
        if (orderDirection is not ("asc" or "desc"))
        {
            errors["direction"] = ["direction must be asc or desc."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var runId = await store.ResolveImportRunIdAsync(importRunId, cancellationToken);
        if (runId is null)
        {
            return importRunId is null
                ? Results.Problem(statusCode: 404, title: "No imported content", detail: "No imported content run is available.")
                : Results.Problem(
                    statusCode: 404,
                    title: "Import run not found",
                    detail: $"Imported content run {importRunId} does not exist.");
        }

        var result = await store.QueryContentAsync(
            runId.Value,
            entityType,
            pageNumber,
            size,
            query,
            order,
            orderDirection,
            cancellationToken);
        return Results.Ok(new PagedResult<ContentEntityDto>(
            runId.Value,
            entityType,
            pageNumber,
            size,
            result.Total,
            result.Items));
    }

    private static async Task<IResult> GetContentByIndexAsync(
        string entityType,
        long localIndex,
        ContentQueryStore store,
        long? importRunId,
        CancellationToken cancellationToken)
    {
        if (!EntityTypes.Contains(entityType))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["entityType"] = [$"Unknown entity type '{entityType}'."]
            });
        }

        var runId = await store.ResolveImportRunIdAsync(importRunId, cancellationToken);
        if (runId is null)
        {
            return Results.Problem(statusCode: 404, title: "No imported content", detail: "No imported content run is available.");
        }

        var entity = await store.GetByLocalIndexAsync(runId.Value, entityType, localIndex, cancellationToken);
        return entity is null
            ? Results.Problem(
                statusCode: 404,
                title: "Content entity not found",
                detail: $"{entityType}/{localIndex} was not found in import run {runId.Value}.")
            : Results.Ok(entity);
    }

    private static async Task<IResult> GetContentByKeyAsync(
        string entityType,
        string contentKey,
        ContentQueryStore store,
        long? importRunId,
        CancellationToken cancellationToken)
    {
        if (!EntityTypes.Contains(entityType))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["entityType"] = [$"Unknown entity type '{entityType}'."]
            });
        }

        if (string.IsNullOrWhiteSpace(contentKey) || contentKey.Length > 128)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["contentKey"] = ["contentKey is required and must be 128 characters or less."]
            });
        }

        var runId = await store.ResolveImportRunIdAsync(importRunId, cancellationToken);
        if (runId is null)
        {
            return Results.Problem(statusCode: 404, title: "No imported content", detail: "No imported content run is available.");
        }

        var entity = await store.GetByContentKeyAsync(runId.Value, entityType, contentKey, cancellationToken);
        return entity is null
            ? Results.Problem(
                statusCode: 404,
                title: "Content entity not found",
                detail: $"{contentKey} was not found in import run {runId.Value}.")
            : Results.Ok(entity);
    }
}
