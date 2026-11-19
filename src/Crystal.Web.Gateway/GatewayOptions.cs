using System.Globalization;
using System.Net;

namespace Crystal.Web.Gateway;

public sealed class GatewayOptions
{
    public const string Protocol = "crystal-v1";

    public string ZoneId { get; init; } = "default";
    public string ZoneTcp { get; init; } = "127.0.0.1:7000";
    public IReadOnlyList<string> AllowedOrigins { get; init; } = ["http://localhost:5173", "http://127.0.0.1:5173"];
    public int MaxFrameBytes { get; init; } = 256 * 1024;
    public int MaxFramesPerSecond { get; init; } = 180;
    public int MaxBytesPerSecond { get; init; } = 4 * 1024 * 1024;
    public int MaxConnections { get; init; } = 256;
    public int IdleTimeoutSeconds { get; init; } = 30;
    public bool AllowMissingOrigin { get; init; }
    public string AssetVersion { get; init; } = "dev";
    public string AssetManifestUrl { get; init; } = "/assets/dev/manifest.json";
    public string ClientBuildHash { get; init; } = "sha256:dev";

    public static GatewayOptions FromConfiguration(IConfiguration configuration)
    {
        string zoneId = configuration["CRYSTAL_WEB_ZONE_ID"] ?? "default";
        string zoneTcp = configuration["CRYSTAL_WEB_ZONE_TCP"] ?? "127.0.0.1:7000";
        string originsValue = configuration["CRYSTAL_WEB_ALLOWED_ORIGINS"] ?? string.Empty;
        string[] origins = originsValue
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new GatewayOptions
        {
            ZoneId = zoneId,
            ZoneTcp = zoneTcp,
            AllowedOrigins = origins.Length == 0
                ? ["http://localhost:5173", "http://127.0.0.1:5173"]
                : origins,
            MaxFrameBytes = ReadInt(configuration, "CRYSTAL_WEB_MAX_FRAME_BYTES", 256 * 1024),
            MaxFramesPerSecond = ReadInt(configuration, "CRYSTAL_WEB_MAX_FRAMES_PER_SECOND", 180),
            MaxBytesPerSecond = ReadInt(configuration, "CRYSTAL_WEB_MAX_BYTES_PER_SECOND", 4 * 1024 * 1024),
            MaxConnections = ReadInt(configuration, "CRYSTAL_WEB_MAX_CONNECTIONS", 256),
            IdleTimeoutSeconds = ReadInt(configuration, "CRYSTAL_WEB_IDLE_TIMEOUT_SECONDS", 30),
            AllowMissingOrigin = ReadBool(configuration, "CRYSTAL_WEB_ALLOW_MISSING_ORIGIN", false),
            AssetVersion = configuration["CRYSTAL_WEB_ASSET_VERSION"] ?? "dev",
            AssetManifestUrl = configuration["CRYSTAL_WEB_ASSET_MANIFEST_URL"] ?? "/assets/dev/manifest.json",
            ClientBuildHash = configuration["CRYSTAL_WEB_CLIENT_BUILD_HASH"] ?? "sha256:dev",
        };
    }

    public bool IsOriginAllowed(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin)) return AllowMissingOrigin;
        if (AllowedOrigins.Contains("*", StringComparer.Ordinal)) return true;

        if (!Uri.TryCreate(origin, UriKind.Absolute, out Uri? candidate)) return false;

        foreach (string allowed in AllowedOrigins)
        {
            if (Uri.TryCreate(allowed, UriKind.Absolute, out Uri? configured) &&
                string.Equals(candidate.Scheme, configured.Scheme, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(candidate.Host, configured.Host, StringComparison.OrdinalIgnoreCase) &&
                candidate.Port == configured.Port)
            {
                return true;
            }
        }

        return false;
    }

    public IPEndPoint ParseUpstream()
    {
        if (string.IsNullOrWhiteSpace(ZoneTcp))
            throw new InvalidOperationException("CRYSTAL_WEB_ZONE_TCP is empty.");

        int separator = ZoneTcp.LastIndexOf(':');
        if (separator <= 0 || separator == ZoneTcp.Length - 1 ||
            !int.TryParse(ZoneTcp[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int port) ||
            port is < 1 or > 65535)
        {
            throw new InvalidOperationException($"Invalid CRYSTAL_WEB_ZONE_TCP '{ZoneTcp}'. Expected host:port.");
        }

        string host = ZoneTcp[..separator].Trim('[', ']');
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            return new IPEndPoint(IPAddress.Loopback, port);

        if (!IPAddress.TryParse(host, out IPAddress? address))
        {
            IPAddress[] addresses = Dns.GetHostAddresses(host);
            address = addresses.FirstOrDefault(static value => value.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                ?? addresses.FirstOrDefault()
                ?? throw new InvalidOperationException($"Cannot resolve zone host '{host}'.");
        }

        return new IPEndPoint(address, port);
    }

    private static int ReadInt(IConfiguration configuration, string key, int fallback)
    {
        string? value = configuration[key];
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) && parsed > 0
            ? parsed
            : fallback;
    }

    private static bool ReadBool(IConfiguration configuration, string key, bool fallback)
    {
        string? value = configuration[key];
        return bool.TryParse(value, out bool parsed) ? parsed : fallback;
    }
}
