using BuzlinkRepository;
using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnePunch.Auth.Domain.Entities;

public class User : IdentityUser<Guid>, IEntity
{
    public string? FullName { get; set; }
    public Guid? DefaultTenantId { get; set; }
    public string? DefaultTenantName { get; set; }

    /// <summary>
    /// Denormalized cache of the user's role(s) in DefaultTenantId — Tenant Service is the
    /// source of truth (UserMembership.Roles); this is only for cheap reads when minting a
    /// token without an extra gRPC round-trip. Stored as a delimited string via EF conversion
    /// (see AuthContext.OnModelCreating) rather than a separate table, since it's just a cache.
    /// </summary>
    public List<string> DefaultTenantRoles { get; set; } = new();
    public string Status { get; set; } = "Active";
    [NotMapped]
    public string EntityType { get; set; } = string.Empty;
}
public class Role : IdentityRole<Guid>, IEntity { }