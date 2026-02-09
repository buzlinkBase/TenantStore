namespace DTR.Core;

public class IsOTFirst8hrRuleSpec : IRuleSpecification
{
    public bool IsSatisfiedBy(TimeRange input, TimeContext context)
    {

        var payload = context.Payload;
        var hasRule = context.Payload.Data.CompanyPolicy.OTEligibility == OvertimeEligibilityRule.OffsetAgainstUndertimeOrLateness;

        if (payload.Data.Employee.ClientId != null)
        {
            //override to client rule
            var policy = payload.Provider.ClientPolicyProvider.GetPolicy(payload.Data.Employee.ClientId ?? Guid.Empty);
            if (policy != null)
            {
                hasRule = policy.OvertimeEligibilityRule == OvertimeEligibilityRule.OffsetAgainstUndertimeOrLateness;
            }
        }
        return hasRule;

    }
}
