namespace DTR.Core;

public record GetCurrentAttendancePayload(Dictionary<AttendanceEmpId, List<Attendance>> AllEmployeesAttendances,
    Employee Employee);
public class DTRProcessorPayloadBuilder
{
    private DTRProcessorPayload _payload;
    public DTRProcessorPayloadBuilder()
    {
        _payload = new DTRProcessorPayload();
    }
    public DTRProcessorPayloadBuilder SetCurrentAtt(List<Attendance> currentAttendance)
    {
        _payload.Data.CurrentAttendance = currentAttendance;
        return this;
    }
    public DTRProcessorPayloadBuilder SetCurrentLeave(LeaveApplication? leave)
    {
        _payload.Data.CurrentLeave = leave;
        return this;
    }


    public DTRProcessorPayloadBuilder SetCurrentDate(DateOnly currentDate)
    {
        _payload.Data.CurrentDate = currentDate;
        return this;
    }
    public DTRProcessorPayloadBuilder SetEmployee(Employee employee)
    {
        _payload.Data.Employee = employee;
        return this;
    }

    public DTRProcessorPayloadBuilder SetCurrentShift(CurrentShift? shift)
    {
        _payload.Data.CurrentShift = shift;
        return this;
    }
    //public DTRProcessorPayloadBuilder SetHolidays(HolidayCollection holidays)
    //{
    //    _payload.Data.Holidays = holidays;
    //    return this;
    //}
    public DTRProcessorPayloadBuilder SetCompanyPolicy(CompanyPolicyRule policy)
    {
        _payload.Data.CompanyPolicy = policy;
        return this;
    }
    public DTRProcessorPayloadBuilder SetEmployeePolicy(EmployeePolicyRule? policy)
    {
        _payload.Data.EmployeePolicy = policy;
        return this;
    }
    public DTRProcessorPayload Build()
    {
        return _payload;
    }
    public DTRProcessorPayloadBuilder SetDayOff(Dictionary<ResDaykey, CurrentRestDay> currentDayoff)
    {
        _payload.Data.CurrentDayoffs = currentDayoff;
        return this;
    }
    public DTRProcessorPayloadBuilder SetDTRContext( DTRContextModel dTRContext)
    {
        _payload.ContextModel = dTRContext;
        return this;
    }

    //providers
    public DTRProcessorPayloadBuilder SetOTProvider(OverTimeServiceProvider provider)
    {
        _payload.Provider.OTProvider = provider;
        return this;
    }
    public DTRProcessorPayloadBuilder SetUTProvider(UnderTimeServiceProvider provider)
    {
        _payload.Provider.UTProvider = provider;
        return this;
    }
    public DTRProcessorPayloadBuilder SetHolidayProvider(HolidayProviderBase holidayProvider)
    {
        _payload.Provider.HolidayProvider = holidayProvider;
        return this;
    }
    public DTRProcessorPayloadBuilder SetLeaveProvider(LeaveApplicationServiceProvider leaveApplicationServiceProvider)
    {
        _payload.Provider.LeaveProvider = leaveApplicationServiceProvider;
        return this;
    }

    public DTRProcessorPayloadBuilder SetAttendanceProvider(AttendanceProvider attendanceProvider)
    {
        _payload.Provider.AttendanceProvider = attendanceProvider;
        return this;
    }
    public DTRProcessorPayloadBuilder SetCurrentShiftProvider(ICurrentShiftProvider CurrentShiftProvider)
    {
        _payload.Provider.CurrentShiftProvider = CurrentShiftProvider;
        return this;
    }

    public DTRProcessorPayloadBuilder SetClientPolicyProvider(ClientPolicyProvider clientPolicyProvider)
    {
        _payload.Provider.ClientPolicyProvider = clientPolicyProvider;
        return this;
    }
}
public class DTRProcessorPayload
{
    public DataPayload Data { get; set; } = new();
    public ProvidersPayload Provider { get; set; } = new();
    public SpecEvaluationCache SharedSpecCache { get; } = new();
    public TimeRangeLedger Ledger { get; } = new();
    public DTRContextModel ContextModel { get; set; }=new();

}
public class DataPayload
{
    public List<Attendance> CurrentAttendance { get; set; }
    public Employee Employee { get; set; }
    public DateOnly CurrentDate { get; set; }
    public DateOnly PayrollStartDate { get; set; }
    public CurrentShift CurrentShift { get; set; }
    public CompanyPolicyRule CompanyPolicy { get; set; }
    public EmployeePolicyRule EmployeePolicy { get; set; }
    public Dictionary<ResDaykey, CurrentRestDay> CurrentDayoffs { get; set; }
    public LeaveApplication? CurrentLeave { get; set; }
}
public class ProvidersPayload
{
    public AttendanceProvider AttendanceProvider { get; set; }
    public ICurrentShiftProvider CurrentShiftProvider { get; set; }
    public OverTimeServiceProvider OTProvider { get; set; }
    public UnderTimeServiceProvider UTProvider { get; set; }
    public HolidayProviderBase HolidayProvider { get; set; }
    public LeaveApplicationServiceProvider LeaveProvider { get; set; }
    public ClientPolicyProvider ClientPolicyProvider { get; set; }
    public DTRContextModel DtrContextModel { get; set; }
}