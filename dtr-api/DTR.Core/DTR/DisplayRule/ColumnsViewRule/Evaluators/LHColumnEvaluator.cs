namespace DTR.Core;

internal class LHColumnEvaluator : IColumnEvaluator
{
    public LHColumnEvaluator()
    {
    }
    public TimeRange ApplyRules(DisplayContext context)
    {
        var Plus8 = context.PipeLineResult.Plus8 ;
        var holiday = context.PipeLineResult.LegalHoliday;
        var holOption = new IsShowWorkOnHolidayInRegColumn().IsSatisfiedBy(context);
        if (!holOption)
        {
            return new TimeRange(Plus8.TotalMinutes+holiday.TotalMinutes,
                holiday.TimeRecords);
        }
        return Plus8;
    }
}