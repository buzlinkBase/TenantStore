namespace DTR.Core;

public class IsAppliedOTSpec : IRuleSpecification
{
    public bool IsSatisfiedBy(TimeRange input, TimeContext context)
    {
        var key = this.CreateSpecCacheKey(context);
        var cached = context.Payload.SharedSpecCache.GetByKey(key);
        if (cached.Found)
            return cached.Value;

        var shift = context.Payload.Data.CurrentShift;

        var isEligible = context.Payload.Provider.OTProvider
            .HasOTApplication(shift.ShiftDate) ;

        context.Payload.SharedSpecCache.Record(key, isEligible);
        return isEligible;

    }
}

public class IsSystemAutoComputeOT : IRuleSpecification
{
    public bool IsSatisfiedBy(TimeRange input, TimeContext context)
    {
        var key = this.CreateSpecCacheKey(context);
        var cached = context.Payload.SharedSpecCache.GetByKey(key);
        if (cached.Found)
            return cached.Value;

        var shift = context.Payload.Data.CurrentShift;
        var isEligible = shift.WithOT  ;
        context.Payload.SharedSpecCache.Record(key, isEligible);
        return isEligible;

    }
}
