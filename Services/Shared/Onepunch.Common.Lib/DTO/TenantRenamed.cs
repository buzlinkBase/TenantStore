namespace Onepunch.Common.Lib.DTO;

/// <summary>
/// Published by Tenant Service after a company (tenant) rename is committed -- the rename starts
/// from HRIS's Company Info "Company Name" (see TenantService.RenameAsync). Auth Service keeps
/// its own copies of the name (User.DefaultTenantName, which every access token's tenantName
/// claim is minted from; Invitation.TenantName; TenantCreationRequest.TenantName) plus a
/// per-member membership cache, and brings them in line on receipt (TenantRenamedWorker).
/// </summary>
public record TenantRenamed
{
    public Guid TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;
    /// <summary>Every member with an account -- their membership cache is evicted and their live
    /// sessions are told to refresh so the new name shows up without re-logging in.</summary>
    public List<Guid> MemberUserIds { get; set; } = new();
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}
