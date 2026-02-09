using Microsoft.EntityFrameworkCore.Metadata.Internal;
namespace DTR.Core;

public class HolidayPlus8TimePipeline
{
    private readonly TimeContext _context;
    private readonly IRuleSpecification _specification;
    public HolidayPlus8TimePipeline(TimeContext context, IRuleSpecification? ElibleSpec = null)
    {
        _context = context;
        _specification = ElibleSpec ?? 
            new IsHolidaySpec(HolidayType.LEGAL)
            .And(new IsEligibleForHoliday(HolidayType.LEGAL));
    }

    public TimeRange Apply(TimeRange cannonicalTimeRange)
    {
        //we may check here if double holiday and is working +16hrs automatically
        //TODO if working on double holiday employee may still received 300%
        //regardless if they are absent in  prior days
        //our current rule is that employee need to be present in prio day before the holiday

        var holidayKey = TimeRangeLedger.CreateKey<HolidayPlus8TimePipeline>(_context);
        var cached = _context.Payload.Ledger.GetByKey(holidayKey);
        if (cached.Found)
        {
            return cached.Value ?? TimeRange.Empty;
        }
        var currentDate = _context.Payload.Data.CurrentShift.ShiftDate;
        var _holidayType = HolidayType.LEGAL;

        //Get current date holiday only 
        var holidays = _context.Payload.Provider.HolidayProvider
           .GetHolidayDuringDate(_holidayType, _context.Payload.Data.Employee, currentDate)
           ;

        if (!holidays.Any()) return TimeRange.Empty;

        var satisfied = _specification.IsSatisfiedBy(cannonicalTimeRange, _context);
        if (!satisfied) return TimeRange.Empty;
        var defaultMinutes = satisfied 
            ? (_context.Payload.Data.CurrentShift.MaxWorkingMinutes) 
            : 0;

        var multiplier = holidays.Count();
        var totalMinutes = defaultMinutes * multiplier;
        var finalRange = new TimeRange(totalMinutes);
        var holCreditOption = _context.Payload.Data.CompanyPolicy.HolidayColumnPresentation;

        if (holCreditOption == HolidayCreditMode.NoCredit)
        {
            finalRange = TimeRange.Empty;
        }

        finalRange.SetMetaData("HolidayCount", multiplier);
        return finalRange;

    }
}