using System.ComponentModel.DataAnnotations;

namespace Onepunch.Common.Lib.DTO;

public record UserCreated
{
    public required string TenantName { get; set; }
    public Guid UserId { get; set; }
}

public record UserJoin
{
    public Guid UserId { get; set; }
    public Guid TenantId  { get; set; }
}
public record TenantJoin 
{
    public Guid HostTenantId  { get; set; }
    public Guid GuestTenantId  { get; set; }
} 