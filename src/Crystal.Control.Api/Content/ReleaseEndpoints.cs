using Crystal.Control.Api.Audit;
using Crystal.Control.Api.Auth;

namespace Crystal.Control.Api.Content;

public static class ReleaseEndpoints
{
    private static readonly HashSet<string> ReleaseStatuses = new(
        [
            "draft",
            "validating",
            "compiled",
            "signed",
            "published",
            "active",
            "archived",
            "failed",
            "rolled_back"
        ],
        StringComparer.Ordinal);

    public static RouteGroupBuilder MapReleaseEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/releases", ListReleasesAsync)
            .RequireAuthorization(Permissions.ReleaseRead);
        api.MapGet("/releases/{id:long}", GetReleaseAsync)
            .RequireAuthorization(Permissions.ReleaseRead);
        api.MapGet("/releases/{id:long}/manifest", GetReleaseManifestAsync)
            .RequireAuthorization(Permissions.ReleaseRead);
        api.MapPost("/releases", CreateReleaseAsync)
            .RequireAuthorization(Permissions.ReleaseWrite);
        api.MapPost("/releases/{id:long}/validate", ValidateReleaseAsync)
            .RequireAuthorization(Permissions.ReleaseWrite);
        api.MapPost("/releases/{id:long}/compile", CompileReleaseAsync)
            .RequireAuthorization(Permissions.ReleaseWrite);
        api.MapPost("/releases/{id:long}/sign", SignReleaseAsync)
            .RequireAuthorization(Permissions.ReleaseWrite);
        api.MapPost("/releases/{id:long}/publish", PublishReleaseAsync)
            .RequireAuthorization(Permissions.ReleasePublish);
        return api;
    }

    private static async Task<IResult> ListReleasesAsync(
        ContentReleaseService service,
        int? limit,
        string? status,
        CancellationToken cancellationToken)
    {
        var take = limit ?? 50;
        var errors = new Dictionary<string, string[]>();
        if (take is < 1 or > 200)
        {
            errors["limit"] = ["limit must be between 1 and 200."];
        }

        var normalizedStatus = string.IsNullOrWhiteSpace(status) ? null : status.Trim();
        if (normalizedStatus is not null && !ReleaseStatuses.Contains(normalizedStatus))
        {
            errors["status"] = [$"Unknown release status '{normalizedStatus}'."];
        }

        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var releases = await service.ListAsync(take, normalizedStatus, cancellationToken);
        return Results.Ok(new { limit = take, items = releases });
    }

    private static async Task<IResult> GetReleaseAsync(
        long id,
        ContentReleaseService service,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await service.GetAsync(id, cancellationToken));
        }
        catch (ReleaseNotFoundException exception)
        {
            return NotFound(exception.Message);
        }
    }

    private static async Task<IResult> GetReleaseManifestAsync(
        long id,
        ContentReleaseService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var json = await service.GetManifestJsonAsync(id, cancellationToken);
            return Results.Content(json, "application/json");
        }
        catch (ReleaseNotFoundException exception)
        {
            return NotFound(exception.Message);
        }
        catch (ReleaseConflictException exception)
        {
            return Conflict(exception.Message);
        }
    }

    private static async Task<IResult> CreateReleaseAsync(
        CreateReleaseRequest request,
        ContentReleaseService service,
        AuditStore auditStore,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var version = request.Version?.Trim();
        if (!ContentReleaseService.IsValidVersion(version))
        {
            errors["version"] = ["version must be 1-128 characters using letters, digits, dot, underscore or hyphen."];
        }

        var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        if (notes is { Length: > 2000 })
        {
            errors["notes"] = ["notes must be 2000 characters or less."];
        }

        if (request.ImportRunId is <= 0)
        {
            errors["importRunId"] = ["importRunId must be a positive integer when supplied."];
        }

        if (errors.Count > 0) return Results.ValidationProblem(errors);

        try
        {
            var release = await service.CreateAsync(
                request.ImportRunId,
                version!,
                notes,
                RequestMetadata.AccountId(context.User),
                cancellationToken);
            await WriteAuditAsync(
                auditStore,
                context,
                "release.create",
                release,
                "success",
                null,
                cancellationToken);
            return Results.Created($"/api/v1/releases/{release.Id}", release);
        }
        catch (ReleaseNotFoundException exception)
        {
            return NotFound(exception.Message);
        }
        catch (ReleaseConflictException exception)
        {
            return Conflict(exception.Message);
        }
        catch (ReleaseValidationException exception)
        {
            return Results.Json(
                exception.Report,
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }
        catch (InvalidDataException exception)
        {
            return Conflict(exception.Message);
        }
    }

    private static async Task<IResult> ValidateReleaseAsync(
        long id,
        ContentReleaseService service,
        AuditStore auditStore,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var report = await service.ValidateAsync(id, cancellationToken);
            var release = await service.GetAsync(id, cancellationToken);
            await WriteAuditAsync(
                auditStore,
                context,
                "release.validate",
                release,
                report.Valid ? "success" : "failure",
                report.Valid ? null : $"{report.Issues.Count} validation issue(s).",
                cancellationToken);
            return report.Valid
                ? Results.Ok(report)
                : Results.Json(report, statusCode: StatusCodes.Status422UnprocessableEntity);
        }
        catch (ReleaseNotFoundException exception)
        {
            return NotFound(exception.Message);
        }
    }

    private static async Task<IResult> CompileReleaseAsync(
        long id,
        ContentReleaseService service,
        AuditStore auditStore,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var release = await service.CompileAsync(id, cancellationToken);
            await WriteAuditAsync(
                auditStore,
                context,
                "release.compile",
                release,
                "success",
                null,
                cancellationToken);
            return Results.Ok(release);
        }
        catch (ReleaseNotFoundException exception)
        {
            return NotFound(exception.Message);
        }
        catch (ReleaseValidationException exception)
        {
            var release = await service.GetAsync(id, cancellationToken);
            await WriteAuditAsync(
                auditStore,
                context,
                "release.compile",
                release,
                "failure",
                exception.Message,
                cancellationToken);
            return Results.Json(
                exception.Report,
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }
        catch (ReleaseConflictException exception)
        {
            return Conflict(exception.Message);
        }
        catch (InvalidDataException exception)
        {
            return Conflict(exception.Message);
        }
    }

    private static async Task<IResult> SignReleaseAsync(
        long id,
        ContentReleaseService service,
        AuditStore auditStore,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var release = await service.SignAsync(id, cancellationToken);
            await WriteAuditAsync(
                auditStore,
                context,
                "release.sign",
                release,
                "success",
                null,
                cancellationToken);
            return Results.Ok(release);
        }
        catch (ReleaseNotFoundException exception)
        {
            return NotFound(exception.Message);
        }
        catch (ReleaseSigningNotConfiguredException exception)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Release signing is not configured",
                detail: exception.Message);
        }
        catch (ReleaseConflictException exception)
        {
            return Conflict(exception.Message);
        }
        catch (InvalidDataException exception)
        {
            return Conflict(exception.Message);
        }
    }

    private static async Task<IResult> PublishReleaseAsync(
        long id,
        ContentReleaseService service,
        AuditStore auditStore,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var release = await service.PublishAsync(id, cancellationToken);
            await WriteAuditAsync(
                auditStore,
                context,
                "release.publish",
                release,
                "success",
                null,
                cancellationToken);
            return Results.Ok(release);
        }
        catch (ReleaseNotFoundException exception)
        {
            return NotFound(exception.Message);
        }
        catch (ReleaseSigningNotConfiguredException exception)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Release signing is not configured",
                detail: exception.Message);
        }
        catch (ReleaseConflictException exception)
        {
            return Conflict(exception.Message);
        }
        catch (InvalidDataException exception)
        {
            return Conflict(exception.Message);
        }
    }

    private static async Task WriteAuditAsync(
        AuditStore auditStore,
        HttpContext context,
        string action,
        ReleaseDto release,
        string result,
        string? reason,
        CancellationToken cancellationToken)
    {
        await auditStore.WriteAsync(
            new AuditEventWrite(
                RequestMetadata.AccountId(context.User),
                context.User.Identity?.Name,
                RequestMetadata.ClientIp(context),
                action,
                "content_release",
                release.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                null,
                release.ContentHash,
                RequestMetadata.RequestId(context),
                result,
                reason,
                RequestMetadata.UserAgent(context),
                new
                {
                    release.Version,
                    release.Status,
                    release.ImportRunId,
                    release.ContentHash,
                    entityCount = release.Counts.Sum(count => count.Count)
                }),
            cancellationToken);
    }

    private static IResult NotFound(string detail)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Release not found",
            detail: detail);
    }

    private static IResult Conflict(string detail)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Release conflict",
            detail: detail);
    }
}
