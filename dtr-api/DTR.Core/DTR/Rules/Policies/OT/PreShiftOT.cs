namespace DTR.Core;

public class PreShiftOTUnrestrictedHandler : PreShiftOTHandler
{
    public TimeRange Calculate(TimeRange input, TimeContext context) => Process(input, context);
}
public class PreShiftOTHandler : OTComputationHandlerBase
{
    protected override bool CanHandle(TimeRange input, TimeContext context)
    {
        var shift = context.Payload.Data.CurrentShift;
        if (shift.ShiftType == TimeShiftType.FLEXI) return false;

        if (context.Payload.Data.Employee.ClientId.HasValue)
        {
            var clientpolicy = context.Payload.Provider.ClientPolicyProvider.GetPolicy(context.Payload.Data.Employee.ClientId.Value);
            if (clientpolicy != null)
            {
                return clientpolicy?.OvertimeInclusionPolicy == OvertimeInclusionPolicy.UseEarlyClockIn;
            }
        }

        return context.Payload.Data.CompanyPolicy.OTInclusionPolicy == OvertimeInclusionPolicy.UseEarlyClockIn;

    }
    protected override TimeRange Process(TimeRange input, TimeContext context)
    {
        var shift = context.Payload.Data.CurrentShift;
        var ledgerKey = TimeRangeLedger.CreateKey(nameof(PreShiftOTHandler), context);
        var blocked = context.Payload.Ledger.GetAllAllocatedExcept(ledgerKey);
        var usable = context.CanonicalTimeRange.TimeRecords
            .Exclude(blocked)
            .MergeOverlapping()
            ;
        return new CalculatePreShiftOverTime().ComputePreShiftOvertime(usable, shift!.StartTime);

    }
}
public class CalculatePreShiftOverTime
{
    public TimeRange ComputePreShiftOvertime(TimeRangeCollection records, DateTime shiftStartTime)
    {
        if (records == null || !records.Any())
            return new TimeRange(0, new TimeRangeCollection());

        var sorted = records.OrderBy(r => r.StartTime).ToList();
        var preShiftOT = new TimeRangeCollection();
        double totalPreShiftMinutes = 0;

        foreach (var r in sorted)
        {
            // Only consider records that start before the shift and end after or at the shift start
            if (r.StartTime < shiftStartTime && r.EndTime <= shiftStartTime)
            {
                var croppedEnd = shiftStartTime;
                var croppedStart = r.StartTime;
                var croppedDuration = (croppedEnd - croppedStart).TotalMinutes;

                if (croppedDuration > 0)
                {
                    var croppedRecord = TimeRecord.Set(croppedStart, croppedEnd, r.Tag + " [Pre-Shift OT]");
                    preShiftOT.Add(croppedRecord);
                    totalPreShiftMinutes += croppedDuration;
                }
            }
        }
        return new TimeRange(totalPreShiftMinutes, preShiftOT);
    }
}