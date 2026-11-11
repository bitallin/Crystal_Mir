using System.Security.Cryptography;
using System.Text;

namespace Crystal.Control.Api;

public sealed class ControlApiOptions
{
    public required string DatabaseConnectionString { get; init; }

    public string? ApiKey { get; init; }

    public required string JwtSigningKey { get; init; }

    public bool IsEphemeralJwtSigningKey { get; init; }

    public required string JwtIssuer { get; init; }

    public required string JwtAudience { get; init; }

    public required TimeSpan AccessTokenLifetime { get; init; }

    public required TimeSpan RefreshTokenLifetime { get; init; }

    public required int ApiRateLimitPermitLimit { get; init; }

    public required int AuthRateLimitPermitLimit { get; init; }

    public required string BootstrapAdminUsername { get; init; }

    public required string BootstrapAdminDisplayName { get; init; }

    public string? BootstrapAdminPassword { get; init; }

    public required IReadOnlyList<string> CorsOrigins { get; init; }

    public string? AssetRoot { get; init; }

    public static ControlApiOptions Load(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var connectionString = FirstNonEmpty(
            configuration.GetConnectionString("Crystal"),
            configuration["ControlApi:DatabaseConnectionString"],
            Environment.GetEnvironmentVariable("CRYSTAL_DB_CONNECTION"),
            Environment.GetEnvironmentVariable("CRYSTAL_DATABASE_URL"));

        if (connectionString is null)
        {
            throw new InvalidOperationException(
                "Database connection is not configured. Set ConnectionStrings__Crystal or CRYSTAL_DB_CONNECTION.");
        }

        var apiKey = FirstNonEmpty(
            configuration["ControlApi:ApiKey"],
            Environment.GetEnvironmentVariable("CRYSTAL_CONTROL_API_KEY"));

        var jwtSigningKey = FirstNonEmpty(
            configuration["ControlApi:JwtSigningKey"],
            Environment.GetEnvironmentVariable("CRYSTAL_CONTROL_JWT_SIGNING_KEY"));
        var isEphemeralJwtSigningKey = false;

        if (jwtSigningKey is null)
        {
            if (environment.IsProduction())
            {
                throw new InvalidOperationException(
                    "CRYSTAL_CONTROL_JWT_SIGNING_KEY must be set in production for account authentication.");
            }

            jwtSigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            isEphemeralJwtSigningKey = true;
        }

        if (Encoding.UTF8.GetByteCount(jwtSigningKey) < 32)
        {
            throw new InvalidOperationException("CRYSTAL_CONTROL_JWT_SIGNING_KEY must be at least 32 bytes.");
        }

        var accessTokenMinutes = ReadInt(configuration, "ControlApi:AccessTokenMinutes", 15, 1, 60);
        var refreshTokenDays = ReadInt(configuration, "ControlApi:RefreshTokenDays", 30, 1, 90);
        var apiRateLimitPermitLimit = ReadInt(
            configuration,
            "ControlApi:ApiRateLimitPermitLimit",
            240,
            1,
            100_000);
        var authRateLimitPermitLimit = ReadInt(
            configuration,
            "ControlApi:AuthRateLimitPermitLimit",
            10,
            1,
            100_000);
        var corsOrigins = (FirstNonEmpty(
                configuration["ControlApi:CorsOrigins"],
                Environment.GetEnvironmentVariable("CRYSTAL_CONTROL_CORS_ORIGINS")) ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ControlApiOptions
        {
            DatabaseConnectionString = PostgresConnectionString.Normalize(connectionString),
            ApiKey = apiKey,
            JwtSigningKey = jwtSigningKey,
            IsEphemeralJwtSigningKey = isEphemeralJwtSigningKey,
            JwtIssuer = FirstNonEmpty(configuration["ControlApi:JwtIssuer"]) ?? "crystal-control-api",
            JwtAudience = FirstNonEmpty(configuration["ControlApi:JwtAudience"]) ?? "crystal-control",
            AccessTokenLifetime = TimeSpan.FromMinutes(accessTokenMinutes),
            RefreshTokenLifetime = TimeSpan.FromDays(refreshTokenDays),
            ApiRateLimitPermitLimit = apiRateLimitPermitLimit,
            AuthRateLimitPermitLimit = authRateLimitPermitLimit,
            BootstrapAdminUsername = FirstNonEmpty(
                configuration["ControlApi:BootstrapAdminUsername"],
                Environment.GetEnvironmentVariable("CRYSTAL_CONTROL_BOOTSTRAP_USERNAME")) ?? "admin",
            BootstrapAdminDisplayName = FirstNonEmpty(
                configuration["ControlApi:BootstrapAdminDisplayName"],
                Environment.GetEnvironmentVariable("CRYSTAL_CONTROL_BOOTSTRAP_DISPLAY_NAME")) ?? "Platform Administrator",
            BootstrapAdminPassword = FirstNonEmpty(
                configuration["ControlApi:BootstrapAdminPassword"],
                Environment.GetEnvironmentVariable("CRYSTAL_CONTROL_BOOTSTRAP_PASSWORD")),
            CorsOrigins = corsOrigins,
            AssetRoot = FirstNonEmpty(
                configuration["ControlApi:AssetRoot"],
                Environment.GetEnvironmentVariable("CRYSTAL_CONTROL_ASSET_ROOT"))
        };
    }

    private static int ReadInt(IConfiguration configuration, string key, int fallback, int minimum, int maximum)
    {
        var configured = configuration[key];
        if (string.IsNullOrWhiteSpace(configured)) return fallback;
        if (!int.TryParse(configured, out var value) || value < minimum || value > maximum)
        {
            throw new InvalidOperationException($"{key} must be an integer between {minimum} and {maximum}.");
        }

        return value;
    }

    public static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}
