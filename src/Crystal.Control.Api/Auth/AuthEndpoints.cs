using System.Text.RegularExpressions;
using Crystal.Control.Api.Audit;
using Microsoft.AspNetCore.Mvc;

namespace Crystal.Control.Api.Auth;

public static partial class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/auth/login", LoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting("auth");
        api.MapPost("/auth/refresh", RefreshAsync)
            .AllowAnonymous()
            .RequireRateLimiting("auth");
        api.MapPost("/auth/logout", LogoutAsync)
            .RequireAuthorization();
        api.MapGet("/auth/me", GetMeAsync)
            .RequireAuthorization();
        api.MapPost("/auth/change-password", ChangePasswordAsync)
            .RequireAuthorization();
        return api;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        AuthStore store,
        PasswordHasher passwordHasher,
        TokenService tokenService,
        AuditStore auditStore,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var username = request.Username?.Trim();
        if (string.IsNullOrWhiteSpace(username) || !UsernamePattern().IsMatch(username))
        {
            errors["username"] = ["username must be 3-64 characters using letters, digits, dot, underscore or hyphen."];
        }

        if (string.IsNullOrEmpty(request.Password) || request.Password.Length > 128)
        {
            errors["password"] = ["password is required and must be 128 characters or less."];
        }

        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var usernameNormalized = username!.ToLowerInvariant();
        var account = await store.GetAccountByUsernameAsync(usernameNormalized, cancellationToken);
        var passwordValid = account is null
            ? VerifyDummyPassword(passwordHasher, request.Password!)
            : passwordHasher.Verify(request.Password!, account.PasswordHash);
        var now = DateTime.UtcNow;
        var locked = account?.LockedUntil is { } lockedUntil && lockedUntil > now;
        var active = account is not null && string.Equals(account.Status, "active", StringComparison.Ordinal);

        if (account is null || !passwordValid || !active || locked)
        {
            if (account is not null && !locked && !passwordValid)
            {
                await store.RecordLoginFailureAsync(account.Id, cancellationToken);
            }

            await TryAuditAsync(
                auditStore,
                logger,
                new AuditEventWrite(
                    account?.Id,
                    username,
                    RequestMetadata.ClientIp(context),
                    "auth.login",
                    "control_account",
                    account?.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    null,
                    null,
                    RequestMetadata.RequestId(context),
                    "failure",
                    account is null ? "unknown_account" : locked ? "locked" : !active ? "inactive" : "invalid_password",
                    RequestMetadata.UserAgent(context),
                    null),
                cancellationToken);
            return Unauthorized(context, "Invalid username or password.");
        }

        await store.RecordLoginSuccessAsync(account.Id, cancellationToken);
        var refresh = tokenService.CreateRefreshTokenMaterial();
        var sessionId = await store.CreateSessionAsync(
            account.Id,
            refresh.Hash,
            refresh.ExpiresAt,
            RequestMetadata.ClientIp(context),
            RequestMetadata.UserAgent(context),
            cancellationToken);
        var access = tokenService.IssueAccessToken(account, sessionId);

        await TryAuditAsync(
            auditStore,
            logger,
            new AuditEventWrite(
                account.Id,
                account.Username,
                RequestMetadata.ClientIp(context),
                "auth.login",
                "control_account",
                account.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                null,
                null,
                RequestMetadata.RequestId(context),
                "success",
                null,
                RequestMetadata.UserAgent(context),
                new { sessionId }),
            cancellationToken);

        return Results.Ok(ToTokenResponse(account, access, refresh, sessionId));
    }

    private static async Task<IResult> RefreshAsync(
        RefreshRequest request,
        AuthStore store,
        TokenService tokenService,
        AuditStore auditStore,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken) || request.RefreshToken.Length > 256)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["refreshToken"] = ["refreshToken is required and must be 256 characters or less."]
            });
        }

        var replacement = tokenService.CreateRefreshTokenMaterial();
        var rotated = await store.RotateRefreshSessionAsync(
            TokenService.HashRefreshToken(request.RefreshToken),
            replacement.Hash,
            replacement.ExpiresAt,
            RequestMetadata.ClientIp(context),
            RequestMetadata.UserAgent(context),
            cancellationToken);

        if (rotated is null)
        {
            await TryAuditAsync(
                auditStore,
                logger,
                new AuditEventWrite(
                    null,
                    null,
                    RequestMetadata.ClientIp(context),
                    "auth.refresh",
                    "control_session",
                    null,
                    null,
                    null,
                    RequestMetadata.RequestId(context),
                    "failure",
                    "invalid_refresh_token",
                    RequestMetadata.UserAgent(context),
                    null),
                cancellationToken);
            return Unauthorized(context, "The refresh token is invalid or expired.");
        }

        var access = tokenService.IssueAccessToken(rotated.Account, rotated.SessionId);
        await TryAuditAsync(
            auditStore,
            logger,
            new AuditEventWrite(
                rotated.Account.Id,
                rotated.Account.Username,
                RequestMetadata.ClientIp(context),
                "auth.refresh",
                "control_session",
                rotated.SessionId.ToString("D"),
                null,
                null,
                RequestMetadata.RequestId(context),
                "success",
                null,
                RequestMetadata.UserAgent(context),
                null),
            cancellationToken);

        return Results.Ok(ToTokenResponse(rotated.Account, access, replacement, rotated.SessionId));
    }

    private static async Task<IResult> LogoutAsync(
        AuthStore store,
        AuditStore auditStore,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var accountId = RequestMetadata.AccountId(context.User);
        var sessionId = RequestMetadata.SessionId(context.User);
        if (accountId is not null && sessionId is not null)
        {
            await store.RevokeSessionAsync(sessionId.Value, accountId.Value, "logout", cancellationToken);
            await TryAuditAsync(
                auditStore,
                logger,
                new AuditEventWrite(
                    accountId,
                    context.User.Identity?.Name,
                    RequestMetadata.ClientIp(context),
                    "auth.logout",
                    "control_session",
                    sessionId.Value.ToString("D"),
                    null,
                    null,
                    RequestMetadata.RequestId(context),
                    "success",
                    null,
                    RequestMetadata.UserAgent(context),
                    null),
                cancellationToken);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> GetMeAsync(
        AuthStore store,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (RequestMetadata.IsApiKey(context.User))
        {
            return Results.Ok(new
            {
                principalType = "api_key",
                username = context.User.Identity?.Name,
                roles = new[] { "service" },
                permissions = new[] { Permissions.Wildcard }
            });
        }

        var accountId = RequestMetadata.AccountId(context.User);
        if (accountId is null) return Unauthorized(context, "A valid access token is required.");

        var account = await store.GetAccountByIdAsync(accountId.Value, cancellationToken);
        return account is null
            ? Unauthorized(context, "The account is no longer available.")
            : Results.Ok(new
            {
                principalType = "account",
                account.Id,
                account.Username,
                account.Email,
                account.DisplayName,
                account.Status,
                account.MfaEnabled,
                account.LastLoginAt,
                account.Roles,
                account.Permissions
            });
    }

    private static async Task<IResult> ChangePasswordAsync(
        ChangePasswordRequest request,
        AuthStore store,
        PasswordHasher passwordHasher,
        AuditStore auditStore,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (RequestMetadata.IsApiKey(context.User))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Password change unavailable",
                detail: "API keys cannot change an account password.");
        }

        var accountId = RequestMetadata.AccountId(context.User);
        if (accountId is null) return Unauthorized(context, "A valid access token is required.");

        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrEmpty(request.CurrentPassword) || request.CurrentPassword.Length > PasswordPolicy.MaxLength)
        {
            errors["currentPassword"] = ["currentPassword is required and must be 128 characters or less."];
        }

        PasswordPolicy.Validate(request.NewPassword, "newPassword", errors);
        if (request.NewPassword is not null && request.NewPassword == request.CurrentPassword)
        {
            errors["newPassword"] = ["newPassword must be different from currentPassword."];
        }

        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var account = await store.GetAccountByIdAsync(accountId.Value, cancellationToken);
        if (account is null) return Unauthorized(context, "The account is no longer available.");

        if (!passwordHasher.Verify(request.CurrentPassword!, account.PasswordHash))
        {
            await TryAuditAsync(
                auditStore,
                logger,
                new AuditEventWrite(
                    account.Id,
                    account.Username,
                    RequestMetadata.ClientIp(context),
                    "auth.password.change",
                    "control_account",
                    account.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    null,
                    null,
                    RequestMetadata.RequestId(context),
                    "failure",
                    "invalid_current_password",
                    RequestMetadata.UserAgent(context),
                    null),
                cancellationToken);
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["currentPassword"] = ["currentPassword is incorrect."]
            });
        }

        var result = await store.ChangePasswordAsync(
            account.Id,
            passwordHasher.Hash(request.NewPassword!),
            RequestMetadata.SessionId(context.User),
            cancellationToken);
        await TryAuditAsync(
            auditStore,
            logger,
            new AuditEventWrite(
                account.Id,
                account.Username,
                RequestMetadata.ClientIp(context),
                "auth.password.change",
                "control_account",
                account.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                null,
                null,
                RequestMetadata.RequestId(context),
                "success",
                null,
                RequestMetadata.UserAgent(context),
                new { revokedSessionCount = result.RevokedSessionCount }),
            cancellationToken);

        return Results.NoContent();
    }

    private static TokenResponse ToTokenResponse(
        AccountRecord account,
        AccessTokenMaterial access,
        RefreshTokenMaterial refresh,
        Guid sessionId)
    {
        return new TokenResponse(
            access.Token,
            refresh.Token,
            "Bearer",
            Math.Max(0, (int)(access.ExpiresAt - DateTime.UtcNow).TotalSeconds),
            access.ExpiresAt,
            refresh.ExpiresAt,
            sessionId,
            new AuthUserDto(
                account.Id,
                account.Username,
                account.Email,
                account.DisplayName,
                account.Status,
                account.MfaEnabled,
                account.Roles,
                account.Permissions));
    }

    private static bool VerifyDummyPassword(PasswordHasher passwordHasher, string password)
    {
        passwordHasher.VerifyDummy(password);
        return false;
    }

    private static IResult Unauthorized(HttpContext context, string detail)
    {
        context.Response.Headers.WWWAuthenticate = "Bearer";
        return Results.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Unauthorized",
            detail: detail);
    }

    private static async Task TryAuditAsync(
        AuditStore store,
        ILogger logger,
        AuditEventWrite entry,
        CancellationToken cancellationToken)
    {
        try
        {
            await store.WriteAsync(entry, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to write audit event {Action}.", entry.Action);
        }
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{2,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernamePattern();
}

public sealed record LoginRequest(string? Username, string? Password);

public sealed record RefreshRequest(string? RefreshToken);

public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

public sealed record AuthUserDto(
    long Id,
    string Username,
    string? Email,
    string DisplayName,
    string Status,
    bool MfaEnabled,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);

public sealed record TokenResponse(
    string AccessToken,
    string RefreshToken,
    string TokenType,
    int ExpiresIn,
    DateTime AccessTokenExpiresAt,
    DateTime RefreshTokenExpiresAt,
    Guid SessionId,
    AuthUserDto User);
