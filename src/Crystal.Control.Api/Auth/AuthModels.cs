namespace Crystal.Control.Api.Auth;

public sealed record AccountRecord(
    long Id,
    string Username,
    string? Email,
    string DisplayName,
    string PasswordHash,
    string Status,
    bool MfaEnabled,
    int FailedLoginCount,
    DateTime? LockedUntil,
    DateTime? LastLoginAt,
    DateTime CreatedAt,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);

public sealed record AccountListItem(
    long Id,
    string Username,
    string? Email,
    string DisplayName,
    string Status,
    bool MfaEnabled,
    DateTime? LastLoginAt,
    DateTime CreatedAt,
    IReadOnlyList<string> Roles);

public sealed record SessionAccountRecord(AccountRecord Account, Guid SessionId);

public sealed record IssuedTokens(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresAt,
    DateTime RefreshTokenExpiresAt);

public sealed record AccessTokenMaterial(string Token, DateTime ExpiresAt);

public sealed record RefreshTokenMaterial(string Token, string Hash, DateTime ExpiresAt);

public sealed record AccountStatusChangeResult(
    AccountRecord Account,
    string PreviousStatus,
    int RevokedSessionCount);

public sealed record AccountRoleChangeResult(
    AccountRecord Account,
    IReadOnlyList<string> PreviousRoles,
    int RevokedSessionCount);

public sealed record AccountPasswordChangeResult(
    AccountRecord Account,
    int RevokedSessionCount);
