using BuzlinkRepository;

namespace TenantStoreApi.Domain.Entities;

public class UserMembership : BaseEntity, IEntityTenant
{
    public Guid TenantId { get; set; }
    public string? TenantName { get; set; }
    public Guid UserId { get; set; }

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
}

public static class UserMembershipExtensions
{
    public static bool HasRole(this UserMembership membership, string role) =>
        membership.Roles.Any(r => r.Role == role);

    public static bool HasAnyRole(this UserMembership membership, params string[] roles) =>
        membership.Roles.Any(r => roles.Contains(r.Role));

    public static List<string> RoleNames(this UserMembership membership) =>
        membership.Roles.Select(r => r.Role).ToList();
}
