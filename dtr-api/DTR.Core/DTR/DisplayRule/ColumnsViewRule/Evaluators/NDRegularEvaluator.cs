namespace DTR.Core;

internal class NDRegularEvaluator : IColumnEvaluator
{
    private readonly EvaluatedColumnResult _evaluated;
    public NDRegularEvaluator(EvaluatedColumnResult evaluated)
    {
        _evaluated = evaluated;
    }
    public TimeRange ApplyRules(DisplayContext context)
    {
        var NonHoldEval = DutyTypeMapFactory.Create[DayType.NONHOLIDAY];
        var NDthresholdWrapper = DutyTypeMapFactory.Create[DayType.NIGHT_DIFF];
        var regular = _evaluated.RegWork + _evaluated.RestWork;
        var IsND = NightDiffChecker.IsDutyNightDiff(context.TimeContext.Payload.Data.CurrentShift.StartTime, context.TimeContext.Payload.Data.CurrentShift.StartTime);
        var topup = context.TimeContext.Payload.Ledger.GetByTag("RegularTimeTopUp", context.TimeContext);

        if (!IsND)
        {
            regular = regular.TimeRecords
                .Exclude(topup.TimeRecords)
                .ToTimeRange();
        }

        var nd = NightDiffCalculator.Calculate(regular);

        return NonHoldEval
                .Evaluate(NDthresholdWrapper
                    .Evaluate(nd, context.TimeContext), context.TimeContext)
                ;
    }
}
