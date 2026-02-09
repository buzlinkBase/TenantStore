namespace DTR.Core;

public class AppliedOvertimePolicy : ConditionalPolicyBase
{
    public AppliedOvertimePolicy(IRuleSpecification specification) : base(specification, SpecFailureBehavior.ReturnEmpty) { }

    protected override TimeRange ApplyIfSatisfied(TimeRange input, TimeContext context)
    {
        var payload = context.Payload;
        var shift = payload.Data.CurrentShift;
        var ledgerKey = this.CreateLedgerKey(context);

        // 🧾 Avoid duplicate claim
        var cached = context.Payload.Ledger.GetByKey(ledgerKey);
        if (cached.Found)
        {
            var value = cached.Value ?? TimeRange.Empty;
            return value;
        }

        // 📋 Pull applied OT data
        var appliedOT = payload.Provider.OTProvider.GetOT(shift.ShiftDate);
        if (appliedOT == null) return TimeRange.Empty;
        var manualMinutes = appliedOT.OTMinutes;

        // 🛑 Attempt override the system generated OT
        var systemKey = TimeRangeLedger.CreateKey<AutoComputeOvertimePolicy>(context);
        var cachedSystem = context.Payload.Ledger.GetByKey(systemKey);
        var systemOT = cachedSystem.Value ?? TimeRange.Empty;
        if (cached.Found && !systemOT.IsEmpty())
        {
            var cropped = systemOT.TimeRecords
                .CropFromStart(manualMinutes);

            var retagged = cropped.TimeRecords
                .Retag("OT_ManualOverride")
                .ToTimeRange();

            if (retagged.TotalMinutes < appliedOT.OverTimeThreshold)
            {
                this.RecordLedger(context, TimeRange.Empty);
                return TimeRange.Empty;
            }
            this.RecordLedger(context, retagged);
            return retagged;
        }
        //Fallback logic: build from usable time
        var blocked = context.Payload.Ledger.GetAllAllocatedExcept(ledgerKey);
        var usable = context.CanonicalTimeRange.TimeRecords
            .Exclude(blocked)
            .MergeOverlapping();

        var otStart = GetOTStartTime(context, appliedOT);
        var shiftEnd = context.Payload.Data.CurrentShift.EndTime;

        var fallbackSlices = usable
            .Where(r => r.StartTime >= otStart && r.StartTime >= shiftEnd)
            .Select(r => new TimeRecord(r.StartTime, r.EndTime, "OT_applied"))
            .ToTimeRangeCollection()
            .CropFromStart(manualMinutes);


        var storeValue = fallbackSlices.TotalMinutes >= shift.OverTimeThreshold ? fallbackSlices : TimeRange.Empty;
        this.RecordLedger(context, storeValue);
        return storeValue;
    }
    private DateTime GetOTStartTime(TimeContext context, OverTimeApplicationEntity application)
    {
        //TODO add logic here for  OvertimeInclusionPolicy
        return application.StartTime.AddMinutes(TimeAllowance.OTTimeCaptureAllowanceMinutes);
    }
}