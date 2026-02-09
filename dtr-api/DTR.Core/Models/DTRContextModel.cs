namespace DTR.Core; 

public class DTRContextBuilder
{
    private DTRContextModel _context = new DTRContextModel();
    public DTRContextBuilder WithCleanAttendance(Dictionary<AttendanceEmpId, List<Attendance>> attendance)
    {
        _context.CleanAttendance = attendance;
        return this;
    }

    public DTRContextBuilder WithOverTimeApplications(Dictionary<OTKey, OverTimeApplicationEntity?> ot)
    {
        _context.OverTimeApplications = ot;
        return this;
    }
    public DTRContextBuilder WithUnderTimeApplications(Dictionary<UTKey, UnderTimeApplication?> ut)
    {
        _context.UnderTimeApplications = ut;
        return this;
    }

    public DTRContextBuilder WithEmployees(List<Employee> employees)
    {
        _context.Employees = employees;
        return this;
    }

    public DTRContextBuilder WithShifts(Dictionary<CurrentTimeShiftKey, CurrentShift> shifts)
    {
        _context.AllShifts = shifts;
        return this;
    }

    public DTRContextBuilder WithLeaves(Dictionary<Leavekey, List<LeaveApplication>> leaves)
    {
        _context.Leaves = leaves;
        return this;
    }

    public DTRContextBuilder WithHolidays(Dictionary<Holidaykey, List<HolidayInfo>> holidays)
    {
        _context.Holidays = holidays;
        return this;
    }
    public DTRContextBuilder WithDayOffs(Dictionary<ResDaykey, CurrentRestDay> dayOffs)
    {
        _context.DayOffs = dayOffs;
        return this;
    }

    //public DTRContextBuilder WithOverTimeOverrides(Dictionary<OverrideOTKey, OverrideOT> overrides)
    //{
    //    _context.OverTimeOverrides = overrides;
    //    return this;
    //}

    public DTRContextBuilder WithCompanyPolicy(CompanyPolicyRule? policy)
    {
        _context.CompanyPolicy = policy ?? new CompanyPolicyRule();
        return this;
    }

    public DTRContextBuilder WithClientPolicy(Dictionary<ClientPolicyKey, ClientPolicyRule> policies)
    {
        _context.ClientPolicies = policies;
        return this;
    }
    public DTRContextBuilder WithEmployeePolicy(Dictionary<EmployeePolicyKey, EmployeePolicyRule> policies)
    {
        _context.EmployeePolicies = policies ?? new Dictionary<EmployeePolicyKey, EmployeePolicyRule>();
        return this;
    }

    public DTRContextModel Build()
    {
        return _context;
    }
}
public class DTRContextModel
{
    public Dictionary<AttendanceEmpId, List<Attendance>> CleanAttendance { get; set; }
    public Dictionary<OTKey, OverTimeApplicationEntity?> OverTimeApplications { get; set; }
    public Dictionary<UTKey, UnderTimeApplication?> UnderTimeApplications { get; set; }
    public List<Employee> Employees { get; set; }
    public Dictionary<CurrentTimeShiftKey, CurrentShift> AllShifts { get; set; }
    public Dictionary<Leavekey, List<LeaveApplication>> Leaves { get; set; }
    public Dictionary<Holidaykey, List<HolidayInfo>> Holidays { get; set; }
    public Dictionary<ResDaykey, CurrentRestDay> DayOffs { get; set; }
    public CompanyPolicyRule CompanyPolicy { get; set; }
    public Dictionary<ClientPolicyKey, ClientPolicyRule> ClientPolicies { get; set; }
    public Dictionary<EmployeePolicyKey, EmployeePolicyRule> EmployeePolicies { get; set; }
    public DTRCalcService? DtrService { get; set; }
    public TimeRangeLedger ValueCache { get; set; } = new();

}
