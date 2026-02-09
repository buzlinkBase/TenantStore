namespace DTR.Core;

public class LateTimePipeline
{
    private readonly TimeContext _context;
    public LateTimePipeline(TimeContext context)
    {
        _context = context;
    }
    public TimeRange Apply(TimeRange cannonicalTimeRange)
    {
        //only get result since pipeline is already run previously in WorkTimePipeline
        return _context.Payload.Ledger.GetByTag("late", _context);
    }
}
