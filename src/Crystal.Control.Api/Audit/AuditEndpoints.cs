using Crystal.Control.Api.Auth;

namespace Crystal.Control.Api.Audit;

public static class AuditEndpoints
{
    public static RouteGroupBuilder MapAuditEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/audit/events", QueryAuditEventsAsync)
            .RequireAuthorization(Permissions.AuditRead);
        return api;
    }

    private static async Task<IResult> QueryAuditEventsAsync(
        AuditStore store,
        int? limit,
        string? action,
        string? result,
        long? actorAccountId,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var take = limit ?? 50;
        if (take is < 1 or > 200)
        {
            errors["limit"] = ["limit must be between 1 and 200."];
        }

        var normalizedAction = string.IsNullOrWhiteSpace(action) ? null : action.Trim();
        if (normalizedAction is { Length: > 128 })
        {
            errors["action"] = ["action must be 128 characters or less."];
        }

        var normalizedResult = string.IsNullOrWhiteSpace(result) ? null : result.Trim().ToLowerInvariant();
        if (normalizedResult is not null && normalizedResult is not ("success" or "failure" or "denied"))
        {
            errors["result"] = ["result must be success, failure or denied."];
        }

        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var events = await store.QueryAsync(
            take,
            normalizedAction,
            normalizedResult,
            actorAccountId,
            cancellationToken);
        return Results.Ok(new { limit = take, items = events });
    }
}
