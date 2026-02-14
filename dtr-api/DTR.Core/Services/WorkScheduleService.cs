
namespace DTR.Core;

public readonly record struct CurrentTimeShiftKey(Guid EmpId, DateOnly ShiftDate);
public class WorkScheduleService
{
    private readonly WorkPlanScheduleService _changeSchedService;
    private readonly TimeShiftService _tsService;
    public WorkScheduleService(WorkPlanScheduleService service,
        TimeShiftService timeShiftService)
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
    private readonly IDTRUnitOfWork _uow;

    public WorkPlanScheduleService(IDTRUnitOfWork uow)
    {
        _uow = uow;
    }
    public async Task<Dictionary<CurrentTimeShiftKey, WorkSchedulePlan?>> GetAllCustomShiftsAync(DateOnly fromDate, DateOnly toDate)
    {
        return await _uow.Repository
              .FindAll<WorkSchedulePlan>()
              .Include(x => x.Employee)
              .ThenInclude(x => x.TimeShift)
              .AsNoTracking()
              .Where(x => x.PayrollDate >= fromDate && x.PayrollDate <= toDate)
              .GroupBy(x => new CurrentTimeShiftKey(x.EmployeeId, x.PayrollDate))
              .ToDictionaryAsync(key => key.Key, val => val.FirstOrDefault())
              ;
    }
}
public class TimeShiftService
{
    private readonly IDTRUnitOfWork _uow;

    public TimeShiftService(IDTRUnitOfWork uow)
    {
        _uow = uow;
    }
    public async Task<List<TimeShift>> GetAllShifts()
    {
        return await _uow.Repository
            .FindAll<TimeShift>()
            .ToListAsync();
    }
}
