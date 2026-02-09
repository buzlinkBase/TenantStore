namespace DTR.Core;

public class IsOverbreaktimeSpec : IRuleSpecification
{
    public IsOverbreaktimeSpec()
    {
    }
    public bool IsSatisfiedBy(TimeRange input, TimeContext context)
    {
        return true;
    }
}