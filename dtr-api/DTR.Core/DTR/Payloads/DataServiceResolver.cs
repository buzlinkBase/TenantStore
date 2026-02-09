using Grpc.Core;

namespace DTR.Core;

public class DataServiceResolver
{
    private readonly EmployeeService _employeeService;
    private readonly WorkScheduleService _workScheduleService;
    private readonly LeaveApplicationService _leaveApplicationService;
    private readonly AttendanceService _attendanceService;
    
    public DataServiceResolver(
        TimeShiftService timeShiftService,
        EmployeeService employeeService,
        WorkScheduleService workScheduleService,
        LeaveApplicationService leaveApplicationService,
        AttendanceService attendanceService)
    {
        _employeeService = employeeService;
        _workScheduleService = workScheduleService;
        _leaveApplicationService = leaveApplicationService;
        _attendanceService = attendanceService;
    }
    public EmployeeService EmployeeService => _employeeService;
    public AttendanceService  AttendanceService => _attendanceService;
    public WorkScheduleService WorkRotationPlanService => _workScheduleService;
    public LeaveApplicationService LeaveAppService  => _leaveApplicationService;
}
