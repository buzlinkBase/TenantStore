namespace OnePunch.Auth.Api.RequestModels;
public class PlanRequestDto
{
    public Guid PlanId { get; set; }
    public DateTime? ValidUntil { get; set; }
}
