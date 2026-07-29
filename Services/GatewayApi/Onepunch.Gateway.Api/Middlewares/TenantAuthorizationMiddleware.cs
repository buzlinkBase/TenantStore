using Onepunch.Gateway.Api.Services;

namespace Onepunch.Gateway.Api.Middlewares;

/// <summary>
/// Flow D: runs after JWT authentication. Reads the authenticated user's id and the
/// single-tenant "tenantId" claim already baked into every token (see Auth's JwtService), checks
/// the short-TTL role cache / Tenant Service gRPC RPC for a live revocation check, then attaches
/// an X-Tenant-Id header before YARP forwards the request downstream.
/// </summary>
public class TenantAuthorizationMiddleware : IMiddleware
{
    private readonly GatewayMembershipClient _membershipClient;

    public TenantAuthorizationMiddleware(GatewayMembershipClient membershipClient)
    {
        _membershipClient = membershipClient;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        var userIdClaim = context.User.FindFirst("sub")?.Value;
        var tenantIdClaim = context.User.FindFirst("tenantId")?.Value;

        if (!Guid.TryParse(userIdClaim, out var userId) || !Guid.TryParse(tenantIdClaim, out var tenantId))
        {
            // Token has no resolvable tenant context (e.g. pre-provisioning) — let it through
            // and let the downstream service apply its own authorization.
            await next(context);
            return;
        }

        var result = await _membershipClient.CheckAsync(userId, tenantId);
        if (result != null && (!result.Found || result.Status != "Active"))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { message = "You are not an active member of this tenant." });
            return;
        }

        context.Request.Headers["X-Tenant-Id"] = tenantId.ToString();
        await next(context);
    }
}
