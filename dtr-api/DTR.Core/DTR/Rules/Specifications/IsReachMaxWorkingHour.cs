namespace DTR.Core;

public class IsReachMaxWorkingHour : IRuleSpecification
{
    public bool IsSatisfiedBy(TimeRange input, TimeContext context)
    {
        var payload = context.Payload;
        var key = this.CreateSpecCacheKey(context);
        var cached = context.Payload.SharedSpecCache.GetByKey(key);
        if (cached.Found)
            return cached.Value;


        var regTime = payload.Ledger.GetByTag("final_RegularTime", context); 

        var result = regTime != null && regTime.TotalMinutes >= payload.Data.CurrentShift.MaxWorkingMinutes;

        payload.SharedSpecCache.Record(key, result);

        return result;

    }
}