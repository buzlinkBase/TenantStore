namespace DTR.Core;

public class KafkaSettings
{
    public const string SectionName = "KafkaSettings";
    public string BootstrapServers { get; set; } = string.Empty;
    public KafkaTopics Topics { get; set; } = new();
}

public class KafkaTopics
{
    public string Employee { get; set; } = string.Empty;
    public string TimeShift { get; set; } = string.Empty;
    public string Holiday  { get; set; } = string.Empty;
    public string ChangeHoliday { get; set; } = string.Empty;
    public string WorkRotationPlan { get; set; } = string.Empty;
    public string ChangeRestDay { get; set; } = string.Empty;
    public string SetRestDayDate { get; set; } = string.Empty;
    public string LeaveApplication { get; set; } = string.Empty;
    public string TenantCreated { get; set; } = string.Empty;

}