
namespace Onepunch.Common.Lib.DTO;
public record TenantCreatedPayload
{
    public Guid TenantId { get; set; }
    public Guid UserId  { get; set; } 
    public string CompanyName  { get; set; }
}

public record TenantUserPayload
{
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public string Email { get; set; } = string.Empty;
}
public class EmailCheckPayload
{
    public Guid TenantId { get; set; }
    public string Email { get; set; }
    public string Status { get; set; }

    //public static EmailCheckPayload Invalid = new EmailCheckPayload { Status = "Registered" };

}
public record UserEmailPayload
{
    public string Token { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    public string TenantName   { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public DateTime IssuedAt { get; set; }
    public DateTime Expiry { get; set; }
    public string ConfirmationRoute { get; set; } = string.Empty;
}
public record UserInvitationPayload
{
    public string Email { get; set; }
    public string Token { get; set; }
    public string TenantName { get; set; }
    public string TenantId { get; set; }
}
public record NoticationResponse
{
    public string Message { get; set; } = string.Empty;
}
public record EmailTokenInfo
{
    public Guid UserId  { get; set; }
    public string Token { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Name { get; set; }
}
public record UserInvitionNotificationPayload
{
    public string? TenantName { get; set; }
    public string? Name { get; set; }
    public string? AppName { get; set; }
    public string Email { get; set; }
    public string Token { get; set; }
    public string InviteLink { get; set; } = string.Empty;
    public DateTime Expiry { get; set; } = DateTime.UtcNow.AddDays(2);
}

public record ResetPasswordEmail 
{
    public string? Name { get; set; }
    public string? AppName { get; set; }
    public required string Email { get; set; }
    public required string Token { get; set; }
    public string ResetLink { get; set; } = string.Empty;
    public DateTime Expiry { get; set; } = DateTime.UtcNow.AddDays(1);
}

