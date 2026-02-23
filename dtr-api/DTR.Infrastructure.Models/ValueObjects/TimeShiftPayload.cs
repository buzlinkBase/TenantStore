namespace DTR.Models.ValueObjects;

public class TimeShiftPayload : BasePayload
{
    public string ShiftName { get; set; } = string.Empty;
    public TimeShiftType ShiftType { get; set; }
    public TimeSpan StartTime { get; set; } = TimeSpan.Zero;
    public TimeSpan EndTime { get; set; } = TimeSpan.Zero;
    public BreakMode WithAMBreak { get; set; } = BreakMode.NONE;
    public TimeSpan? AMStartTime { get; set; } = TimeSpan.Zero;
    public TimeSpan? AMEndTime { get; set; } = TimeSpan.Zero;
    public BreakMode WithLunchBreak { get; set; } = BreakMode.UNPAID_BREAK;
    public TimeSpan? LunchStartTime { get; set; } = TimeSpan.Zero;
    public TimeSpan? LunchEndTime { get; set; } = TimeSpan.Zero;
    public BreakMode WithPMBreak { get; set; } = BreakMode.NONE;
    public TimeSpan? PMStartTime { get; set; } = TimeSpan.Zero;
    public TimeSpan? PMEndTime { get; set; } = TimeSpan.Zero;
    public double GracePeriodMinutes { get; set; } = 0;
    public double BreakDurationMinutes { get; set; } = 60;
    public bool WithOT { get; set; } = false;
    public bool OTRequireTimeIn { get; set; } = false;
    public TimeSpan OTStart { get; set; }
    public double OverTimeThreshold { get; set; } = 60;
    public double MinimumWorkMinutes { get; set; } = 0;
    public double MaxWorkingMinutes { get; set; } = 480;
}

