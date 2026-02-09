namespace DTR.Core;

public class OverBreakPolicy : ConditionalPolicyBase
{
    private readonly IRuleSpecification _spec;
    public OverBreakPolicy(IRuleSpecification spec) : base(spec)
    {
        _spec = spec;
    }

    protected override TimeRange ApplyIfSatisfied(TimeRange input, TimeContext context)
    {

        return ComputeOverBreak(context, context.Payload.Data.CurrentShift);
    }

    private TimeRange ComputeOverBreak(TimeContext context, CurrentShift shift)
    {
        //if (shift.LunchBreakOption != BreakOption.PaidBreak) return TimeRange.Empty;
        //var allbreaks = context.Payload.Ledger.GetByTag("allbreaks", context).TimeRecords;
        var amBreak = context.Payload.Ledger.GetByTag("AM_BREAK", context).TimeRecords;
        var pmbreak = context.Payload.Ledger.GetByTag("PM_BREAK", context).TimeRecords;
        var lunchbreak = context.Payload.Ledger.GetByTag("LUNCH_BREAK", context).TimeRecords;

        double lunchTotalBreak  = shift.LunchBreakOption==BreakMode.PAID_BREAK 
            ?  context.Payload.Data.CurrentShift.LunchBreakDurationMinutes 
            : 0;
        double amTotalBreak = 0;
        double pmTotalBreak = 0;

        if (shift.WithAMBreak == BreakMode.PAID_BREAK && shift.AMBreakStartTime.HasValue && shift.AMBreakEndTime.HasValue)
        {
            amTotalBreak += (shift.AMBreakEndTime.Value - shift.AMBreakStartTime.Value).TotalMinutes;
        }

        if (shift.WithPMBreakTime == BreakMode.PAID_BREAK && shift.PMBreakStartTime.HasValue && shift.PMBreakEndTime.HasValue)
        {
            pmTotalBreak += (shift.PMBreakEndTime.Value - shift.PMBreakStartTime.Value).TotalMinutes;
        }

        var amoverBreaks = OverbreakCalculator.ComputeOverbreaks(amBreak, amTotalBreak);
        var pmoverBreaks = OverbreakCalculator.ComputeOverbreaks(pmbreak, pmTotalBreak);
        var lunchoverBreaks = OverbreakCalculator.ComputeOverbreaks(lunchbreak, lunchTotalBreak);

        var timeRange = amoverBreaks.ToTimeRange() 
            + pmoverBreaks.ToTimeRange() 
            + lunchoverBreaks.ToTimeRange();

        context.Payload.Ledger.RecordByTag("overbreak", context, timeRange);
        return timeRange;
    }
}
