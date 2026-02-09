using System.Collections.ObjectModel;
namespace DTR.Core;

public record struct TimeRangeLedgerCacheKey(string RuleKey, DateOnly Date, Guid EmployeeId);

public class TimeRangeLedger : ValueCache<TimeRangeLedgerCacheKey, TimeRange>
{
    public void RecordByTag(string keyName, TimeContext context, TimeRange timeRange)
    {
        var key = CreateKey(keyName, context);
        Record(key, timeRange);
    }

    public override (bool Found, TimeRange Value) GetByKey(TimeRangeLedgerCacheKey key)
    {
        var result = base.GetByKey(key);
        if (result.Found)
        {
            return (result.Found, result.Value ?? TimeRange.Empty);
        }
        return (result.Found, TimeRange.Empty);
    }

    public TimeRange GetByTag(string tagName, TimeContext context)
    {
        var key = CreateKey(tagName, context);
        var (found, result) = base.GetByKey(key);
        return found ? result! : TimeRange.Empty;
    }

    public TimeRangeCollection GetAllocated(TimeRangeLedgerCacheKey key)
    {
        var (found, timeRange) = base.GetByKey(key);
        return found ? timeRange!.TimeRecords : new TimeRangeCollection();
    }

    public TimeRangeCollection GetAllAllocatedExcept(TimeRangeLedgerCacheKey key)
    {
        return Snapshot()
            .Where(kvp => kvp.Key.Date == key.Date && kvp.Key.EmployeeId == key.EmployeeId && kvp.Key != key)
            .SelectMany(kvp => kvp.Value.TimeRecords)
            .ToTimeRangeCollection();
    }

    public IEnumerable<(TimeRangeLedgerCacheKey Key, TimeRangeCollection Range)> GetAllClaims(DateOnly date, Guid employeeId)
    {
        return Snapshot()
            .Where(kvp => kvp.Key.Date == date && kvp.Key.EmployeeId == employeeId)
            .Select(kvp => (kvp.Key, kvp.Value.TimeRecords));
    }

    public IReadOnlyDictionary<TimeRangeLedgerCacheKey, TimeRange> Snapshot() =>
        new ReadOnlyDictionary<TimeRangeLedgerCacheKey, TimeRange>(Records);

    public static TimeRangeLedgerCacheKey CreateKey(string keyName, TimeContext context)
        => new(keyName, context.Payload.Data.CurrentDate, context.Payload.Data.Employee.Id);

    public static TimeRangeLedgerCacheKey CreateKey<TPolicy>(DateOnly date, Guid employeeId)
        => new(typeof(TPolicy).Name, date, employeeId);

    public static TimeRangeLedgerCacheKey CreateKey<TPolicy>(TimeContext context)
        => CreateKey<TPolicy>(context.Payload.Data.CurrentDate, context.Payload.Data.Employee.Id);
}

//public record struct TimeRangeLedgerCacheKey(string ruleKey, DateOnly Date, Guid EmployeeId);
//public class TimeRangeLedger
//{
//    private readonly Dictionary<TimeRangeLedgerCacheKey, TimeRange> _allocations = new();
//    public void Record(TimeRangeLedgerCacheKey key, TimeRange timeRange)
//    {
//        _allocations[key] = timeRange;
//    }
//    public void RecordByTag(string keyName,TimeContext context,  TimeRange timeRange)
//    {
//        var key=CreateKey(keyName,context);
//        Record(key,timeRange);
//    }

//    public bool GetByKey(TimeRangeLedgerCacheKey key, out TimeRange timeRange)
//    {
//        return _allocations.TryGetValue(key, out timeRange);
//    }
//    public TimeRange GetByTag(string tagName, TimeContext context)
//    {
//        var key = CreateKey(tagName, context);
//        if (GetByKey(key, out var result))
//            return result;
//        return TimeRange.Empty;
//    }

//    public TimeRange GetByKey(TimeRangeLedgerCacheKey key)
//    {
//        if (GetByKey(key, out var result))
//            return result;
//        return TimeRange.Empty;
//    }

//    public TimeRangeCollection GetAllocated(TimeRangeLedgerCacheKey key)
//    {
//        return _allocations.TryGetValue(key, out var timeRange)
//            ? timeRange.TimeRecords
//            : new TimeRangeCollection();
//    }

//    public TimeRangeCollection GetAllAllocatedExcept(TimeRangeLedgerCacheKey key)
//    {
//        return _allocations
//            .Where(kvp => kvp.Key.Date == key.Date && kvp.Key.EmployeeId == key.EmployeeId && kvp.Key != key)
//            .SelectMany(kvp => kvp.Value.TimeRecords)
//            .ToTimeRangeCollection();
//    }

//    public IEnumerable<(TimeRangeLedgerCacheKey Key, TimeRangeCollection Range)> GetAllClaims(DateOnly date, Guid employeeId)
//    {
//        return _allocations
//            .Where(kvp => kvp.Key.Date == date && kvp.Key.EmployeeId == employeeId)
//            .Select(kvp => (kvp.Key, kvp.Value.TimeRecords));
//    }

//    public IReadOnlyDictionary<TimeRangeLedgerCacheKey, TimeRange> Snapshot() => _allocations;

//    public static TimeRangeLedgerCacheKey CreateKey(string KeyName, TimeContext context)
//        => new(KeyName, context.Payload.Data.CurrentDate, context.Payload.Data.Employee.Id);

//    public static TimeRangeLedgerCacheKey CreateKey<TPolicy>(DateOnly date, Guid employeeId)
//        => new(typeof(TPolicy).Name, date, employeeId);

//    public static TimeRangeLedgerCacheKey CreateKey<TPolicy>(TimeContext context)
//        => CreateKey<TPolicy>(context.Payload.Data.CurrentDate, context.Payload.Data.Employee.Id);
//}

