namespace DTR.Core;

public interface IHolidayOTSliceProvider
{
    public TimeRange Calculate(HolidayType holidayType);
}
public class HolidayOTFactory
{
    public static IHolidayOTSliceProvider Create(TimeContext context, TimeRange OT)
    {
        switch (context.Payload.Data.CompanyPolicy.HolidayTimeBasis)
        {
            case HolidayTimeBasis.BasedOnTimeInDayType:
                return new TimeInDayOTHourProvider(context, OT);
            case HolidayTimeBasis.BasedOnActualWorkHours:
                return new ActualDayOTHourProvider(context, OT);
            default:
                throw new NotImplementedException();
        }
    }
}

public class TimeInDayOTHourProvider : IHolidayOTSliceProvider
{
    private readonly TimeContext _context;
    private readonly TimeRange _oT;

    public TimeInDayOTHourProvider(TimeContext context, TimeRange OT)
    {
        _context = context;
        _oT = OT;
    }
    public TimeRange Calculate(HolidayType holidayType)
    {
        return _oT;
    }
}
public class ActualDayOTHourProvider : IHolidayOTSliceProvider
{
    private readonly TimeContext _context;
    private readonly TimeRange _oT;
    public ActualDayOTHourProvider(TimeContext context, TimeRange OT)
    {
        _context = context;
        _oT = OT;
    }
    public TimeRange Calculate(HolidayType holidayType)
    {
        //get holiday Range Including OT
        var key = TimeRangeLedger.CreateKey($"ACTUAL_OT_{holidayType}", _context);
        var cached = _context.Payload.Ledger.GetByKey(key);
        if (cached.Found)
        {
            return cached.Value ?? TimeRange.Empty;
        }

        //get holiday range
        var shifFrom = _context.CanonicalTimeRange.TimeRecords.MinBy(x => x.StartTime)?.StartTime ?? DateTime.MinValue;
        var toDate = _context.CanonicalTimeRange.TimeRecords.MaxBy(x => x.EndTime)?.EndTime ?? DateTime.MinValue;
        var shift = new CurrentShift() { StartTime = shifFrom, EndTime = toDate };

        var holiday = _context.Payload.Provider.HolidayProvider
            .GetHolidayDuringShift(holidayType, _context.Payload.Data.Employee, shift)
            .ToTimeRange();

        var timeRange = HolidayOTCalculator.Calculate(_oT, holiday);

        if (holiday.TotalMinutes > 0)
        {
            var exclude_key = TimeRangeLedger.CreateKey($"REGULAR_OT_ACTUAL_{holidayType}", _context);
            var exlude = _oT.TimeRecords.Exclude(timeRange.TimeRecords)
                .ToTimeRange();
            _context.Payload.Ledger.Record(exclude_key, exlude);
        }

        _context.Payload.Ledger.Record(key, timeRange);
        return timeRange;

    }
}
