using Microsoft.EntityFrameworkCore;
using TenantStoreApi.Domain.Entities;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Core.Services;

// Read-only feature/action catalog -- system-seeded, never admin-typed free text.
public class PermissionService : BaseService<Permission>
{
    public PermissionService(IUnitOfWorkService uow) : base(uow)
    {
    }

    public Task<List<Permission>> FindAllAsync(CancellationToken token)
    {
        return GetQueryable().OrderBy(x => x.Module).ThenBy(x => x.Feature).ThenBy(x => x.Action).ToListAsync(token);
    }
}

public class RoleService : BaseService<Role>
{
    public RoleService(IUnitOfWorkService uow) : base(uow)
    {
    }

    // System Roles (Owner/Admin/Member/Employee) + every Custom Role this tenant defined.
    public Task<List<Role>> FindAllForTenantAsync(Guid tenantId, CancellationToken token)
    {
        return GetQueryable(x => x.TenantId == null || x.TenantId == tenantId)
            .Include(x => x.RolePermissions).ThenInclude(x => x.Permission)
            .OrderBy(x => x.IsSystemRole ? 0 : 1).ThenBy(x => x.Name)
            .ToListAsync(token);
    }

    public Task<Role?> FindOneWithPermissionsAsync(Guid id, CancellationToken token)
    {
        return GetQueryable(x => x.Id == id)
            .Include(x => x.RolePermissions).ThenInclude(x => x.Permission)
            .FirstOrDefaultAsync(token);
    }

    // Only Custom Roles are admin-creatable; System Roles are seeded once and fixed.
    // Commits via Context.SaveChangesAsync directly rather than the BaseService/UnitOfWork
    // CommitChangesAsync wrapper -- that wrapper only performs a real commit on the first call per
    // UnitOfWork instance (an internal one-shot guard), so a second write on the same scoped uow
    // would silently no-op.
    public async Task<Role> AddCustomRoleAsync(Guid tenantId, string name, string description, CancellationToken token)
    {
        var role = new Role { Id = Guid.CreateVersion7(), TenantId = tenantId, Name = name, Description = description, IsSystemRole = false };
        await CreateAsync(role, token);
        await Context.SaveChangesAsync(token);
        return role;
    }

    public async Task<Role> UpdateCustomRoleAsync(Guid id, string name, string description, CancellationToken token)
    {
        var role = await GetOneAsync(id, token) ?? throw new KeyNotFoundException("Role not found.");
        if (role.IsSystemRole) throw new InvalidOperationException("System roles cannot be edited.");

        role.Name = name;
        role.Description = description;
        await ModifyAsync(role, token);
        await Context.SaveChangesAsync(token);
        return role;
    }

    public async Task DeleteCustomRoleAsync(Guid id, CancellationToken token)
    {
        var role = await GetOneAsync(id, token) ?? throw new KeyNotFoundException("Role not found.");
        if (role.IsSystemRole) throw new InvalidOperationException("System roles cannot be deleted.");

        var stillAssigned = await Context.MembershipRoles.AnyAsync(x => x.RoleId == id, token);
        if (stillAssigned) throw new InvalidOperationException("Cannot delete a role that is still assigned to one or more members.");

        var rolePermissions = await Context.RolePermissions.Where(x => x.RoleId == id).ToListAsync(token);
        Context.RolePermissions.RemoveRange(rolePermissions);
        var roleEntity = await Context.Roles.FindAsync([id], token) ?? role;
        Context.Roles.Remove(roleEntity);
        await Context.SaveChangesAsync(token);
    }

    public async Task SetPermissionsAsync(Guid roleId, List<Guid> permissionIds, CancellationToken token)
    {
        var role = await GetOneAsync(roleId, token) ?? throw new KeyNotFoundException("Role not found.");
        if (role.IsSystemRole) throw new InvalidOperationException("System role permissions are fixed and cannot be changed.");

        var existing = await Context.RolePermissions.Where(x => x.RoleId == roleId).ToListAsync(token);
        Context.RolePermissions.RemoveRange(existing);

        var toAdd = permissionIds.Distinct()
            .Select(permissionId => new RolePermission { Id = Guid.CreateVersion7(), RoleId = roleId, PermissionId = permissionId })
            .ToList();
        if (toAdd.Count > 0)
        {
            await Context.RolePermissions.AddRangeAsync(toAdd, token);
        }

        await Context.SaveChangesAsync(token);
    }

