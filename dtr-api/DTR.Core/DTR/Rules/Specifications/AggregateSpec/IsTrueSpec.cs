namespace DTR.Core;

public class IsTrueSpec : IRuleSpecification
{
    public bool IsSatisfiedBy(TimeRange input, TimeContext context) => true;
}
public class IsFalseSpec : IRuleSpecification
{
    public bool IsSatisfiedBy(TimeRange input, TimeContext context) => false;
}
