namespace DTR.Core;

public class UnderTimeServiceProvider  
{
    private readonly Dictionary<UTKey, UnderTimeApplication?> _UnderTimeApplications;
    private readonly Employee _employee;
    public UnderTimeServiceProvider(Dictionary<UTKey,UnderTimeApplication?> UnderTimeApplications,Employee currentEmployee)
    {
        _UnderTimeApplications = UnderTimeApplications;
        _employee = currentEmployee;
    } 

    public UnderTimeApplication? GetUT(DateOnly date)
    {
        var key = new UTKey(_employee.Id, date);
        return _UnderTimeApplications.TryGetValue(key,out var application) ? application : null;
    }
    public bool HasUTApplication(DateOnly date)
    {
        return GetUT(date) != null; 
    }
}
 