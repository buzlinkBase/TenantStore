namespace TenantStoreApi.Domain.DTOs;



public class UpdateTenant
{
    public string Status { get; set; }
}

public class UpdateTenantProfile
{
    [System.ComponentModel.DataAnnotations.MaxLength(500)]
    public string? Address { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(100)]
    public string? Contact { get; set; }
    /// <summary>Null when the caller doesn't send classification at all (older clients): the stored
    /// values are then left alone. When present, it replaces all four (a blank clears it).</summary>
    public TenantClassification? Classification { get; set; }
}

/// <summary>How a company is classified: the stable codes of the industry lookup and the
/// <c>CompanyClassification</c> lists. All optional.</summary>
public class TenantClassification
{
    [System.ComponentModel.DataAnnotations.MaxLength(64)]
    public string? IndustryCode { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(64)]
    public string? SubIndustryCode { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(32)]
    public string? BusinessType { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(32)]
    public string? CompanySize { get; set; }
}

public class TenantModel
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TenantName { get; set; }
    public string Status { get; set; }
    public string? IndustryCode { get; set; }
    public string? SubIndustryCode { get; set; }
    public string? BusinessType { get; set; }
    public string? CompanySize { get; set; }
}

