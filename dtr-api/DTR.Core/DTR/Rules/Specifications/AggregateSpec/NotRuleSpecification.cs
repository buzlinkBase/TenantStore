namespace DTR.Core;

public class NotRuleSpecification : IRuleSpecification
{
    private readonly IRuleSpecification _inner;

    public NotRuleSpecification(IRuleSpecification inner) => _inner = inner;

    public bool IsSatisfiedBy(TimeRange input, TimeContext context) =>
        !_inner.IsSatisfiedBy(input, context);
}





