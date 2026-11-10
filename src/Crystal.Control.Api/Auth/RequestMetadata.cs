using System.Security.Claims;

namespace Crystal.Control.Api.Auth;

public static class RequestMetadata
{
    public static string? ClientIp(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        if (address is null) return null;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        return address.ToString();
    }

    public static string RequestId(HttpContext context)
    {
        var supplied = context.Request.Headers["X-Request-ID"].ToString();
        if (!string.IsNullOrWhiteSpace(supplied) && supplied.Length <= 128) return supplied;
        return context.TraceIdentifier;
    }

    public static string? UserAgent(HttpContext context)
    {
        return context.Request.Headers.UserAgent.ToString();
    }

    public static long? AccountId(ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? principal.FindFirstValue("sub");
        return long.TryParse(value, out var accountId) ? accountId : null;
    }

    public static Guid? SessionId(ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue("sid");
        return Guid.TryParse(value, out var sessionId) ? sessionId : null;
    }

    public static bool IsApiKey(ClaimsPrincipal principal)
    {
        return string.Equals(
            principal.FindFirstValue("auth_method"),
            "api_key",
            StringComparison.Ordinal);
    }
}
