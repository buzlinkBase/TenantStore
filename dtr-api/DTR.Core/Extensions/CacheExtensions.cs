namespace DTR.Core;

public static class CacheExtensions
{
    public static void RecordSpec<T>(this T spec, TimeContext context,  bool result) where T : IRuleSpecification
    {
        var key = spec.CreateSpecCacheKey(context);
        context.Payload.SharedSpecCache.Record(key, result);

    } 
    public static void RecordLedger<T>(this T policy,  TimeContext context, TimeRange timeRange ) where T : IConditionalPolicy
    {
        var ledgerKey = TimeRangeLedger.CreateKey<T>(context);
        context.Payload.Ledger.Record(ledgerKey, timeRange); 
    } 
    public static TimeRangeLedgerCacheKey CreateLedgerKey<T>(this T policy, TimeContext context) where T : IConditionalPolicy
    {
        return TimeRangeLedger.CreateKey<T>(context); 
    }
    public static SpecEvaluationCacheKey CreateSpecCacheKey<T>(this T spec,  TimeContext context) where T : IRuleSpecification
    {
        var key = SpecEvaluationCache.CreateKey<T>(context);
        return key;
    }
}