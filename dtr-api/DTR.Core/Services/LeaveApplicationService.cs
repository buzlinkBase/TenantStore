
namespace DTR.Core;

public class LeaveApplicationService : ServiceBase<LeaveApplication>
{
    public LeaveApplicationService(IDTRUnitOfWork uow) : base(uow) { }
    public async Task<Dictionary<Leavekey, List<LeaveApplication>>> FindByDateRangeAsync(DateOnly fromDate, DateOnly toDate, HashSet<Guid> employeeIds)
    {
        var data = await _uow.Repository
                .Find<LeaveApplication>(x => x.LeaveDateFrom >= fromDate
                    && x.LeaveDateTo <= toDate
                    && employeeIds.Contains(x.EmployeeId))
                 .GroupBy(a => new Leavekey(a.EmployeeId))
                 .ToDictionaryAsync(g => g.Key, g => g.OrderBy(x => x.LeaveDateFrom).ToList());
        ;
        return data;
    }
}
public readonly record struct Leavekey(Guid EmpId);