namespace DTR.Core;

public class WholeDayLateHandler : ILateDeductionHandler
{
    public bool CanHandle(TimeRange lateSlice, TimeContext context) =>
        new IsWholeDayLateThresholdSpec().IsSatisfiedBy(lateSlice, context);

    public TimeRange Apply(TimeRange regTime, TimeRange lateSlice, TimeContext context)
    {
        var MaxWorkingMinutes = context.Payload.Data.CurrentShift.MaxWorkingMinutes;
        context.Payload.Ledger.RecordByTag("late", context, new TimeRange(MaxWorkingMinutes));
        return TimeRange.Set(0, regTime.TimeRecords);
    }
}