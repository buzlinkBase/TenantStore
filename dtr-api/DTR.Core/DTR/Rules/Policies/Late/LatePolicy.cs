 
using Microsoft.EntityFrameworkCore;
namespace DTR.Core;

public class LatePolicy : ConditionalPolicyBase
{
    public LatePolicy(IRuleSpecification spec, SpecFailureBehavior behavior = SpecFailureBehavior.ReturnInput) : base(spec, behavior)
    {
    }
    protected override TimeRange ApplyIfSatisfied(TimeRange regTime, TimeContext context)
    {
        var payload = context.Payload;
        var shift = payload.Data.CurrentShift;
        var regkey = this.CreateLedgerKey(context);
        var cached = context.Payload.Ledger.GetByKey(regkey);
        if (cached.Found)
        {
            var value = cached.Value ?? TimeRange.Empty;
            return value;
        }
        var shiftStart = shift.StartTime;
        var endShift = shift.EndTime;
        var firsthalfShiftEnd = endShift;

        if (shift.LunchBreakOption == BreakMode.UNPAID_BREAK && shift.LunchStartTime.HasValue)
        {
            firsthalfShiftEnd = shift.LunchStartTime.Value;
        }
        else if (shift.LunchBreakOption == BreakMode.UNPAID_BREAK && !shift.LunchStartTime.HasValue)
        {
            var half = (shift.MaxWorkingMinutes / 2) - (shift.LunchBreakDurationMinutes / 2);
            firsthalfShiftEnd = shift.StartTime.AddMinutes(half);
        }

        //if (shift.PunchMode == PunchMode.FOUR_PUNCHES && shift.LunchStartTime.HasValue)
        //{
        //    firsthalfShiftEnd = shift.LunchStartTime.Value;
        //}
        //else if (shift.PunchMode == PunchMode.TWO_PUNCHES)
        //{
        //    firsthalfShiftEnd = shiftStart.AddHours(4);
        //}

        var firstLog = regTime.TimeRecords
            .Where(x => x.StartTime <= firsthalfShiftEnd)//only compute late if below first half else ut
            .MinBy(r => r.StartTime)?.StartTime;

        var maxRequiredWH = payload.Data.CurrentShift.MaxWorkingMinutes;
        var maxWH = regTime.TotalMinutes;
        var lacking = Math.Max(0, maxRequiredWH - regTime.TotalMinutes);

        if (firstLog is null
            || firstLog <= shiftStart
            || firstLog >= endShift
            || maxWH >= maxRequiredWH
            || lacking <= 0
            )
        {
            //no lates
            payload.Ledger.RecordByTag("late", context, TimeRange.Empty);
            return regTime;
        }

        //if first8 applied get the remaining diff time from the total reg minutes
        var lateEndLog = payload.Ledger.GetByTag("RegularTimeTopUp", context)
            .IsEmpty() ? firstLog.Value : shiftStart.AddMinutes(lacking);

        var lateSlice = new TimeRangeCollection { new TimeRecord(shiftStart, lateEndLog, "late_slice_entry") }.ToTimeRange();

        var processor = new LateDeductionProcessor(new ILateDeductionHandler[]
        {
            new WholeDayLateHandler(),
            new HalfDayLateHandler()
        });

        var result = processor.Process(regTime, lateSlice, context);
        return result;
    }
}