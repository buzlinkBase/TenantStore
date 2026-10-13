using System.Security.Claims;

namespace Onepunch.Common.Lib.Security;

// Reads the role and "permission" claims AuthApi's JwtService mints from the tenant's roles. Shared
// by every API that sits behind that token (HRMS, ADMS and the procurement/accounting/billing
// systems) so they all answer "may this caller?" the same way.
public static class PermissionClaims
{
    public const string PermissionClaimType = "permission";
    public const string OwnerRole = "Owner";
    public const string AdminRole = "Admin";

    public static bool IsOwner(this ClaimsPrincipal user) =>
        user.FindAll(ClaimTypes.Role).Any(c => c.Value == OwnerRole);

    public static bool IsOwnerOrAdmin(this ClaimsPrincipal user) =>
        user.FindAll(ClaimTypes.Role).Any(c => c.Value is OwnerRole or AdminRole);

    // Owner passes everything. Admin passes only HRMS codes: every HRMS permission is guaranteed to
    // Admin anyway (the shortcut just covers the window right after workspace creation, before the
    // async membership rows exist), but a separate system is not granted to Admin by default, so
    // being an HRMS admin must not open it. Everyone else needs the permission claim itself.
    public static bool HasPermission(this ClaimsPrincipal user, string code)
    {
        if (user.IsOwner()) return true;
        if (user.IsOwnerOrAdmin() && PermissionProducts.ProductOf(code) == PermissionProducts.Hrms) return true;
        return user.FindAll(PermissionClaimType).Any(c => c.Value == code);
    }

    public static bool HasAnyPermission(this ClaimsPrincipal user, params string[] codes) =>
        codes.Any(user.HasPermission);
}
