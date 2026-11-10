using System.Diagnostics;
using Crystal.Control.Api.Content;

namespace Crystal.Control.Api.Health;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health/live", () => Results.Ok(new
        {
            status = "ok",
            service = "crystal-control-api"
        }));

        endpoints.MapGet("/health/ready", GetReadinessAsync);
        return endpoints;
    }

    private static async Task<IResult> GetReadinessAsync(
        ContentQueryStore store,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var ready = await store.IsDatabaseReadyAsync(cancellationToken);
            stopwatch.Stop();
            if (!ready)
            {
                return Results.Problem(
                    statusCode: 503,
                    title: "Not ready",
                    detail: "Database schema is not initialized.");
            }

            return Results.Ok(new
            {
                status = "ready",
                database = "ok",
                latencyMs = stopwatch.Elapsed.TotalMilliseconds
            });
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            logger.LogWarning(exception, "Control API readiness check failed.");
            return Results.Problem(
                statusCode: 503,
                title: "Not ready",
                detail: "Database readiness check failed.");
        }
    }
}
