namespace Onepunch.Common.Lib.DTO;

// Attendance HRIS sends to the Client Portal over the message bus. The portal keeps its own data-only copy of these types (it doesn't
// reference this project); MassTransit routes by the full type name, so none of them may be moved or renamed. Every row is keyed by
// HRIS's own ids ("external ids"), so re-sending a row updates it instead of duplicating it.

public sealed record PortalClientRow(string ExternalId, string Code, string Name);

public sealed record PortalSiteRow(string ExternalId, string ClientExternalId, string Code, string Name);

/// <summary><see cref="Position"/>: the kind of personnel the post needs ("Regular guard", "Detachment commander"); null from older senders.</summary>
public sealed record PortalPostRow(string ExternalId, string SiteExternalId, string Code, string Name, string? Position = null);

public sealed record PortalGuardRow(string ExternalId, string ClientExternalId, string Code, string FullName);

/// <summary>Required headcount of a post's shift on a day: from the post's contracted manning when HRIS has it, else from the work schedule.</summary>
public sealed record PortalScheduleRow(string PostExternalId, DateOnly WorkDate, string ShiftName, int RequiredHeadcount);

public sealed record PortalPunchRow(DateTime At, string Kind);

/// <summary>
/// One employee-day. The first fields are the original contract; the ones after <see cref="Punches"/> were added later and default when a
/// sender doesn't fill them (older HRIS builds), so either side can be upgraded first.
/// <list type="bullet">
/// <item><see cref="RegularHours"/> still includes rest-day hours; <see cref="RestDayHours"/> says how many of them were on a rest day.</item>
/// <item><see cref="HolidayHours"/> = <see cref="LegalHolidayHours"/> + <see cref="SpecialHolidayHours"/>; <see cref="HolidayOvertimeHours"/> is the part
/// of <see cref="OvertimeHours"/> worked on a holiday.</item>
/// <item>A day not worked is sent too: <see cref="Absent"/>, or leave (<see cref="LeaveHours"/>, <see cref="LeaveType"/>, <see cref="LeavePaid"/>),
/// or a rest day (<see cref="DayType"/> RestDay with no times).</item>
/// <item><see cref="DayType"/>: Regular, RestDay, Legal, Special, DoubleLegal, RestLegal, RestSpecial or RestDoubleLegal.
/// <see cref="ShiftKind"/>: Day or Night.</item>
/// <item><see cref="DeploymentRole"/> (Regular, Reliever, Augmentation) and <see cref="EventTag"/> come from the employee's deployment.</item>
/// </list>
/// </summary>
public sealed record PortalDtrRow(
    string ExternalId,
    string ClientExternalId,
    string SiteExternalId,
    string PostExternalId,
    string GuardExternalId,
    DateOnly WorkDate,
    string ShiftName,
    DateTime? TimeIn,
    DateTime? TimeOut,
    int BreakMinutes,
    int LateMinutes,
    int UndertimeMinutes,
    decimal RegularHours,
    decimal OvertimeHours,
    decimal NightDiffHours,
    decimal HolidayHours,
    bool OvertimeApproved,
    IReadOnlyList<PortalPunchRow> Punches,
    string DayType = "Regular",
    decimal RestDayHours = 0,
    decimal LegalHolidayHours = 0,
    decimal SpecialHolidayHours = 0,
    decimal HolidayOvertimeHours = 0,
    bool Absent = false,
    decimal LeaveHours = 0,
    string? LeaveType = null,
    bool LeavePaid = false,
    DateTime? ScheduledStart = null,
    DateTime? ScheduledEnd = null,
    // Null from older senders, so the portal falls back to reading the shift name and time-in.
    string? ShiftKind = null,
    string? DeploymentRole = null,
    string? EventTag = null);

public sealed record PortalRowError(string ExternalId, string Error);

/// <summary>
/// Posted DTR rows to add or replace in the portal, with the clients, sites, posts, guards and scheduled headcount they need (parents
/// first). A large batch is split into several messages of at most <see cref="MaxRecords"/> rows, each carrying only its own parents.
/// </summary>
public record ClientPortalAttendanceUpserted
{
    public const int MaxRecords = 1000;

    /// <summary>The HRIS company, which is the portal's agency.</summary>
    public Guid TenantId { get; set; }

    public string BatchCode { get; set; } = string.Empty;

    /// <summary>Approved, PunchChanged or Resend.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Which message of the send this is (1-based) and how many there are, echoed in the result so HRIS can add them up.</summary>
    public int Part { get; set; } = 1;
    public int Parts { get; set; } = 1;

    public List<PortalClientRow> Clients { get; set; } = [];
    public List<PortalSiteRow> Sites { get; set; } = [];
    public List<PortalPostRow> Posts { get; set; } = [];
    public List<PortalGuardRow> Guards { get; set; } = [];
    public List<PortalScheduleRow> Schedules { get; set; } = [];
    public List<PortalDtrRow> Records { get; set; } = [];
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>DTR rows that are no longer posted in HRIS (batch deleted, unposted, or reopened by a leave change): the portal drops them.</summary>
public record ClientPortalAttendanceRemoved
{
    public const int MaxRecords = 1000;

    public Guid TenantId { get; set; }
    public string BatchCode { get; set; } = string.Empty;

    /// <summary>BatchDeleted, Unposted or LeaveChanged.</summary>
    public string Reason { get; set; } = string.Empty;

    public int Part { get; set; } = 1;
    public int Parts { get; set; } = 1;

    /// <summary>The HRIS DailyRecord ids, as the external ids the rows were sent with.</summary>
    public List<string> RecordExternalIds { get; set; } = [];
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// The portal's answer to one upsert or removal message, so HRIS can show what was accepted and what was refused (a row the client already
/// verified, or a date in a locked period, is refused rather than changed).
/// </summary>
public record ClientPortalAttendanceResult
{
    public const string Upsert = "Upsert";
    public const string Remove = "Remove";

    public Guid TenantId { get; set; }
    public string BatchCode { get; set; } = string.Empty;

    /// <summary>Upsert or Remove.</summary>
    public string Operation { get; set; } = string.Empty;

    /// <summary>Copied from the message this answers.</summary>
    public int Part { get; set; } = 1;
    public int Parts { get; set; } = 1;

    public int Records { get; set; }
    public int Accepted { get; set; }
    public int Rejected { get; set; }
    public List<PortalRowError> Errors { get; set; } = [];
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
}
