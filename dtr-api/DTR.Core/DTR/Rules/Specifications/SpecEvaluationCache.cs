namespace DTR.Core;

public record struct CachedKey(string KeyName);
public record struct SpecEvaluationCacheKey(string SpecName, DateOnly Date, Guid EmployeeId);
public class SpecEvaluationCache : ValueCache<SpecEvaluationCacheKey, bool>
{
    public void RecordTag(string tag, TimeContext context, bool result)
    {
        Record(CreateKey(tag, context), result);
    }
    public (bool Found, bool Value) GetByTag(string tag, TimeContext context)
        => GetByKey(CreateKey(tag, context));

    public static SpecEvaluationCacheKey CreateKey<TSpec>(DateOnly date, Guid employeeId)
        => new(typeof(TSpec).Name, date, employeeId);

    public static SpecEvaluationCacheKey CreateKey<TSpec>(TimeContext context)
        => new(typeof(TSpec).Name, context.Payload.Data.CurrentDate, context.Payload.Data.Employee.Id);

    public static SpecEvaluationCacheKey CreateKey(string specName, TimeContext context)
        => new(specName, context.Payload.Data.CurrentDate, context.Payload.Data.Employee.Id);

    public static SpecEvaluationCacheKey CreateKey(string specName, DateOnly date, Guid employeeId)
        => new(specName, date, employeeId);
}

public class ValueCache<TKey, TValue>
    where TKey : notnull
{
    protected readonly Dictionary<TKey, TValue> Records = new();
    public void Record(TKey key, TValue value)
        => Records[key] = value;
    public virtual (bool Found, TValue? Value) GetByKey(TKey key)
    {
        var found = Records.TryGetValue(key, out var value);
        return (found, value);
    }
    public bool ContainsKey(TKey key)
        => Records.ContainsKey(key);
    public IReadOnlyCollection<TKey> GetAllKeys()
    => Records.Keys;
}

public class BoolCache : ValueCache<CachedKey, bool>;
public class ObjectCache : ValueCache<ObjectCacheKey, object>;
public record struct ObjectCacheKey(object Value);
