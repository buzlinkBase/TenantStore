namespace DTR.Core;

public class HolidayDutyTimePipeline
{
    private readonly TimeContext _context;
    private readonly HolidayType _holidayType;

    public HolidayDutyTimePipeline(TimeContext context, HolidayType holidayType)
    {
        _context = context;
        _holidayType = holidayType;
    }

    public TimeRange Apply(TimeRange cannonicalTimeRange)
    {
        var spec = new IsHolidaySpec(_holidayType)
            .And(new IsEligibleWorkHours());

        var pipeLine = new WorkTimePipeline(_context, spec)
            .Apply(cannonicalTimeRange);

        HolidayType value = (HolidayType)_holidayType;
        var holidayRange = _context.Payload.Ledger.GetByTag($"holiday_portion_{value}", _context);

        if (_holidayType == HolidayType.SPECIAL)
        {
            var holidays = _context.Payload.Provider.HolidayProvider 
                .GetHolidayDuringDate(_holidayType, _context.Payload.Data.Employee, _context.Payload.Data.CurrentDate);

            var multiplier = holidays.Count();
            holidayRange.SetMetaData("SPHolidayCount", multiplier);
        }
        return holidayRange;
    }
}