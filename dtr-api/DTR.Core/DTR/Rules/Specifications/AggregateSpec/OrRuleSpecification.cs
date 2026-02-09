namespace DTR.Core;

public class OrRuleSpecification : IRuleSpecification
{
    private readonly IRuleSpecification _left;
    private readonly IRuleSpecification _right;

    public OrRuleSpecification(IRuleSpecification left, IRuleSpecification right)
    {
        _left = left;
        _right = right;
    }

    public bool IsSatisfiedBy(TimeRange input, TimeContext context) =>
        _left.IsSatisfiedBy(input, context) || _right.IsSatisfiedBy(input, context);
}











