//using BuzlinkRepository;

//namespace Onepunch.Common.Lib.Entities;
//public class BaseEntity : EntityBase { }

//public class OutboxMessage : BaseEntity
//{
//    public string Key { get; set; } = Guid.NewGuid().ToString();
//    public string Topic { get; set; } = Guid.NewGuid().ToString();
//    public string Payload { get; set; } = string.Empty;
//    public DateTime? ProcessedOn { get; set; }
//    public DateTime? LastAttemptOn { get; set; }
//    public DateTime? NextRetryOn { get; set; }
//    public int RetryCount { get; set; } = 0;
//    public string Remarks { get; set; } = string.Empty;
//    public bool RetryForever { get; set; }
//    public new DateTime CreatedAt { get; set; } = DateTime.UtcNow;
//    public new OutBoxState Status
//    {
//        get => EnumParserConfig.SafeParseEnum(base.Status, OutBoxState.INVALID);
//        set => base.Status = value.ToString();
//    }
//}