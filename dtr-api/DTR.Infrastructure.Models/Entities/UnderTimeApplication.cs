namespace DTR.Models;

public class UnderTimeApplication   : BaseEntity
{
    public Guid EmployeeId { get; set; }
    public virtual Employee Employee { get; set; }
    public DateOnly PayrollDate  { get; set; }
    public double UTMinutes { get; set; }
    public string Remarks { get; set; } = string.Empty;
    public OTStatus OTStatus { get; set; }
}

