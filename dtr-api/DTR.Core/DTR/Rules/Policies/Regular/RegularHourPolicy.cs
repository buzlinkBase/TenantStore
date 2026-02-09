 
namespace DTR.Core;

public class RegularHourPolicy : ConditionalPolicyBase
{
    public RegularHourPolicy(IRuleSpecification ruleSpecification) : base(ruleSpecification)
    {
    }
    protected override TimeRange ApplyIfSatisfied(TimeRange input, TimeContext context)
    {
        var payload = context.Payload;
        var ledgerKey = this.CreateLedgerKey(context);
        var cached = context.Payload.Ledger.GetByKey(ledgerKey);
        if (cached.Found)
        {
            var value = cached.Value ?? TimeRange.Empty;
            return value;
        } 

        var shift = payload.Data.CurrentShift;

        var raw = context.CanonicalTimeRange.TimeRecords;
        var blocked = payload.Ledger.GetAllAllocatedExcept(ledgerKey);

        //exclude scheduled breaks
        var cleanAfterBreak = ExcludeBreakTime(context, raw.Exclude(blocked), shift);
        var usable = ComputeUsableTime(context, cleanAfterBreak, shift);

        //consider manually set UNDERTIME
        var applied = new AppliedUnderTimeHandler(context.CanonicalTimeRange, context);
        var overrideUt = Math.Max(applied.Handle().TotalMinutes, 0);

        GracePeriodAdjustment.SetGracePeriod(context, usable);//set grace period
        var shifMinutes = TimeAllocationFactory.Create(input, context)
            .GetMaximumMinutes();

        var MaxMinutes = Math.Max(shifMinutes - overrideUt, 0);
        var capped = usable.CapAndCrop(shift, MaxMinutes);
        payload.Ledger.Record(ledgerKey, capped);
        return capped;

    }

    private TimeRangeCollection ComputeUsableTime(TimeContext context, TimeRangeCollection usable, CurrentShift shift)
    {

        //if (shift.ShiftType == TimeShiftType.FLEXI || shift.LunchBreakOption != PunchMode.PAID_BREAK_COMPRESS) return usable;
        if (shift.ShiftType == TimeShiftType.FLEXI) return usable;
        //for paid compress break Time
        //extract breaks
        var xtractor = BreakExtractorFactory.Create(context);
        var allbreaks = new TimeRangeCollection();
        allbreaks.AddRange(xtractor.SelectMany(x => x.Extract(usable, shift)));
        allbreaks = allbreaks.MergeOverlapping().Retag("clean_Breaks");
        if (!allbreaks.Any()) return usable;

        //compute breaks window
        var ledgerKey = TimeRangeLedger.CreateKey("allbreaks", context);
        context.Payload.Ledger.Record(ledgerKey, allbreaks.ToTimeRange());

        double paidbreaks = shift.LunchBreakOption == BreakMode.PAID_BREAK
           ? context.Payload.Data.CurrentShift.LunchBreakDurationMinutes
           : 0;

        if (shift.WithAMBreak == BreakMode.PAID_BREAK && shift.AMBreakStartTime.HasValue && shift.AMBreakEndTime.HasValue)
        {
            paidbreaks += (shift.AMBreakEndTime.Value - shift.AMBreakStartTime.Value).TotalMinutes;
        }

        if (shift.WithPMBreakTime == BreakMode.PAID_BREAK && shift.PMBreakStartTime.HasValue && shift.PMBreakEndTime.HasValue)
        {
            paidbreaks += (shift.PMBreakEndTime.Value - shift.PMBreakStartTime.Value).TotalMinutes;
        }

        var paidBreak = PaidBreakCalculator.ComputePaidBreaks(allbreaks, paidbreaks);
        context.Payload.Ledger.RecordByTag("paidBreak",context, paidBreak.ToTimeRange());
        var newUsable = (usable.ToTimeRange() + paidBreak.ToTimeRange()).TimeRecords;
        return newUsable;
    }

