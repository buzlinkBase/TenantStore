namespace DTR.Core;

public class CurrentShift
{
    public string ShiftName { get; set; } = string.Empty;
    public TimeShiftType ShiftType { get; set; }
    public DateOnly ShiftDate { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }

    //morning
    public BreakMode WithAMBreak { get; set; }
    public DateTime? AMBreakStartTime { get; set; }
    public DateTime? AMBreakEndTime { get; set; }

    //lunch
    public BreakMode LunchBreakOption { get; set; }
    public DateTime? LunchStartTime { get; set; }
    public DateTime? LunchEndTime { get; set; }

    //pm break
    public BreakMode WithPMBreakTime { get; set; }
    public DateTime? PMBreakStartTime { get; set; }
    public DateTime? PMBreakEndTime { get; set; }


    //OT
    public bool WithOT { get; set; } = true;
    public bool OTRequireTimeIn { get; set; } = true;
    public double OverTimeThreshold { get; set; } = 60;
    public TimeSpan? OTStartTime { get; set; }

    public bool IsCrossDate => CrossDateChecker.IsCrossDate(StartTime, EndTime);
    public double GracePeriodMinutes { get; set; } = 0;
    public double LunchBreakDurationMinutes { get; set; } = 60;
    public double MinimumWorkingMinutes { get; set; } = 0;
    public double MaxWorkingMinutes { get; set; } = 480;

    public IEnumerable<DateOnly> GetShiftDays()
    {
        for (var date = DateOnly.FromDateTime(StartTime.Date); date <= DateOnly.FromDateTime(EndTime.Date); date = date.AddDays(1))
        {
            yield return date;
        }
    }
    public CurrentShift Clone()
    {
        return (CurrentShift)this.MemberwiseClone();
    }
}
