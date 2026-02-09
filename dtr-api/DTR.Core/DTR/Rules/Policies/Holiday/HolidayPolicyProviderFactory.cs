namespace DTR.Core;

public class HolidayPolicyProviderFactory
{
    public static IHolidayTimeProvider Create(TimeRange input, TimeContext context)
    {
        switch (context.Payload.Data.CompanyPolicy.HolidayTimeBasis)
        {
            case HolidayTimeBasis.BasedOnTimeInDayType:
                return new TimeInDayTypeProvider(input, context);
            case HolidayTimeBasis.BasedOnActualWorkHours:
                return new ActualWorkHoursProvider(input, context);
            default:
                throw new NotImplementedException("IHolidayTimeProvider");
        }
    }
}

public class TimeInDayTypeProvider : IHolidayTimeProvider
{
    private readonly TimeContext _context;
    public TimeInDayTypeProvider(TimeRange input, TimeContext context)
    {
        _context = context;
    }

    public TimeRange Calculate(HolidayType holidayType, TimeRange RegularTimeRange)
    {
        HolidayType value = (HolidayType)holidayType;
        _context.Payload.Ledger.RecordByTag($"holiday_portion_{value}", _context, RegularTimeRange);
        _context.Payload.Ledger.RecordByTag("non_holiday_portion", _context, TimeRange.Empty);
        return RegularTimeRange;
    }
}

public class ActualWorkHoursProvider : IHolidayTimeProvider
{
    private readonly TimeContext _context;
    public ActualWorkHoursProvider(TimeRange input, TimeContext context)
    {
        _context = context;
    }
    public TimeRange Calculate(HolidayType holidayType, TimeRange RegularTimeRange)
    {
        var holidaySlices = _context.Payload.Provider.HolidayProvider
            .GetHolidayDuringShift(holidayType
            , _context.Payload.Data.Employee
            , _context.Payload.Data.CurrentShift);

        var late = _context.Payload.Ledger.GetByTag("late", _context);
        //TODO exclude UT here or check if RegularTimeRange excludes the ut already
        //var ledgerKey = TimeRangeLedger.CreateKey("undertime", context);
        //if (payload.Ledger.GetByKey(ledgerKey, out var cached))
        //    return cached; 

        var usableRange = holidaySlices
            .MergeOverlapping()
            .Exclude(late.TimeRecords)
            ;

        var result = IntersectHolidaySlices(RegularTimeRange, usableRange, holidayType);
        return result;
    }

    private TimeRange IntersectHolidaySlices(TimeRange RegularTimeRange,
        TimeRangeCollection holidaySlices,
        HolidayType holidayType
        )
    {
        if (!holidaySlices.Any()) return TimeRange.Empty;

        var holidaySplice = holidaySlices
           .Intersect(RegularTimeRange.TimeRecords)
           .ToTimeRange();

        HolidayType value = (HolidayType)holidayType;
        _context.Payload.Ledger.RecordByTag($"holiday_portion_{value}", _context, holidaySplice);


        HolidayType specialType = HolidayType.SPECIAL;
        HolidayType legalType = HolidayType.LEGAL;
        var special = _context.Payload.Ledger.GetByTag($"holiday_portion_{specialType}", _context);
        var legal = _context.Payload.Ledger.GetByTag($"holiday_portion_{legalType}", _context);
        var allHolidayRange = special + legal;

        var nonHolidaySplice = RegularTimeRange
            .TimeRecords
            .MergeOverlapping()
            .Exclude(allHolidayRange.TimeRecords)
            .ToTimeRange()
            ;

        _context.Payload.Ledger.RecordByTag("non_holiday_portion", _context, nonHolidaySplice);
        return RegularTimeRange;

    }
}
public interface IHolidayTimeProvider
{
    TimeRange Calculate(HolidayType holidayType, TimeRange RegularTimeRange);
}