    // Generalizes the old hardcoded TenantRoles.IsValid([Admin, Member]) check: any System Role
    // except Owner, or any Custom Role belonging to this tenant, is grantable through the
    // membership API. Owner is never grantable this way (ownership transfer is a separate flow).
    public async Task<bool> IsAssignableAsync(string roleName, Guid tenantId, CancellationToken token)
    {
        var role = await GetQueryable(x => x.Name == roleName && (x.TenantId == null || x.TenantId == tenantId))
            .FirstOrDefaultAsync(token);
        if (role == null) return false;
        return !(role.IsSystemRole && role.Name == "Owner");
    }

    public Task<Role?> FindByNameAsync(string name, Guid tenantId, CancellationToken token)
    {
        return GetQueryable(x => x.Name == name && (x.TenantId == null || x.TenantId == tenantId))
            .FirstOrDefaultAsync(token);
    }
}

// Seeds the global Permission catalog and the four System Roles (Owner/Admin/Member/Employee),
// then backfills existing MembershipRole rows (Migration A's nullable RoleId) to point at them.
// Idempotent -- safe to run on every TenantApi startup.
public class PermissionCatalogSeederService
{
    private readonly IUnitOfWorkService _uow;

    public PermissionCatalogSeederService(IUnitOfWorkService uow)
    {
        _uow = uow;
    }

    private static readonly (string Module, string Feature, string[] Actions)[] Catalog =
    [
        ("Setup", "Organization Setup", ["View", "Create", "Edit", "Delete"]),
        ("Setup", "Workforce Setup", ["View", "Create", "Edit", "Delete"]),
        ("Setup", "Time Shift Setup", ["View", "Create", "Edit", "Delete"]),
        ("Setup", "Deductions & Income Setup", ["View", "Create", "Edit", "Delete"]),
        ("Setup", "Leave Setup", ["View", "Create", "Edit", "Delete"]),
        ("Setup", "Statutory Tables", ["View", "Create", "Edit", "Delete"]),
        ("Setup", "Biometric Setup", ["View", "Create", "Edit", "Delete"]),

        ("Timekeeping", "Upload Attendance", ["View", "Edit", "Export"]),
        ("Timekeeping", "Attendance Manual Entry", ["View", "Edit", "Export"]),
        ("Timekeeping", "Raw Logs", ["View", "Edit", "Export"]),
        ("Timekeeping", "Unregistered Employees", ["View", "Edit", "Export"]),
        ("Timekeeping", "Incomplete Punches", ["View", "Edit", "Export"]),

        ("Change Schedule", "Work Rotation", ["View", "Create", "Approve"]),
        ("Change Schedule", "Change Rest Day", ["View", "Create", "Approve"]),
        ("Change Schedule", "Change Holiday", ["View", "Create", "Approve"]),

        ("DTR Generation", "DTR Master", ["View", "Manage"]),
        ("DTR Generation", "DTR Summary", ["View", "Manage"]),

        ("Payroll Generation", "Payroll Run", ["View", "Create", "Approve", "Export"]),
        ("Payroll Generation", "Payroll Summary", ["View", "Create", "Approve", "Export"]),
        ("Payroll Generation", "13th Month Run", ["View", "Create", "Approve", "Export"]),
        ("Payroll Generation", "Last Pay Run", ["View", "Create", "Approve", "Export"]),
        ("Payroll Generation", "Year-End Adjustment Run", ["View", "Create", "Approve", "Export"]),

        ("Applications", "Leave", ["View", "Approve", "Delete"]),
        ("Applications", "Overtime", ["View", "Approve", "Delete"]),
        ("Applications", "Official Business", ["View", "Approve", "Delete"]),
        ("Applications", "Pass Slip", ["View", "Approve", "Delete"]),
        ("Applications", "Loan/Deduction", ["View", "Approve", "Delete"]),
        ("Applications", "Other Income", ["View", "Approve", "Delete"]),
        ("Applications", "Salary Adjustment", ["View", "Approve", "Delete"]),

        ("Reports", "Government Statutory Reports", ["View", "Export"]),
        ("Reports", "Payroll Reports", ["View", "Export"]),
        ("Reports", "BIR Reports", ["View", "Export"]),

        ("Security", "Users", ["View", "Create", "Edit", "Delete", "Manage"]),
        ("Security", "Roles", ["View", "Create", "Edit", "Delete", "Manage"]),
        ("Security", "Permissions", ["View", "Create", "Edit", "Delete", "Manage"]),
        ("Security", "Audit Trail", ["View", "Create", "Edit", "Delete", "Manage"]),

        ("Employee Portal", "Employee Self-Service Portal", ["View", "Manage"]),

        // What UserMembershipService already enforces today via HasAnyRole(Owner, Admin) --
        // folded into the same catalog so it's one permission system, not two. Named "Tenant
        // Members"/"Tenant Roles" (not "Members"/"Roles") to avoid colliding with the Security
        // module's "Users"/"Roles" features above, which are a different concept (the HR
        // product's own admin screens) even though the words overlap.
        ("Tenant Administration", "Tenant Members", ["Manage"]),
        ("Tenant Administration", "Tenant Roles", ["Manage"]),
    ];

