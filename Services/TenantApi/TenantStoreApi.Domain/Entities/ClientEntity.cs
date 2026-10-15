using BuzlinkRepository;
using Onepunch.AuditEngine;

namespace TenantStoreApi.Domain.Entities;

/// <summary>
/// A client of a tenant, as the identity layer knows it. The client portal owns the real client
/// record (sites, modules, billing); this row is the mirror other services need to scope a
/// membership's roles to one client. Id is the portal's Client.Id, so both sides agree on it.
/// </summary>
[Audited]
public class ClientEntity : BaseEntity, IEntityTenant
{
    public Guid TenantId { get; set; }

    /// <summary>The HRIS client id, when the client came from HRIS; null for portal-native clients.</summary>
    public string? ExternalId { get; set; }

    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// One role a ClientUser membership holds within one client. A person can belong to several
/// clients with different roles, hence the client in the key.
/// </summary>
[DisableSoftDelete]
[Audited]
public class ClientMembershipRole : BaseEntity
{
    public Guid UserMembershipId { get; set; }
    public virtual UserMembership UserMembership { get; set; } = null!;

    public Guid ClientEntityId { get; set; }
    public virtual ClientEntity ClientEntity { get; set; } = null!;

    public Guid RoleId { get; set; }
    public virtual Role Role { get; set; } = null!;
}
