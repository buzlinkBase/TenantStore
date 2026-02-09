namespace DTR.Core;

public record CurrentShiftProviderPayload
{
    public Dictionary<CurrentTimeShiftKey, CurrentShift> AllTimeShifts { get; set; }
    public Employee Employee { get; set; }
    public DateOnly CurDate { get; set; }
    public CurrentShiftProviderPayload()
    {
        AllTimeShifts = new Dictionary<CurrentTimeShiftKey, CurrentShift>();
        Employee = new Employee();
        CurDate = new DateOnly();
    }
    public CurrentShiftProviderPayload(Dictionary<CurrentTimeShiftKey, CurrentShift> allTimeShifts, Employee employee, DateOnly curDate)
    {
        this.AllTimeShifts = allTimeShifts;
        this.Employee = employee;
        this.CurDate = curDate;
    }
};
public interface ICurrentShiftProvider
{
    CurrentShiftProviderPayload Payload { get; }

    CurrentShift? GetCurrentShift();
    CurrentShift? GetNextShift();
    CurrentShift? GetNextShift(CurrentShift? current);
    CurrentShift? GetPreviousShift();
    CurrentShift? GetShiftByDate(DateOnly payrollDate);
}
public class CrossMultiDateCurrentShiftProvider : ICurrentShiftProvider
{
    private readonly DTRContextModel _context;
    private readonly CurrentShiftProviderPayload _payload;
    public CrossMultiDateCurrentShiftProvider(DTRContextModel context, CurrentShiftProviderPayload payload)
    {
        _context = context;
        _payload = payload;
    }

    public CurrentShiftProviderPayload Payload => _payload;
    public CurrentShift? GetCurrentShift() => GetAdjacentShift(0);
    private CurrentShift? GetNextShift() => GetAdjacentShift(1);
    public CurrentShift? GetPreviousShift() => GetAdjacentShift(-1);

    private CurrentShift? GetAdjacentShift(int adjascentDay)
    {
        if (_payload?.AllTimeShifts == null || _payload.Employee == null) return default;
        var filterDate = _payload.CurDate.AddDays(adjascentDay);
        var key = new CurrentTimeShiftKey(_payload.Employee.Id, filterDate);
        _payload.AllTimeShifts.TryGetValue(key, out var shift);
        if (shift == null) return null;
        var curTimeRecords = new TimeRangeCollection()
        {
            new TimeRecord { StartTime = shift.StartTime, EndTime = shift.EndTime }
        };

        /// Always check cache for exclusions
        /// exlude current shift if it is covered from prior shift and Skipped
        var toExclude = new TimeRangeCollection();
        var keyPrio = new TimeRangeLedgerCacheKey("PrioShift", filterDate, _payload.Employee.Id);
        var result = _context.ValueCache.GetByKey(keyPrio);
        if (result.Found && result.Value != null)
        {
            toExclude = result.Value.TimeRecords;
        }
        var skipkey = new TimeRangeLedgerCacheKey("Skipped", filterDate, _payload.Employee.Id);
        var result2 = _context.ValueCache.GetByKey(skipkey);
        if (result2.Found && result2.Value != null)
        {
            toExclude.AddRange(result2.Value.TimeRecords);
        }

        //exlude current shift
        var currentShiftCollectionRange = curTimeRecords
            .MergeOverlapping()
            .Exclude(toExclude)
            .Where(x => x.StartTime > shift.StartTime)
            .ToTimeRangeCollection();

        //check if currentshift is not totaly overriden by the prioShift(NextShift)
        if (currentShiftCollectionRange.TotalMinutes() == 0) return null;
        var newShift = currentShiftCollectionRange.FirstOrDefault();
        var cloneShift = shift.Clone();
        cloneShift.StartTime = newShift?.StartTime ?? shift.StartTime;
        cloneShift.EndTime = newShift?.EndTime ?? shift.EndTime;
        CacheCoveredDates(cloneShift);
        return shift;
    }

    public CurrentShift? GetNextShift(CurrentShift? current)
    {
        if (_payload?.AllTimeShifts == null || _payload.Employee == null) return default;
        if (current == null) return null;
        var filterDate = current.EndTime.AddDays(1); // tomorrow
        if (current.IsCrossDate)
        {
            filterDate = current.EndTime; // same date
        }
        var key = new CurrentTimeShiftKey(_payload.Employee.Id, DateOnly.FromDateTime(filterDate.Date));
        var next = _payload.AllTimeShifts.TryGetValue(key, out var shift) ? shift : null;
        if (next is not null)
        {
            //set nextshift cached
            //for check in next day shift
            var trc = new TimeRangeCollection();
            var nextShiftKey = new TimeRangeLedgerCacheKey("PrioShift", next.ShiftDate, _payload.Employee.Id);
            trc.Add(new TimeRecord(next.StartTime, next.EndTime));
            _context.ValueCache.Record(nextShiftKey, TimeRange.Set(trc));
        }
        CacheCoveredDates(next);
        return next;
    }

