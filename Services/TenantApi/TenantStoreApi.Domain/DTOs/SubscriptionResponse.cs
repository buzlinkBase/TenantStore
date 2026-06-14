namespace TenantStoreApi.Domain.DTOs;

public class SubscriptionResponse
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid PlanId { get; set; }
    public string? PlanName { get; set; }
    public string SubStatus { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int DaysRemaining { get; set; }
    public bool IsActive { get; set; }
}
