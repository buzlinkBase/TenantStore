namespace TenantStoreApi.Domain.DTOs;

public class PlanResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Days { get; set; }
}
