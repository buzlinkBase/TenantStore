namespace DTR.Core;

public class HolidayInfo
{
    public Guid HolidayId  { get; set; }
    public  Guid AreaId   { get; set; }
    public DateOnly PayrollDate  { get; set; }
    public HolidayWorkType WorkType { get; set; } = HolidayWorkType.NonWorking;
    public HolidayType HolType { get; set; }
    public Guid EmployeeId  { get; set; } 
    public bool IsPaid { get; set; } 
    public ChangeSchedState State  { get; set; }

}
