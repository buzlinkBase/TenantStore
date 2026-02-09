namespace Onepunch.Common.Lib.DTO;

public record RMQPayload<T> where T : class, new()
{
    public Guid EventId { get; set; }
    public Guid CausationId { get; set; }
    public Guid CorrelationId { get; set; }
    public string EventType { get; set; }
    public DateTime TimeStampt => DateTime.UtcNow;
    public T Data { get; set; }
}
public record TenantPayload
{
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public string Email { get; set; } = string.Empty;
}

public record TenantCreatedPayload
{
    public Guid TenantId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
public record UserInvitationNotificationInfo 
{
    public string Token { get; set; } = string.Empty;
    public string Email  { get; set; } = string.Empty;
    public string FullName     { get; set; } = string.Empty;
    public string TenantName   { get; set; } = string.Empty;
    public string AppName   { get; set; } = string.Empty; 
    public string Purpose { get; set; } = string.Empty;
    public DateTime IssuedAt { get; set; }
    public DateTime Expiry { get; set; }
    public string ConfirmationRoute { get; set; } = string.Empty;
}

public record UserCreatedPayload : TenantPayload { }
public record TenantForConfirmation : TenantPayload { }
public record InvitedUserForConfirmation : TenantPayload { }
public record TenantActivatdPayload : TenantPayload { }
public record UserActivatedPayload : TenantPayload { }
public record NotifTenantForConfirmation : TenantPayload
{
    public string Token { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public DateTime IssuedAt { get; set; }
    public DateTime Expiry { get; set; }
    public string ConfirmationRoute { get; set; } = string.Empty;
}
public record UserInvitationPayload
{
    public string Email { get; set; }
    public string Token { get; set; }
    public string TenantName   { get; set; }
    public string TenantId  { get; set; }
}
public record NoticationResponse
{
    public string Message { get; set; } = string.Empty;
}

public record EmailTokenInfo 
{
    public Guid TenantId { get; set; }
    public string Token { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Name { get; set; } 
}