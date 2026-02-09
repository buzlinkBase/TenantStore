namespace DTR.Core;

public class HalfDayLateHandler : ILateDeductionHandler
{
    public bool CanHandle(TimeRange lateSlice, TimeContext context)
    {
        var spec = new IsHalfDayLateThresholdSpec();
        var MaxWorkingMinutes = context.Payload.Data.CurrentShift.MaxWorkingMinutes;
        return spec.IsSatisfiedBy(lateSlice, context) && lateSlice.TotalMinutes <= MaxWorkingMinutes/2;
    }

    public TimeRange Apply(TimeRange regTime, TimeRange lateSlice, TimeContext context)
    {
        var MaxWorkingMinutes = context.Payload.Data.CurrentShift.MaxWorkingMinutes;
        var cap = regTime.TimeRecords.CropFromStart(MaxWorkingMinutes/2);
        var minutesToCrop = Math.Min(MaxWorkingMinutes/2, regTime.TotalMinutes);
        var remaining = new TimeRange(minutesToCrop, cap.TimeRecords);
        context.Payload.Ledger.RecordByTag("late", context, new TimeRange(MaxWorkingMinutes - minutesToCrop));
        return remaining;
    }
}