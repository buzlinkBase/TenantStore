namespace DTR.Core;

public static class RuleSpecificationExtensions
{
    public static IRuleSpecification And(this IRuleSpecification left, IRuleSpecification right) =>
        new AndRuleSpecification(left, right);

    public static IRuleSpecification Or(this IRuleSpecification left, IRuleSpecification right) =>
        new OrRuleSpecification(left, right);

    public static IRuleSpecification Not(this IRuleSpecification spec) =>
        new NotRuleSpecification(spec);
}