using System.Text.Json;
using System.Text.RegularExpressions;
using Crystal.Agent.Contracts;
using Crystal.Control.Api.Auth;
using Crystal.Control.Api.Content;

namespace Crystal.Control.Api.Zones;

public static class ZoneEndpoints
{
    private static readonly HashSet<string> CommandTypes = new(
        [
            AgentCommandTypes.StageContent,
            AgentCommandTypes.ActivateContent,
            AgentCommandTypes.RollbackContent,
            AgentCommandTypes.ReloadContent,
            AgentCommandTypes.StopZone
        ],
        StringComparer.Ordinal);

    private static readonly HashSet<string> HeartbeatStatuses = new(
        [AgentStatuses.Online, AgentStatuses.Degraded],
        StringComparer.Ordinal);

    private static readonly HashSet<string> ResultStatuses = new(
        [AgentStatuses.Succeeded, AgentStatuses.Failed, AgentStatuses.Rejected],
        StringComparer.Ordinal);

    private static readonly Regex ZoneIdPattern = new(
        "^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$",
        RegexOptions.CultureInvariant);

    public static RouteGroupBuilder MapZoneEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/zones/register", RegisterAsync)
            .AllowAnonymous();
        api.MapGet("/zones", ListZonesAsync)
            .AllowAnonymous();
        api.MapGet("/zones/{zoneId}", GetZoneAsync)
            .AllowAnonymous();
        api.MapGet("/zones/{zoneId}/metrics", GetZoneMetricsAsync)
            .AllowAnonymous();
        api.MapPost("/zones/{zoneId}/heartbeat", HeartbeatAsync)
            .AllowAnonymous();
        api.MapGet("/zones/{zoneId}/commands", ListCommandsAsync)
            .AllowAnonymous();
        api.MapPost("/zones/{zoneId}/commands", CreateCommandAsync)
            .RequireAuthorization(Permissions.ZoneWrite);
        api.MapPost("/zones/{zoneId}/commands/{commandId}/result", CompleteCommandAsync)
            .AllowAnonymous();
        return api;
    }

    private static async Task<IResult> RegisterAsync(
        AgentRegistrationRequest request,
        ZoneStore store,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (!IsValidZoneId(request.ZoneId))
        {
            errors["zoneId"] = ["zoneId must match ^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$."];
        }

        if (string.IsNullOrWhiteSpace(request.ZoneName) || request.ZoneName.Length > 128)
        {
            errors["zoneName"] = ["zoneName must be between 1 and 128 characters."];
        }

        if (string.IsNullOrWhiteSpace(request.AgentId) || request.AgentId.Length > 128)
        {
            errors["agentId"] = ["agentId must be between 1 and 128 characters."];
        }

        if (string.IsNullOrWhiteSpace(request.AgentVersion) || request.AgentVersion.Length > 64)
        {
            errors["agentVersion"] = ["agentVersion must be between 1 and 64 characters."];
        }

        if (request.ProtocolVersion <= 0 || request.ContentSchemaVersion <= 0)
        {
            errors["protocolVersion"] = ["protocolVersion and contentSchemaVersion must be positive."];
        }

        if (request.StartedAt == default)
        {
            errors["startedAt"] = ["startedAt is required."];
        }

        var capabilities = request.Capabilities ?? Array.Empty<string>();
        if (capabilities.Count > 32 ||
            capabilities.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 64))
        {
            errors["capabilities"] = ["capabilities must contain at most 32 non-empty values of 64 characters or less."];
        }

        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var normalized = request with
        {
            ZoneName = request.ZoneName.Trim(),
            AgentId = request.AgentId.Trim(),
            AgentVersion = request.AgentVersion.Trim(),
            Capabilities = capabilities.Distinct(StringComparer.Ordinal).ToArray()
        };
        return Results.Ok(await store.RegisterAsync(normalized, cancellationToken));
    }

    private static async Task<IResult> ListZonesAsync(
        ZoneStore store,
        int? limit,
        CancellationToken cancellationToken)
    {
        var take = limit ?? 100;
        if (take is < 1 or > 200)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["limit"] = ["limit must be between 1 and 200."]
            });
        }

        return Results.Ok(new ZoneListResponse(await store.ListAsync(take, cancellationToken)));
    }

    private static async Task<IResult> GetZoneAsync(
        string zoneId,
        ZoneStore store,
        CancellationToken cancellationToken)
    {
        if (!IsValidZoneId(zoneId)) return InvalidZoneId();
        var zone = await store.GetAsync(zoneId, cancellationToken);
        return zone is null
            ? ZoneNotFound(zoneId)
            : Results.Ok(zone);
    }

    private static async Task<IResult> HeartbeatAsync(
        string zoneId,
        AgentHeartbeatRequest request,
        ZoneStore store,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (!IsValidZoneId(zoneId))
        {
            errors["zoneId"] = ["zoneId is invalid."];
        }

        if (!string.Equals(zoneId, request.ZoneId, StringComparison.Ordinal))
        {
            errors["zoneId"] = ["Route zoneId must match the heartbeat zoneId."];
        }

        if (string.IsNullOrWhiteSpace(request.AgentId))
        {
            errors["agentId"] = ["agentId is required."];
        }

        if (!HeartbeatStatuses.Contains(request.Status))
        {
            errors["status"] = ["status must be online or degraded."];
        }

        if (request.ObservedAt == default)
        {
            errors["observedAt"] = ["observedAt is required."];
        }

        if (request.PlayerCount < 0 || request.TickP95Ms < 0 ||
            request.ErrorRate is < 0 or > 1)
        {
            errors["metrics"] = ["playerCount and tickP95Ms must be non-negative; errorRate must be between 0 and 1."];
        }

        if (request.ActiveReleaseId is <= 0)
        {
            errors["activeReleaseId"] = ["activeReleaseId must be positive when supplied."];
        }

        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var zone = await store.HeartbeatAsync(zoneId, request, cancellationToken);
        return zone is null
            ? Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Zone Agent is not registered",
                detail: $"Zone {zoneId} is not registered for agent {request.AgentId}.")
            : Results.NoContent();
    }

    private static async Task<IResult> GetZoneMetricsAsync(
        string zoneId,
        ZoneStore store,
        ControlApiOptions options,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int? limit,
        CancellationToken cancellationToken)
    {
        if (!IsValidZoneId(zoneId)) return InvalidZoneId();

        var take = limit ?? 300;
        if (take is < 1 or > 2000)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["limit"] = ["limit must be between 1 and 2000."]
            });
        }

        var windowEnd = (to ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var windowStart = (from ?? windowEnd.AddHours(-1)).ToUniversalTime();
        if (windowStart >= windowEnd)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["from"] = ["from must be earlier than to."]
            });
        }

        if (windowEnd - windowStart > TimeSpan.FromDays(7))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["from"] = ["The metrics query window must not exceed 7 days."]
            });
        }

        var zone = await store.GetAsync(zoneId, cancellationToken);
        if (zone is null) return ZoneNotFound(zoneId);

        IReadOnlyList<ZoneMetricSampleDto> samples;
        try
        {
            samples = await store.ListMetricSamplesAsync(
                zoneId,
                windowStart,
                windowEnd,
                take,
                cancellationToken);
        }
        catch (ZoneNotFoundException exception)
        {
            return ZoneNotFound(exception.Message);
        }

        var summary = BuildMetricsSummary(samples);
        var health = ZoneHealthEvaluator.Evaluate(
            zone,
            samples,
            DateTimeOffset.UtcNow,
            new ZoneHealthThresholds(
                options.ZoneTickP95WarningMs,
                options.ZoneTickP95CriticalMs,
                options.ZoneErrorRateWarning,
                options.ZoneErrorRateCritical));
        return Results.Ok(new ZoneMetricsResponse(
            zoneId,
            windowStart,
            windowEnd,
            summary,
            health,
            samples));
    }

    private static ZoneMetricsSummaryDto BuildMetricsSummary(IReadOnlyList<ZoneMetricSampleDto> samples)
    {
        if (samples.Count == 0)
        {
            return new ZoneMetricsSummaryDto(
                0,
                null,
                null,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0);
        }

        var latest = samples[^1];
        return new ZoneMetricsSummaryDto(
            samples.Count,
            samples[0].ObservedAt,
            latest.ObservedAt,
            latest.PlayerCount,
            latest.TickP95Ms,
            latest.ErrorRate,
            samples.Average(sample => sample.PlayerCount),
            samples.Average(sample => sample.TickP95Ms),
            samples.Max(sample => sample.TickP95Ms),
            samples.Average(sample => sample.ErrorRate),
            samples.Max(sample => sample.ErrorRate));
    }

    private static async Task<IResult> ListCommandsAsync(
        string zoneId,
        ZoneStore store,
        bool? claim,
        int? limit,
        int? leaseSeconds,
        CancellationToken cancellationToken)
    {
        if (!IsValidZoneId(zoneId)) return InvalidZoneId();
        var take = limit ?? 50;
        if (take is < 1 or > 200)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["limit"] = ["limit must be between 1 and 200."]
            });
        }

        if (claim == true)
        {
            var lease = leaseSeconds ?? 60;
            if (lease is < 5 or > 300)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["leaseSeconds"] = ["leaseSeconds must be between 5 and 300."]
                });
            }

            try
            {
                var claimed = await store.ClaimCommandsAsync(zoneId, take, lease, cancellationToken);
                return Results.Ok(new AgentCommandListResponse(claimed));
            }
            catch (ZoneNotFoundException exception)
            {
                return ZoneNotFound(exception.Message);
            }
        }

        try
        {
            return Results.Ok(new ZoneCommandListResponse(
                await store.ListCommandsAsync(zoneId, take, cancellationToken)));
        }
        catch (ZoneNotFoundException exception)
        {
            return ZoneNotFound(exception.Message);
        }
    }

    private static async Task<IResult> CreateCommandAsync(
        string zoneId,
        CreateZoneCommandRequest request,
        ZoneStore store,
        ContentReleaseService releaseService,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (!IsValidZoneId(zoneId))
        {
            errors["zoneId"] = ["zoneId is invalid."];
        }

        var type = request.Type?.Trim();
        if (type is null || !CommandTypes.Contains(type))
        {
            errors["type"] = ["type is not a supported Agent command."];
        }

        long? releaseId = null;
        JsonElement? payload = null;
        if (type is AgentCommandTypes.StageContent or AgentCommandTypes.ActivateContent or AgentCommandTypes.RollbackContent)
        {
            if (!TryReadReleaseId(request.Payload, out var parsedReleaseId))
            {
                errors["payload"] = ["release commands require payload.releaseId as a positive integer."];
            }
            else
            {
                releaseId = parsedReleaseId;
                payload = JsonSerializer.SerializeToElement(
                    new ReleaseCommandPayload(parsedReleaseId),
                    AgentContractJson.Options);
            }
        }
        else if (request.Payload is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined })
        {
            errors["payload"] = ["reload and stop commands do not accept a payload."];
        }

        var expiresAt = request.ExpiresAt?.ToUniversalTime() ?? DateTimeOffset.UtcNow.AddMinutes(10);
        var now = DateTimeOffset.UtcNow;
        if (expiresAt <= now || expiresAt > now.AddHours(24))
        {
            errors["expiresAt"] = ["expiresAt must be in the future and no more than 24 hours away."];
        }

        if (errors.Count > 0) return Results.ValidationProblem(errors);

        if (releaseId is long id)
        {
            try
            {
                var release = await releaseService.GetAsync(id, cancellationToken);
                if (release.Status is not ("published" or "active"))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["payload"] = [$"Release {id} is '{release.Status}', expected published or active."]
                    });
                }

                await releaseService.GetArtifactAsync(id, cancellationToken);
            }
            catch (ReleaseNotFoundException exception)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["payload"] = [exception.Message]
                });
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
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["payload"] = [exception.Message]
                });
            }
        }

        try
        {
            var command = await store.CreateCommandAsync(
                zoneId,
                type!,
                releaseId,
                payload,
                expiresAt,
                cancellationToken);
            return Results.Created(
                $"/api/v1/zones/{zoneId}/commands/{command.CommandId}",
                command);
        }
        catch (ZoneNotFoundException exception)
        {
            return ZoneNotFound(exception.Message);
        }
        catch (ZoneCommandConflictException exception)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Zone command conflict",
                detail: exception.Message);
        }
    }

    private static async Task<IResult> CompleteCommandAsync(
        string zoneId,
        Guid commandId,
        AgentCommandResult result,
        ZoneStore store,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (!IsValidZoneId(zoneId))
        {
            errors["zoneId"] = ["zoneId is invalid."];
        }

        if (!string.Equals(zoneId, result.ZoneId, StringComparison.Ordinal))
        {
            errors["zoneId"] = ["Route zoneId must match the command result zoneId."];
        }

        if (commandId.ToString() != result.CommandId)
        {
            errors["commandId"] = ["commandId must match the route command id."];
        }

        if (!ResultStatuses.Contains(result.Status))
        {
            errors["status"] = ["status must be succeeded, failed or rejected."];
        }

        if (result.Status is AgentStatuses.Failed or AgentStatuses.Rejected &&
            string.IsNullOrWhiteSpace(result.Error))
        {
            errors["error"] = ["error is required for failed or rejected commands."];
        }

        if (result.Error is { Length: > 2000 })
        {
            errors["error"] = ["error must not exceed 2000 characters."];
        }

        if (errors.Count > 0) return Results.ValidationProblem(errors);

        try
        {
            var command = await store.CompleteCommandAsync(zoneId, commandId, result, cancellationToken);
            return command is null
                ? Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Zone command not found",
                    detail: $"Command {commandId} does not exist for zone {zoneId}.")
                : Results.Ok(command);
        }
        catch (ZoneCommandConflictException exception)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Zone command conflict",
                detail: exception.Message);
        }
    }

    private static bool TryReadReleaseId(JsonElement? payload, out long releaseId)
    {
        releaseId = 0;
        return payload is { ValueKind: JsonValueKind.Object } value &&
               value.TryGetProperty("releaseId", out var property) &&
               property.TryGetInt64(out releaseId) &&
               releaseId > 0;
    }

    private static bool IsValidZoneId(string? zoneId)
    {
        return !string.IsNullOrWhiteSpace(zoneId) && ZoneIdPattern.IsMatch(zoneId);
    }

    private static IResult InvalidZoneId()
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["zoneId"] = ["zoneId must match ^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$."]
        });
    }

    private static IResult ZoneNotFound(string zoneIdOrDetail)
    {
        var detail = zoneIdOrDetail.StartsWith("Zone ", StringComparison.Ordinal)
            ? zoneIdOrDetail
            : $"Zone {zoneIdOrDetail} does not exist.";
        return Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Zone not found",
            detail: detail);
    }
}
