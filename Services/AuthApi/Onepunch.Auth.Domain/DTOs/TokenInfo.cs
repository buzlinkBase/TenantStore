using System.ComponentModel.DataAnnotations;

namespace Onepunch.Auth.Domain.DTOs;

public class TokenInfo
{
    public Guid JTI { get; set; }
    public Guid UserId { get; set; }
    public string? Email { get; set; }
    public Guid TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public bool IsValid { get; set; }
    public bool IsExpired { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? ErrorMessage { get; set; }
}

public record LoginPayload
{
    [EmailAddress]
    public required string Email { get; set; }
    public required string Password { get; set; }
}

public record LoginResponse : LoginResponseSimple
{
    public string RefreshToken { get; set; } = string.Empty;
}

public record LoginResponseSimple
{
    public string? Name { get; set; }
    public string? Email { get; set; }
    public List<string> Roles { get; set; } = new();
    // Deduped effective permission codes ("Feature:Action") for the user's default tenant --
    // derived fresh from Tenants below (see UserService.ComposeLoginResponse), not cached, so it
    // can't go stale the way Roles/DefaultTenantRoles can.
    public List<string> Permissions { get; set; } = new();
    public string ErrorMessage { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    //public Guid? DefaultTenantId { get; set; }
    //public string? DefaultTenantName  { get; set; }
    public List<UsersTenant> Tenants { get; set; }
    public DateTime Expiry { get; set; }

    /// <summary>
    /// Flow C(login) MFA extension point: true when an MFA challenge must be completed before
    /// AccessToken/RefreshToken are usable. Always false today (see NoOpMfaChallengeService) —
    /// reserved so a real MFA implementation can plug in without a breaking response shape change.
    /// </summary>
    public bool MfaRequired { get; set; }
    public string? MfaChallengeToken { get; set; }
}


public class UsersTenant
{
    public Guid TenantId { get; set; }
    public string? Name { get; set; }
    public List<string> Roles { get; set; } = new();
    public List<string> Permissions { get; set; } = new();
    public string State { get; set; }

    /// <summary>Raw HR-resource-provisioning status text (null until it reports in).</summary>
    public string? HrDbStatus { get; set; }
    /// <summary>True once the tenant's HR resources have been provisioned and are safe to use.</summary>
    public bool HrDbReady { get; set; }
}

public class CreateWorkspaceRequest
{
    public string TenantName { get; set; } = string.Empty;

    // Everything below is optional company detail the owner may fill in at creation.
    [System.ComponentModel.DataAnnotations.MaxLength(500)]
    public string? Address { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(100)]
    public string? Contact { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(200)]
    public string? Email { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(50)]
    public string? Tin { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(50)]
    public string? RdoCode { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(50)]
    public string? SssNumber { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(50)]
    public string? PhilHealthNumber { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(50)]
    public string? PagIbigNumber { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(200)]
    public string? AuthorizedSignatoryName { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(200)]
    public string? AuthorizedSignatoryTitle { get; set; }

    // Classification: industry/sub-industry codes come from Tenant Service's /industries lookup,
    // the rest from the fixed lists in CompanyClassification. Tenant Service drops unknown codes.
    [System.ComponentModel.DataAnnotations.MaxLength(64)]
    public string? IndustryCode { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(64)]
    public string? SubIndustryCode { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(32)]
    public string? BusinessType { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(32)]
    public string? CompanySize { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(32)]
    public string? AcquisitionSource { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(200)]
    public string? AcquisitionSourceOther { get; set; }
}