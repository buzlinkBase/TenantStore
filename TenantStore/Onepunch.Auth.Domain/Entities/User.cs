using BuzlinkRepository;
using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnePunch.Auth.Domain.Entities;

public class User : IdentityUser<Guid>, IEntity
{
    public Guid TenantId { get; set; }
    public string? Name { get; set; }
    public string Status { get; set; } = "Pending";
    [NotMapped]
    public string EntityType { get; set; }=string.Empty;
}
public class Role : IdentityRole<Guid> { }