
namespace Onepunch.Common.Lib.DTO;

public record TenantCreationRequested
{
    public required string TenantName { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string Email { get; set; }
    public string FullName { get; set; }
}

public record UserJoin
{
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public string FullName { get; set; }
    public List<string> Roles { get; set; } = new() { "Member" };
    /// <summary>
    /// The invited email address, used to match this acceptance against a placeholder
    /// UserMembership row (Status="Invited") created when the invite was sent (see
    /// UserInvited/UserInvitedWorker), so Tenant Service can activate it in place.
    /// </summary>
    public string Email { get; set; } = string.Empty;
}
public record TenantJoin
{
    public Guid HostTenantId { get; set; }
    public Guid GuestTenantId { get; set; }
}

public record UserOnboarded
{
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public Guid? EmployeeId { get; set; }
    public string Email { get; set; } = string.Empty;
    /// <summary>
    /// Set when the invitation was for a client login: hrms-api links the new account to that
    /// client (ClientUserAccount), the way EmployeeId links an employee.
    /// </summary>
    public Guid? ClientId { get; set; }
}

public record InvitationAccepted
{
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public string Email { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new() { "Member" };
}