    private TimeRangeCollection ExcludeBreakTime(TimeContext context, TimeRangeCollection usable, CurrentShift shift)
    {
        if (shift.ShiftType == TimeShiftType.FLEXI) return usable;
        if (shift.WithAMBreak == BreakMode.UNPAID_BREAK && shift.AMBreakStartTime.HasValue && shift.AMBreakEndTime.HasValue)
        {
            var tr = new TimeRangeCollection()
            {
                new TimeRecord(shift.AMBreakStartTime.Value,shift.AMBreakEndTime.Value,"morning_break_period")
            };
            var key = TimeRangeLedger.CreateKey("morning_break_period", context);
            context.Payload.Ledger.Record(key, tr.ToTimeRange());
            usable = usable.Exclude(tr);
        }
        if (shift.WithPMBreakTime == BreakMode.UNPAID_BREAK && shift.PMBreakStartTime.HasValue && shift.PMBreakEndTime.HasValue)
        {
            var tr = new TimeRangeCollection()
            {
                new TimeRecord(shift.PMBreakStartTime.Value,shift.PMBreakEndTime.Value,"pm_break_period")
            };
            var key = TimeRangeLedger.CreateKey("pm_break_period", context);
            context.Payload.Ledger.Record(key, tr.ToTimeRange());
            usable = usable.Exclude(tr);
        }
        //LUNCH BREAK
        if (shift.LunchBreakOption == BreakMode.UNPAID_BREAK)
        {
            if (shift.LunchStartTime.HasValue && shift.LunchEndTime.HasValue)
            {
                var tr = new TimeRangeCollection()
                    {
                        new TimeRecord(shift.LunchStartTime.Value,shift.LunchEndTime.Value,"lunch_break_period_use_time")
                    };
                var key = TimeRangeLedger.CreateKey("lunch_break_period_use_time", context);
                context.Payload.Ledger.Record(key, tr.ToTimeRange());
                usable = usable.Exclude(tr);
            }
            else if (shift.LunchBreakDurationMinutes > 0)//fallback if only breakduration is set
            {
                var tr = new TimeRangeCollection()
                    {
                        new TimeRecord(shift.StartTime.AddHours(4),shift.StartTime.AddHours(4).AddMinutes(shift.LunchBreakDurationMinutes),"lunch_break_period_use_computeBreak")
                    };
                var key = TimeRangeLedger.CreateKey("break_period", context);
                context.Payload.Ledger.Record(key, tr.ToTimeRange());
                usable = usable.Exclude(tr);
            }
        }

        //This is a fallback for prior settings that has 2punches
        if (shift.LunchBreakOption == BreakMode.NONE
            && ((shift.EndTime - shift.StartTime).TotalMinutes > shift.MaxWorkingMinutes)
            && shift.LunchBreakDurationMinutes > 0)
        {
            var breaks = Math.Min(shift.LunchBreakDurationMinutes, Math.Max(0, (shift.EndTime - shift.StartTime).TotalMinutes - shift.MaxWorkingMinutes));
            if (breaks > 0)
            {
                var tr = new TimeRangeCollection()
                    {
                        new TimeRecord(shift.StartTime.AddHours(4),shift.StartTime.AddHours(4).AddMinutes(breaks),"No_lunch_break_gt_480MinutesShift")
                    };
                var key = TimeRangeLedger.CreateKey("No_lunch_break_gt_480MinutesShift", context);
                context.Payload.Ledger.Record(key, tr.ToTimeRange());
                usable = usable.Exclude(tr);
            }
        }
        return usable;
    }
}


public class GracePeriodAdjustment
{
    public static void SetGracePeriod(TimeContext context, TimeRangeCollection usable)
    {
        //Alter the first log to cater grace period
        if (!usable.Any()) return;
        var payload = context.Payload;
        var shift = payload.Data.CurrentShift;
        var firstPair = usable[0];
        var withGraceTime = firstPair.StartTime.AddMinutes(shift.GracePeriodMinutes * -1);
        if (firstPair.StartTime > shift.StartTime && withGraceTime <= shift.StartTime)
        {
            var newStartTime = firstPair.StartTime.AddMinutes(shift.GracePeriodMinutes * -1);
            usable[0] = new TimeRecord(newStartTime, firstPair.EndTime, "Grace Included");
        }
    }
}
public interface ITimeAllocation
{
    double GetMaximumMinutes();
}
public class TimeAllocationFactory
{
    public static ITimeAllocation Create(TimeRange input, TimeContext context)
    {
        switch (context.Payload.Data.CurrentShift.ShiftType)
        {
            case TimeShiftType.FIXED:
                return new FixShiftAllocation(input, context);
            case TimeShiftType.FLEXI:
                return new FlexiShiftAllocation(input, context);
            default:
                throw new NotImplementedException("TimeAllocationFactory");
        }
    }
}
public class FixShiftAllocation : ITimeAllocation
{
    private readonly TimeRange _input;
    private readonly TimeContext _context;

    public FixShiftAllocation(TimeRange input, TimeContext context)
    {
        _input = input;
        _context = context;
    }
    public double GetMaximumMinutes()
    {
        var shift = _context.Payload.Data.CurrentShift;
        //var shiftTotalMinutes = TimeRangeCalculator.GetTotalMinutes(shift.StartTime, shift.EndTime);
        //shiftTotalMinutes -= shift.LunchBreakDurationMinutes;
        return shift.MaxWorkingMinutes;
        //return TimeRangeCalculator.GetTotalMinutes(shift.StartTime, shift.EndTime);
        //return Math.Min(480, TimeRangeCalculator.GetTotalMinutes(shift.StartTime, shift.EndTime));
    }
}
public class FlexiShiftAllocation : ITimeAllocation
{
    private readonly TimeRange _input;
    private readonly TimeContext _context;

    public FlexiShiftAllocation(TimeRange input, TimeContext context)
    {
        _input = input;
        _context = context;
    }

    public double GetMaximumMinutes()
    {
        var shift = _context.Payload.Data.CurrentShift;
        return shift.MaxWorkingMinutes;
    }
}
