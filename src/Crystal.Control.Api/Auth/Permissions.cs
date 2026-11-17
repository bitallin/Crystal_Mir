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
    public const string ContentWrite = "content:write";
    public const string AssetRead = "asset:read";
    public const string ReleaseRead = "release:read";
    public const string ReleaseWrite = "release:write";
    public const string ReleasePublish = "release:publish";
    public const string ReleaseRollback = "release:rollback";
    public const string ZoneRead = "zone:read";
    public const string ZoneWrite = "zone:write";

    public static readonly IReadOnlyList<string> All =
    [
        AccountRead,
        AccountWrite,
        RoleRead,
        RoleWrite,
        AuditRead,
        ContentRead,
        ContentWrite,
        ReleaseRead,
        ReleaseWrite,
        ReleasePublish,
        ReleaseRollback,
        AssetRead,
        "asset:write",
        ZoneRead,
        ZoneWrite,
        "zone:merge",
        "payment:read",
        "payment:write",
        "payment:refund",
        "gm:operate",
        "system:configure"
    ];
}
