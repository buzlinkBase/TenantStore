namespace DTR.Core;

public class TimeAllowance
{
    public static double TimeInAllowance { get; set; } = -120;
    public static double DoublePunchGap { get; set; }
    public static double LunchPaidBreakCaptureAllowance { get; set; } = 0;
    public static double SnackBreakAllowance { get; set; } = 15;//15mins
    //public static double BreakDeductionThreshold { get; set; }
    //public static double ValidBreakTheshold { get; set; } = DoublePunchGap;
    //added to OT start time to capture earlies OT time
    //example: OT start: 17:30 ,OTTimeCaptureAllowanceMinutes=-10
    //so the earliest acceptable valid OT time is 17:20
    public static double OTTimeCaptureAllowanceMinutes { get; set; } = -30;
    public static int AttLookbackDays { get; set; } = -7;
    public static int AttLookforward { get; set; } = 4;
}
