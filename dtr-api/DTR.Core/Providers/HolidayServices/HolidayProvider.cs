namespace DTR.Core;

public static class HolidayProviderFactory
{
    public static HolidayProviderBase Create(Employee employee, Dictionary<Holidaykey, List<HolidayInfo>> holidays)
    {
        return new DefaultHolidayProvider(holidays);
    }
}

public sealed class DefaultHolidayProvider : HolidayProviderBase
{
    public DefaultHolidayProvider(Dictionary<Holidaykey, List<HolidayInfo>> holidays) : base(holidays)
    {
    }
    public override TimeRangeCollection GetHolidayDuringDate(HolidayType type, Employee employee, DateOnly date)
    {
        var shift = new CurrentShift()
        {
            //Employee = employee,
            StartTime = date.ToDateTime(TimeOnly.MinValue),
            EndTime = date.AddDays(1).ToDateTime(TimeOnly.MinValue)
        };
        return GetHolidayDuringShift(type, employee, shift);
    }
    public override TimeRangeCollection GetHolidayDuringShift(HolidayType type, Employee employee, CurrentShift shift)
    {
        var shiftRange = new TimeRecord(shift.StartTime, shift.EndTime);

        var holidayKeys = new[]
        {
             new Holidaykey(employee.Id, DateOnly.FromDateTime(shift.StartTime)),
             new Holidaykey(employee.Id, DateOnly.FromDateTime(shift.EndTime))
        };

        var filterChain = new HolidayFilterChain();
        var holidaySlices = holidayKeys
            .SelectMany(key =>
                _holidays.TryGetValue(key, out var holidays)
                    ? holidays?.Where(h => h.HolType == type && filterChain.IsApplicable(h, employee)) ?? []
                    : Enumerable.Empty<HolidayInfo>())
            .GroupBy(h => new { h.HolidayId })
            .Select(g =>
            {
                var h = g.First();
                var record = new TimeRecord(
                    h.PayrollDate.ToDateTime(TimeOnly.MinValue),
                    //h.PayrollDate.ToDateTime(TimeOnly.MaxValue),
                    h.PayrollDate.AddDays(1).ToDateTime(TimeOnly.MinValue),
                    $"Holiday_{h.HolType}");

                return record.Intersect(shiftRange, $"Holiday_{type}");
            })
            .Where(r => r != null)
            .OfType<TimeRecord>();

        return holidaySlices.ToTimeRangeCollection();
    }

    public override HolidayInfo? GetHolidayInfoDuringShift(HolidayType type, Employee employee, CurrentShift shift)
    {
        //must order worktype for WorkTypeResolver
        var shiftRange = new TimeRecord(shift.StartTime, shift.EndTime);
        var holidayKeys = new[]
        {
             new Holidaykey(employee.Id, DateOnly.FromDateTime(shift.StartTime)),
             new Holidaykey(employee.Id, DateOnly.FromDateTime(shift.EndTime))
        };
        var filterChain = new HolidayFilterChain();
        var holidaySlices = holidayKeys
            .SelectMany(key =>
                _holidays.TryGetValue(key, out var holidays)
                    ? holidays?.Where(h => h.HolType == type && filterChain.IsApplicable(h, employee)) ?? []
                    : Enumerable.Empty<HolidayInfo>())
            .OrderByDescending(h => h.WorkType)
            .GroupBy(h => new { h.HolidayId })
            .Select(x => x.FirstOrDefault())
            .FirstOrDefault();
        return holidaySlices;
    }
}
public  abstract class HolidayProviderBase
{
    protected readonly Dictionary<Holidaykey, List<HolidayInfo>> _holidays;
    public HolidayProviderBase(Dictionary<Holidaykey, List<HolidayInfo>> holidays)
    {
        _holidays = holidays;
    }
    public abstract TimeRangeCollection GetHolidayDuringDate(HolidayType type, Employee employee, DateOnly date);
    public abstract TimeRangeCollection GetHolidayDuringShift(HolidayType type, Employee employee, CurrentShift shift);
    public abstract HolidayInfo? GetHolidayInfoDuringShift(HolidayType type, Employee employee, CurrentShift shift);
}
