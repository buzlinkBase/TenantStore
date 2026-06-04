using BuzlinkRepository;
using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnePunch.Auth.Domain.Entities;

public class User : IdentityUser<Guid>, IEntity
{
    public string? FullName { get; set; }
    public Guid? DefaultTenantId { get; set; }
    public string? DefaultTenantName { get; set; }
    public string? DefaultTenantRole { get; set; }
    public string Status { get; set; } = "Active";
    [NotMapped]
    public string EntityType { get; set; } = string.Empty;
}
public class Role : IdentityRole<Guid>, IEntity { }