namespace DTR.Core;

public class First8HrPolicy : ConditionalPolicyBase
{
    public First8HrPolicy(SpecFailureBehavior behavior = SpecFailureBehavior.ReturnEmpty)
        : base(new IsOTFirst8hrRuleSpec(), behavior) { }

    protected override TimeRange ApplyIfSatisfied(TimeRange regTimeRange, TimeContext context)
    {
        var shift = context.Payload.Data.CurrentShift;
        var ledger = context.Payload.Ledger;
        var requiredMaxWorkingMinutes = shift.MaxWorkingMinutes;
        var canonicalTime = context.CanonicalTimeRange;

        var ledgerKey = TimeRangeLedger.CreateKey("first8_time_range", context);
        var cached = context.Payload.Ledger.GetByKey(ledgerKey);
        if (cached.Found)
        {
            var value = cached.Value ?? TimeRange.Empty;
            return value;
        }

        var alreadyClaimed = regTimeRange.TotalMinutes;
        if (alreadyClaimed >= requiredMaxWorkingMinutes) //no need to patch time
        {
            //no UT or Late
            ledger.Record(ledgerKey, regTimeRange);
            return regTimeRange;
        }
        //cannot borrow morethan MaxWorkingMinutes
        var shortfall = requiredMaxWorkingMinutes - alreadyClaimed;
        // ⚙️ Dynamically run OT handler
        TimeRange computedOT = new OverTimeHandlerProcessor(context).Handle(canonicalTime);
        if (computedOT.IsEmpty())//no OT
        {
            ledger.Record(ledgerKey, regTimeRange);
            return regTimeRange;
        }

        // ✂️ Slice OT records to borrow into RegularTime
        //if crop from end ND might fall
        var otRecords = computedOT.TimeRecords;
        var reclaimed = otRecords
            .CropFromEnd(shortfall)
            .TimeRecords
            .Retag("RegularTimeTopUp")
            .ToTimeRange();

        var remainingOT = otRecords
            .Exclude(reclaimed.TimeRecords)
            .Retag("OT_after_RegularFulfilled")
            .ToTimeRange();

        //Alter cannonical CanonicalTimeRange
        //this makes the ND and other Pipeline to depend on this modified time
        context.CanonicalTimeRange = context.CanonicalTimeRange
            .TimeRecords
            .Exclude(reclaimed.TimeRecords)
            .Retag("adjusted_cannonical_time")
            .ToTimeRange();

        // 🔗 Merge regular and reclaimed
        var shiftts = new TimeRangeCollection
        {
            new TimeRecord
            {
                StartTime=shift.StartTime,
                EndTime=shift.EndTime
            }
        };
        var amBreak = context.Payload.Ledger.GetByTag("morning_break_period", context);
        var lunchBreak = context.Payload.Ledger.GetByTag("lunch_break_period_use_time", context);
        var pmBreak = context.Payload.Ledger.GetByTag("pm_break_period", context);
        var break_period = context.Payload.Ledger.GetByTag("break_period", context);
        var NoneBreak = context.Payload.Ledger.GetByTag("No_lunch_break_gt_480MinutesShift", context);
        var missingTime = shiftts
            .Exclude(amBreak.TimeRecords)
            .Exclude(lunchBreak.TimeRecords)
            .Exclude(break_period.TimeRecords)
            .Exclude(pmBreak.TimeRecords)
            .Exclude(NoneBreak.TimeRecords)
            .Exclude(regTimeRange.TimeRecords)
            .ToTimeRange()
            .TimeRecords.CapAndCrop(shift, reclaimed.TotalMinutes)
            ;

        // 🧾 Update ledger
        ledger.Record(TimeRangeLedger.CreateKey("RegularTimeTopUp", context), missingTime);
        ledger.Record(TimeRangeLedger.CreateKey("FinalOT", context), remainingOT);

        var finalRegular = regTimeRange + missingTime;
        ledger.Record(ledgerKey, finalRegular);
        return finalRegular;
    }
}

