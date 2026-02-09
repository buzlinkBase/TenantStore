
using MessagePack;

namespace DTR.Models;

[MessagePackObject]
public class TimeShift  
{
    [Key(0)] public string ShiftName { get; set; } = string.Empty;
    [Key(1)] public TimeShiftType ShiftType { get; set; }
    [Key(2)] public TimeSpan StartTime { get; set; } = TimeSpan.Zero;
    [Key(3)] public TimeSpan EndTime { get; set; } = TimeSpan.Zero;
    [Key(4)] public BreakMode WithAMBreak { get; set; } = BreakMode.PAID_BREAK;
    [Key(5)] public TimeSpan? AMStartTime { get; set; } = TimeSpan.Zero;
    [Key(6)] public TimeSpan? AMEndTime { get; set; } = TimeSpan.Zero;
    [Key(7)] public BreakMode WithLunchBreak { get; set; } = BreakMode.UNPAID_BREAK;
    [Key(8)] public TimeSpan? LunchStartTime { get; set; } = TimeSpan.Zero;
    [Key(9)] public TimeSpan? LunchEndTime { get; set; } = TimeSpan.Zero;
    [Key(10)] public BreakMode WithPMBreak { get; set; } = BreakMode.PAID_BREAK;
    [Key(11)] public TimeSpan? PMStartTime { get; set; } = TimeSpan.Zero;
    [Key(12)] public TimeSpan? PMEndTime { get; set; } = TimeSpan.Zero;
    [Key(13)] public double GracePeriodMinutes { get; set; } = 0;
    [Key(14)] public double BreakDurationMinutes { get; set; } = 60;
    [Key(15)] public bool WithOT { get; set; } = false;
    [Key(16)] public bool OTRequireTimeIn { get; set; } = false;
    [Key(17)] public TimeSpan OTStart { get; set; }
    [Key(18)] public double OverTimeThreshold { get; set; } = 60;
    [Key(19)] public double MinimumWorkMinutes { get; set; } = 0;
    [Key(20)] public double MaxWorkingMinutes { get; set; } = 480;
    [Key(21)]  public Guid Id { get; set; }
}
