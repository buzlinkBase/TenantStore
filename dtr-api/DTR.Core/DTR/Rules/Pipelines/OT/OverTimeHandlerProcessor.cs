namespace DTR.Core;

public class OverTimeHandlerProcessor
{
    private readonly TimeContext _context;
    public OverTimeHandlerProcessor(TimeContext context)
    {
        _context = context;
    }
    public TimeRange Handle(TimeRange canonicalTimeRange)
    {
        // Capture system OT first (needed for override OT to work)
        var systemHandler = new AutoComputedOTHandler(canonicalTimeRange, _context);
        systemHandler.Handle();

        // Run approval-based OT calc, chaining to system handler
        var appliedHandler = new ApprovalBasedOTHandler(canonicalTimeRange, _context);
        appliedHandler.SetNext(systemHandler);

        var otTimeRange = appliedHandler.Handle();

        _context.Payload.Ledger.RecordByTag("OT", _context, otTimeRange);
        return otTimeRange;
    }
}

