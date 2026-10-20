namespace Onepunch.Common.Lib.DTO;

// Live attendance HRIS sends to the Client Portal over the message bus, ahead of (and apart from) the approved DTR batches in
// ClientPortalAttendance.cs. The portal keeps its own data-only copy of these types; MassTransit routes by the full type name, so none of
// them may be moved or renamed. Ids are HRIS's own ("external ids"), the same ones the DTR rows use, so both feeds land on the same
// clients, sites, posts and guards.

/// <summary>
/// One planned shift of a post on a day: when it starts and ends (null when the post's hours vary) and how many people it needs.
/// <see cref="Start"/> and <see cref="End"/> are local (Philippine) times; a night shift ends the next day.
/// </summary>
public sealed record PortalShiftPlanRow(string PostExternalId, DateOnly WorkDate, string ShiftName, DateTime? Start, DateTime? End, int RequiredHeadcount);

/// <summary>Who is deployed to a post and in what role (Regular, Reliever, Augmentation), from when to when.</summary>
public sealed record PortalAssignmentRow(string PostExternalId, string GuardExternalId, string Role, DateOnly From, DateOnly? To, string? EventTag);

/// <summary>
/// A company's planned manning for some days (sent daily for the coming week, and when someone asks): the shifts each post needs and who
/// is deployed. It replaces what the portal had for these posts and days. One message per client.
/// </summary>
public record ClientPortalManningPublished
{
    public Guid TenantId { get; set; }
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public List<PortalClientRow> Clients { get; set; } = [];
    public List<PortalSiteRow> Sites { get; set; } = [];
    public List<PortalPostRow> Posts { get; set; } = [];
    public List<PortalGuardRow> Guards { get; set; } = [];
    public List<PortalShiftPlanRow> Shifts { get; set; } = [];
    public List<PortalAssignmentRow> Assignments { get; set; } = [];
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A clock punch as it happened, where it happened: the site of the device (else the person's own), and the post they are deployed to
/// there when HRIS knows it. <see cref="At"/> is local (Philippine) time. <see cref="Source"/> is how it was taken (Device, Mobile, Manual).
/// </summary>
public sealed record PortalLivePunchRow(
    string ExternalId, string ClientExternalId, string SiteExternalId, string? PostExternalId, string GuardExternalId, DateTime At, string Source, string? Device);

/// <summary>
/// Punches just saved in HRIS, with the clients, sites, posts and guards they need. Sent as they arrive, for live monitoring only: billing
/// still comes from approved DTR batches. Old punches (uploaded long after) are not sent.
/// </summary>
public record ClientPortalPunchesRecorded
{
    public const int MaxPunches = 1000;

    public Guid TenantId { get; set; }
    public List<PortalClientRow> Clients { get; set; } = [];
    public List<PortalSiteRow> Sites { get; set; } = [];
    public List<PortalPostRow> Posts { get; set; } = [];
    public List<PortalGuardRow> Guards { get; set; } = [];
    public List<PortalLivePunchRow> Punches { get; set; } = [];
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}
