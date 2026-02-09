namespace DTR.Models;

public class OverTimeApplicationEntity  : BaseEntity
{
    public Guid EmployeeId { get; set; }
    public virtual Employee Employee { get; set; }
    public DateOnly OTDate { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public bool FlexiEndTime { get; set; }
    public bool PaidByNetDutyTime   { get; set; }  
    public double OverTimeThreshold { get; set; }
    public string Remarks { get; set; } = string.Empty;
    public OTStatus OTStatus { get; set; }
    public double OTMinutes { get; set; }
    public double OTBeforeOverride  { get; set; }

}

