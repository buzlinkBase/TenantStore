namespace DTR.Core;

public class IsHolidaySpec : IRuleSpecification
{
    private readonly HolidayType _holidayType;
    public IsHolidaySpec(HolidayType holidayType)
    {
        _holidayType = holidayType;
    }
    public bool IsSatisfiedBy(TimeRange input, TimeContext context)
    {
        var key = SpecEvaluationCache.CreateKey(nameof(IsHolidaySpec) + (HolidayType)_holidayType, context);
        var cached = context.Payload.SharedSpecCache.GetByKey(key);
        if (cached.Found)
            return cached.Value;

        var eval = HolidaySpecComputationBasesFactory.Create(input, context);
        var data= eval.Evaluate(_holidayType);
        context.Payload.SharedSpecCache.Record(key, data);
        return data;

    }
}

public class IsCurrentShiftPresent : IRuleSpecification
{
    public bool IsSatisfiedBy(TimeRange input, TimeContext context)
    {
        return context.Payload.Data.CurrentShift!=null;
    }
}


public class HolidaySpecComputationBasesFactory
{
    public static IHolidaySpecEvaluator Create(TimeRange input, TimeContext context)
    {
        switch (context.Payload.Data.CompanyPolicy.HolidayTimeBasis)
        {
            case HolidayTimeBasis.BasedOnTimeInDayType:
                return new TimeInDayTypeEvaluator(input, context);
            case HolidayTimeBasis.BasedOnActualWorkHours:
                return new ActualWorkEvaluator(input, context);
            default:
                throw new NotImplementedException("IHolidaySpecEvaluator");
        }
    }
}

public class TimeInDayTypeEvaluator : IHolidaySpecEvaluator
{
    private readonly TimeContext _context;
    public TimeInDayTypeEvaluator(TimeRange input, TimeContext context)
    {
        _context = context;
    }
    public bool Evaluate(HolidayType holidayType)
    {
        var payload = _context.Payload;
        var shift = payload.Data.CurrentShift;
        var key = SpecEvaluationCache.CreateKey(nameof(IsHolidaySpec) + holidayType.ToString(),
            _context.Payload.Data.CurrentDate, _context.Payload.Data.Employee.Id);

        var cached = _context.Payload.SharedSpecCache.GetByKey(key);
        if (cached.Found)
            return cached.Value;

        return payload.Provider.HolidayProvider
            .GetHolidayDuringDate(holidayType, _context.Payload.Data.Employee, payload.Data.CurrentShift.ShiftDate)
            .Any();
    }
}
public class ActualWorkEvaluator : IHolidaySpecEvaluator
{
    private readonly TimeContext _context;
    public ActualWorkEvaluator(TimeRange input, TimeContext context)
    {
        _context = context;
    }
    public bool Evaluate(HolidayType holidayType)
    {
        var payload = _context.Payload;
        var shift = payload.Data.CurrentShift;
        var key = SpecEvaluationCache.CreateKey(nameof(IsHolidaySpec) + holidayType.ToString(),
            _context.Payload.Data.CurrentDate, _context.Payload.Data.Employee.Id);

        var cached = _context.Payload.SharedSpecCache.GetByKey(key);
        if (cached.Found)
            return cached.Value;


        return payload.Provider.HolidayProvider
            .GetHolidayDuringShift(holidayType, _context.Payload.Data.Employee, payload.Data.CurrentShift)
            .Any();
    }
}
public interface IHolidaySpecEvaluator
{
    bool Evaluate(HolidayType holidayType);
}
