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
        api.MapGet("/content/map/catalog", GetMapCatalogAsync)
            .RequireAuthorization(Permissions.ContentRead);
        api.MapGet("/content/map/{localIndex:long}/relations", GetMapRelationsAsync)
            .RequireAuthorization(Permissions.ContentRead);
        api.MapGet("/content/monster/{localIndex:long}/drops", GetMonsterDropsAsync)
            .RequireAuthorization(Permissions.ContentRead);
        api.MapGet("/content/npc/{localIndex:long}/script", GetNpcScriptAsync)
            .RequireAuthorization(Permissions.ContentRead);
        api.MapGet("/content/script/{localIndex:long}/validation", GetScriptValidationAsync)
            .RequireAuthorization(Permissions.ContentRead);
        api.MapGet("/content/recipe/{localIndex:long}/relations", GetRecipeRelationsAsync)
            .RequireAuthorization(Permissions.ContentRead);
        api.MapGet("/content/asset/{localIndex:long}/file", GetAssetFileAsync)
            .RequireAuthorization(Permissions.AssetRead);
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

    private static async Task<IResult> GetMapCatalogAsync(
        ContentQueryStore store,
        long? importRunId,
        int? page,
        int? pageSize,
        string? q,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var pageNumber = page ?? 1;
        var size = pageSize ?? 500;
        if (pageNumber < 1)
        {
            errors["page"] = ["page must be at least 1."];
        }

        if (size is < 1 or > 500)
        {
            errors["pageSize"] = ["pageSize must be between 1 and 500."];
        }

        var query = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        if (query is { Length: > 100 })
        {
            errors["q"] = ["q must be 100 characters or less."];
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

        var (items, total) = await store.QueryMapCatalogAsync(
            runId.Value,
            pageNumber,
            size,
            query,
            cancellationToken);
        return Results.Ok(new PagedResult<MapSummaryDto>(
            runId.Value,
            ContentEntityTypes.Map,
            pageNumber,
            size,
            total,
            items));
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

    private static async Task<IResult> GetMapRelationsAsync(
        long localIndex,
        ContentQueryStore store,
        ContentRelationStore relationStore,
        long? importRunId,
        int? respawnPage,
        int? respawnPageSize,
        CancellationToken cancellationToken)
    {
        var page = respawnPage ?? 1;
        var size = respawnPageSize ?? 200;
        var errors = new Dictionary<string, string[]>();
        if (page < 1)
        {
            errors["respawnPage"] = ["respawnPage must be at least 1."];
        }

        if (size is < 1 or > 500)
        {
            errors["respawnPageSize"] = ["respawnPageSize must be between 1 and 500."];
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

        var relations = await relationStore.GetMapRelationsAsync(
            runId.Value,
            localIndex,
            page,
            size,
            cancellationToken);
        return relations is null
            ? Results.Problem(
                statusCode: 404,
                title: "Map not found",
                detail: $"map/{localIndex} was not found in import run {runId.Value}.")
            : Results.Ok(relations);
    }

    private static async Task<IResult> GetMonsterDropsAsync(
        long localIndex,
        ContentQueryStore store,
        ContentRelationStore relationStore,
        long? importRunId,
        CancellationToken cancellationToken)
    {
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

        var drops = await relationStore.GetMonsterDropsAsync(runId.Value, localIndex, cancellationToken);
        return drops is null
            ? Results.Problem(
                statusCode: 404,
                title: "Monster not found",
                detail: $"monster/{localIndex} was not found in import run {runId.Value}.")
            : Results.Ok(drops);
    }

    private static async Task<IResult> GetNpcScriptAsync(
        long localIndex,
        ContentQueryStore store,
        ContentRelationStore relationStore,
        long? importRunId,
        CancellationToken cancellationToken)
    {
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

        var script = await relationStore.GetNpcScriptAsync(runId.Value, localIndex, cancellationToken);
        return script is null
            ? Results.Problem(
                statusCode: 404,
                title: "NPC not found",
                detail: $"npc/{localIndex} was not found in import run {runId.Value}.")
            : Results.Ok(script);
    }

    private static async Task<IResult> GetScriptValidationAsync(
        long localIndex,
        ContentQueryStore store,
        ContentRelationStore relationStore,
        long? importRunId,
        CancellationToken cancellationToken)
    {
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

        var validation = await relationStore.GetScriptValidationAsync(
            runId.Value,
            localIndex,
            cancellationToken);
        return validation is null
            ? Results.Problem(
                statusCode: 404,
                title: "Script not found",
                detail: $"script/{localIndex} was not found in import run {runId.Value}.")
            : Results.Ok(validation);
    }

    private static async Task<IResult> GetAssetFileAsync(
        HttpContext context,
        long localIndex,
        ContentQueryStore store,
        ContentAssetService assetService,
        long? importRunId,
        CancellationToken cancellationToken)
    {
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

        var asset = await assetService.ResolveAsync(runId.Value, localIndex, cancellationToken);
        if (asset.IsOk && asset.FullPath is not null && asset.Entity is not null)
        {
            var sha256 = ReadAssetSha256(asset.Entity.Data);
            if (!string.IsNullOrWhiteSpace(sha256))
            {
                context.Response.Headers.ETag = $"\"{sha256}\"";
            }

            context.Response.Headers.CacheControl = "private, max-age=300";
            return Results.File(
                asset.FullPath,
                asset.ContentType ?? "application/octet-stream",
                Path.GetFileName(asset.FullPath),
                enableRangeProcessing: true);
        }

        return asset.Status switch
        {
            ContentAssetStatus.NotFound => Results.Problem(
                statusCode: 404,
                title: "Asset not found",
                detail: $"asset/{localIndex} was not found in import run {runId.Value}."),
            ContentAssetStatus.NotConfigured => Results.Problem(
                statusCode: 503,
                title: "Asset store is not configured",
                detail: asset.Error),
            ContentAssetStatus.MissingFile => Results.Problem(
                statusCode: 404,
                title: "Asset file is missing",
                detail: asset.Error),
            ContentAssetStatus.InvalidPath => Results.Problem(
                statusCode: 400,
                title: "Invalid asset path",
                detail: asset.Error),
            ContentAssetStatus.IntegrityMismatch => Results.Problem(
                statusCode: 409,
                title: "Asset integrity check failed",
                detail: asset.Error),
            _ => Results.Problem(
                statusCode: 422,
                title: "Invalid asset metadata",
                detail: asset.Error)
        };
    }

    private static string? ReadAssetSha256(System.Text.Json.JsonElement data)
    {
        return data.ValueKind == System.Text.Json.JsonValueKind.Object &&
               data.TryGetProperty("sha256", out var value) &&
               value.ValueKind == System.Text.Json.JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static async Task<IResult> GetRecipeRelationsAsync(
        long localIndex,
        ContentQueryStore store,
        ContentRelationStore relationStore,
        long? importRunId,
        CancellationToken cancellationToken)
    {
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

        var recipe = await relationStore.GetRecipeRelationsAsync(runId.Value, localIndex, cancellationToken);
        return recipe is null
            ? Results.Problem(
                statusCode: 404,
                title: "Recipe not found",
                detail: $"recipe/{localIndex} was not found in import run {runId.Value}.")
            : Results.Ok(recipe);
    }
}
