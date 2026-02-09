namespace DTR.Core;

public class IsEligibleWorkHours : IRuleSpecification
{
    public bool IsSatisfiedBy(TimeRange input, TimeContext context)
    {
        var payload = context.Payload;
        var key = this.CreateSpecCacheKey(context);

        var cached = context.Payload.SharedSpecCache.GetByKey(key);
        if (cached.Found)
            return cached.Value;

        var minimumWorkHours = payload.Data.CurrentShift.MinimumWorkingMinutes;
        var attTotalMinutes = context.CanonicalTimeRange.TotalMinutes;
        var reaching = minimumWorkHours <= attTotalMinutes;

        payload.SharedSpecCache.Record(key, reaching);
        return reaching;

    }
}