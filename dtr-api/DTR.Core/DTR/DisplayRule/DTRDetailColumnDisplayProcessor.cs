namespace DTR.Core;

public static class DTRDetailColumnDisplayProcessor
{
    public static NightDiffEvaluationResult ComputeNightDiff(EvaluatedColumnResult evaluated, DisplayContext dContext)
    {
        var context = dContext.TimeContext;
        var NightDiffThresholdEvaluator = DutyTypeMapFactory.Create[DayType.NIGHT_DIFF];
        //var NonHoldEval = DutyTypeMapFactory.Create[DayType.NONHOLIDAY];
        var IsND = NightDiffChecker.IsDutyNightDiff(context.Payload.Data.CurrentShift.StartTime, context.Payload.Data.CurrentShift.StartTime);
        TimeRange worktime = context.Payload.Ledger.GetByTag("work_time", context);
        if (!IsND)
        {
            //exclude topup if not ND
            var topup = context.Payload.Ledger.GetByTag("RegularTimeTopUp", context);
            worktime = worktime
              .TimeRecords.Exclude(topup.TimeRecords)
              .ToTimeRange()
              ;
        }

        var NDLH = new ColumnEvaluatorHandler(new NDLHColumnEvaluator(evaluated)).Handle(dContext);
        var NDREGULAR = new ColumnEvaluatorHandler(new NDRegularEvaluator(evaluated)).Handle(dContext);

        return new NightDiffEvaluationResult
        {
            Regular = !evaluated.RegWork.IsEmpty() ? NDREGULAR : TimeRange.Empty,
            Rest = !evaluated.RestWork.IsEmpty() ? NDREGULAR : TimeRange.Empty,
            ND = NightDiffThresholdEvaluator.Evaluate(NightDiffCalculator.Calculate(worktime), context),
            NDOT = NightDiffThresholdEvaluator.Evaluate(NightDiffCalculator.Calculate(evaluated.OverTime), context),
            RegOT = NightDiffThresholdEvaluator.Evaluate(NightDiffCalculator.Calculate(evaluated.RegOT), context),
            RestOT = NightDiffThresholdEvaluator.Evaluate(NightDiffCalculator.Calculate(evaluated.RestOT), context),
            LH = NDLH,
            SP = NightDiffThresholdEvaluator.Evaluate(NightDiffCalculator.Calculate(evaluated.SPHoliday), context),
            LHOT = NightDiffThresholdEvaluator.Evaluate(NightDiffCalculator.Calculate(evaluated.LHOT), context),
            SPOT = NightDiffThresholdEvaluator.Evaluate(NightDiffCalculator.Calculate(evaluated.SPOT), context),
        };
    }

    public static EvaluatedColumnResult DisplayRule(DisplayContext displayContext)
    {
        var context = displayContext.TimeContext;
        var payload = context.Payload;
        var regDayEvaluator = DutyTypeMapFactory.Create[DayType.REGULAR];
        var restDayEvaluator = DutyTypeMapFactory.Create[DayType.RESTDAY];

        var regOTEvaluator = DutyTypeMapFactory.Create[DayType.REGULAR_OVERTIME];
        var legalHolOTEvaluator = DutyTypeMapFactory.Create[DayType.LEGAL_HOLIDAY_OVERTIME];
        var specialHolOTEvaluator = DutyTypeMapFactory.Create[DayType.SPECIAL_HOLIDAY_OVERTIME];

        var provider = HolidayOTFactory.Create(context, displayContext.PipeLineResult.OT);
        var legalOT = provider.Calculate(HolidayType.LEGAL);
        var SpecialOT = provider.Calculate(HolidayType.SPECIAL);

        //if actual OT settings is used,posible some OT is exclude out of bound for Holiday
        var legalOTAdditionalRange = context.Payload.Ledger.GetByTag($"REGULAR_OT_ACTUAL_{HolidayType.LEGAL}", context);
        var specialOTAdditionalRange = context.Payload.Ledger.GetByTag($"REGULAR_OT_ACTUAL_{HolidayType.SPECIAL}", context);
        var additionalRange = legalOTAdditionalRange + specialOTAdditionalRange;

        var regularDisplay = new ColumnEvaluatorHandler(new RegularColumnStrategy()).Handle(displayContext);
        var holidayDisplay = new ColumnEvaluatorHandler(new LHColumnEvaluator()).Handle(displayContext);
        var spHolidayDisplay = new ColumnEvaluatorHandler(new SPColumEvaluator()).Handle(displayContext);
        var OverTime = new ColumnEvaluatorHandler(new OTColumnEvaluator()).Handle(displayContext);
        var evaluated = new EvaluatedColumnResult
        {
            RegWork = regDayEvaluator.Evaluate(regularDisplay, context),
            RestWork = restDayEvaluator.Evaluate(regularDisplay, context),
            OverTime = OverTime,
            LegalHoliday = holidayDisplay,
            SPHoliday = spHolidayDisplay,
            RegOT = additionalRange + regOTEvaluator.Evaluate(regDayEvaluator.Evaluate(OverTime, context), context),
            RestOT = additionalRange + regOTEvaluator.Evaluate(restDayEvaluator.Evaluate(OverTime, context), context),
            LHOT = legalHolOTEvaluator.Evaluate(legalOT, context),
            SPOT = specialHolOTEvaluator.Evaluate(SpecialOT, context),
        };
        return evaluated;
    }
}
public class EvaluatedColumnResult
{
    public TimeRange RegWork { get; set; }
    public TimeRange RestWork { get; set; }
    public TimeRange LegalHoliday { get; set; }
    public TimeRange SPHoliday { get; set; }

    public TimeRange OverTime { get; set; }
    public TimeRange RegOT { get; set; }
    public TimeRange RestOT { get; set; }

    public TimeRange LHOT { get; set; }
    public TimeRange SPOT { get; set; }

}
public class NightDiffEvaluationResult
{
    public TimeRange Regular { get; set; }
    public TimeRange Rest { get; set; }
    public TimeRange ND { get; set; }
    public TimeRange NDOT { get; set; }

    public TimeRange RegOT { get; set; }
    public TimeRange RestOT { get; set; }

    public TimeRange LH { get; set; }
    public TimeRange SP { get; set; }

    public TimeRange LHOT { get; set; }
    public TimeRange SPOT { get; set; }
}
public class PipeLineResult
{
    public TimeRange Regular { get; set; }
    public TimeRange LegalHoliday { get; set; }
    public TimeRange SpecialHoliday { get; set; }
    public TimeRange Plus8 { get; set; }
    public TimeRange OT { get; set; }
    public TimeRange Late { get; set; }
    public TimeRange UT { get; set; }
    public TimeRange Overbreak { get; set; }
    public TimeRange Leave { get; set; }
}