namespace Onepunch.Common.Lib.DTO;

/// <summary>
/// Published by Tenant Service after a committed change to what a client-portal user may do within
/// one client (roles, status, removal, or a role's permissions). The client portal drops its cached
/// resolution for (UserId, ClientEntityId) so the change applies right away instead of when the
/// cache expires. Separate from <see cref="MembershipChanged"/>, which is about HRIS sessions.
/// </summary>
public record ClientMembershipChanged
{
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public Guid ClientEntityId { get; set; }
    public string ChangeType { get; set; } = string.Empty; // RolesChanged, StatusChanged, Removed
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}
