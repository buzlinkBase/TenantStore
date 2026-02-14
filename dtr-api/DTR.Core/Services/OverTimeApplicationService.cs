namespace DTR.Core;
public class OverTimeApplicationService : ServiceBase<OverTimeApplicationEntity>
{
    public OverTimeApplicationService(IDTRUnitOfWork uow) : base(uow) { }
    protected override ValidationMessage AddOrUpdateValidation(OverTimeApplicationEntity model)
    {
        var exists = _uow.Repository
            .Find<OverTimeApplicationEntity>(x => (x.EmployeeId == model.EmployeeId)
            && x.OTDate == model.OTDate
            && x.Id != model.Id);

        if (exists.Any())
        {
            return new ValidationMessage(false, "OT is already added for this employee for the selected date");
        }
        return base.AddOrUpdateValidation(model);
    }

    public async Task<Dictionary<OTKey, OverTimeApplicationEntity?>> FindByDateRangeAsync(DateOnly from, DateOnly to, HashSet<Guid> employeeIds)
    {
        Dictionary<OTKey, OverTimeApplicationEntity?> data = await _uow.Repository
                 .Find<OverTimeApplicationEntity>(x => x.OTStatus == OTStatus.Approved && (x.OTDate >= from && x.OTDate <= to) && employeeIds.Contains(x.EmployeeId))
                 .GroupBy(a => new OTKey(a.EmployeeId, a.OTDate))
                 .ToDictionaryAsync(g => g.Key, g => g.OrderBy(x => x.OTDate).FirstOrDefault());
        ;
        return data;
    }

    public OverTimeApplicationEntity? FindOT(DateOnly date, Guid employeeId)
    {
        return FindAll()
            .Where(x => x.OTDate == date && x.EmployeeId == employeeId)
            .FirstOrDefault()
            ;
    }
}
public readonly record struct OTKey(Guid EmpId, DateOnly OTDate);