namespace DTR.Core;

public abstract class HolidayHandler
{
    protected readonly List<Holiday> Holidays;
    protected readonly Dictionary<Holidaykey, List<ChangeHoliday>> Changes;
    protected HolidayHandler NextHandler { get; set; }
    protected HolidayHandler(List<Holiday> holidays, Dictionary<Holidaykey, List<ChangeHoliday>> changes)
    {
        Holidays = holidays;
        Changes = changes;
    }

    public void SetNextHandler(HolidayHandler handler)
    {
        NextHandler = handler;
    }
    protected abstract bool IsApplicable(Employee employee, DateOnly payrollDate);
    public List<HolidayInfo> Handle(Employee employee, DateOnly payrollDate)
    {
        if (IsApplicable(employee, payrollDate))
        {
            return GetCurrentHoliday(employee, payrollDate);
        }
        else if (NextHandler != null)
        {
            return NextHandler.Handle(employee, payrollDate);
        }
        return [];
    }
    protected abstract List<HolidayInfo> GetCurrentHoliday(Employee employee, DateOnly payrollDate);
}

public class OverrideHolidayHandler : HolidayHandler
{
    // Caches resolved overrides per employee/payroll date key
    private readonly Dictionary<Holidaykey, List<HolidayInfo>> _holidayCache;

    public OverrideHolidayHandler(List<Holiday> holidays, Dictionary<Holidaykey, List<ChangeHoliday>> changes)
        : base(holidays, changes)
    {
        _holidayCache = new Dictionary<Holidaykey, List<HolidayInfo>>();
    }

    protected override bool IsApplicable(Employee employee, DateOnly payrollDate)
    {
        var key = new Holidaykey(employee.Id, payrollDate);

        if (_holidayCache.ContainsKey(key))
            return _holidayCache[key].Any();

        RunCheck(employee, payrollDate);
        return _holidayCache[key].Any();
    }

    protected override List<HolidayInfo> GetCurrentHoliday(Employee employee, DateOnly payrollDate)
    {
        var key = new Holidaykey(employee.Id, payrollDate);

        if (_holidayCache.TryGetValue(key, out var cached))
            return cached;

        RunCheck(employee, payrollDate);
        return _holidayCache[key];
    }

    private void RunCheck(Employee employee, DateOnly payrollDate)
    {
        var key = new Holidaykey(employee.Id, payrollDate);

        var replacementEntries = Changes?
            .Where(kvp => kvp.Value.Any(x => x.State == ChangeSchedState.REPLACEMENT))
            .ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value
                    .Where(x => x.State == ChangeSchedState.REPLACEMENT)
                    .ToList()
            );

        if (replacementEntries == null || !replacementEntries.TryGetValue(key, out var replacements) || !Holidays.Any())
        {
            _holidayCache[key] = new List<HolidayInfo>();
            return;
        }

        // Filter only those replacements that apply to tracked holidays
        var trackedHolidayIds = Holidays.Select(h => h.Id).ToHashSet();

        var matchedReplacements = replacements
            .Where(x => x.EmployeeId == employee.Id &&
                        x.PayrollDate == payrollDate &&
                        trackedHolidayIds.Contains(x.HolidayId))
            .ToList();

        var result = matchedReplacements.Select(x => new HolidayInfo
        {
            EmployeeId = x.EmployeeId,
            HolType = x.Holiday.HolType,
            AreaId= x.Holiday.AreaId,
            PayrollDate = x.PayrollDate,
            State = ChangeSchedState.REPLACEMENT,
            WorkType = x.Holiday.WorkType,
            HolidayId=x.HolidayId,
            IsPaid=x.Holiday.IsPaid,
        }).ToList();

        _holidayCache[key] = result;
    }
}
public class FallBackHolidayHandler : HolidayHandler
{
    private readonly Dictionary<Holidaykey, List<HolidayInfo>> _holidayCache;
    public FallBackHolidayHandler(List<Holiday> holidays, Dictionary<Holidaykey, List<ChangeHoliday>> changes)
        : base(holidays, changes)
    {
        _holidayCache = new Dictionary<Holidaykey, List<HolidayInfo>>();
    }

    protected override bool IsApplicable(Employee employee, DateOnly payrollDate)
    {
        var key = new Holidaykey(employee.Id, payrollDate);
        if (_holidayCache.ContainsKey(key))
            return _holidayCache[key].Any();

        RunCheck(employee, payrollDate);
        return _holidayCache[key].Any();
    }

    protected override List<HolidayInfo> GetCurrentHoliday(Employee employee, DateOnly payrollDate)
    {
        var key = new Holidaykey(employee.Id, payrollDate);
        if (_holidayCache.TryGetValue(key, out var result))
            return result;

        RunCheck(employee, payrollDate);
        return _holidayCache[key];
    }

    private void RunCheck(Employee employee, DateOnly payrollDate)
    {
        var key = new Holidaykey(employee.Id, payrollDate);

        var overridden = Changes?
            .Where(kvp => kvp.Value.Any(x => x.State == ChangeSchedState.OVERRIDEN))
            .ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.Where(x => x.State == ChangeSchedState.OVERRIDEN).ToList()
            );

        if (overridden == null || !Holidays.Any())
        {
            _holidayCache[key] = new List<HolidayInfo>();
            return;
        }

        var overriddenIds = overridden.Values
            .SelectMany(x => x)
            .Where(xx => xx.EmployeeId == employee.Id && xx.PayrollDate == payrollDate)
            .Select(xx => xx.HolidayId)
            .ToHashSet();

        var unoverriddenHolidays = Holidays
            .Where(x => !overriddenIds.Contains(x.Id))
            .ToList();

        var result = unoverriddenHolidays
            .Select(x => new HolidayInfo
            {
                EmployeeId = employee.Id,
                HolType = x.HolType,
                AreaId = x.AreaId,
                PayrollDate = x.HolDate,
                State = ChangeSchedState.DEFAULT,
                WorkType = x.WorkType,
                HolidayId=x.Id,
                IsPaid=x.IsPaid,
            })
            .ToList();

        _holidayCache[key] = result;
    }
}