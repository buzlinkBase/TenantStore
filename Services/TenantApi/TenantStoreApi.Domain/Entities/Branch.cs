using BuzlinkRepository;
namespace TenantStoreApi.Domain.Entities;
public class Branch : BaseEntity , IEntityTenant
{
    public string Code { get; set; } 
    public string Name { get; set; }=string.Empty;
    public string? ShortName { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? Contact { get; set; }
    public string? ManagerName  { get; set; }
    public string Email { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
}
