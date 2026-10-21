namespace Onepunch.Common.Lib.DTO;

/// <summary>
/// Published by HRIS after a committed change to one of its clients (created, edited, made inactive,
/// deleted). HRIS is where clients are kept, so the Client Portal follows: its copy carries the same
/// id, and a client that is inactive or gone in HRIS has all its users switched off there. The portal
/// keeps its own data-only copy of this type (it doesn't reference this project); MassTransit routes by
/// the full type name, so neither may be moved or renamed.
/// </summary>
public record ClientChanged
{
    /// <summary>The HRIS company, which is the portal's agency.</summary>
    public Guid TenantId { get; set; }

    public Guid ClientId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>HRIS's own status text for the client ("Active" / "ACTIVE" while it is in use).</summary>
    public string Status { get; set; } = string.Empty;

    public string ChangeType { get; set; } = string.Empty; // Created, Updated, Deleted
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
} 