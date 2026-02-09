namespace DTR.Core;

public class PolicyPipeline
{
    private readonly List<IConditionalPolicy> _policies = new();
    public PolicyPipeline AddPolicy(IConditionalPolicy policy)
    {
        _policies.Add(policy);
        return this;
    }
    public TimeRange Execute(TimeRange input, TimeContext context)
    {
        foreach (var policy in _policies)
        {
            input = policy.Apply(input, context);
        }
        return input;
    } 
}