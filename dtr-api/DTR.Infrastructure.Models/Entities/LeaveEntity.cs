
namespace DTR.Models;
public class LeaveApplication : BaseEntity
{
    public Guid LeaveId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateOnly LeaveDateFrom { get; set; }
    public DateOnly LeaveDateTo { get; set; }
    public LeaveDayType LeaveType { get; set; }
    public PayType PayType { get; set; }
    public virtual ICollection<LeaveApplicationDetail> Details { get; set; }
}

public class LeaveApplicationDetail : BaseEntity
{
    public Guid ApplicationId { get; set; }
    public virtual LeaveApplication Application { get; set; }
    public Guid EmployeeId { get; set; }
    public DateOnly LeaveDate { get; set; }
    public LeaveDayType LeaveType { get; set; }
    public PayType PayType { get; set; }

}
