using Crystal.Control.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Crystal.Control.Api.Audit;

public sealed class AuditAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden)
        {
            try
            {
                var store = context.RequestServices.GetRequiredService<AuditStore>();
                await store.WriteAsync(
                    new AuditEventWrite(
                        RequestMetadata.AccountId(context.User),
                        context.User.Identity?.Name,
                        RequestMetadata.ClientIp(context),
                        "authz.denied",
                        "http",
                        context.Request.Path,
                        null,
                        null,
                        RequestMetadata.RequestId(context),
                        "denied",
                        "insufficient_permission",
                        RequestMetadata.UserAgent(context),
                        new { method = context.Request.Method }),
                    context.RequestAborted);
            }
            catch (Exception exception)
            {
                var logger = context.RequestServices.GetRequiredService<ILogger<AuditAuthorizationResultHandler>>();
                logger.LogError(exception, "Failed to write authorization denial audit event.");
            }
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
