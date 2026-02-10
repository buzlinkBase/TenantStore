using BuzlinkRepository;

namespace Onepunch.Common.Lib.Entities;

public class BaseEntity : EntityBase { }
public class OutboxMessage : BaseEntity
{
    public string Key  { get; set; }  
    public string Topic { get; set; }  
    public string Payload { get; set; } = string.Empty;
    public DateTime? ProcessedOn { get; set; }
    public DateTime? LastAttemptOn { get; set; }
    public DateTime? NextRetryOn { get; set; }
    public int RetryCount { get; set; } = 0;
    public string Remarks { get; set; } = string.Empty;
    public new OutBoxState Status
    {
        get => EnumParserConfig.SafeParseEnum(base.Status, OutBoxState.INVALID);
        set => base.Status = value.ToString();
    }
}