namespace DTR.Core;


public class ManualBatchEntryLogResponse
{
    public Guid Id { get; set; }
    public string BatchCode { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string FromDate { get; set; }
    public string ToDate { get; set; }
    public TimeSpan? Time1 { get; set; }
    public TimeSpan? Time2 { get; set; }
}