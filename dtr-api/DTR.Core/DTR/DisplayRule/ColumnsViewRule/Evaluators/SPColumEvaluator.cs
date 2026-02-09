namespace DTR.Core;

internal class SPColumEvaluator : IColumnEvaluator
{
    public SPColumEvaluator()
    {
    }
    public TimeRange ApplyRules(DisplayContext context)
    {
        return context.PipeLineResult.SpecialHoliday;
    }
}