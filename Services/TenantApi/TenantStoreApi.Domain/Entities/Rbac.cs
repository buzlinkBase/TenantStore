using BuzlinkRepository;
using Onepunch.AuditEngine;
using Onepunch.Common.Lib.Security;

namespace TenantStoreApi.Domain.Entities;

// Global feature/action catalog -- TenantContext has no auto-tenant-scoping (every service
// filters by TenantId by hand), so this is a genuinely single, shared table across every tenant,
// not re-seeded per tenant. One row per (Feature, Action) pair, e.g. Feature="Payroll Runs",
// Action="Approve", Code="Payroll Runs:Approve".
public class Permission : BaseEntity
{
    // Which system the permission belongs to. Module stays the UI grouping inside a product.
    public string Product { get; set; } = PermissionProducts.Hrms;
    public string Module { get; set; } = string.Empty;
    public string Feature { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

// TenantId null = System Role (Owner/Admin/Member/Employee -- ships with the product, seeded
// once, non-deletable, permissions fixed). TenantId set = a Custom Role an org's admin defined
// for themselves only.
[Audited]
public class Role : BaseEntity
{
    public Guid? TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsSystemRole { get; set; }
    // Label for the role editor's product filter. Null = spans products (Owner/Admin/Member/
    // Employee/Client, and custom roles created before products existed). Name stays unique per
    // company regardless of product because membership rows and JWT role claims reference it by name.
    public string? Product { get; set; }
    public virtual ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}

// Not [Audited]: a row holds only ids, so capturing it recorded "Delete"/"Create" with nothing
// readable. RoleService.SetPermissionsAsync records one event per save on the Role instead,
// naming each permission granted or removed.
//
// [DisableSoftDelete]: revoking a grant deletes the row. A role's permissions are a plain set --
// there's nothing to recover, and a lingering soft-deleted row only invites confusion about what a
// role actually holds.
[DisableSoftDelete]
public class RolePermission : BaseEntity
{
    public Guid RoleId { get; set; }
    public virtual Role Role { get; set; } = null!;
    public Guid PermissionId { get; set; }
    public virtual Permission Permission { get; set; } = null!;
}
