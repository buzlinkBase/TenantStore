namespace DTR.Core; 
public class PostShiftOTUnrestrictedHandler : PostShiftOTHandler
{
    public TimeRange Calculate(TimeRange input, TimeContext context) => Process(input, context);
}
public class PostShiftOTHandler : OTComputationHandlerBase
{
    protected override bool CanHandle(TimeRange input, TimeContext context)
    {
        if (context.Payload.Data.Employee.ClientId.HasValue)
        {
            var clientpolicy = context.Payload.Provider.ClientPolicyProvider.GetPolicy(context.Payload.Data.Employee.ClientId.Value);
            if (clientpolicy != null)
            {
                return clientpolicy.OvertimeInclusionPolicy == OvertimeInclusionPolicy.UsePostShiftWork;
            }
        }
        return context.Payload.Data.CompanyPolicy.OTInclusionPolicy == OvertimeInclusionPolicy.UsePostShiftWork;
    }

    protected override TimeRange Process(TimeRange input, TimeContext context)
    {

        var shift = context.Payload.Data.CurrentShift;
        var ledgerKey = TimeRangeLedger.CreateKey(nameof(PostShiftOTHandler), context);
        var otStart = AutoComputeOTStartFactory.Create(context).GetOTStartTime();
        if (!otStart.HasValue)
        {
            context.Payload.Ledger.Record(ledgerKey, TimeRange.Empty);
            return TimeRange.Empty;
        }

        otStart = otStart.Value;
        var blocked = context.Payload.Ledger
            .GetAllAllocatedExcept(ledgerKey)
            .MergeOverlapping()
            ;
        var usable = context.CanonicalTimeRange.TimeRecords
            .Exclude(blocked)
            .MergeOverlapping();

        var withCaptureAllowance = otStart.Value.AddMinutes(TimeAllowance.OTTimeCaptureAllowanceMinutes);
        var postShiftOT = usable
            .Where(r => r.StartTime >= withCaptureAllowance)
            .Select(r => r.Tag("OT_auto"))
            .ToTimeRangeCollection()
            .ToTimeRange();


        if (!postShiftOT.TimeRecords.Any()) return TimeRange.Empty;
        var otOut = postShiftOT.TimeRecords.MaxBy(x => x.EndTime)?.EndTime;
        if (otOut == null) return TimeRange.Empty;

        var OTshift = new CurrentShift() { StartTime = otStart.Value, EndTime = otOut.Value };

        var tr = new TimeRangeCollection()
        {
            new TimeRecord(OTshift.StartTime,OTshift.EndTime)
        };

        var OTIntersectedTime = postShiftOT.TimeRecords.Intersect(tr);
        var finalRange = TimeRange.Set(OTIntersectedTime.TotalMinutes(), OTIntersectedTime);
        context.Payload.Ledger.Record(ledgerKey, finalRange);
        return finalRange;

    }
}
public class AutoComputeOTStartFactory
{
    public static IOTStartProvider Create(TimeContext context)
    {
        var shift = context.Payload.Data.CurrentShift;
        if (shift.OTRequireTimeIn)
        {
            return new AutoComputeOTTimeInProvider(context);
        }
        else if (!shift.OTRequireTimeIn)
        {
            return new AutoComputeOTTimeInNotRequiredProvider(context);
        }
        throw new NotImplementedException();
    }
}
public class AutoComputeOTTimeInProvider : IOTStartProvider
{
    private readonly TimeContext _context;

    public AutoComputeOTTimeInProvider(TimeContext context)
    {
        _context = context;
    }
    public DateTime? GetOTStartTime()
    {
        var _shift = _context.Payload.Data.CurrentShift;

        if (!_shift.OTRequireTimeIn
          || _shift.OTStartTime == null
          || !_shift.OTStartTime.HasValue) return null;

        var date = _shift.EndTime.Date;
        var baseOTStart = date.Add(_shift.OTStartTime.Value);
        //with 15mins allowance from timeshift end
        var allowedStart = baseOTStart;
        return allowedStart;
    }
}
public class AutoComputeOTTimeInNotRequiredProvider : IOTStartProvider
{
    private readonly TimeContext _context;

    public AutoComputeOTTimeInNotRequiredProvider(TimeContext context)
    {
        _context = context;
    }
    public DateTime? GetOTStartTime()
    {

        var NotRequiredProcessor = OTTimeInNotRequiredProviderFactory.Create(_context);
        return NotRequiredProcessor.GetOTStartTime();
    }
}
public class OTTimeInNotRequiredProviderFactory
{
    public static IShiftTypeIdentifier Create(TimeContext context)
    {
        switch (context.Payload.Data.CurrentShift.ShiftType)
        {
            case TimeShiftType.FIXED://base on timeshift
                return new FixedOT(context);
            case TimeShiftType.FLEXI://based on regularClaimed caping
                return new FlexiOT(context);
            default:
                throw new NotImplementedException("IShiftTypeIdentifier");
        }
    }
}
public class FixedOT : IShiftTypeIdentifier
{
    private readonly TimeContext _context;
    public FixedOT(TimeContext context)
    {
        _context = context;
    }
    public DateTime? GetOTStartTime()
    {
        var date = _context.Payload.Data.CurrentShift.EndTime;
        return date;
    }
}
public class FlexiOT : IShiftTypeIdentifier
{
    private readonly TimeContext _context;

    public FlexiOT(TimeContext context)
    {
        _context = context;
    }
    public DateTime? GetOTStartTime()
    {
        //starttime is the shift end time or starttime after the regularhour was claimed
        var regKey = TimeRangeLedger.CreateKey<RegularHourPolicy>(_context);
        var data = _context.Payload.Ledger.GetAllAllocatedExcept(regKey);
        var usable = _context.CanonicalTimeRange.TimeRecords.Exclude(data);
        var startTime = usable.MinBy(x => x.StartTime)?.StartTime ?? _context.Payload.Data.CurrentShift.EndTime;
        return startTime;
    }
}
public interface IShiftTypeIdentifier
{
    DateTime? GetOTStartTime();
}
public interface IOTStartProvider
{
    DateTime? GetOTStartTime();
}