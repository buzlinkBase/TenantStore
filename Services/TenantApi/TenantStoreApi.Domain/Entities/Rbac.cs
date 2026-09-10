namespace TenantStoreApi.Domain.Entities;

// Global feature/action catalog -- TenantContext has no auto-tenant-scoping (every service
// filters by TenantId by hand), so this is a genuinely single, shared table across every tenant,
// not re-seeded per tenant. One row per (Feature, Action) pair, e.g. Feature="Payroll Runs",
// Action="Approve", Code="Payroll Runs:Approve".
public class Permission : BaseEntity
{
    public string Module { get; set; } = string.Empty;
    public string Feature { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

// TenantId null = System Role (Owner/Admin/Member/Employee -- ships with the product, seeded
// once, non-deletable, permissions fixed). TenantId set = a Custom Role an org's admin defined
// for themselves only.
public class Role : BaseEntity
{
    public Guid? TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsSystemRole { get; set; }
    public virtual ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}

public class RolePermission : BaseEntity
{
    public Guid RoleId { get; set; }
    public virtual Role Role { get; set; } = null!;
    public Guid PermissionId { get; set; }
    public virtual Permission Permission { get; set; } = null!;
}
