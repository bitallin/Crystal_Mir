using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace Crystal.Control.Api.Auth;

public sealed class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ControlApiOptions _options;

    public ApiKeyMiddleware(RequestDelegate next, ControlApiOptions options)
    {
        _next = next;
        _options = options;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsPublicPath(context.Request.Path))
        {
            await _next(context);
            return;
        }

        var headerApiKey = context.Request.Headers["X-Api-Key"].ToString();
        if (!string.IsNullOrWhiteSpace(headerApiKey))
        {
            if (TryAuthenticateApiKey(context, headerApiKey)) await _next(context);
            else await WriteUnauthorizedAsync(context);
            return;
        }

        var authorization = context.Request.Headers.Authorization.ToString();
        const string bearerPrefix = "Bearer ";
        if (authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var bearerValue = authorization[bearerPrefix.Length..].Trim();
            if (TryAuthenticateApiKey(context, bearerValue))
            {
                await _next(context);
                return;
            }

            if (!LooksLikeJwt(bearerValue))
            {
                await WriteUnauthorizedAsync(context);
                return;
            }
        }

        await _next(context);
    }

    private static bool IsPublicPath(PathString path)
    {
        return path.StartsWithSegments("/health") ||
               path.Equals("/api/v1/auth/login", StringComparison.OrdinalIgnoreCase) ||
               path.Equals("/api/v1/auth/refresh", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryAuthenticateApiKey(HttpContext context, string provided)
    {
        if (_options.ApiKey is null || !FixedTimeEquals(provided, _options.ApiKey)) return false;

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, "control-api-key"),
                new Claim("auth_method", "api_key"),
                new Claim(Permissions.ClaimType, Permissions.Wildcard)
            ],
            authenticationType: "ApiKey",
            nameType: ClaimTypes.Name,
            roleType: ClaimTypes.Role);
        context.User = new ClaimsPrincipal(identity);
        return true;
    }

    private static bool LooksLikeJwt(string token)
    {
        return token.Count(character => character == '.') == 2;
    }

    private static async Task WriteUnauthorizedAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = "Bearer";
        await context.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Unauthorized",
                Detail = "A valid API key or access token is required.",
                Instance = context.Request.Path
            },
            options: null,
            contentType: "application/problem+json",
            cancellationToken: context.RequestAborted);
    }

    private static bool FixedTimeEquals(string provided, string expected)
    {
        var providedBytes = Encoding.UTF8.GetBytes(provided);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        return providedBytes.Length == expectedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }
}
