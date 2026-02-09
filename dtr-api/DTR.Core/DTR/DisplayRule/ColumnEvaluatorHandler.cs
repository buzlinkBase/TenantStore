namespace DTR.Core;

public interface IColumnEvaluator
{
    TimeRange ApplyRules(DisplayContext context);
}
public class ColumnEvaluatorHandler
{
    private readonly IColumnEvaluator _strategy;
    public ColumnEvaluatorHandler(IColumnEvaluator strategy)
    {
        _strategy = strategy;
    }
    public TimeRange Handle(DisplayContext context)
    {
        return _strategy.ApplyRules(context);
    }
}