namespace DTR.Core;

public class ApprovalBasedOTHandler : OverTimeHandler
{
    private IRuleSpecification spec;
    public ApprovalBasedOTHandler(TimeRange input, TimeContext context) : base(input,context)
    {
    }
    protected override bool CanHandle()
    {
        spec = new IsAppliedOTSpec();
        return spec.IsSatisfiedBy(Input, Context);
    }

    protected override TimeRange Process()
    {
        var pipeline = new PolicyPipeline()
             .AddPolicy(new AppliedOvertimePolicy(spec)) 
             ;
        return pipeline.Execute(Input, Context);    
    }
}

