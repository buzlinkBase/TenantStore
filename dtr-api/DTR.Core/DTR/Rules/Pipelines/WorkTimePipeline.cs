namespace DTR.Core;

public class WorkTimePipeline
{
    private readonly TimeContext _context;
    private readonly IRuleSpecification _specification;
    public WorkTimePipeline(TimeContext context, IRuleSpecification specification)
    {
        _context = context;
        _specification = specification;
    }
    public TimeRange Apply(TimeRange cannonicalTimeRange)
    {
        var fact = RegularTimeShiftRuleFactory.Create(_context, _specification);
        return fact.Apply(cannonicalTimeRange);
    }
}

public interface IRegularTimeShiftRule
{
    TimeRange Apply(TimeRange baseRange);
}

public class RegularTimeShiftRuleFactory
{
    public static IRegularTimeShiftRule Create(TimeContext context, IRuleSpecification specification)
    {
        switch (context.Payload.Data.CurrentShift.ShiftType)
        {
            case TimeShiftType.FIXED:
                return new FixedShiftRegularTimePolicy(context, specification);
            case TimeShiftType.FLEXI:
                return new FixedShiftRegularTimePolicy(context, specification);
            default:
                throw new NotImplementedException("IRegularTimeShiftRule");
        }
    }
}

public class FixedShiftRegularTimePolicy : IRegularTimeShiftRule
{
    private readonly TimeContext _context;
    private readonly IRuleSpecification _specification;

    public FixedShiftRegularTimePolicy(TimeContext context, IRuleSpecification specification)
    {
        _context = context;
        _specification = specification;
    }
    public TimeRange Apply(TimeRange baseRange)
    {
        var regKey = TimeRangeLedger.CreateKey("work_time", _context);
        var cached = _context.Payload.Ledger.GetByKey(regKey);
        if (cached.Found)
        {
            var value = cached.Value ?? TimeRange.Empty;
            _context.Payload.Ledger.RecordByTag("work_time", _context, value);
            return value;
        }

        var pipeline = new PolicyPipeline()
             .AddPolicy(new RegularHourPolicy(_specification))
             //.AddPolicy(new BreakPolicy())//breaks is excluded already in RegularHourPolicy
             .AddPolicy(new First8HrPolicy(SpecFailureBehavior.ReturnInput))
             .AddPolicy(new LatePolicy(new IsFixedScheduleSpec(), SpecFailureBehavior.ReturnInput))
             .AddPolicy(new HolidayPolicy(new IsHolidaySpec(HolidayType.LEGAL), HolidayType.LEGAL, SpecFailureBehavior.ReturnInput))//returned input
             .AddPolicy(new HolidayPolicy(new IsHolidaySpec(HolidayType.SPECIAL), HolidayType.SPECIAL, SpecFailureBehavior.ReturnInput))//returned input
             ;

        var workTimeRange = pipeline.Execute(baseRange, _context);

        _context.Payload.Ledger.RecordByTag("work_time", _context, workTimeRange);
        return workTimeRange;

    }
}
