namespace DTR.Core;

public class RegularCurrentScheduledTimeCalculator : BaseTimeCalculator
{
    protected override bool IsValid()
    {
        return base.IsValid() && _payload.Data.CurrentAttendance.Count() > 1;
    }
    public RegularCurrentScheduledTimeCalculator(DTRProcessorPayload payload) : base(payload)
    {
    }
    protected override TimeRange Processor()
    {
        return TimeRangeCalculator.GetTimeRange(_payload.Data.CurrentAttendance);
    }
}