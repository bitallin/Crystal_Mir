namespace Crystal.Control.Api.Auth;

public static class Permissions
{
    public const string ClaimType = "permission";
    public const string Wildcard = "*";

    public const string AccountRead = "account:read";
    public const string AccountWrite = "account:write";
    public const string RoleRead = "role:read";
    public const string RoleWrite = "role:write";
    public const string AuditRead = "audit:read";
    public const string ContentRead = "content:read";
    public const string AssetRead = "asset:read";

    public static readonly IReadOnlyList<string> All =
    [
        AccountRead,
        AccountWrite,
        RoleRead,
        RoleWrite,
        AuditRead,
        ContentRead,
        "content:write",
        "release:read",
        "release:write",
        "release:publish",
        "release:rollback",
        AssetRead,
        "asset:write",
        "zone:read",
        "zone:write",
        "zone:merge",
        "payment:read",
        "payment:write",
        "payment:refund",
        "gm:operate",
        "system:configure"
    ];
}
