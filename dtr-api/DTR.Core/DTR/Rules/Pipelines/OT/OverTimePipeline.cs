namespace DTR.Core;

public class OverTimePipeline
{
    private readonly TimeContext _context;
    public OverTimePipeline(TimeContext context)
    {
        _context = context;
    }
    public TimeRange Apply(TimeRange cannonicalTimeRange)
    {
        
        var ledger = _context.Payload.Ledger;
        var key = TimeRangeLedger.CreateKey("FinalOT", _context);
        var cached = _context.Payload.Ledger.GetByKey(key);
        if (cached.Found)
        {
            return cached.Value ?? TimeRange.Empty;
        } 

        //check if rendered 8hr
        if (IsRequire8HourWork())
        {
            //TODO update settings change label to workhours
            //check if employee rendered the required hours
            var workTime = _context.Payload.Ledger.GetByTag("work_time", _context);
            var reach8hr = workTime.TotalMinutes >= _context.Payload.Data.CurrentShift.MaxWorkingMinutes;
            if (!reach8hr)
            {
                return TimeRange.Empty;
            }
        }

        //get actual OT
        return new OverTimeHandlerProcessor(_context).Handle(cannonicalTimeRange);

    }
    private bool IsRequire8HourWork()
    {
        var restrictOt = _context.Payload.Data.CompanyPolicy.OTEligibility == OvertimeEligibilityRule.RequireFullRegularHours;
        if (_context.Payload.Data.Employee.ClientId.HasValue)
        {
            var clientPolicy = _context.Payload.Provider.ClientPolicyProvider.GetPolicy(_context.Payload.Data.Employee.ClientId);
            if (clientPolicy != null)
            {
                restrictOt = clientPolicy.OvertimeEligibilityRule == OvertimeEligibilityRule.RequireFullRegularHours;
            }
        }
        return restrictOt;
    }
}

