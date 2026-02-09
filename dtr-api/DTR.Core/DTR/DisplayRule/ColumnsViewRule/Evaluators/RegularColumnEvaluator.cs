namespace DTR.Core;

internal class RegularColumnStrategy : IColumnEvaluator
{
    public RegularColumnStrategy()
    {
    }
    public TimeRange ApplyRules(DisplayContext context)
    {
        var regular = context.PipeLineResult.Regular;
        var holiday = context.PipeLineResult.LegalHoliday;

        //On holidays regular here is either 0 or non_holiday_workTime
        //if ActualWorkHours is selected
        if (new IsShowWorkOnHolidayInRegColumn().IsSatisfiedBy(context))
        {
            return regular + holiday;
        }
        return regular;
    }
}
