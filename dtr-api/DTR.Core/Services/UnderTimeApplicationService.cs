namespace DTR.Core;

public class UnderTimeApplicationService : ServiceBase<UnderTimeApplication>
{
    public UnderTimeApplicationService(IDTRUnitOfWork uow) : base(uow) { }
    protected override ValidationMessage AddOrUpdateValidation(UnderTimeApplication model)
    {
        var exists = _uow.Repository
            .Find<UnderTimeApplication>(x => (x.EmployeeId == model.EmployeeId)
            && x.PayrollDate == model.PayrollDate
            && x.Id != model.Id);

        if (exists.Any())
        {
            return new ValidationMessage(false, "UT is already added for this employee for the selected date");
        }
        return base.AddOrUpdateValidation(model);
    }
    public async Task<Dictionary<UTKey, UnderTimeApplication?>> FindByDateRangeAsync(DateOnly from, DateOnly to, HashSet<Guid> employeeIds)
    {
        Dictionary<UTKey, UnderTimeApplication?> data = await _uow.Repository
                 .Find<UnderTimeApplication>(x => x.OTStatus==OTStatus.Approved &&  (x.PayrollDate >= from && x.PayrollDate <= to) && employeeIds.Contains(x.EmployeeId))
                 .GroupBy(a => new UTKey(a.EmployeeId, a.PayrollDate))
                 .ToDictionaryAsync(g => g.Key, g => g.OrderBy(x => x.PayrollDate).FirstOrDefault());
        ;
        return data;
    }

    public UnderTimeApplication? FindUT(DateOnly date, Guid employeeId)
    {
        return FindAll()
            .Where(x => x.PayrollDate == date && x.EmployeeId == employeeId)
            .FirstOrDefault()
            ;
    } 
}
public readonly record struct UTKey(Guid EmpId, DateOnly OTDate);