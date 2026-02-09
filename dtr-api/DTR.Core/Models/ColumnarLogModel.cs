namespace DTR.Core;

public class ColumnarLogModel
{
    public Guid EmployeeId { get; set; }
    public Guid? ClientId { get; set; }
    public Guid? PayrollGroupId { get; set; }
    public Guid? DepartmentId  { get; set; }
    public string EmpNo { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public DateOnly WorkDate { get; set; }
    public string ShiftName { get; set; } = string.Empty;
    public DateTime ShiftStart { get; set; }
    public DateTime ShiftEnd { get; set; }
    public DateTime? BreakOut { get; set; }
    public DateTime? BreakIn { get; set; }

    public AttInfo? Log1 { get; set; }
    public AttInfo? Log2 { get; set; }
    public AttInfo? Log3 { get; set; }
    public AttInfo? Log4 { get; set; }
    public AttInfo? Log5 { get; set; }
    public AttInfo? Log6 { get; set; }
    public AttInfo? Log7 { get; set; }
    public AttInfo? Log8 { get; set; }
    public AttInfo? Log9 { get; set; }
    public AttInfo? Log10 { get; set; }
    public AttInfo? Log11 { get; set; }
    public AttInfo? Log12 { get; set; }
    public AttInfo? Log13 { get; set; }
    public AttInfo? Log14 { get; set; }
    public AttInfo? Log15 { get; set; }
    public AttInfo? Log16 { get; set; }
    public AttInfo? Log17 { get; set; }
    public AttInfo? Log18 { get; set; }
    public AttInfo? Log19 { get; set; }
    public AttInfo? Log20 { get; set; }
}
public class RowLogModel
{
    public Guid EmployeeId { get; set; }
    public string EmpNo { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public DateOnly WorkDate { get; set; }
    public string ShiftName { get; set; } = string.Empty;
    public DateTime ShiftStart { get; set; }
    public DateTime ShiftEnd { get; set; }
    public DateTime? BreakOut { get; set; }
    public DateTime? BreakIn { get; set; }

    public AttInfo? Log1 { get; set; }
}
public class AttInfo
{
    public AttInfo(Guid attId, DateTime workTime)
    {
        AttId = attId;
        WorkTime = workTime;
    }
    public Guid AttId { get; set; }
    public DateTime WorkTime { get; set; }
    public static AttInfo Set(Guid Id, DateTime workTime) => new AttInfo(Id, workTime);
}
