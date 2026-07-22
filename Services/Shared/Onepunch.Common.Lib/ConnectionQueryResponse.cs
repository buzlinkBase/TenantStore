using MessagePack;

namespace Onepunch.Common.Lib;

[MessagePackObject]
public record ConnectionQueryResponse
{
    [Key(0)]
    public Guid TenantId { get; set; }
    [Key(1)]
    public bool Success { get; set; } = false;
    [Key(2)]
    public string ConnectionString { get; set; } = string.Empty;
}