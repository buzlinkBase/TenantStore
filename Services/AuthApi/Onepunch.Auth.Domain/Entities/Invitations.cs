using OnePunch.Auth.Domain.Entities;

namespace Onepunch.Auth.Domain.Entities;

public class Invitation : BaseEntity
{
    public Guid UserId { get; set; }//sending the invites
    public Guid TenantId { get; set; }
    public string TenantName { get; set; }
    public string Email { get; set; }//invited
    public string Token { get; set; }
    public DateTime Expiry { get; set; }
    public InvitationStatus Status { get; set; }
    public List<string> Roles { get; set; } = new() { "Member" };
    public Guid? EmployeeId { get; set; }
}

public enum InvitationStatus
{
    Pending,
    Accepted,
}