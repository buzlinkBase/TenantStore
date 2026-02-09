namespace DTR.Core;

public class OverbreaktimePipeline
{
    private readonly TimeContext _context;

    public OverbreaktimePipeline(TimeContext context)
    {
        _context = context;
    }

    public TimeRange Apply(TimeRange cannonicalTimeRange)
    {

        var spec = new IsOverbreaktimeSpec();
            //.And(new IsFixedScheduleSpec());

        var overBreakPolicy = new OverBreakPolicy(spec);
       
        var pipeline = new PolicyPipeline()
          .AddPolicy(overBreakPolicy);

        var result=pipeline.Execute(cannonicalTimeRange, _context);
         
        return result;
    } 
}
