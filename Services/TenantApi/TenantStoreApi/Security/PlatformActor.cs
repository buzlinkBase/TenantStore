using System.Security.Claims;
using TenantStoreApi.Core.Services.Platform;

namespace TenantStoreApi.Security;

/// <summary>Who a platform action is recorded against: the signed-in operator, from their token.</summary>
public static class PlatformActor
{
    public static string From(ClaimsPrincipal user) =>
        user.FindFirst("email")?.Value is { Length: > 0 } email ? $"platform:{email}" : "platform";

    /// <summary>The operator as an identity (token <c>sub</c>) -- for rules about WHO acts, like a
    /// second operator approving a deletion. Emails can be reused; ids can't.</summary>
    public static PlatformOperator Operator(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirst("sub")?.Value, out var id)
            ? new PlatformOperator(id, From(user))
            : throw new UnauthorizedAccessException("The operator's token has no subject.");
}
