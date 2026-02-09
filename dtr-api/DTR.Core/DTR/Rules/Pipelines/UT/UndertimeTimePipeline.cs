namespace DTR.Core;

public class UndertimeTimePipeline
{
    private readonly TimeContext _context;

    public UndertimeTimePipeline(TimeContext context)
    {
        _context = context;
    }

    public TimeRange Apply(TimeRange cannonicalTimeRange)
    {
        var applied = new AppliedUnderTimeHandler(cannonicalTimeRange, _context);
        var actual  = new ActualUnderTimeHandler(cannonicalTimeRange, _context);
        applied.SetNext(actual);
        return applied.Handle(); 
    }
}


