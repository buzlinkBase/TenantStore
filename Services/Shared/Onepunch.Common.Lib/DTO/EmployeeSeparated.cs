namespace Onepunch.Common.Lib.DTO;

/// <summary>
/// Published by hrms-api when an employee becomes separated (Terminated, Resigned, Retired or
/// Deceased) and has a linked login. Tenant Service revokes that account's membership of the
/// company so the person can no longer sign in to it.
/// </summary>
public record EmployeeSeparated
{
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
}
