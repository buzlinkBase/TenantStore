using BuzlinkRepository;

namespace Onepunch.Common.Lib.Entities;

public class BaseEntity : EntityBase { }
public class OutboxMessage : BaseEntity
{
    public Guid EventId { get; set; }
    public Guid? CausationId { get; set; }
    public Guid? CorrelationId { get; set; }
    public Guid? AggregateId { get; set; } // e.g. tableId
    public string AggregateType { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
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