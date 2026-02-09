namespace DTR.Core;

public interface IConditionalPolicy
{
    TimeRange Apply(TimeRange input, TimeContext context);
}

public enum SpecFailureBehavior
{
    ReturnEmpty,
    ReturnInput,
}

public abstract class ConditionalPolicyBase : IConditionalPolicy
{
    private readonly SpecFailureBehavior _failureBehavior;
    protected readonly IRuleSpecification Spec;
    protected ConditionalPolicyBase(IRuleSpecification spec, SpecFailureBehavior behavior = SpecFailureBehavior.ReturnEmpty)
    {
        Spec = spec;//.And(new IsCurrentShiftPresent());
        _failureBehavior = behavior;
    }
    public TimeRange Apply(TimeRange input, TimeContext context)
    {
        return Spec.IsSatisfiedBy(input, context)
            ? ApplyIfSatisfied(input, context)
            : (_failureBehavior == SpecFailureBehavior.ReturnEmpty ? TimeRange.Empty : input);
    }
    protected abstract TimeRange ApplyIfSatisfied(TimeRange input, TimeContext context);
}
