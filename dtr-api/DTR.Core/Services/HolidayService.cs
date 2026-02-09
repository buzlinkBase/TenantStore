namespace DTR.Core;

public record HolidayResult(Guid Id, 
    string Description,
    Guid AreaId,
    string HolDate, 
    string HolidayType,
    bool IsRecuring,
    bool IsPaid,
    HolidayWorkType WorkType);

public class HolidayService : ServiceBase<Holiday>
{
    public HolidayService(IDTRUnitOfWork uow) : base(uow) { }


    public override void AddOrUpdate(Holiday model)
    { 
        base.AddOrUpdate(model);

        var changed = _uow.Repository
            .Find<ChangeHoliday>(x => x.HolidayId == model.Id)
            .FirstOrDefault()
            ; 
    }
    public List<HolidayResult> FilterRecordSetupMasterList(int year)
    {
        return _uow.Repository.Find<Holiday>(x =>
        x.IsRecuring || x.HolYear == year)
            .Select(x => new HolidayResult(x.Id,
             x.Description,
             x.AreaId,
             x.IsRecuring ? $"{x.HolDate.ToString("MMM dd,")} {year}" : x.HolDate.ToString("MMM dd, yyyy"),
             x.HolType == HolidayType.LEGAL ? "Legal" : "Special",
             x.IsRecuring,
             x.IsPaid,
             x.WorkType))
            .ToList() 
            ;
    }

    public async Task<Dictionary<Holidaykey, List<HolidayInfo>>> FindByRangeAsync(DateOnly from, DateOnly dateTo,List<Employee> employees)
    {
        return await new HolidayQueryService(_uow)
            .GetHolidays(from, dateTo, employees);
    }

    protected override ValidationMessage AddOrUpdateValidation(Holiday model)
    {
        var exists = _uow.Repository.Find<Holiday>(x => (x.Description.ToLower() == model.Description.ToLower()) && x.Id != model.Id);
        if (exists.Any())
        {
            return new ValidationMessage(false, "Holiday is already exists");
        }
        else if (string.IsNullOrWhiteSpace(model.Description))
        {
            return new ValidationMessage(false, "Invalid holiday name");
        }
        return base.AddOrUpdateValidation(model);
    }
}

public readonly record struct Holidaykey(Guid EmpId, DateOnly PayrollId);
