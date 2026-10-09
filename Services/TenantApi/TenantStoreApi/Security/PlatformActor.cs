using System.Security.Claims;

namespace TenantStoreApi.Security;

/// <summary>Who a platform action is recorded against: the signed-in operator, from their token.</summary>
public static class PlatformActor
{
    public static string From(ClaimsPrincipal user) =>
        user.FindFirst("email")?.Value is { Length: > 0 } email ? $"platform:{email}" : "platform";
}
