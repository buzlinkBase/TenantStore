namespace DTR.Core;

public class CurrentRestDay
{
    public Employee Employee { get; set; }
    //public Guid EmployeeId  { get; set; }
    public DateOnly PayrollDate  { get; set; }
    public DayName DayName { get; set; }
    public ChangeSchedState State  { get; set; } 
}
