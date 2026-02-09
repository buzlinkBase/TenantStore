namespace DTR.Core;

public class AutoComputeOvertimePolicy : ConditionalPolicyBase
{
    private readonly PostShiftOTHandler _postShiftHandler;
    private readonly PreShiftOTHandler _preShiftHandler;
    private readonly OutsideShiftOverTimeHandler _outsideShiftHandler;

    public AutoComputeOvertimePolicy(IRuleSpecification specification) : base(specification,SpecFailureBehavior.ReturnEmpty) {
        _postShiftHandler = new PostShiftOTHandler();
        _preShiftHandler = new PreShiftOTHandler();
        _outsideShiftHandler = new OutsideShiftOverTimeHandler();
        _postShiftHandler.SetNextHandler(_preShiftHandler);
        _preShiftHandler.SetNextHandler(_outsideShiftHandler);
    }
    protected override TimeRange ApplyIfSatisfied(TimeRange input, TimeContext context)
    {
        var key = TimeRangeLedger.CreateKey<AutoComputeOvertimePolicy>(context);
        var cached = context.Payload.Ledger.GetByKey(key);
        if (cached.Found)
        {
            var value = cached.Value ?? TimeRange.Empty;
            return value;
        } 
 

        var shift = context.Payload.Data.CurrentShift;
        var timeRange = _postShiftHandler.Handle(input, context);

        var storeValue = timeRange.TotalMinutes >= shift.OverTimeThreshold
            ? timeRange
            : TimeRange.Empty;

        context.Payload.Ledger.Record(key, storeValue);
        return storeValue;
    }
}

public abstract class OTComputationHandlerBase
{
    private OTComputationHandlerBase? _nextHandler;
    protected abstract bool CanHandle(TimeRange input, TimeContext context);
    public TimeRange Handle(TimeRange input, TimeContext context)
    {
        if (CanHandle(input, context))
        {
            return Process(input, context);
        }
        return _nextHandler?.Handle(input, context) ?? TimeRange.Empty;
    }

    public void SetNextHandler(OTComputationHandlerBase handler) => _nextHandler = handler;
    protected abstract TimeRange Process(TimeRange input, TimeContext context);
}
