namespace DTR.Core;

public class TimeContext
{
    public DTRProcessorPayload Payload  { get; set; }
    public TimeRange CanonicalTimeRange { get; set; } //untouch TimeRange 
}

public class DisplayContext
{
    public TimeContext TimeContext { get; set; }
    public PipeLineResult PipeLineResult { get; set; }
}

