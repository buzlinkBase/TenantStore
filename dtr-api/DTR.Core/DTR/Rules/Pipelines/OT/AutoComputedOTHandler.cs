namespace DTR.Core;
//Get computed time by the Pipeline
public class AutoComputedOTHandler : OverTimeHandler
{
    public AutoComputedOTHandler(TimeRange input, TimeContext context) : base(input, context)
    { 
    }
    protected override TimeRange Process()
    {

        var pipeline = new PolicyPipeline()
             .AddPolicy(new AutoComputeOvertimePolicy(new IsSystemAutoComputeOT()))
             //.AddPolicy(new TrimOTForFirst8HrPolicy(new IsOTFirst8hrRuleSpec(), SpecFailureBehavior.ReturnInput))
             ; 
        return pipeline.Execute(Input, Context);

    }
}

