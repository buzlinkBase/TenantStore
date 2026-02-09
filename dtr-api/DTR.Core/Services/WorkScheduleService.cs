
namespace DTR.Core;

public readonly record struct CurrentTimeShiftKey(Guid EmpId, DateOnly ShiftDate);
public class WorkScheduleService
{
    private readonly WorkPlanScheduleService _changeSchedService;
    private readonly TimeShiftService _tsService;
    public WorkScheduleService(WorkPlanScheduleService service, TimeShiftService timeShiftService)
    {
        _changeSchedService = service;
        _tsService = timeShiftService;
    }
    public async Task<Dictionary<CurrentTimeShiftKey, CurrentShift>> GetShiftsAsync(DateOnly fromDate, DateOnly toDate, IEnumerable<Employee> employees)
    {
        var shifCollection = new Dictionary<CurrentTimeShiftKey, CurrentShift>();
        if (employees == null || !employees.Any())
        {
            return shifCollection;
        }
        var allShifts = await _tsService.GetAllShifts();
        var employeesActiveShifts = await _changeSchedService.GetAllCustomShiftsAync(fromDate, toDate);

        var schedShift = new OverrideSchedule(employeesActiveShifts, allShifts);
        schedShift.SetNextHandler(new FallbackSchedule(allShifts));

        for (DateOnly i = fromDate; i <= toDate; i = i.AddDays(1))
        {
            foreach (var employee in employees)
            {
                var key = new CurrentTimeShiftKey(employee.Id, i);
                var shift = schedShift.Handle(employee, i);
                if (shift != null)
                {
                    shifCollection[key] = shift;
                }
            }
        }
        return shifCollection;
    }
}

public class WorkPlanScheduleService
{
    private readonly IWorkSheduleMessage _client;
    public WorkPlanScheduleService(IWorkSheduleMessage client)
    {
        _client = client;
    }
    public async Task<Dictionary<CurrentTimeShiftKey, WorkSchedulePlan?>> GetAllCustomShiftsAync(DateOnly fromDate, DateOnly toDate)
    {
        var request = new DateRequestPayload(fromDate, toDate);
        ResponseModel<List<WorkSchedulePlan>> response = await _client.GetAll(request);
        if (response?.Data == null || !response.Data.Any())
            return new Dictionary<CurrentTimeShiftKey, WorkSchedulePlan?>();

        // We use the record's value-based equality. 
        // Ensure the Data is already local (which it is, because it's a response from _client)
        var dictionary = response.Data
            .GroupBy(x => new CurrentTimeShiftKey(x.EmployeeId, x.PayrollDate))
            .ToDictionary(g => g.Key, g => g.FirstOrDefault());
        return dictionary;
    }
}
public class TimeShiftService
{
    private readonly ITimeShiftMessage _client;
    public TimeShiftService(ITimeShiftMessage client)
    {
        _client = client;
    }
    public async Task<List<TimeShift>> GetAllShifts()
    {
        var response = await _client.GetAll();
        return response.Data ?? new List<TimeShift>();
    }
}
