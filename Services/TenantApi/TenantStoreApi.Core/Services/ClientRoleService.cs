using MassTransit;
using Microsoft.EntityFrameworkCore;
using Onepunch.Common.Lib.DTO;
using Onepunch.Common.Lib.Security;

namespace TenantStoreApi.Core.Services;

public sealed record ClientPermissionView(Guid Id, string Module, string Feature, string Action, string Code);

public sealed record ClientRoleView(
    Guid Id,
    string Name,
    string Description,
    bool IsSystemRole,
    IReadOnlyList<ClientPermissionView> Permissions);

/// <summary>
/// The roles a client's people can hold: the built-in client roles (fixed) and custom roles the
/// client defines for itself. A custom role is scoped to its client, carries only client-side
/// permissions, and can't carry one the person editing it doesn't hold. Like the client portal's own
/// role editor, but stored in the shared authorization store.
///
/// Mutations don't commit; the entry point owns the unit of work (CommitChangesAsync).
/// </summary>
public class ClientRoleService : BaseService<Role>
{
    private const string ClientPermissionModule = "Client Users";

    private readonly ClientAccessService _access;
    private readonly IPublishEndpoint _publisher;

    public ClientRoleService(IUnitOfWorkService uow, ClientAccessService access, IPublishEndpoint publisher) : base(uow)
    {
        _access = access;
        _publisher = publisher;
    }

    /// <summary>The permissions a client role may carry (the client side of the catalog).</summary>
    public async Task<List<ClientPermissionView>> ListPermissionsAsync(CancellationToken token = default) =>
        await ClientSidePermissions().OrderBy(p => p.Feature).ThenBy(p => p.Action)
            .Select(p => new ClientPermissionView(p.Id, p.Module, p.Feature, p.Action, p.Code))
            .ToListAsync(token);

    public async Task<List<ClientRoleView>> ListRolesAsync(ClientCaller caller, Guid clientEntityId, CancellationToken token = default)
    {
        var entity = await _access.GetEntityAsync(clientEntityId, token);
        await _access.AuthorizeAsync(caller, entity, token,
            ClientPermissions.UsersView, ClientPermissions.UsersAssignRoles, ClientPermissions.RolesManage);

        var roles = await Context.Roles
            .Include(r => r.RolePermissions).ThenInclude(rp => rp.Permission)
            .Where(r => r.Audience == RoleAudience.Client && (r.IsSystemRole || r.ClientEntityId == entity.Id))
            .ToListAsync(token);

        return roles
            .OrderBy(r => r.IsSystemRole ? 0 : 1).ThenBy(r => r.Name)
            .Select(r => new ClientRoleView(
                r.Id, r.Name, r.Description, r.IsSystemRole,
                r.RolePermissions
                    .Select(rp => new ClientPermissionView(rp.Permission.Id, rp.Permission.Module, rp.Permission.Feature, rp.Permission.Action, rp.Permission.Code))
                    .OrderBy(p => p.Feature).ThenBy(p => p.Action).ToList()))
            .ToList();
    }

    public async Task<Role> CreateCustomRoleAsync(
        ClientCaller caller, Guid clientEntityId, string name, string description, IReadOnlyCollection<Guid> permissionIds, CancellationToken token = default)
    {
        var entity = await _access.GetEntityAsync(clientEntityId, token);
        var scope = await _access.AuthorizeAsync(caller, entity, token, ClientPermissions.RolesManage);

        var trimmed = (name ?? string.Empty).Trim();
        await EnsureNameFreeAsync(entity, trimmed, exceptRoleId: null, token);

        var role = new Role
        {
            Id = Guid.CreateVersion7(),
            TenantId = entity.TenantId,
            ClientEntityId = entity.Id,
            Audience = RoleAudience.Client,
            Product = PermissionProducts.ClientPortal,
            Name = trimmed,
            Description = description ?? string.Empty,
            IsSystemRole = false,
        };
        await Context.Roles.AddAsync(role, token);
        await GrantAsync(role, scope, permissionIds, token);
        return role;
    }

    public async Task<Role> UpdateCustomRoleAsync(
        ClientCaller caller, Guid clientEntityId, Guid roleId, string name, string description, CancellationToken token = default)
    {
        var entity = await _access.GetEntityAsync(clientEntityId, token);
        await _access.AuthorizeAsync(caller, entity, token, ClientPermissions.RolesManage);
        var role = await LoadCustomRoleAsync(entity, roleId, token);

        var trimmed = (name ?? string.Empty).Trim();
        await EnsureNameFreeAsync(entity, trimmed, role.Id, token);
        role.Name = trimmed;
        role.Description = description ?? string.Empty;
        return role;
    }

