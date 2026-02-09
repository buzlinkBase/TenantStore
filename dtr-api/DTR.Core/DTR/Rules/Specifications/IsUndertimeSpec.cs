using Microsoft.EntityFrameworkCore;
namespace DTR.Core;

public class IsUndertimeSpec : IRuleSpecification
{
    public IsUndertimeSpec()
    {
    }

    public bool IsSatisfiedBy(TimeRange input, TimeContext context)
    {
        //to compute undertime first log must be 3hours prior end time
        //var workTime = context.Payload.Ledger.GetByTag("work_time", context);
        //if (workTime.IsEmpty()) return false;
        //var adjustedEnd = context.Payload.Data.CurrentShift.EndTime.AddHours(-3);
        //var firslog = workTime.TimeRecords.MinBy(x => x.StartTime)?.StartTime;
        //if (firslog == null) return false;
        //return firslog >= adjustedEnd;
        return true;
    }
}