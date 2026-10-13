using MassTransit;
using Microsoft.EntityFrameworkCore;
using Onepunch.AuditEngine;
using Onepunch.Common.Lib.Security;
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
    private readonly IPublishEndpoint? _publisher;
    private readonly IAuditWriter? _audit;

    // publisher and audit are optional so the many direct constructions in tests stay valid; the
    // container always supplies them.
    public RoleService(IUnitOfWorkService uow, IPublishEndpoint? publisher = null, IAuditWriter? audit = null) : base(uow)
    {
        _publisher = publisher;
        _audit = audit;
    }

    public const string PermissionGranted = "Granted";
    public const string PermissionNotGranted = "Not granted";

    // System Roles (Owner/Admin/Member/Employee/Client) + every Custom Role this tenant defined.
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

        var previousName = role.Name;
        role.Name = name;
        role.Description = description;
        await ModifyAsync(role, token);
        if (previousName != name)
        {
            // Members' role rows carry the name too (it's what their sign-in claims show).
            var assignments = await Context.MembershipRoles
                .Where(x => x.RoleId == role.Id ||
                            (x.RoleId == null && x.Role == previousName && x.UserMembership.TenantId == role.TenantId))
                .ToListAsync(token);
            assignments.ForEach(x => x.Role = name);
            await NotifyMembersAsync(role, previousName, token);
        }
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

        var before = existing.Where(x => x.DeletedAt == null).Select(x => x.PermissionId).ToHashSet();
        await RecordPermissionChangesAsync(role, before, distinctIds, token);

        await NotifyMembersAsync(role, role.Name, token);
        await CommitChangesAsync(token);
    }

    // One audit event per save, on the role, naming each permission granted or removed -- so the
    // trail reads "Payroll Run:Approve: Not granted -> Granted" rather than a bare row id. Written
    // before the commit so it saves (and is forwarded to hrms-api) with the change itself.
    private async Task RecordPermissionChangesAsync(Role role, IReadOnlySet<Guid> before, IReadOnlySet<Guid> after, CancellationToken token)
    {
        if (_audit == null) return;
        var granted = after.Except(before).ToList();
        var removed = before.Except(after).ToList();
        if (granted.Count == 0 && removed.Count == 0) return;

        var changedIds = granted.Concat(removed).ToList();
        var codes = await Context.Permissions.AsNoTracking()
            .Where(p => changedIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Code, token);
        string CodeOf(Guid id) => codes.GetValueOrDefault(id) ?? id.ToString();

        var changes = granted.Select(id => new AuditChange(CodeOf(id), PermissionNotGranted, PermissionGranted))
            .Concat(removed.Select(id => new AuditChange(CodeOf(id), PermissionGranted, PermissionNotGranted)))
            .OrderBy(c => c.Property, StringComparer.Ordinal)
            .ToList();

        await _audit.WriteAsync(new AuditEvent
        {
            EventType = AuditEventTypes.Update,
            EntityType = nameof(Role),
            EntityId = role.Id.ToString(),
            OrganizationId = role.TenantId?.ToString(),
            Description = $"Changed permissions of role '{role.Name}': {granted.Count} granted, {removed.Count} removed",
            Changes = changes,
        }, token);
    }

    // Same signal as changing one member's roles (UserMembershipService.PublishRoleChangedAsync):
    // AuthApi's MembershipChangedWorker drops each member's cached membership and pushes a live
    // "roles changed" to their open sessions, which silently refresh their token -- so edited
    // permissions apply right away instead of when the token expires. Sent through the EF bus
    // outbox, so it goes out only if the commit below succeeds.
    //
    // storedName: the role's name as the membership rows still hold it (the old one on a rename);
    // rows saved before RoleId existed are matched by it, within this company only.
    private async Task NotifyMembersAsync(Role role, string storedName, CancellationToken token)
    {
        if (_publisher == null || role.TenantId is not { } tenantId) return;

        var members = await Context.Memberships.AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.Status == "Active" && m.UserId != Guid.Empty &&
                        m.Roles.Any(r => r.RoleId == role.Id || (r.RoleId == null && r.Role == storedName)))
            .Select(m => new { m.UserId, Roles = m.Roles.Select(r => new { r.RoleId, r.Role }).ToList() })
            .ToListAsync(token);

        foreach (var member in members)
        {
            await _publisher.Publish(new MembershipChanged
            {
                UserId = member.UserId,
                TenantId = tenantId,
                ChangeType = "RoleChanged",
                // The roles as they'll read after this save (a rename shows the new name).
                NewRoles = member.Roles
                    .Select(r => r.RoleId == role.Id || r.Role == storedName ? role.Name : r.Role)
                    .ToList(),
            }, token);
        }
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

    /// <summary>The assignable roles (never Owner) <paramref name="caller"/> may grant, per RoleGrantPolicy.</summary>
    public async Task<List<Role>> GrantableRolesAsync(UserMembership caller, Guid tenantId, CancellationToken token)
    {
        var callerIsOwner = caller.HasRole(TenantRoles.Owner);
        var callerCodes = caller.EffectivePermissionCodes().ToHashSet();
        var roles = await FindAllForTenantAsync(tenantId, token);
        return roles
            .Where(r => !(r.IsSystemRole && r.Name == TenantRoles.Owner))
            .Where(r => RoleGrantPolicy.CanGrant(callerIsOwner, callerCodes, r.Name, r.IsSystemRole,
                r.RolePermissions.Select(rp => rp.Permission.Code)))
            .ToList();
    }

    /// <summary>
    /// Throws when any of <paramref name="roles"/> is one <paramref name="caller"/> may not grant.
    /// InvalidOperationException, not UnauthorizedAccessException: MembersController returns the
    /// former's message as a 400 so the caller learns why, the latter as a bare 403.
    /// </summary>
    public async Task EnsureCanGrantAsync(UserMembership caller, IEnumerable<string> roles, Guid tenantId, CancellationToken token)
    {
        var grantable = (await GrantableRolesAsync(caller, tenantId, token))
            .Select(r => r.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var denied = roles.FirstOrDefault(r => !grantable.Contains(r));
        if (denied != null)
            throw new InvalidOperationException(RoleGrantPolicy.DeniedMessage(denied));
    }

    public Task<Role?> FindByNameAsync(string name, Guid tenantId, CancellationToken token)
    {
        return GetQueryable(x => x.Name == name && (x.TenantId == null || x.TenantId == tenantId))
            .FirstOrDefaultAsync(token);
    }
}

