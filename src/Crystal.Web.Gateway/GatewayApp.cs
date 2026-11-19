using System.Net.WebSockets;
using System.Text;

namespace Crystal.Web.Gateway;

public static class GatewayApp
{
    public static WebApplication Build(string[] args, GatewayOptions? options = null)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        bool fromConfiguration = options is null;
        GatewayOptions gatewayOptions = options ?? GatewayOptions.FromConfiguration(builder.Configuration);
        if (fromConfiguration && builder.Environment.IsProduction())
            ValidateProductionConfiguration(gatewayOptions);
        builder.Services.AddSingleton(gatewayOptions);
        builder.Services.AddSingleton<GatewayConnectionLimiter>();

        WebApplication app = builder.Build();
        app.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromSeconds(15),
        });

        app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
        app.MapGet("/health/ready", () => Results.Ok(new
        {
            status = "ready",
            zoneId = gatewayOptions.ZoneId,
            upstream = gatewayOptions.ZoneTcp,
        }));
        app.MapGet("/api/web/bootstrap", () => Results.Ok(new
        {
            protocol = GatewayOptions.Protocol,
            zoneId = gatewayOptions.ZoneId,
            wsUrl = $"/ws/game?zone={Uri.EscapeDataString(gatewayOptions.ZoneId)}",
            assetVersion = gatewayOptions.AssetVersion,
            assetManifestUrl = gatewayOptions.AssetManifestUrl,
            clientBuildHash = gatewayOptions.ClientBuildHash,
        }));
        app.MapGet("/metrics", () => Results.Text(GatewayMetrics.SnapshotJson(), "application/json"));
        app.MapGet("/ws/game", HandleWebSocketAsync);

        return app;
    }

    private static void ValidateProductionConfiguration(GatewayOptions options)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(options.ClientBuildHash, "^sha256:[0-9a-f]{64}$"))
            throw new InvalidOperationException("CRYSTAL_WEB_CLIENT_BUILD_HASH must be the real Web client build hash in Production.");

        if (string.IsNullOrWhiteSpace(options.AssetVersion) ||
            string.Equals(options.AssetVersion, "dev", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("CRYSTAL_WEB_ASSET_VERSION must be an immutable production asset version.");

        if (options.AllowedOrigins.Contains("*", StringComparer.Ordinal))
            throw new InvalidOperationException("Wildcard Origin is not allowed in Production.");
    }

    private static async Task HandleWebSocketAsync(
        HttpContext context,
        GatewayOptions options,
        GatewayConnectionLimiter limiter)
    {
        string? origin = context.Request.Headers["Origin"].ToString();
        if (!options.IsOriginAllowed(origin))
        {
            GatewayMetrics.OriginRejected();
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        string zone = context.Request.Query["zone"].ToString();
        if (string.IsNullOrWhiteSpace(zone)) zone = options.ZoneId;
        if (!string.Equals(zone, options.ZoneId, StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (!limiter.TryEnter(options.MaxConnections))
        {
            GatewayMetrics.Rejected();
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        string? requestedProtocol = context.WebSockets.WebSocketRequestedProtocols
            .FirstOrDefault(value => string.Equals(value, GatewayOptions.Protocol, StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(requestedProtocol))
        {
            limiter.Exit();
            context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
            await context.Response.WriteAsync($"WebSocket subprotocol {GatewayOptions.Protocol} is required.");
            return;
        }

        bool opened = false;
        try
        {
            using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync(requestedProtocol);
            opened = true;
            GatewayMetrics.Accepted();
            GatewayMetrics.ConnectionOpened();
            await WebSocketBridge.RunAsync(
                socket,
                options.ParseUpstream(),
                options,
                context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Crystal.Web.Gateway.Bridge"),
                context.RequestAborted);
        }
        catch (Exception exception) when (exception is WebSocketException or InvalidOperationException)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Crystal.Web.Gateway")
                .LogWarning(exception, "WebSocket session failed before or during bridge startup.");
        }
        finally
        {
            if (opened) GatewayMetrics.ConnectionClosed();
            limiter.Exit();
        }
    }
}

public sealed class GatewayConnectionLimiter
{
    private int _active;

    public bool TryEnter(int limit)
    {
        if (limit <= 0) return false;
        int current = Volatile.Read(ref _active);
        while (current < limit)
        {
            int updated = Interlocked.CompareExchange(ref _active, current + 1, current);
            if (updated == current) return true;
            current = updated;
        }

        return false;
    }

    public void Exit() => Interlocked.Decrement(ref _active);
}
