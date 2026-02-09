namespace DTR.Core;

public class IsRestDaySpec : IRuleSpecification
{
    public bool IsSatisfiedBy(TimeRange input, TimeContext context)
    {
        var payload = context.Payload;

        var key = this.CreateSpecCacheKey(context);
        var cached = context.Payload.SharedSpecCache.GetByKey(key);
        if (cached.Found)
            return cached.Value;

        var isRestDay = RestDayChecker
            .IsRestDay(context.Payload); 
        payload.SharedSpecCache.Record(key, isRestDay);
        return isRestDay; 
    }
}