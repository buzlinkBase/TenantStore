namespace DTR.Core;

internal class OTColumnEvaluator : IColumnEvaluator
{
    public OTColumnEvaluator()
    {
    }
    public TimeRange ApplyRules(DisplayContext context)
    {
        return context.PipeLineResult.OT;
    }
}