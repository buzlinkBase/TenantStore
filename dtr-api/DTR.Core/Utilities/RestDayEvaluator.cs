namespace DTR.Core;


public class RestDayEvaluator : IDutyDayEvaluator
{
    public TimeRange Evaluate(TimeRange range, TimeContext context)
        => RestDayChecker.IsRestDay(context.Payload) ? range : TimeRange.Empty;
}

public class RegularDayEvaluator : IDutyDayEvaluator
{
    public TimeRange Evaluate(TimeRange range, TimeContext context)
        => !RestDayChecker.IsRestDay(context.Payload) ? range : TimeRange.Empty;
}

public class RegularOTEvaluator : IDutyDayEvaluator
{
    public TimeRange Evaluate(TimeRange range, TimeContext context)
    {
        return new NonHolidayEvaluator().Evaluate(range, context);
    }
}

public class NonHolidayEvaluator  : IDutyDayEvaluator
{
    public TimeRange Evaluate(TimeRange range, TimeContext context)
    {
        return !context.IsHoliday() ? range : TimeRange.Empty;
    }
}


public class LegalHolidayOTEvaluator : IDutyDayEvaluator
{
    public TimeRange Evaluate(TimeRange range, TimeContext context)
    {
        return context.IsLegalHoliday() ? range : TimeRange.Empty;
    }
}
public class SpecialHolidayOTEvaluator : IDutyDayEvaluator
{
    public TimeRange Evaluate(TimeRange range, TimeContext context)
    {
        return context.IsSpecialHoliday() ? range : TimeRange.Empty;
    }
}

public class NightDiffEvaluator : IDutyDayEvaluator
{
    public TimeRange Evaluate(TimeRange range, TimeContext context)
    {
        return context.Payload.Data.CompanyPolicy.NightDiffThreshold <= range.TotalMinutes
            ? range : TimeRange.Empty;
    }
}



public static class DutyTypeMapFactory
{
    public static readonly Dictionary<DayType, IDutyDayEvaluator> Create = new()
    {
        { DayType.RESTDAY, new RestDayEvaluator() },
        { DayType.REGULAR, new RegularDayEvaluator()},
        { DayType.REGULAR_OVERTIME, new RegularOTEvaluator()},
        { DayType.LEGAL_HOLIDAY_OVERTIME, new LegalHolidayOTEvaluator()},
        { DayType.SPECIAL_HOLIDAY_OVERTIME, new SpecialHolidayOTEvaluator()},
        { DayType.NIGHT_DIFF, new NightDiffEvaluator()},
        { DayType.NONHOLIDAY, new NonHolidayEvaluator()},
    };
}

public interface IDutyDayEvaluator
{
    TimeRange Evaluate(TimeRange range, TimeContext context);
}

