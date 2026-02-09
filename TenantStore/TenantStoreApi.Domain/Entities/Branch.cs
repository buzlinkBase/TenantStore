namespace TenantStoreApi.Domain.Entities;

public class Branch : BaseEntity
{
    public string Name { get; set; }=string.Empty;
    public string? Address { get; set; }
    public string? Contact { get; set; }
    public string? ManagerName  { get; set; }

}