using System.ComponentModel.DataAnnotations;

namespace DTR.Models.ValueObjects;

public class HolidayPayload  : BasePayload
{
    public string Description { get; set; } = string.Empty;
    public HolidayType HolType { get; set; } = HolidayType.LEGAL;
    public HolidayWorkType WorkType { get; set; } = HolidayWorkType.NonWorking;
    public int HolYear { get; set; }
    public DateOnly HolDate { get; set; }
    public bool IsRecuring { get; set; }
    public bool IsPaid { get; set; }
    public Guid AreaId { get; set; }
}

public class ChangeHolidayPaload : BasePayload
{
    public Guid BatchEntryId { get; set; }
    public Guid HolidayId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateOnly PayrollDate { get; set; }
    public ChangeSchedState State { get; set; }
}

public class WorkSchedulePlanPayload  : BasePayload
{
    public DateOnly PayrollDate { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid TimeShiftId { get; set; }
    public Guid Batch { get; set; }
    public string User { get; set; } = string.Empty;
}