    private void CacheCoveredDates(CurrentShift? shift)
    {
        //cached days in between shift
        if (shift == null) return;
        List<DateOnly> dates = shift
            .GetShiftDays()
            .Where(x => x > DateOnly.FromDateTime(shift.StartTime.Date)
             && x < DateOnly.FromDateTime(shift.EndTime.Date))
            .ToList();
        foreach (var currentDate in dates)
        {
            var skipkey = new TimeRangeLedgerCacheKey("Skipped", currentDate, _payload.Employee.Id);
            var trc = new TimeRangeCollection();
            trc.Add(new TimeRecord(currentDate.ToDateTime(TimeOnly.MinValue), currentDate.AddDays(1).ToDateTime(TimeOnly.MinValue)));
            _context.ValueCache.Record(skipkey, TimeRange.Set(trc));
        }
    }

    CurrentShift? ICurrentShiftProvider.GetNextShift()
    {
        return GetNextShift();
    }

    public CurrentShift? GetShiftByDate(DateOnly payrollDate)
    {
        if (_payload?.AllTimeShifts == null || _payload.Employee == null) return default;
        var key = new CurrentTimeShiftKey(_payload.Employee.Id, payrollDate);
        return _payload.AllTimeShifts.TryGetValue(key, out var shift) ? shift : null;
    }

    //public CurrentShift? GetShiftByDate(DateOnly payrollDate)
    //{
    //    if (_payload?.AllTimeShifts == null || _payload.Employee == null) return default;
    //    var key = new CurrentTimeShiftKey(_payload.Employee.Id, payrollDate);

    //    return _payload.AllTimeShifts.TryGetValue(key, out var shift) ? shift : null;
    //}

    /// <summary>
    /// Trim next shift against current (priority) shift, applying TimeAllowance.
    /// </summary>
    //public CurrentShift? GetTrimmedNextShift()
    //{
    //    var current = GetCurrentShift();
    //    var next = GetNextShift(current);
    //    if (current == null || next == null) return next;
    //    // Build a time range collection for the next shift
    //    var trc = new TimeRangeCollection();
    //    var key = new TimeRangeLedgerCacheKey("PrioShift", next.ShiftDate, _payload.Employee.Id);
    //    trc.Add(new TimeRecord(next.StartTime, next.EndTime));
    //    _context.ValueCache.Record(key, TimeRange.Set(trc));
    //    return next;
    //}

    //private TimeSpan GetAllowanceTimeSpan(CurrentShift current)
    //    => TimeSpan.FromMinutes(current.ShiftType == TimeShiftType.FLEXI ? 0 : TimeAllowance.TimeInAllowance * -1);

}
public class CurrentShiftProvider : ICurrentShiftProvider
{
    private readonly DTRContextModel _context;
    private readonly CurrentShiftProviderPayload _payload;
    //public CurrentShiftProvider()
    //{
    //    _payload = new CurrentShiftProviderPayload();
    //}
    public CurrentShiftProvider(DTRContextModel context, CurrentShiftProviderPayload payload)
    {
        _context = context;
        _payload = payload;
    }
    public CurrentShiftProviderPayload Payload => _payload;
    public CurrentShift? GetCurrentShift() => GetAdjacentShift(0);
    public CurrentShift? GetNextShift() => GetAdjacentShift(1);
    public CurrentShift? GetPreviousShift() => GetAdjacentShift(-1);
    private CurrentShift? GetAdjacentShift(int adjascentDay)
    {
        if (_payload?.AllTimeShifts == null || _payload.Employee == null) return default;
        var filterDate = _payload.CurDate.AddDays(adjascentDay);
        var key = new CurrentTimeShiftKey(_payload.Employee.Id, filterDate);
        return _payload.AllTimeShifts.TryGetValue(key, out var shift) ? shift : null;
    }
    public CurrentShift? GetNextShift(CurrentShift? current)
    {
        // 1. Guard clauses: ensure payload and employee exist
        if (_payload?.AllTimeShifts == null || _payload.Employee == null) return default;
        if (current == null) return null;
        // 2. Default filterDate = next day after current.EndTime
        var filterDate = current.EndTime.AddDays(1); // tomorrow

        // 3. If current shift crosses dates, use EndTime's date instead
        if (current.IsCrossDate)
        {
            filterDate = current.EndTime; // same date
        }

        // 4. Build a key for lookup
        var key = new CurrentTimeShiftKey(
            _payload.Employee.Id,
            DateOnly.FromDateTime(filterDate.Date)
        );
        // 5. Try to get the next shift from dictionary
        return _payload.AllTimeShifts.TryGetValue(key, out var shift) ? shift : null;
    }
    public CurrentShift? GetShiftByDate(DateOnly payrollDate)
    {
        if (_payload?.AllTimeShifts == null || _payload.Employee == null) return default;
        var key = new CurrentTimeShiftKey(_payload.Employee.Id, payrollDate);
        return _payload.AllTimeShifts.TryGetValue(key, out var shift) ? shift : null;
    }
}
