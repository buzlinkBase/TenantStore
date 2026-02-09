namespace DTR.Models;

public class OfficialBusiness   : BaseEntity
{
    public Guid EmployeeId { get; set; }
    public DateOnly ObDate { get; set; }
    public TimeSpan FromTime { get; set; }
    public TimeSpan ToTime { get; set; }
    public string Remarks  { get; set; }=string.Empty;
} 

