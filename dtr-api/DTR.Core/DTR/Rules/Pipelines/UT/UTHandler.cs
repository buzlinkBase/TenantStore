namespace DTR.Core;

public abstract class UnderTimeHandler
{
    private UnderTimeHandler _nextHandler;
    protected readonly TimeContext Context;
    protected readonly TimeRange Input;

    protected UnderTimeHandler(TimeRange input, TimeContext context)
    {
        Context = context;
        Input = input;
    }
    protected virtual bool CanHandle() => true;
    protected abstract TimeRange Process();
    public TimeRange Handle()
    {
        if (CanHandle())
        {
            return Process();
        }
        if (_nextHandler != null)
        {
            return _nextHandler.Handle();
        }
        return new TimeRange();
    }
    public void SetNext(UnderTimeHandler nextHandler)
    {
        _nextHandler = nextHandler;
    }
}

public class AppliedUnderTimeHandler : UnderTimeHandler
{
    public AppliedUnderTimeHandler(TimeRange input, TimeContext context) : base(input, context)
    {
    }

    protected override bool CanHandle()
    {
        var spec = new IsAppliedUnderTimeSpec();
        return spec.IsSatisfiedBy(Input, Context);
    }

    protected override TimeRange Process()
    {
        var shift = Context.Payload.Data.CurrentShift;
        var underTimeInfo = Context.Payload.Provider.UTProvider
            .GetUT(shift.ShiftDate);

        return new TimeRange(underTimeInfo?.UTMinutes ?? 0);
    }
}
public class ActualUnderTimeHandler : UnderTimeHandler
{
    private readonly TimeContext _context;

    public ActualUnderTimeHandler(TimeRange input, TimeContext context) : base(input, context)
    {
        _context = context;
    }
    protected override TimeRange Process()
    {
        var spec = new IsUndertimeSpec()
           .And(new IsFixedScheduleSpec()); 
       
        var pipeline = new PolicyPipeline()
          .AddPolicy(new UndertimePolicy(spec));
        return pipeline.Execute(Context.CanonicalTimeRange, Context);
    }
}