    public async Task SetPermissionsAsync(
        ClientCaller caller, Guid clientEntityId, Guid roleId, IReadOnlyCollection<Guid> permissionIds, CancellationToken token = default)
    {
        var entity = await _access.GetEntityAsync(clientEntityId, token);
        var scope = await _access.AuthorizeAsync(caller, entity, token, ClientPermissions.RolesManage);
        var role = await LoadCustomRoleAsync(entity, roleId, token);

        await GrantAsync(role, scope, permissionIds, token);
        await NotifyHoldersAsync(role, entity, token);
    }

    public async Task DeleteCustomRoleAsync(ClientCaller caller, Guid clientEntityId, Guid roleId, CancellationToken token = default)
    {
        var entity = await _access.GetEntityAsync(clientEntityId, token);
        await _access.AuthorizeAsync(caller, entity, token, ClientPermissions.RolesManage);
        var role = await LoadCustomRoleAsync(entity, roleId, token);

        if (await Context.ClientMembershipRoles.AnyAsync(l => l.RoleId == role.Id, token))
            throw new InvalidOperationException("Cannot delete a role that is still assigned to one or more users.");

        Context.RolePermissions.RemoveRange(role.RolePermissions);
        Context.Roles.Remove(role);
    }

    private IQueryable<Permission> ClientSidePermissions() =>
        Context.Permissions.Where(p => p.Product == PermissionProducts.ClientPortal && p.Module == ClientPermissionModule);

    // Only this client's own custom roles are editable here; built-in roles are fixed, and another
    // client's roles are reported as missing.
    private async Task<Role> LoadCustomRoleAsync(ClientEntity entity, Guid roleId, CancellationToken token)
    {
        var role = await Context.Roles
            .Include(r => r.RolePermissions).ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(r => r.Id == roleId && r.Audience == RoleAudience.Client
                && (r.IsSystemRole || (r.TenantId == entity.TenantId && r.ClientEntityId == entity.Id)), token)
            ?? throw new KeyNotFoundException("Role not found.");
        if (role.IsSystemRole) throw new InvalidOperationException("Built-in roles cannot be changed.");
        return role;
    }

    private async Task EnsureNameFreeAsync(ClientEntity entity, string name, Guid? exceptRoleId, CancellationToken token)
    {
        if (name.Length == 0) throw new ArgumentException("A role name is required.");

        // Names are unique within what this client can pick from: the built-in roles plus its own.
        var taken = await Context.Roles.AnyAsync(r =>
            r.Audience == RoleAudience.Client
            && r.Id != exceptRoleId
            && r.Name == name
            && (r.IsSystemRole || r.ClientEntityId == entity.Id), token);
        if (taken) throw new InvalidOperationException($"A role named '{name}' already exists.");
    }

    // Reconciles the role's grants to exactly permissionIds, limited to client-side permissions the
    // editor holds themselves (agency staff hold them all).
    private async Task GrantAsync(Role role, ClientCallerScope scope, IReadOnlyCollection<Guid> permissionIds, CancellationToken token)
    {
        var wanted = permissionIds.Distinct().ToList();
        var permissions = await ClientSidePermissions().Where(p => wanted.Contains(p.Id)).ToListAsync(token);
        if (permissions.Count != wanted.Count) throw new ArgumentException("Invalid permission.");

        // Only changes are checked, so an editor can leave in place a grant they don't hold.
        var current = role.RolePermissions.Select(rp => rp.PermissionId).ToHashSet();
        var denied = permissions.FirstOrDefault(p => !current.Contains(p.Id) && !scope.Holds(p.Code));
        if (denied != null) throw new InvalidOperationException(RoleGrantPolicy.DeniedMessage(role.Name));

        var wantedSet = wanted.ToHashSet();
        var removed = role.RolePermissions.Where(rp => !wantedSet.Contains(rp.PermissionId)).ToList();
        Context.RolePermissions.RemoveRange(removed);
        foreach (var rp in removed) role.RolePermissions.Remove(rp);

        foreach (var permission in permissions.Where(p => !current.Contains(p.Id)))
        {
            var grant = new RolePermission { Id = Guid.CreateVersion7(), RoleId = role.Id, PermissionId = permission.Id };
            await Context.RolePermissions.AddAsync(grant, token);
        }
    }

    // Everyone holding the role sees the change immediately, rather than when the portal's cached
    // resolution expires.
    private async Task NotifyHoldersAsync(Role role, ClientEntity entity, CancellationToken token)
    {
        var userIds = await Context.ClientMembershipRoles.AsNoTracking()
            .Where(l => l.RoleId == role.Id && l.ClientEntityId == entity.Id && l.UserMembership.UserId != Guid.Empty)
            .Select(l => l.UserMembership.UserId)
            .Distinct()
            .ToListAsync(token);

        foreach (var userId in userIds)
        {
            await _publisher.Publish(new ClientMembershipChanged
            {
                UserId = userId,
                TenantId = entity.TenantId,
                ClientEntityId = entity.Id,
                ChangeType = "RolesChanged",
            }, token);
        }
    }
}