    private const string OwnerRole = "Owner";
    private const string AdminRole = "Admin";
    private const string MemberRole = "Member";
    private const string EmployeeRole = "Employee";
    private static readonly string[] AdminGrantedCodes = ["Tenant Members:Manage", "Tenant Roles:Manage"];

    private TenantContext Context => _uow.Context;

    public async Task EnsureSeededAsync(CancellationToken token)
    {
        // Committed separately (not batched with the role seed below) because
        // SeedSystemRolesAsync queries Permissions back out -- an uncommitted Add isn't visible
        // to that query yet.
        if (await SeedPermissionsAsync(token))
        {
            await Context.SaveChangesAsync(token);
        }

        if (await SeedSystemRolesAsync(token))
        {
            await Context.SaveChangesAsync(token);
        }

        await BackfillMembershipRolesAsync(token);
    }

    private async Task<bool> SeedPermissionsAsync(CancellationToken token)
    {
        if (await Context.Permissions.AnyAsync(token)) return false;

        var permissions = Catalog
            .SelectMany(row => row.Actions.Select(action => new Permission
            {
                Id = Guid.CreateVersion7(),
                Module = row.Module,
                Feature = row.Feature,
                Action = action,
                Code = $"{row.Feature}:{action}",
                Description = $"{action} access to {row.Feature}",
            }))
            .ToList();

        await Context.Permissions.AddRangeAsync(permissions, token);
        return true;
    }

    private async Task<bool> SeedSystemRolesAsync(CancellationToken token)
    {
        if (await Context.Roles.AnyAsync(x => x.IsSystemRole, token)) return false;

        var allPermissions = await Context.Permissions.ToListAsync(token);
        var byCode = allPermissions.ToDictionary(x => x.Code);

        var owner = new Role { Id = Guid.CreateVersion7(), Name = OwnerRole, Description = "Full access to everything.", IsSystemRole = true };
        var admin = new Role { Id = Guid.CreateVersion7(), Name = AdminRole, Description = "Manages tenant members and roles.", IsSystemRole = true };
        var member = new Role { Id = Guid.CreateVersion7(), Name = MemberRole, Description = "Standard member.", IsSystemRole = true };
        var employee = new Role { Id = Guid.CreateVersion7(), Name = EmployeeRole, Description = "Employee-linked member.", IsSystemRole = true };
        await Context.Roles.AddRangeAsync([owner, admin, member, employee], token);

        var rolePermissions = new List<RolePermission>();
        rolePermissions.AddRange(allPermissions.Select(p => new RolePermission { Id = Guid.CreateVersion7(), RoleId = owner.Id, PermissionId = p.Id }));
        rolePermissions.AddRange(AdminGrantedCodes
            .Where(byCode.ContainsKey)
            .Select(code => new RolePermission { Id = Guid.CreateVersion7(), RoleId = admin.Id, PermissionId = byCode[code].Id }));
        await Context.RolePermissions.AddRangeAsync(rolePermissions, token);

        return true;
    }

    // MembershipRole.RoleId is nullable (Migration A) -- this maps every still-unbackfilled row's
    // legacy Role string onto the matching System Role's Id. Safe to run every startup: only rows
    // where RoleId IS NULL are touched, and it commits its own small batch of changes.
    private async Task BackfillMembershipRolesAsync(CancellationToken token)
    {
        var unbackfilled = await Context.MembershipRoles
            .Where(x => x.RoleId == null)
            .ToListAsync(token);
        if (unbackfilled.Count == 0) return;

        var systemRoles = await Context.Roles
            .Where(x => x.IsSystemRole)
            .ToDictionaryAsync(x => x.Name, token);

        foreach (var membershipRole in unbackfilled)
        {
            if (systemRoles.TryGetValue(membershipRole.Role, out var role))
            {
                membershipRole.RoleId = role.Id;
            }
        }

        await Context.SaveChangesAsync(token);
    }
}
