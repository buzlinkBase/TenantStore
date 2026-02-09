namespace DTR.Core;

public class IsAppliedUnderTimeSpec  : IRuleSpecification
{
    public bool IsSatisfiedBy(TimeRange input, TimeContext context)
    {
        var key = this.CreateSpecCacheKey(context);
        var cached = context.Payload.SharedSpecCache.GetByKey(key);
        if (cached.Found)
            return cached.Value;


        var shift = context.Payload.Data.CurrentShift;

        var isEligible =  context.Payload.Provider.UTProvider
            .HasUTApplication(shift.ShiftDate);

        context.Payload.SharedSpecCache.Record(key, isEligible);
        return isEligible;

    } 
}
 