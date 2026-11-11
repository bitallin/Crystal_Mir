using System.Threading.RateLimiting;
using Crystal.Control.Api;
using Crystal.Control.Api.Accounts;
using Crystal.Control.Api.Audit;
using Crystal.Control.Api.Auth;
using Crystal.Control.Api.Content;
using Crystal.Control.Api.Health;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
var options = ControlApiOptions.Load(builder.Configuration, builder.Environment);

builder.WebHost.ConfigureKestrel(serverOptions => serverOptions.AddServerHeader = false);
builder.Services.AddSingleton(options);
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(options.DatabaseConnectionString));
builder.Services.AddSingleton<ContentQueryStore>();
builder.Services.AddSingleton<ContentRelationStore>();
builder.Services.AddSingleton<PasswordHasher>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<AuthStore>();
builder.Services.AddSingleton<AuditStore>();
builder.Services.AddSingleton<AccountBootstrap>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, AuditAuthorizationResultHandler>();
builder.Services.AddCors(cors =>
{
    cors.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(options.CorsOrigins.ToArray())
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(jwt =>
    {
        jwt.MapInboundClaims = false;
    });
builder.Services.AddAuthorization(authorization =>
{
    foreach (var permission in Permissions.All)
    {
        authorization.AddPolicy(permission, policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.AddRequirements(new PermissionRequirement(permission));
        });
    }
});
builder.Services.AddProblemDetails();
builder.Services.AddRateLimiter(rateLimiter =>
{
    rateLimiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    rateLimiter.AddPolicy("api", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = options.ApiRateLimitPermitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    rateLimiter.AddPolicy("auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = options.AuthRateLimitPermitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

var app = builder.Build();

var tokenService = app.Services.GetRequiredService<TokenService>();
var jwtOptions = app.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
    .Get(JwtBearerDefaults.AuthenticationScheme);
jwtOptions.TokenValidationParameters = tokenService.CreateValidationParameters();
jwtOptions.Events = new JwtBearerEvents
{
    OnTokenValidated = async context =>
    {
        var principal = context.Principal;
        var sessionValue = principal?.FindFirst("sid")?.Value;
        var accountValue = principal?.FindFirst("sub")?.Value;
        if (!Guid.TryParse(sessionValue, out var sessionId) ||
            !long.TryParse(accountValue, out var accountId))
        {
            context.Fail("Access token is missing a valid session or account identifier.");
            return;
        }

        var store = context.HttpContext.RequestServices.GetRequiredService<AuthStore>();
        if (!await store.IsSessionActiveAsync(sessionId, accountId, context.HttpContext.RequestAborted))
        {
            context.Fail("The session has expired or has been revoked.");
        }
    }
};

if (options.IsEphemeralJwtSigningKey)
{
    app.Logger.LogWarning(
        "Using an ephemeral JWT signing key. Set CRYSTAL_CONTROL_JWT_SIGNING_KEY for stable sessions.");
}

if (options.ApiKey is null)
{
    app.Logger.LogWarning(
        "Service API key is disabled. Only account access tokens can call the Control API.");
}

app.UseExceptionHandler();
app.UseStatusCodePages(async statusContext =>
{
    var response = statusContext.HttpContext.Response;
    if (response.HasStarted) return;

    response.ContentType = "application/problem+json";
    await response.WriteAsJsonAsync(
        new ProblemDetails
        {
            Status = response.StatusCode,
            Title = "Request failed",
            Instance = statusContext.HttpContext.Request.Path
        },
        options: null,
        contentType: "application/problem+json",
        cancellationToken: statusContext.HttpContext.RequestAborted);
});
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    await next(context);
});
if (options.CorsOrigins.Count > 0)
{
    app.UseCors();
}
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<ApiKeyMiddleware>();
app.UseAuthorization();

app.MapHealthEndpoints();
var api = app.MapGroup("/api/v1").RequireRateLimiting("api");
api.MapAuthEndpoints();
api.MapContentEndpoints();
api.MapAccountEndpoints();
api.MapAuditEndpoints();

await app.Services.GetRequiredService<AccountBootstrap>()
    .EnsureAsync(app.Lifetime.ApplicationStopping);

var dataSource = app.Services.GetRequiredService<NpgsqlDataSource>();
app.Lifetime.ApplicationStopped.Register(dataSource.Dispose);

app.Run();

public partial class Program;
