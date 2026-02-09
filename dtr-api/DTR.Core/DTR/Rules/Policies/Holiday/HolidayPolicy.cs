namespace DTR.Core;

public class  HolidayPolicy : ConditionalPolicyBase
{
    private readonly HolidayType _holidayType;
    public HolidayPolicy(IRuleSpecification specification,HolidayType holidayType, SpecFailureBehavior  behavior) : base(specification, behavior)
    {
        _holidayType = holidayType;
    }
    protected override TimeRange ApplyIfSatisfied(TimeRange regularTimeRange, TimeContext context)
    {
        if (regularTimeRange == null || regularTimeRange.TotalMinutes==0)
        {
            return TimeRange.Empty;
        }

        var provider = HolidayPolicyProviderFactory.Create(regularTimeRange, context);
        var timeRange= provider.Calculate(_holidayType, regularTimeRange);

        return regularTimeRange;

    }
}
