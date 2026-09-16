using Microsoft.EntityFrameworkCore;
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
    // Commits via the UnitOfWork's CommitChangesAsync (not a raw Context.SaveChangesAsync) -- the
    // UnitOfWork opens a real transaction the moment it's constructed (see
    // BuzlinkRepository.UnitOfWork), so a plain SaveChangesAsync writes inside that transaction
    // without ever committing it; the write gets silently rolled back once the scoped DbContext is
    // disposed at the end of the request. CommitChangesAsync only no-ops on a *second* call against
    // the same UnitOfWork instance, which never happens here since each HTTP request gets its own
    // freshly-scoped instance.
    public async Task<Role> AddCustomRoleAsync(Guid tenantId, string name, string description, CancellationToken token)
    {
        var role = new Role { Id = Guid.CreateVersion7(), TenantId = tenantId, Name = name, Description = description, IsSystemRole = false };
        await CreateAsync(role, token);
        await CommitChangesAsync(token);
        return role;
    }

    public async Task<Role> UpdateCustomRoleAsync(Guid id, string name, string description, CancellationToken token)
    {
        var role = await GetOneAsync(id, token) ?? throw new KeyNotFoundException("Role not found.");
        if (role.IsSystemRole) throw new InvalidOperationException("System roles cannot be edited.");

        role.Name = name;
        role.Description = description;
        await ModifyAsync(role, token);
        await CommitChangesAsync(token);
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
        await CommitChangesAsync(token);
    }

    public async Task SetPermissionsAsync(Guid roleId, List<Guid> permissionIds, CancellationToken token)
    {
        var role = await GetOneAsync(roleId, token) ?? throw new KeyNotFoundException("Role not found.");
        if (role.IsSystemRole) throw new InvalidOperationException("System role permissions are fixed and cannot be changed.");

        var distinctIds = permissionIds.Distinct().ToHashSet();

        // Reconcile against every row for this role -- soft-deleted ones included. Plain
        // RemoveRange()+re-Add would route the removal through the shared SoftDeleteInterceptor,
        // which turns a Deleted entity into an UPDATE (sets DeletedAt) rather than physically
        // removing the row. Inserting a brand-new row for a permission that already has a
        // soft-deleted row on this role then collides on IX_RolePermissions_RoleId_PermissionId
        // the moment it's re-granted, so a previously-revoked permission being re-checked must
        // reactivate its existing row instead of getting a new one.
        var existing = await Context.RolePermissions.IgnoreQueryFilters()
            .Where(x => x.RoleId == roleId)
            .ToListAsync(token);

        foreach (var row in existing)
        {
            if (distinctIds.Contains(row.PermissionId))
            {
                if (row.DeletedAt != null)
                {
                    row.DeletedAt = null;
                    row.Status = "Active";
                }
            }
            else if (row.DeletedAt == null)
            {
                Context.RolePermissions.Remove(row);
            }
        }

        var alreadyPresentIds = existing.Select(x => x.PermissionId).ToHashSet();
        var toAdd = distinctIds
            .Where(id => !alreadyPresentIds.Contains(id))
            .Select(permissionId => new RolePermission { Id = Guid.CreateVersion7(), RoleId = roleId, PermissionId = permissionId })
            .ToList();
        if (toAdd.Count > 0)
        {
            await Context.RolePermissions.AddRangeAsync(toAdd, token);
        }

        await CommitChangesAsync(token);
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

        // ManageOwnTeam: granted instead of (not alongside) Create to a Supervisor-style
        // Custom Role -- lets them schedule Work Rotation only for their own direct reports
        // (Employee.ManagerId in hrms-api), not the whole company. Create remains "assign for
        // anyone," unscoped, same as before.
        ("Change Schedule", "Work Rotation", ["View", "Create", "Approve", "ManageOwnTeam"]),
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
    // The frontend's "My Portal" menu is gated on this permission (see hrms-ui-onepunch's
    // navigation.const.ts) -- Employee is the role whose entire purpose is the self-service
    // portal, so it's granted by default rather than left for a tenant to assign manually.
    private static readonly string[] EmployeeGrantedCodes = ["Employee Self-Service Portal:View"];

    private TenantContext Context => _uow.Context;

    public async Task EnsureSeededAsync(CancellationToken token)
    {
        // IUnitOfWorkService opens a real transaction the moment it's constructed (see
        // BuzlinkRepository.UnitOfWork) -- Context.SaveChangesAsync below writes inside that
        // transaction but does NOT commit it. The intermediate SaveChangesAsync calls are still
        // needed (SeedSystemRolesAsync queries Permissions back out, which requires the earlier
        // insert to be flushed -- reads-your-own-writes works fine within the same open
        // transaction), but without a final _uow.CommitChangesAsync, everything above gets rolled
        // back the moment the DbContext/scope is disposed, silently, with no exception.
        if (await SeedPermissionsAsync(token))
        {
            await Context.SaveChangesAsync(token);
        }

        if (await SeedSystemRolesAsync(token))
        {
            await Context.SaveChangesAsync(token);
        }

        if (await GrantNewPermissionsToOwnerAsync(token))
        {
            await Context.SaveChangesAsync(token);
        }

        if (await GrantMissingCodesToRoleAsync(EmployeeRole, EmployeeGrantedCodes, token))
        {
            await Context.SaveChangesAsync(token);
        }

        await BackfillMembershipRolesAsync(token);

        await _uow.CommitChangesAsync("", token);
    }

    // Incremental, not "only if the table is empty" -- Catalog gets new entries over time (e.g.
    // Work Rotation's ManageOwnTeam action), and every existing tenant's Permissions table needs
    // to pick those up on the next startup, not just a brand-new tenant's first-ever seed.
    private async Task<bool> SeedPermissionsAsync(CancellationToken token)
    {
        var existingCodes = await Context.Permissions.Select(x => x.Code).ToHashSetAsync(token);

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
            .Where(p => !existingCodes.Contains(p.Code))
            .ToList();

        if (permissions.Count == 0) return false;

        await Context.Permissions.AddRangeAsync(permissions, token);
        return true;
    }

    // Mirrors SeedPermissionsAsync's incrementality: SeedSystemRolesAsync only ever runs once
    // (gated on "no system role exists yet"), so a permission added to the catalog after a
    // tenant's roles already exist would otherwise never reach Owner -- silently breaking its
    // "Full access to everything" description. Runs every startup; a no-op once Owner is
    // caught up (including right after SeedSystemRolesAsync itself just granted everything).
    private async Task<bool> GrantNewPermissionsToOwnerAsync(CancellationToken token)
    {
        var owner = await Context.Roles.FirstOrDefaultAsync(x => x.IsSystemRole && x.Name == OwnerRole, token);
        if (owner == null) return false;

        var grantedIds = await Context.RolePermissions
            .Where(x => x.RoleId == owner.Id)
            .Select(x => x.PermissionId)
            .ToHashSetAsync(token);

        var missingIds = await Context.Permissions
            .Where(p => !grantedIds.Contains(p.Id))
            .Select(p => p.Id)
            .ToListAsync(token);

        if (missingIds.Count == 0) return false;

        var newGrants = missingIds.Select(id => new RolePermission { Id = Guid.CreateVersion7(), RoleId = owner.Id, PermissionId = id });
        await Context.RolePermissions.AddRangeAsync(newGrants, token);
        return true;
    }

    // Same reasoning as GrantNewPermissionsToOwnerAsync, for Employee's own fixed grant list
    // (EmployeeGrantedCodes) instead of "everything" -- an existing tenant's Employee role must
    // pick up Employee Self-Service Portal:View even though SeedSystemRolesAsync (which grants it
    // to brand-new tenants) never runs again for them.
    private async Task<bool> GrantMissingCodesToRoleAsync(string roleName, string[] codes, CancellationToken token)
    {
        var role = await Context.Roles.FirstOrDefaultAsync(x => x.IsSystemRole && x.Name == roleName, token);
        if (role == null) return false;

        var byCode = await Context.Permissions
            .Where(p => codes.Contains(p.Code))
            .ToDictionaryAsync(p => p.Code, token);

        var grantedIds = await Context.RolePermissions
            .Where(x => x.RoleId == role.Id)
            .Select(x => x.PermissionId)
            .ToHashSetAsync(token);

        var toGrant = codes
            .Where(byCode.ContainsKey)
            .Select(code => byCode[code].Id)
            .Where(id => !grantedIds.Contains(id))
            .ToList();

        if (toGrant.Count == 0) return false;

        var newGrants = toGrant.Select(id => new RolePermission { Id = Guid.CreateVersion7(), RoleId = role.Id, PermissionId = id });
        await Context.RolePermissions.AddRangeAsync(newGrants, token);
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
        rolePermissions.AddRange(EmployeeGrantedCodes
            .Where(byCode.ContainsKey)
            .Select(code => new RolePermission { Id = Guid.CreateVersion7(), RoleId = employee.Id, PermissionId = byCode[code].Id }));
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
