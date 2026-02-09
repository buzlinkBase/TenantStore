namespace DTR.Core;

internal static class CommonExtensions
{
    internal static bool IsHoliday(this TimeContext context)
    {
        var holiday = new IsHolidaySpec(HolidayType.LEGAL)
        .Or(new IsHolidaySpec(HolidayType.SPECIAL))
        ;
        return holiday.IsSatisfiedBy(context.CanonicalTimeRange, context);
    }
    internal static bool IsLegalHoliday(this TimeContext context)
    {
        var holiday = new IsHolidaySpec(HolidayType.LEGAL) ;
        return holiday.IsSatisfiedBy(context.CanonicalTimeRange, context);
    }
    internal static bool IsSpecialHoliday(this TimeContext context)
    {
        var holiday = new IsHolidaySpec(HolidayType.SPECIAL);
        return holiday.IsSatisfiedBy(context.CanonicalTimeRange, context);
    }
}
