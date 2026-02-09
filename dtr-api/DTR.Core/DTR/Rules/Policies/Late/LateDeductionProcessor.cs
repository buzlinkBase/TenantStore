namespace DTR.Core;

public interface ILateDeductionHandler {
    bool CanHandle(TimeRange lateSlice, TimeContext context);
    TimeRange Apply(TimeRange regTime, TimeRange lateSlice, TimeContext context);
}
public class LateDeductionProcessor
{
    private readonly List<ILateDeductionHandler> _handlers;

    public LateDeductionProcessor(IEnumerable<ILateDeductionHandler> handlers)
    {
        _handlers = handlers.ToList();
    }

    public TimeRange Process(TimeRange regTime, TimeRange lateSlice, TimeContext context)
    {
        foreach (var handler in _handlers)
        {
            if (handler.CanHandle(lateSlice, context))
                return handler.Apply(regTime, lateSlice, context);
        }
        // No handler matched — return original
        context.Payload.Ledger.RecordByTag("late", context, lateSlice);
        return regTime;
    }
}