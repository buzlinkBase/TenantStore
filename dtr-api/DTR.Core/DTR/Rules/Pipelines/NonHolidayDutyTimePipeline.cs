namespace DTR.Core;

public class NonHolidayDutyTimePipeline
{
    private readonly TimeContext _context;

    public NonHolidayDutyTimePipeline(TimeContext context)
    {
        _context = context;
    }

    public TimeRange Apply(TimeRange cannonicalTimeRange)
    {
        var isHoliday = new IsHolidaySpec(HolidayType.LEGAL)
        .Or(new IsHolidaySpec(HolidayType.SPECIAL));

        var spec = new IsEligibleWorkHours();
        var workRange = new WorkTimePipeline(_context, spec)
            .Apply(cannonicalTimeRange);

        if (isHoliday.IsSatisfiedBy(cannonicalTimeRange, _context))  //if holiday return only nonholiday
        {
            //we may also check the   new IsHolTimeInDayType() here 
            var regKey = TimeRangeLedger.CreateKey("non_holiday_portion", _context);
            var cached = _context.Payload.Ledger.GetByKey(regKey);
            if (cached.Found)
            {
                var value = cached.Value ?? TimeRange.Empty;
                _context.Payload.Ledger.RecordByTag("final_RegularTime", _context, value);
                return value;
            }
            else
            {
                return TimeRange.Empty;
            }
        }

        //non holiday
        _context.Payload.Ledger.RecordByTag("final_RegularTime", _context, workRange);
        return workRange;

    }
}

//showHoldOn