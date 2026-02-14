 

namespace DTR.Core;

public class CurrentRangeDTRPayload
{
    public static async Task<DTRContextModel> SetPayload(bool canprocess,
        DTRRequestPayload payload,
        DataServiceResolver dataServiceProvider,
        IDTRUnitOfWork uow,
        bool removeDoublePunch = true)
    {
        var (fromDate, toDate) = GetDateRange(payload);
        var cleanAttendance = await LoadCleanAttendance(canprocess, payload, dataServiceProvider.AttendanceService, removeDoublePunch);
        var employees = ExtractEmployees(payload, uow, cleanAttendance);
        var employeeIds = new HashSet<Guid>(employees.Select(e => e.Id));
        var clientIds = ExtractClientIds(employees);
        var shiftsTask = dataServiceProvider.WorkRotationPlanService.GetShiftsAsync(fromDate, toDate, employees);
        var leavesTask = dataServiceProvider.LeaveAppService.FindByDateRangeAsync(fromDate, toDate, employeeIds);
        var holidaysTask = new HolidayService(uow).FindByRangeAsync(fromDate, toDate, employees);
        var overtimeTask = new OverTimeApplicationService(uow).FindByDateRangeAsync(fromDate, toDate, employeeIds);
        var undertimeTask = new UnderTimeApplicationService(uow).FindByDateRangeAsync(fromDate, toDate, employeeIds);
        var dayOffsTask = new ChangeRestDayService(uow).GetAllDayOffAsync(fromDate, toDate, employees);

        var companyPolicy = await LoadCompanyPolicy(uow);
        var clientPolicy = await LoadClientPolicy(uow, companyPolicy, clientIds);
        var employeePolicy = await LoadEmployeePolicy(uow, employeeIds);

        await Task.WhenAll(shiftsTask, leavesTask, holidaysTask, overtimeTask, undertimeTask, dayOffsTask);

        return new DTRContextBuilder()
            .WithCleanAttendance(cleanAttendance)
            .WithEmployees(employees)
            .WithShifts(await shiftsTask)
            .WithLeaves(await leavesTask)
            .WithHolidays(await holidaysTask)
            .WithDayOffs(await dayOffsTask)
            .WithOverTimeApplications(await overtimeTask)
            .WithUnderTimeApplications(await undertimeTask)
            .WithCompanyPolicy(companyPolicy)
            .WithClientPolicy(clientPolicy)
            .WithEmployeePolicy(employeePolicy)
            .Build();
    }

    private static (DateOnly fromDate, DateOnly toDate) GetDateRange(DTRRequestPayload payload)
    {
        var from = payload.FromDate.AddDays(TimeAllowance.AttLookbackDays);
        var to = payload.ToDate.AddDays(TimeAllowance.AttLookforward);
        return (from, to);
    }

    private static async Task<Dictionary<AttendanceEmpId, List<Attendance>>> LoadCleanAttendance(
        bool canProcess,
        DTRRequestPayload payload,
        AttendanceService attendanceService,
        bool removeDoublePunch)
    {

        var (fromDate, toDate) = GetDateRange(payload);
        var rawLogs = await attendanceService.LoadAttForDTRProcess(fromDate, toDate, canProcess, payload.EmployeeId, payload.DepartmentId, payload.ClientId, payload.PayrollGroupId);
        var util = new AttendanceUtility(rawLogs);
        var gap = removeDoublePunch ? TimeAllowance.DoublePunchGap : 0;
        return util.RemoveDoublePunch(gap);
    }

    private static List<Employee> ExtractEmployees(
        DTRRequestPayload payload,
        IDTRUnitOfWork uow,
        Dictionary<AttendanceEmpId, List<Attendance>> cleanAttendance)
    {
        if (payload.EmployeeId != null)
        {
            var emp = uow.Repository.FindOne<Employee>(payload.EmployeeId.Value);
            return emp != null ? new List<Employee> { emp } : new List<Employee>();
        }
        // Build predicate with AND logic
        var employees = uow.Repository.Find<Employee>(x =>
            (payload.ClientId == null || x.ClientId == payload.ClientId) &&
            (payload.PayrollGroupId == null || x.PayrollGroupId == payload.PayrollGroupId) &&
            (payload.DepartmentId == null || x.DepartmentId == payload.DepartmentId)
        ).ToList();

        return employees;
    }

    private static List<Guid?> ExtractClientIds(List<Employee> employees)
    {
        return employees
            .Where(e => e.ClientId.HasValue && e.ClientId != Guid.Empty)
            .Select(e => e.ClientId)
            .ToList();
    }

    private static async Task<CompanyPolicyRule> LoadCompanyPolicy(IDTRUnitOfWork uow)
    {
        var settings = await new GeneralSettingService(uow).GetSettingsAsync("Company");
        return new CompanyPolicyService().Transform(settings);
    }

    private static async Task<Dictionary<ClientPolicyKey, ClientPolicyRule>> LoadClientPolicy(IDTRUnitOfWork uow, CompanyPolicyRule companyPolicy, List<Guid?> clientIds)
    {
        var ids = new HashSet<string>(clientIds.Select(id => id!.Value.ToString()));
        var settings = await new GeneralSettingService(uow).GetSettingsAsync("Client", ids);
        return new ClientPolicyService().Transform(companyPolicy, settings);
    }

    private static async Task<Dictionary<EmployeePolicyKey, EmployeePolicyRule>> LoadEmployeePolicy(IDTRUnitOfWork uow, HashSet<Guid> employeeIds)
    {
        var ids = new HashSet<string>(employeeIds.Select(id => id.ToString()));
        var settings = await new GeneralSettingService(uow).GetSettingsAsync("Employee", ids);
        return new EmployeePolicyService().Transform(settings);
    }
}

public class EmployeeComparer : IEqualityComparer<Employee>
{
    public bool Equals(Employee? x, Employee? y) => x?.Id == y?.Id;
    public int GetHashCode(Employee obj) => obj.Id.GetHashCode();
}

public record struct DTRRequestPayload(
    DateOnly FromDate,
    DateOnly ToDate,
    Guid? DepartmentId,
    Guid? EmployeeId,
    Guid? ClientId,
    Guid? PayrollGroupId);

public record struct EmployeeRequestPayload(
    Guid? DepartmentId,
    Guid? EmployeeId,
    Guid? ClientId,
    Guid? PayrollGroupId);

public record DateRequestPayload(
    DateOnly FromDate,
    DateOnly ToDate);

public record DateEmployeeRequestPayload : DateRequestPayload
{
    public DateEmployeeRequestPayload(DateOnly FromDate, DateOnly ToDate, HashSet<Guid> EmployeeIds)
        : base(FromDate, ToDate)
    {
    }
}
