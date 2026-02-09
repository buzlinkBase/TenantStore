namespace DTR.Core;

public class IsHolTimeInDayType : IRuleSpecification
{
    public bool IsSatisfiedBy(TimeRange input, TimeContext context)
    {
        return context.Payload.Data.CompanyPolicy.HolidayTimeBasis == HolidayTimeBasis.BasedOnTimeInDayType;
    }
}
