namespace DTR.Core;

public interface IRuleSpecification
{
    bool IsSatisfiedBy(TimeRange input, TimeContext context);
} 
 