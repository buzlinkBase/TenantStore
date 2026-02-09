namespace DTR.Core;

internal class NDLHColumnEvaluator : IColumnEvaluator
{
    private readonly EvaluatedColumnResult _evaluated;
    public NDLHColumnEvaluator(EvaluatedColumnResult evaluated)
    {
        _evaluated = evaluated;
    }
    public TimeRange ApplyRules(DisplayContext context)
    {

        var NDthresholdWrapper = DutyTypeMapFactory.Create[DayType.NIGHT_DIFF];
        var holiday = context.PipeLineResult.LegalHoliday;
        var IsND = NightDiffChecker.IsDutyNightDiff(context.TimeContext.Payload.Data.CurrentShift.StartTime, context.TimeContext.Payload.Data.CurrentShift.StartTime);
        var topup = context.TimeContext.Payload.Ledger.GetByTag("RegularTimeTopUp", context.TimeContext);
        
        if (new IsHolidaySpec(HolidayType.LEGAL).Not().IsSatisfiedBy(context.TimeContext.CanonicalTimeRange, context.TimeContext))
        {
            return TimeRange.Empty;
        }

        if (new IsShowWorkOnHolidayInRegColumn().IsSatisfiedBy(context))
        {
            TimeRange worktime = context.TimeContext.Payload.Ledger.GetByTag("work_time", context.TimeContext);
            if (!IsND)
            {
                worktime = worktime.TimeRecords.Exclude(topup.TimeRecords)
                    .ToTimeRange();
            }
            return NDthresholdWrapper.Evaluate(NightDiffCalculator.Calculate(worktime), context.TimeContext);
        }
        else
        {
            var nd = NightDiffCalculator.Calculate(_evaluated.LegalHoliday);
            if (!IsND)
            {
                nd = nd.TimeRecords.Exclude(topup.TimeRecords)
                     .ToTimeRange();
            }
            return NDthresholdWrapper.Evaluate(nd, context.TimeContext);
        }
    }
}
