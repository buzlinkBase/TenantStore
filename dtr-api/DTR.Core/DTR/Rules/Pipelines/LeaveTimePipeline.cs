namespace DTR.Core;

public class LeaveTimePipeline : IConditionalPolicy
{
    public TimeRange Apply(TimeRange input, TimeContext context)
    {
        //var leave = context.Payload.Data.LeaveApplicationForDate;
        //if (leave == null) return TimeRange.Empty;

        //var slice = new TimeRangeCollection
        //{
        //    new TimeRecord(leave.StartTime, leave.EndTime, "LEAVE")
        //};

        //return new TimeRange(slice.TotalMinutes(), slice);
        return TimeRange.Empty;
    }
}