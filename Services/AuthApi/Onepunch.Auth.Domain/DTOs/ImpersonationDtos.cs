namespace Onepunch.Auth.Domain.DTOs;

public class StartImpersonationRequest
{
    public Guid TargetUserId { get; set; }
    public Guid TenantId { get; set; }
    /// <summary>Why the operator needs to be inside this account; recorded with the session.</summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>What the console gets back: a code the HRIS app redeems once, within 60 seconds.</summary>
public class StartImpersonationResult
{
    public Guid SessionId { get; set; }
    public string ExchangeCode { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
}

public class ExchangeImpersonationRequest
{
    public string Code { get; set; } = string.Empty;
}

/// <summary>The impersonated session, as the HRIS app needs it to sign in and to show the banner.</summary>
public class ImpersonationSessionResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public DateTime Expiry { get; set; }
    public Guid SessionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = [];
    public List<string> Permissions { get; set; } = [];
    public List<UsersTenant> Tenants { get; set; } = [];
    public string OperatorEmail { get; set; } = string.Empty;
}
