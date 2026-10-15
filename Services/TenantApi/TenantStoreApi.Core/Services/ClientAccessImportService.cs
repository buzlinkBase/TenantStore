using Microsoft.EntityFrameworkCore;
using Onepunch.Common.Lib.Security;

namespace TenantStoreApi.Core.Services;

/// <summary>A custom role a client had before its people moved here: its name and the permission codes it carried.</summary>
public sealed record ImportRole(string Name, string? Description, IReadOnlyList<string> PermissionCodes);

/// <summary>A person who already had access to the client, to be given the roles named (built-in ones, or the client's own listed alongside).</summary>
public sealed record ImportMember(Guid AccountId, string? Email, string? FullName, bool Active, IReadOnlyList<string> RoleNames);

public sealed record ClientAccessImport(IReadOnlyList<ImportRole> CustomRoles, IReadOnlyList<ImportMember> Members);

/// <summary>What an import did. Anything it could not do is in <see cref="Warnings"/>; nothing is silently dropped.</summary>
public sealed record ImportReport(int RolesCreated, int RolesAlreadyThere, int MembersGranted, IReadOnlyList<string> Warnings);

/// <summary>
/// Brings a client's existing people and custom roles over from the client portal's own tables, which held them before Tenant Service did.
/// Safe to run again: roles that exist are left alone and a person already holding a role keeps it, so a second run changes nothing that
/// the first one did. It only ever adds; it never removes a role or a person.
///
/// Doesn't commit; the caller owns the unit of work (CommitChangesAsync).
/// </summary>
public class ClientAccessImportService : BaseService<UserMembership>
{
    private readonly ClientAccessService _access;
    private readonly ClientRoleService _roles;
    private readonly ClientMembershipService _members;

    public ClientAccessImportService(IUnitOfWorkService uow, ClientAccessService access, ClientRoleService roles, ClientMembershipService members) : base(uow)
    {
        _access = access;
        _roles = roles;
        _members = members;
    }

    public async Task<ImportReport> ImportAsync(Guid clientEntityId, ClientAccessImport import, CancellationToken token = default)
    {
        var entity = await _access.GetEntityAsync(clientEntityId, token);
        var warnings = new List<string>();

        // Importing is an agency-level act: it isn't done on behalf of a client user, so no one's own permissions limit what is brought over.
        var caller = ClientCaller.Agency(Guid.Empty);
        var created = 0;
        var existing = 0;

        var known = await ClientRolesByNameAsync(entity, token);
        foreach (var role in import.CustomRoles)
        {
            var name = (role.Name ?? string.Empty).Trim();
            if (known.ContainsKey(name))
            {
                existing++;
                if (known[name].IsBuiltIn)
                    warnings.Add($"Role '{name}' has the name of a built-in role, so members are given the built-in role instead.");
                continue;
            }

            var codes = role.PermissionCodes.Distinct().ToList();
            var permissions = await Context.Permissions
                .Where(p => p.Product == PermissionProducts.ClientPortal && p.Module == ClientPermissionModule && codes.Contains(p.Code))
                .ToListAsync(token);
            foreach (var unknown in codes.Except(permissions.Select(p => p.Code)))
                warnings.Add($"Role '{name}': permission '{unknown}' is not one a client role can carry, so it was left out.");

            try
            {
                var made = await _roles.CreateCustomRoleAsync(caller, clientEntityId, name, role.Description ?? string.Empty, permissions.Select(p => p.Id).ToList(), token);
                known[name] = (made.Id, false);
                created++;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                warnings.Add($"Role '{name}' was not created: {ex.Message}");
            }
        }
        // Members are given roles by looking them up, which reads what is saved.
        await Context.SaveChangesAsync(token);

        var granted = 0;
        foreach (var member in import.Members)
        {
            var label = member.Email ?? member.AccountId.ToString();
            var roleIds = new List<Guid>();
            foreach (var roleName in member.RoleNames.Select(n => n.Trim()).Distinct())
            {
                if (known.TryGetValue(roleName, out var role))
                    roleIds.Add(role.Id);
                else
                    warnings.Add($"{label}: role '{roleName}' does not exist for this client, so it was not given.");
            }
            if (roleIds.Count == 0)
            {
                warnings.Add($"{label} has no role to be given, so was not brought over.");
                continue;
            }

            var membership = await _members.GrantAccessAsync(member.AccountId, clientEntityId, member.Email, member.FullName, roleIds, token);
            await Context.SaveChangesAsync(token);
            granted++;

            if (!member.Active)
            {
                try
                {
                    await _members.SetStatusAsync(caller, clientEntityId, membership.Id, "Inactive", token);
                    await Context.SaveChangesAsync(token);
                }
                catch (InvalidOperationException ex)
                {
                    warnings.Add($"{label} was inactive but is left active: {ex.Message}");
                }
            }
        }

        return new ImportReport(created, existing, granted, warnings);
    }

    private const string ClientPermissionModule = "Client Users";

    // The roles a client can hold, by name: the built-in ones, and its own custom ones.
    private async Task<Dictionary<string, (Guid Id, bool IsBuiltIn)>> ClientRolesByNameAsync(ClientEntity entity, CancellationToken token)
    {
        var roles = await Context.Roles.AsNoTracking()
            .Where(r => r.Audience == RoleAudience.Client && (r.IsSystemRole || (r.TenantId == entity.TenantId && r.ClientEntityId == entity.Id)))
            .ToListAsync(token);
        return roles.ToDictionary(r => r.Name, r => (r.Id, r.IsSystemRole));
    }
}
