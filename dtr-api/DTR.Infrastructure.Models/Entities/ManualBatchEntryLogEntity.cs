namespace DTR.Models;

public class ManualBatchEntryLogEntity  : BaseEntity
{
    public string BatchCode { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public TimeSpan? Time1 { get; set; }
    public TimeSpan? Time2 { get; set; } 
} 