namespace DTR.Core;

public class LeaveApplicationServiceProvider  
{
    private readonly Dictionary<Leavekey, List<LeaveApplication>> _leaveApplications;
    private readonly Employee _employee;
    public LeaveApplicationServiceProvider(Dictionary<Leavekey, List<LeaveApplication>> leaveApplocations ,Employee currentEmployee)
    {
        _leaveApplications = leaveApplocations;
        _employee = currentEmployee;
    }
    public LeaveApplication? GetLeave(DateOnly date)
    {
        var key = new Leavekey(_employee.Id);
        if (_leaveApplications.TryGetValue(key, out var applications))
        {
            return applications.FirstOrDefault(app =>
                app.LeaveDateFrom <= date && date <= app.LeaveDateTo);
        }
        return null;
    }
}
