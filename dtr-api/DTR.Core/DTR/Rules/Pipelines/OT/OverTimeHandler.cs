namespace DTR.Core;

public abstract class OverTimeHandler
{
    private OverTimeHandler _nextHandler;
    protected readonly TimeContext Context;
    protected readonly TimeRange Input;

    protected OverTimeHandler(TimeRange input, TimeContext context)
    {
        Context = context;
        Input = input;
    }
    protected virtual bool CanHandle()=>true;
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
    public void SetNext(OverTimeHandler nextHandler)
    {
        _nextHandler = nextHandler;
    }
}

