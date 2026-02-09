using MessagePack;

namespace DTR.Models; 

[MessagePackObject]
public partial class LeaveApplication
{
    [Key(0)]
    public Guid Id  { get; set; }
    [Key(1)]
    public Guid LeaveId { get; set; }
    [Key(2)]
    public Guid EmployeeId { get; set; }
    [Key(3)]
    public DateOnly LeaveDateFrom { get; set; }
    [Key(4)]
    public DateOnly LeaveDateTo { get; set; }
    [Key(5)]
    public LeaveDayType LeaveType { get; set; }
    [Key(6)]
    public PayType PayType { get; set; }
    [Key(7)]
    public List<LeaveApplicationDetail> Details { get; set; }
}

[MessagePackObject]
public class LeaveApplicationDetail  
{
    [Key(0)]
    public Guid Id { get; set; }
    [Key(1)]
    public Guid ApplicationId { get; set; }
    [Key(2)]
    public Guid EmployeeId { get; set; }
    [Key(3)]
    public DateOnly LeaveDate { get; set; }
    [Key(4)]
    public LeaveDayType LeaveType { get; set; }
    [Key(5)]
    public PayType PayType { get; set; }
}
