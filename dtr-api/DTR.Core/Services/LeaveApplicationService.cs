
namespace DTR.Core;
public class LeaveApplicationService  
{
    private readonly ILeavesMessage _client;
    public LeaveApplicationService(ILeavesMessage client)
    {
        _client = client;
    }    
    public async Task<Dictionary<Leavekey, List<LeaveApplication>>> FindByDateRangeAsync(DateOnly fromDate, DateOnly toDate, HashSet<Guid> employeeIds)
    {
        var request=new DateEmployeeRequestPayload(fromDate, toDate, employeeIds) ;
        var result = await _client.GetAll(request);
        if (result.Data == null || !result.Data.Any()) return new Dictionary<Leavekey, List<LeaveApplication>>();

        var data = result.Data.GroupBy(a => new Leavekey(a.EmployeeId))
                 .ToDictionary(g => g.Key, g => g.OrderBy(x => x.LeaveDateFrom).ToList());
        return data;
    }
}

public readonly record struct Leavekey(Guid EmpId);