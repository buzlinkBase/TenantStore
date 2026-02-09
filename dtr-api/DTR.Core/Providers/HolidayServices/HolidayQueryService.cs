namespace DTR.Core;

public class HolidayQueryService
{
    private readonly IDTRUnitOfWork _uow;
    public HolidayQueryService(IDTRUnitOfWork uow)
    {
        _uow = uow;
    }
    private List<Holiday> GetAllHolidays(DateOnly from, DateOnly to)
    {
        from = from.AddDays(TimeAllowance.AttLookbackDays * -1);
        var holidays = _uow.Repository
            .Find<Holiday>(x => x.IsRecuring || (x.HolDate >= from && x.HolDate <= to))
            .ToList()
            ;

        return holidays
            .Select(x =>
            {
                DateOnly holDate;
                if (x.IsRecuring)
                {
                    holDate = new DateOnly(from.Year, x.HolDate.Month, x.HolDate.Day);
                    if (from.Year != to.Year)
                    {
                        var year = x.HolDate.Month >= from.Month ? from.Year : to.Year;
                        holDate = new DateOnly(year, x.HolDate.Month, x.HolDate.Day);
                    }
                }
                else
                {
                    holDate = x.HolDate;
                }

                return new Holiday
                {
                    Id = x.Id,
                    AreaId = x.AreaId,
                    Description = x.Description,
                    HolDate = holDate,
                    HolType = x.HolType,
                    HolYear = holDate.Year,
                    IsRecuring = x.IsRecuring,
                    Status = x.Status,
                    IsPaid = x.IsPaid,
                    TenantId = x.TenantId,
                    WorkType = x.WorkType,
                };
            })
            .Where(h => h.HolDate >= from && h.HolDate <= to)
            .ToList();
    } 
    public async Task<Dictionary<Holidaykey, List<HolidayInfo>>> GetHolidays(DateOnly fromDate, DateOnly toDate, List<Employee> employees)
    {
        var holidays = GetAllHolidays(fromDate, toDate);
        var allChanged = await _uow.Repository
            .Find<ChangeHoliday>(x => x.PayrollDate >= fromDate && x.PayrollDate <= toDate)
            .GroupBy(x => new Holidaykey(x.EmployeeId, x.PayrollDate))
            .ToDictionaryAsync(x => x.Key, x => x.Distinct().ToList())
            ;
        
        var overrideOff = new OverrideHolidayHandler(holidays, allChanged);
        overrideOff.SetNextHandler(new FallBackHolidayHandler(holidays, allChanged));
        var holidayDic = new Dictionary<Holidaykey, List<HolidayInfo>>();

        foreach (var employee in employees)
        {
            for (DateOnly curDate = fromDate; curDate <= toDate; curDate = curDate.AddDays(1))
            {
                var result = overrideOff.Handle(employee, curDate);
                if (result.Any())
                {
                    var key = new Holidaykey(employee.Id, curDate);
                    holidayDic[key] = result;
                }
            }
        }
        return holidayDic;
    }
}