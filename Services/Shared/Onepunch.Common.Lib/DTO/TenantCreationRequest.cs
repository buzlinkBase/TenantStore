
namespace Onepunch.Common.Lib.DTO;

public record TenantCreationRequest
{
    public required string TenantName { get; set; }
    public Guid TenantId  { get; set; }
    public Guid UserId { get; set; }
}

public class PlanRequest 
{
    public Guid TenantId { get; set; }
    public Guid UserId  { get; set; }
    public Guid PlanId { get; set; }
    public DateTime? ValidUntil  { get; set; }
}

public record UserJoin
{
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public string Role { get; set; } = "Member";
}
public record TenantJoin 
{
    public Guid HostTenantId  { get; set; }
    public Guid GuestTenantId  { get; set; }
} 

public record ServicePlanNotFound
{
    public Guid PlanId  { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
}

public record TenantPaymentReceived 
{
    public Guid PlanId  { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }

}