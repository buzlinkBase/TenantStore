namespace DTR.Core;

public class OverTimeServiceProvider  
{
    private readonly Dictionary<OTKey, OverTimeApplicationEntity?> _overTimeApplications;
    private readonly Employee _employee;
    public OverTimeServiceProvider( Dictionary<OTKey,OverTimeApplicationEntity?> overTimeApplications,Employee currentEmployee)
    {
        _overTimeApplications = overTimeApplications;
        _employee = currentEmployee;
    } 

    public OverTimeApplicationEntity? GetOT(DateOnly date)
    {
        var key = new OTKey(_employee.Id, date);
        return _overTimeApplications.TryGetValue(key,out var application) ? application : null;
    }
    public bool HasOTApplication(DateOnly date)
    {
        return GetOT(date) != null; 
    }
}
 