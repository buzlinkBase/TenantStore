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

        //this makes the ND and other Pipeline to depend on this modified time
        context.CanonicalTimeRange = context.CanonicalTimeRange
            .TimeRecords
            .Exclude(reclaimed.TimeRecords)
            .Retag("adjusted_cannonical_time")
            .ToTimeRange();

        // 🧾 Update ledger
        ledger.Record(TimeRangeLedger.CreateKey("RegularTimeTopUp", context), reclaimed);
        ledger.Record(TimeRangeLedger.CreateKey("FinalOT", context), remainingOT);

        // 🔗 Merge regular and reclaimed
        var finalRegular = regTimeRange + reclaimed;
        ledger.Record(ledgerKey, finalRegular);
        return finalRegular;
    }
}
