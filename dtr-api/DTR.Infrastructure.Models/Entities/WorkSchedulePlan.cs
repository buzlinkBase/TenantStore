using MessagePack;

namespace DTR.Models;

[MessagePackObject]
public class WorkSchedulePlan
{
    [Key(0)] public DateOnly PayrollDate { get; set; }
    [Key(1)] public Guid EmployeeId { get; set; }
    [Key(2)] public Guid TimeShiftId { get; set; }
}
