 
namespace DTR.Core;

public class OutsideShiftOverTimeHandler : OTComputationHandlerBase
{
    protected override bool CanHandle(TimeRange input, TimeContext context)
    {
        if (context.Payload.Data.Employee.ClientId.HasValue)
        {
            var clientpolicy = context.Payload.Provider.ClientPolicyProvider.GetPolicy(context.Payload.Data.Employee.ClientId.Value);
            if (clientpolicy != null)
            {
                return clientpolicy?.OvertimeInclusionPolicy == OvertimeInclusionPolicy.UseAllExcessOver8Hours;
            }
        }
        return context.Payload.Data.CompanyPolicy.OTInclusionPolicy == OvertimeInclusionPolicy.UseAllExcessOver8Hours;
    }

    protected override TimeRange Process(TimeRange input, TimeContext context)
    {

        var shift = context.Payload.Data.CurrentShift;
        var preOTTimeRange = new PreShiftOTUnrestrictedHandler().Calculate(input, context);
        var postOTTimeRange = new PostShiftOTUnrestrictedHandler().Calculate(input, context);
        return preOTTimeRange + postOTTimeRange;

    }
}


//public class OutsideShiftOvertimeCalculator
//{
//    public TimeRange ComputeOvertimeOutsideShift(TimeRangeCollection records, DateTime shiftStartTime, DateTime shiftEndTime)
//    {
//        if (records == null || !records.Any())
//            return new TimeRange(0, new TimeRangeCollection());

//        var sorted = records.OrderBy(r => r.StartTime).ToList();
//        var overtimeRecords = new TimeRangeCollection();
//        double totalOvertimeMinutes = 0;

//        foreach (var r in sorted)
//        {
//            // Pre-shift OT: starts before shiftStartTime and ends after it
//            if (r.StartTime < shiftStartTime && r.EndTime > shiftStartTime)
//            {
//                var croppedStart = r.StartTime;
//                var croppedEnd = shiftStartTime;
//                var duration = (croppedEnd - croppedStart).TotalMinutes;

//                if (duration > 0)
//                {
//                    var cropped = TimeRecord.Set(croppedStart, croppedEnd, r.Tag + " [Pre-Shift OT]");
//                    overtimeRecords.Add(cropped);
//                    totalOvertimeMinutes += duration;
//                }
//            }
//            else if (r.EndTime <= shiftStartTime)
//            {
//                var duration = r.TotalMinutes();
//                overtimeRecords.Add(r.Tag(r.Tag + " [Pre-Shift OT]"));
//                totalOvertimeMinutes += duration;
//            }

//            // Post-shift OT: starts before shiftEndTime and ends after it
//            else if (r.StartTime < shiftEndTime && r.EndTime > shiftEndTime)
//            {
//                var croppedStart = shiftEndTime;
//                var croppedEnd = r.EndTime;
//                var duration = (croppedEnd - croppedStart).TotalMinutes;

//                if (duration > 0)
//                {
//                    var cropped = TimeRecord.Set(croppedStart, croppedEnd, r.Tag + " [Post-Shift OT]");
//                    overtimeRecords.Add(cropped);
//                    totalOvertimeMinutes += duration;
//                }
//            }
//            else if (r.StartTime >= shiftEndTime)
//            {
//                var duration = r.TotalMinutes();
//                overtimeRecords.Add(r.Tag(r.Tag + " [Post-Shift OT]"));
//                totalOvertimeMinutes += duration;
//            }
//        }

//        return new TimeRange(totalOvertimeMinutes, overtimeRecords);

//    }
//}