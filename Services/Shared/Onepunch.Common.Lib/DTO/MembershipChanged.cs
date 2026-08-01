namespace Onepunch.Common.Lib.DTO;

/// <summary>
/// Flow H (Cache Invalidation): published by Tenant Service after every committed
/// membership/role write, so Auth Service can evict its per-user membership cache entry
/// (see MembershipCacheService) instead of relying solely on the 5-minute TTL backstop.
/// </summary>
public record MembershipChanged
{
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public string ChangeType { get; set; } = string.Empty; // RoleChanged, MemberAdded, MemberRemoved, OwnerTransferred, TenantDeleted, TenantSuspended
    /// <summary>The member's complete role set after the change (for ChangeType="RoleChanged").</summary>
    public List<string> NewRoles { get; set; } = new();
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}
