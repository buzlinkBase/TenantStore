//namespace DTR.Core;

//public class DeductFirst8HrPolicy : ConditionalPolicyBase
//{
  

//    protected override TimeRange ApplyIfSatisfied(TimeRange input, TimeContext context)
//    { 
//        var shift = context.Payload.Data.CurrentShift;
//        var ledger = context.Payload.Ledger;

//        var ledgerKey = TimeRangeLedger.CreateKey<RegularHourPolicy>(context);

//        ledger.GetBykey(ledgerKey, out var regularClaim);
//        var totalRegular = regularClaim?.TotalMinutes ?? 0;

//        if (totalRegular >= shift.MaxWorkingMinutes) return input; // no need to alter

//        // Try topping up from unclaimed time

//        var cannonicalTimeRange = context.CanonicalTimeRange;
//        var otTime = new OverTimeHandlerManager(context)
//            .Handle(cannonicalTimeRange);

//        var topUp = RegularHourFulfillment.TopUpRegularTimeIfNeeded(context, "RegularTimeTopUp") ;
        
//        var newTotal = topUp.TotalMinutes + totalRegular;

//        if (newTotal < shift.MaxWorkingMinutes)
//            return TimeRange.Empty; // Block OT

//        // Only allow OT for leftover time beyond 8hr
//        var blocked = ledger.GetAllAllocatedExcept();
//        var usable = context.CanonicalTimeRange.TimeRecords.Exclude(blocked);

//        var otStart = usable.FlattenFrom(shift.StartTime.AddMinutes(shift.MaxWorkingMinutes));
//        otStart = otStart.TagAll("OT_after_RegularFulfilled");
//        ledger.Record("OT_after_RegularFulfilled", otStart);
//        return otStart;


//        ////validate First8Rule here
//        //var fulfilled = context.Payload.Ledger.GetByTag("RegularTime", context)?.TotalMinutes ?? 0;
//        //if (_specification.IsSatisfiedBy(input, context) && fulfilled < shift.MaxWorkingMinutes)
//        //{
//        //    this.RecordLedger(context, TimeRange.Empty);
//        //    return TimeRange.Empty;
//        //}

//        //return postShiftOT;
//        return input;
//    }
//}
