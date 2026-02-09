namespace DTR.Core;

public class TrimOTForFirst8HrPolicy : ConditionalPolicyBase
{
    public TrimOTForFirst8HrPolicy(IRuleSpecification spec, SpecFailureBehavior behavior = SpecFailureBehavior.ReturnInput)
        : base(spec, behavior) { }

    protected override TimeRange ApplyIfSatisfied(TimeRange otRange, TimeContext context)
    {
        var shift = context.Payload.Data.CurrentShift;
        var ledger = context.Payload.Ledger;
        var requiredMinutes = shift.MaxWorkingMinutes;

        var ledgerKey = TimeRangeLedger.CreateKey<TrimOTForFirst8HrPolicy>(context);
        var cached = context.Payload.Ledger.GetByKey(ledgerKey);
        if (cached.Found)
        {
            var value = cached.Value ?? TimeRange.Empty;
            return value;
        } 

        // ⛳ Get previously claimed RegularTime
        var regularKey = TimeRangeLedger.CreateKey<RegularHourPolicy>(context);
        var cachedreg = context.Payload.Ledger.GetByKey(regularKey);
        var regTime = cachedreg.Value ?? TimeRange.Empty;
        var regClaimed = regTime?.TotalMinutes ?? 0;

        if (regClaimed >= requiredMinutes)
        {
            // Regular already fulfilled — no OT trimming needed
            ledger.Record(ledgerKey, otRange);
            return otRange;
        }

        var shortfall = requiredMinutes - regClaimed;
        var otRecords = otRange.TimeRecords;

        // ✂️ Trim OT from the start to fill the gap
        var borrowed = otRecords
            .CropFromStart(shortfall)
            .TimeRecords
            .Retag("RegularTimeTopUp")
            .ToTimeRange();

        var remainingOT = otRecords
            .Exclude(borrowed.TimeRecords)
            .Retag("OT_after_RegularFulfilled")
            .ToTimeRange();

        // 🔐 Record ledger adjustments
        ledger.Record(TimeRangeLedger.CreateKey("RegularTimeTopUp", context), borrowed);
        ledger.Record(TimeRangeLedger.CreateKey("FinalOT", context), remainingOT);
        ledger.Record(ledgerKey, remainingOT);

        return remainingOT;
    }
} 
