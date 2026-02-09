namespace DTR.Core;

public class IsWholeDayLateThresholdSpec : IRuleSpecification
{
    public bool IsSatisfiedBy(TimeRange input, TimeContext context)
    {
        if (!context.Payload.Data.CompanyPolicy.IsWholeDayLateOn) return false;
        var key = this.CreateSpecCacheKey(context);
        var cached = context.Payload.SharedSpecCache.GetByKey(key);
        if (cached.Found)
            return cached.Value;


        var Threshold = context.Payload.Data.CompanyPolicy
            .WholeDayLateThresholdMinutes;

        bool result = input.TotalMinutes >= Threshold;

        this.RecordSpec(context, result);

        return result && Threshold > 0;

    }
}