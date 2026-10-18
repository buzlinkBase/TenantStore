
namespace Onepunch.Common.Lib.DTO;

public record TenantCreationRequested
{
    public required string TenantName { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string Email { get; set; }
    public string FullName { get; set; }
    /// <summary>Optional company details the owner entered when creating the company.</summary>
    public CompanyProfile? Profile { get; set; }
}

/// <summary>
/// Optional company details captured at company creation. Tenant Service keeps only the
/// console-facing ones (Address, Contact); HRIS seeds its Company record from the full set.
/// Every member is optional -- blank values are normalized to null by <see cref="Normalize"/>.
/// </summary>
public record CompanyProfile
{
    public string? Address { get; set; }
    public string? Contact { get; set; }
    public string? Email { get; set; }
    public string? Tin { get; set; }
    public string? RdoCode { get; set; }
    public string? SssNumber { get; set; }
    public string? PhilHealthNumber { get; set; }
    public string? PagIbigNumber { get; set; }
    public string? AuthorizedSignatoryName { get; set; }
    public string? AuthorizedSignatoryTitle { get; set; }

    /// <summary>Trims every value and turns blanks into null; null when nothing was entered.</summary>
    public static CompanyProfile? Normalize(CompanyProfile? profile)
    {
        if (profile == null) return null;

        static string? Clean(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        var cleaned = new CompanyProfile
        {
            Address = Clean(profile.Address),
            Contact = Clean(profile.Contact),
            Email = Clean(profile.Email),
            Tin = Clean(profile.Tin),
            RdoCode = Clean(profile.RdoCode),
            SssNumber = Clean(profile.SssNumber),
            PhilHealthNumber = Clean(profile.PhilHealthNumber),
            PagIbigNumber = Clean(profile.PagIbigNumber),
            AuthorizedSignatoryName = Clean(profile.AuthorizedSignatoryName),
            AuthorizedSignatoryTitle = Clean(profile.AuthorizedSignatoryTitle),
        };

        return cleaned == new CompanyProfile() ? null : cleaned;
    }
}

public record UserJoin
{
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public string FullName { get; set; }
    public List<string> Roles { get; set; } = new() { "None" };
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
    public List<string> Roles { get; set; } = new() { "None" };
}