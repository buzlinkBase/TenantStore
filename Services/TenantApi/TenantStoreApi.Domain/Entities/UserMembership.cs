using BuzlinkRepository;

namespace TenantStoreApi.Domain.Entities;

public class UserMembership : BaseEntity, IEntityTenant
{
    public Guid TenantId { get; set; }
    public string? TenantName { get; set; }
    public string? FullName { get; set; }
    public Guid UserId { get; set; }
    public bool IsHidden { get; set; } = false;//account owner must be hidden from display 
    /// <summary>
    /// Set only for placeholder rows created from an invite (Status="Invited") before the
    /// invitee has an account/UserId yet. Cleared once UserJoinWorker activates the row.
    /// </summary>
    public string? InvitedEmail { get; set; }

    /// <summary>A user can hold multiple roles at once within the same tenant membership.</summary>
    public virtual ICollection<MembershipRole> Roles { get; set; } = new List<MembershipRole>();
}

/// <summary>One role assignment within a UserMembership. Many-to-one: a membership can have
/// several of these (e.g. both "Admin" and a custom "Billing" role simultaneously).</summary>
public class MembershipRole : BaseEntity
{
    public Guid UserMembershipId { get; set; }
    public virtual UserMembership UserMembership { get; set; } = null!;
    public string Role { get; set; } = string.Empty;

    // Migration A (additive): nullable alongside the legacy Role string column above, backfilled
    // by PermissionCatalogSeederService, made required in a later migration once the backfill is
    // confirmed -- see the RBAC implementation plan. New code should read/write RoleId; Role
    // (string) stays only until Migration B drops it.
    public Guid? RoleId { get; set; }
    public virtual Role? RoleRef { get; set; }
}

public static class UserMembershipExtensions
{
    public static bool HasRole(this UserMembership membership, string role) =>
        membership.Roles.Any(r => r.Role == role);

    public static bool HasAnyRole(this UserMembership membership, params string[] roles) =>
        membership.Roles.Any(r => roles.Contains(r.Role));

    public static List<string> RoleNames(this UserMembership membership) =>
        membership.Roles.Select(r => r.Role).ToList();

    // The real, permission-based gate -- replaces string-based HasAnyRole(Owner, Admin) checks.
    // Requires .Include(x => x.Roles).ThenInclude(x => x.RoleRef).ThenInclude(x => x.RolePermissions)
    // .ThenInclude(x => x.Permission) to have been loaded; returns false (deny) if RoleRef wasn't
    // loaded/backfilled yet rather than throwing, so a not-yet-backfilled row fails closed.
    public static bool HasPermission(this UserMembership membership, string code) =>
        membership.Roles.Any(r => r.RoleRef?.RolePermissions.Any(rp => rp.Permission.Code == code) == true);

    public static List<string> EffectivePermissionCodes(this UserMembership membership) =>
        membership.Roles
            .Where(r => r.RoleRef != null)
            .SelectMany(r => r.RoleRef!.RolePermissions.Select(rp => rp.Permission.Code))
            .Distinct()
            .ToList();
}
