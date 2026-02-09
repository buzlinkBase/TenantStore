namespace DTR.Models;

public class RestDay : BaseEntity
{
    public DayName DayName { get; set; }
    public Guid EmployeeId { get; set; }
    public virtual Employee? Employee { get; set; }
}


/// <summary>
/// THIS TABLE CONTAINS SPECIFIC OFF DATES
/// </summary>
[DisableSoftDelete]
public class RestDayDate : BaseEntity 
{
    public Guid EmployeeId { get; set; }
    public virtual Employee? Employee { get; set; }
    public DateOnly PayrollDate  { get; set; } 
}


