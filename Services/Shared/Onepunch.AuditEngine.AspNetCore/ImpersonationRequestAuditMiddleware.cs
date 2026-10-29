using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Onepunch.AuditEngine.AspNetCore;

/// <summary>
/// While a support operator is signed in as a company user, records EVERY request -- reads included --
/// as an <see cref="EventType"/> event: the operator is the actor, the user they acted as is in the
/// metadata (see <see cref="ClaimsPrincipalAuditActorResolver"/>). The event is written and committed
/// before the request runs, and a request that can't be recorded is refused: no unaudited action
/// is possible inside an impersonation session.
///
/// Place after authentication. Requests without the impersonation claim pass through untouched.
/// </summary>
public sealed class ImpersonationRequestAuditMiddleware(RequestDelegate next, ClaimsAuditActorOptions claimOptions)
{
    public const string EventType = "ImpersonatedRequest";

    public async Task InvokeAsync(HttpContext context, IAuditWriter writer, IAuditCommitter committer, ILogger<ImpersonationRequestAuditMiddleware> logger)
    {
        var user = context.User;
        if (user.Identity?.IsAuthenticated != true || user.FindFirst(claimOptions.OperatorIdClaimType) == null)
        {
            await next(context);
            return;
        }

        try
        {
            await writer.WriteAsync(new AuditEvent
            {
                Id = Guid.NewGuid(),
                OccurredAtUtc = DateTime.UtcNow,
                EventType = EventType,
                EntityType = "Request",
                EntityId = context.Request.Path.Value,
                Description = $"{context.Request.Method} {context.Request.Path.Value}",
            }, context.RequestAborted);
            await committer.CommitAsync(context.RequestAborted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Impersonation request could not be audited; refusing {Method} {Path}",
                context.Request.Method, context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        await next(context);
    }
}

public static class ImpersonationRequestAuditExtensions
{
    public static IApplicationBuilder UseImpersonationRequestAudit(this IApplicationBuilder app) =>
        app.UseMiddleware<ImpersonationRequestAuditMiddleware>();
}
