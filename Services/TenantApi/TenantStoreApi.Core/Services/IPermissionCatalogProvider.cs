using Onepunch.Common.Lib.Security;

namespace TenantStoreApi.Core.Services;

public record SystemRoleDefinition(string Name, string Description, string? Product = null, string Audience = RoleAudience.Staff);

// One product's slice of the global permission catalog (HRMS today; procurement, accounting and
// billing later). PermissionCatalogSeederService seeds every registered provider, so adding a
// product is adding a provider class and registering it -- the seeder itself doesn't change.
public interface IPermissionCatalogProvider
{
    /// <summary>Product key stored on each Permission row (see <see cref="PermissionProducts"/>).</summary>
    string Product { get; }

    IReadOnlyList<(string Module, string Feature, string[] Actions)> Features { get; }

    /// <summary>System roles this product introduces, on top of the cross-product Owner/Admin/Member/Employee/Client.</summary>
    IReadOnlyList<SystemRoleDefinition> SystemRoles { get; }

    /// <summary>
    /// Fixed grants per system role name, as permission codes. Owner is not listed: it always gets
    /// every permission of every product. Admin gets nothing from a product that doesn't list it.
    /// </summary>
    IReadOnlyDictionary<string, string[]> RoleGrants { get; }
}
