
using System.ComponentModel.DataAnnotations;

namespace DTR.Models;

public class TimeShift : BaseEntity
{
    [Required]
    [MaxLength(100)]
    public string ShiftName { get; set; } = string.Empty;
    public TimeShiftType ShiftType { get; set; }
    [Required]
    public TimeSpan StartTime { get; set; } = TimeSpan.Zero;
    [Required]
    public TimeSpan EndTime { get; set; } = TimeSpan.Zero;
    //am
    public BreakMode WithAMBreak { get; set; } = BreakMode.NONE;
    public TimeSpan? AMStartTime { get; set; } = TimeSpan.Zero;
    public TimeSpan? AMEndTime { get; set; } = TimeSpan.Zero;

    //lunch
    public BreakMode WithLunchBreak { get; set; } = BreakMode.UNPAID_BREAK;
    public TimeSpan? LunchStartTime { get; set; } = TimeSpan.Zero;
    public TimeSpan? LunchEndTime { get; set; } = TimeSpan.Zero;

    //PM
    public BreakMode WithPMBreak { get; set; } = BreakMode.NONE;
    public TimeSpan? PMStartTime { get; set; } = TimeSpan.Zero;
    public TimeSpan? PMEndTime { get; set; } = TimeSpan.Zero;

    public double GracePeriodMinutes { get; set; } = 0;
    public double BreakDurationMinutes { get; set; } = 60;

    public bool WithOT { get; set; } = false;
    public bool OTRequireTimeIn { get; set; } = false;
    //public double OTBreakAllowance { get; set; } = 0;//minutes
    public TimeSpan OTStart { get; set; }

    //Minimum extra hours before overtime applies.
    public double OverTimeThreshold { get; set; } = 60;
    //public bool PaidByNetDutyTime { get; set; }
    //flexi
    public double MinimumWorkMinutes { get; set; } = 0;
    public double MaxWorkingMinutes { get; set; } = 480;
}