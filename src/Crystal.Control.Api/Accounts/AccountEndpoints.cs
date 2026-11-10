using System.Text.RegularExpressions;
using Crystal.Control.Api.Audit;
using Crystal.Control.Api.Auth;
using Microsoft.AspNetCore.Mvc;

namespace Crystal.Control.Api.Accounts;

public static partial class AccountEndpoints
{
    public static RouteGroupBuilder MapAccountEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/accounts", ListAccountsAsync)
            .RequireAuthorization(Permissions.AccountRead);
        api.MapGet("/roles", ListRolesAsync)
            .RequireAuthorization(Permissions.RoleRead);
        api.MapPost("/accounts", CreateAccountAsync)
            .RequireAuthorization(Permissions.AccountWrite);
        api.MapPost("/accounts/{id:long}/status", UpdateAccountStatusAsync)
            .RequireAuthorization(Permissions.AccountWrite);
        api.MapPost("/accounts/{id:long}/password-reset", ResetAccountPasswordAsync)
            .RequireAuthorization(Permissions.AccountWrite);
        api.MapPut("/accounts/{id:long}/roles", UpdateAccountRolesAsync)
            .RequireAuthorization(Permissions.AccountWrite);
        return api;
    }

    private static async Task<IResult> ListAccountsAsync(
        AuthStore store,
        int? limit,
        CancellationToken cancellationToken)
    {
        var take = limit ?? 50;
        if (take is < 1 or > 200)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["limit"] = ["limit must be between 1 and 200."]
            });
        }

        return Results.Ok(new { limit = take, items = await store.ListAccountsAsync(take, cancellationToken) });
    }

    private static async Task<IResult> ListRolesAsync(AuthStore store, CancellationToken cancellationToken)
    {
        return Results.Ok(new { items = await store.ListRolesAsync(cancellationToken) });
    }

    private static async Task<IResult> CreateAccountAsync(
        CreateAccountRequest request,
        AuthStore store,
        PasswordHasher passwordHasher,
        AuditStore auditStore,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var username = request.Username?.Trim();
        if (string.IsNullOrWhiteSpace(username) || !UsernamePattern().IsMatch(username))
        {
            errors["username"] = ["username must be 3-64 characters using letters, digits, dot, underscore or hyphen."];
        }

        var displayName = string.IsNullOrWhiteSpace(request.DisplayName)
            ? username
            : request.DisplayName.Trim();
        if (displayName is null || displayName.Length is < 1 or > 128)
        {
            errors["displayName"] = ["displayName must be between 1 and 128 characters."];
        }

        var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        if (email is { Length: > 254 } || (email is not null && !EmailPattern().IsMatch(email)))
        {
            errors["email"] = ["email must be a valid address of 254 characters or less."];
        }

        PasswordPolicy.Validate(request.Password, "password", errors);
        var roleCodes = NormalizeRoles(request.Roles, errors);

        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var accountId = RequestMetadata.AccountId(context.User);
        try
        {
            var account = await store.CreateAccountAsync(
                username!,
                username!.ToLowerInvariant(),
                email,
                displayName!,
                passwordHasher.Hash(request.Password!),
                roleCodes,
                cancellationToken);

            await auditStore.WriteAsync(
                new AuditEventWrite(
                    accountId,
                    context.User.Identity?.Name,
                    RequestMetadata.ClientIp(context),
                    "account.create",
                    "control_account",
                    account.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    null,
                    null,
                    RequestMetadata.RequestId(context),
                    "success",
                    null,
                    RequestMetadata.UserAgent(context),
                    new { account.Username, roles = account.Roles }),
                cancellationToken);

            return Results.Created($"/api/v1/accounts/{account.Id}", new
            {
                account.Id,
                account.Username,
                account.Email,
                account.DisplayName,
                account.Status,
                account.MfaEnabled,
                account.LastLoginAt,
                account.CreatedAt,
                account.Roles
            });
        }
        catch (DuplicateAccountException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Account already exists",
                detail: "The requested username is already in use.");
        }
        catch (UnknownRoleException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["roles"] = [exception.Message]
            });
        }
    }

    private static async Task<IResult> UpdateAccountStatusAsync(
        long id,
        UpdateAccountStatusRequest request,
        AuthStore store,
        AuditStore auditStore,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var status = request.Status?.Trim().ToLowerInvariant();
        if (status is not ("active" or "disabled"))
        {
            errors["status"] = ["status must be active or disabled."];
        }

        if (errors.Count > 0) return Results.ValidationProblem(errors);

        try
        {
            var result = await store.SetAccountStatusAsync(
                id,
                status!,
                RequestMetadata.AccountId(context.User),
                cancellationToken);
            await auditStore.WriteAsync(
                new AuditEventWrite(
                    RequestMetadata.AccountId(context.User),
                    context.User.Identity?.Name,
                    RequestMetadata.ClientIp(context),
                    "account.status",
                    "control_account",
                    id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    null,
                    null,
                    RequestMetadata.RequestId(context),
                    "success",
                    null,
                    RequestMetadata.UserAgent(context),
                    new
                    {
                        username = result.Account.Username,
                        beforeStatus = result.PreviousStatus,
                        afterStatus = result.Account.Status,
                        revokedSessionCount = result.RevokedSessionCount
                    }),
                cancellationToken);
            return Results.Ok(ToAccountResponse(result.Account));
        }
        catch (AccountNotFoundException)
        {
            return AccountNotFound(id);
        }
        catch (AccountLifecycleConflictException exception)
        {
            return AccountConflict(exception.Message);
        }
    }

    private static async Task<IResult> ResetAccountPasswordAsync(
        long id,
        ResetAccountPasswordRequest request,
        AuthStore store,
        PasswordHasher passwordHasher,
        AuditStore auditStore,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        PasswordPolicy.Validate(request.NewPassword, "newPassword", errors);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var actorAccountId = RequestMetadata.AccountId(context.User);
        if (actorAccountId == id)
        {
            return AccountConflict("Use /api/v1/auth/change-password to change your own password.");
        }

        try
        {
            var result = await store.ResetPasswordAsync(
                id,
                passwordHasher.Hash(request.NewPassword!),
                cancellationToken);
            await auditStore.WriteAsync(
                new AuditEventWrite(
                    actorAccountId,
                    context.User.Identity?.Name,
                    RequestMetadata.ClientIp(context),
                    "account.password.reset",
                    "control_account",
                    id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    null,
                    null,
                    RequestMetadata.RequestId(context),
                    "success",
                    null,
                    RequestMetadata.UserAgent(context),
                    new
                    {
                        username = result.Account.Username,
                        revokedSessionCount = result.RevokedSessionCount
                    }),
                cancellationToken);
            return Results.NoContent();
        }
        catch (AccountNotFoundException)
        {
            return AccountNotFound(id);
        }
    }

    private static async Task<IResult> UpdateAccountRolesAsync(
        long id,
        UpdateAccountRolesRequest request,
        AuthStore store,
        AuditStore auditStore,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var roleCodes = NormalizeRoles(request.Roles, errors);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        try
        {
            var result = await store.ReplaceAccountRolesAsync(id, roleCodes, cancellationToken);
            await auditStore.WriteAsync(
                new AuditEventWrite(
                    RequestMetadata.AccountId(context.User),
                    context.User.Identity?.Name,
                    RequestMetadata.ClientIp(context),
                    "account.roles.update",
                    "control_account",
                    id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    null,
                    null,
                    RequestMetadata.RequestId(context),
                    "success",
                    null,
                    RequestMetadata.UserAgent(context),
                    new
                    {
                        username = result.Account.Username,
                        beforeRoles = result.PreviousRoles,
                        afterRoles = result.Account.Roles,
                        revokedSessionCount = result.RevokedSessionCount
                    }),
                cancellationToken);
            return Results.Ok(ToAccountResponse(result.Account));
        }
        catch (AccountNotFoundException)
        {
            return AccountNotFound(id);
        }
        catch (UnknownRoleException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["roles"] = [exception.Message]
            });
        }
        catch (AccountLifecycleConflictException exception)
        {
            return AccountConflict(exception.Message);
        }
    }

    private static string[] NormalizeRoles(
        IReadOnlyList<string>? roles,
        IDictionary<string, string[]> errors)
    {
        var roleCodes = (roles ?? [])
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Select(role => role.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (roleCodes.Length == 0)
        {
            errors["roles"] = ["at least one role is required."];
        }
        else if (roleCodes.Length > 32)
        {
            errors["roles"] = ["no more than 32 roles can be assigned to one account."];
        }
        else if (roleCodes.Any(role => !RolePattern().IsMatch(role)))
        {
            errors["roles"] = ["roles must use lowercase letters, digits, underscore or hyphen."];
        }

        return roleCodes;
    }

    private static object ToAccountResponse(AccountRecord account)
    {
        return new
        {
            account.Id,
            account.Username,
            account.Email,
            account.DisplayName,
            account.Status,
            account.MfaEnabled,
            account.LastLoginAt,
            account.CreatedAt,
            account.Roles
        };
    }

    private static IResult AccountNotFound(long id)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Account not found",
            detail: $"Account {id} does not exist.");
    }

    private static IResult AccountConflict(string detail)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Account operation conflicts with current state",
            detail: detail);
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{2,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernamePattern();

    [GeneratedRegex("^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    [GeneratedRegex("^[a-z][a-z0-9_-]{1,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex RolePattern();
}

public sealed record CreateAccountRequest(
    string? Username,
    string? Email,
    string? DisplayName,
    string? Password,
    IReadOnlyList<string>? Roles);

public sealed record UpdateAccountStatusRequest(string? Status);

public sealed record ResetAccountPasswordRequest(string? NewPassword);

public sealed record UpdateAccountRolesRequest(IReadOnlyList<string>? Roles);