// Seeds the global Permission catalog and the System Roles (Owner/Admin/Member/Employee/Client),
// then backfills existing MembershipRole rows (Migration A's nullable RoleId) to point at them.
// Idempotent -- safe to run on every TenantApi startup.
//
// The catalog itself comes from the registered IPermissionCatalogProviders, one per product.
public class PermissionCatalogSeederService
{
    private const string OwnerRole = TenantRoles.Owner;

    // Cross-product roles (Product null). Each product's own roles come from its provider.
    private static readonly SystemRoleDefinition[] CoreSystemRoles =
    [
        new(OwnerRole, "Full access to everything."),
        new(TenantRoles.Admin, "Manages tenant members and roles."),
        new(TenantRoles.Member, "Standard member."),
        new(TenantRoles.Employee, "Employee-linked member."),
        // Reserved for the upcoming client-facing portal (the client counterpart of the
        // Employee Self-Service Portal). Deliberately granted nothing yet -- it gets its own
        // portal permission(s) once that feature exists, the way Employee has
        // EmployeeGrantedCodes.
        new(TenantRoles.Client, "Client-linked member."),
    ];

    private readonly IUnitOfWorkService _uow;
    private readonly IReadOnlyList<IPermissionCatalogProvider> _providers;

    // providers is optional so the many direct constructions in tests stay valid (they get HRMS,
    // the only product that existed); the container always supplies the registered set.
    public PermissionCatalogSeederService(IUnitOfWorkService uow, IEnumerable<IPermissionCatalogProvider>? providers = null)
    {
        _uow = uow;
        _providers = providers?.ToList() ?? [new HrmsPermissionCatalog()];

        var duplicate = _providers.GroupBy(p => p.Product).FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null) throw new InvalidOperationException($"Permission catalog product '{duplicate.Key}' is registered more than once.");
    }

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
        if (await BackfillPermissionProductsAsync(token))
        {
            await Context.SaveChangesAsync(token);
        }

        if (await SeedPermissionsAsync(token))
        {
            await Context.SaveChangesAsync(token);
        }

        if (await SeedMissingSystemRolesAsync(token))
        {
            await Context.SaveChangesAsync(token);
        }

        if (await GrantNewPermissionsToOwnerAsync(token))
        {
            await Context.SaveChangesAsync(token);
        }

        // A role can be granted codes by several products; merge them so it's one pass per role.
        var grantsByRole = _providers
            .SelectMany(p => p.RoleGrants)
            .GroupBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.SelectMany(x => x.Value).Distinct().ToArray());
        foreach (var (roleName, codes) in grantsByRole)
        {
            if (await GrantMissingCodesToRoleAsync(roleName, codes, token))
            {
                await Context.SaveChangesAsync(token);
            }
        }

        await BackfillMembershipRolesAsync(token);

        await _uow.CommitChangesAsync("", token);
    }

    // Rows that existed before Permission.Product was added come out of the migration with a blank
    // product (the column's database default); give each the product its code says it belongs to.
    private async Task<bool> BackfillPermissionProductsAsync(CancellationToken token)
    {
        var blank = await Context.Permissions.Where(x => x.Product == "").ToListAsync(token);
        foreach (var permission in blank)
        {
            permission.Product = PermissionProducts.ProductOf(permission.Code);
        }

        return blank.Count > 0;
    }

    // Incremental, not "only if the table is empty" -- Catalog gets new entries over time (e.g.
    // Work Rotation's ManageOwnTeam action), and every existing tenant's Permissions table needs
    // to pick those up on the next startup, not just a brand-new tenant's first-ever seed.
    private async Task<bool> SeedPermissionsAsync(CancellationToken token)
    {
        var existingCodes = await Context.Permissions.Select(x => x.Code).ToHashSetAsync(token);

        var permissions = _providers
            .SelectMany(provider => provider.Features.SelectMany(row => row.Actions.Select(action => new Permission
            {
                Id = Guid.CreateVersion7(),
                Product = provider.Product,
                Module = row.Module,
                Feature = row.Feature,
                Action = action,
                Code = PermissionProducts.BuildCode(provider.Product, row.Feature, action),
                Description = $"{action} access to {row.Feature}",
            })))
            .Where(p => !existingCodes.Contains(p.Code))
            .ToList();

        if (permissions.Count == 0) return false;

        await Context.Permissions.AddRangeAsync(permissions, token);
        return true;
    }

    // The only thing that grants Owner its permissions -- both on a brand-new seed (right after
    // SeedMissingSystemRolesAsync creates the role) and whenever the catalog later gains a code,
    // which keeps its "Full access to everything" description true. Runs every startup; a no-op
    // once Owner is caught up.
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

    // Same reasoning as GrantNewPermissionsToOwnerAsync, for a role's own fixed grant list
    // (AdminGrantedCodes, EmployeeGrantedCodes) instead of "everything" -- grants a brand-new
    // role its codes and catches an existing one up whenever its list gains a code.
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

    // Incremental like SeedPermissionsAsync: creates whichever System Roles don't exist yet, so a
    // role added here later (e.g. Client) reaches existing tenants on their next startup too,
    // not just brand-new ones. Only creates the rows -- permissions are granted by the steps
    // that run right after it on every startup (GrantNewPermissionsToOwnerAsync for Owner,
    // GrantMissingCodesToRoleAsync for Admin/Employee), so a fresh seed and a catch-up take the
    // exact same path.
    private async Task<bool> SeedMissingSystemRolesAsync(CancellationToken token)
    {
        var existingNames = await Context.Roles
            .Where(x => x.IsSystemRole)
            .Select(x => x.Name)
            .ToHashSetAsync(token);

        var missing = CoreSystemRoles.Concat(_providers.SelectMany(p => p.SystemRoles))
            .Where(role => !existingNames.Contains(role.Name))
            .Select(role => new Role
            {
                Id = Guid.CreateVersion7(),
                Name = role.Name,
                Description = role.Description,
                IsSystemRole = true,
                Product = role.Product,
            })
            .ToList();

        if (missing.Count == 0) return false;

        await Context.Roles.AddRangeAsync(missing, token);
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
