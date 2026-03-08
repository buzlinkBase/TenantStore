using BuzlinkRepository;
using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnePunch.Auth.Domain.Entities;

public class User : IdentityUser<Guid>, IEntity, IEntityTenant
{
    public string? Name { get; set; }
    public string Status { get; set; } = "Pending";
    [NotMapped]
    public string EntityType { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
}
public class Role : IdentityRole<Guid> { }