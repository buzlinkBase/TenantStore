using Refit;
namespace DTR.Core.Interfaces;

public interface IEmployeeMessage
{
    [Get("/api/v1/employees/{id}")]
    Task<ResponseModel<Employee?>> GetOne(Guid id);

    [Post("/api/v1/employees/all")]
    Task<ResponseModel<List<Employee>>> GetDtrEmployees([Body] EmployeeRequestPayload payload);
}
public interface ITimeShiftMessage
{
    [Get("/api/v1/timeshifts")]
    Task<ResponseModel<List<TimeShift>>> GetAll();
}

public interface IWorkSheduleMessage
{
    [Get("/api/v1/workScheduleplans/range")]
    Task<ResponseModel<List<WorkSchedulePlan>>> GetAll([Query] DateRequestPayload payload);
}

public interface ILeavesMessage
{
    [Get("/api/v1/leaves/range")]
    Task<ResponseModel<List<LeaveApplication>>> GetAll([Query] DateEmployeeRequestPayload payload);
}
