namespace DTR.Models;
public class ChangeHoliday : BaseEntity 
{
    public Guid BatchEntryId { get; set; }
    public Guid HolidayId { get; set; }
    public Guid EmployeeId { get; set; }
    //public virtual Holiday Holiday { get; set; }
    //public virtual Employee Employee { get; set; }
    public DateOnly PayrollDate { get; set; }
    public ChangeSchedState State { get; set; }
}