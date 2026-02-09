namespace DTR.Core;

public class IsFixedScheduleSpec : IRuleSpecification
{
    public bool IsSatisfiedBy(TimeRange input, TimeContext context)
    {
        return context.Payload.Data.CurrentShift.ShiftType == TimeShiftType.FIXED;
    }
}
