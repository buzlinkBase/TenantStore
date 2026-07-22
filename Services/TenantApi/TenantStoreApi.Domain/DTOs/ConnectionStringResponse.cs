using MessagePack;

namespace TenantStoreApi.Domain.DTOs;

[MessagePackObject]
public class ConnectionStringResponse
{
    [Key(0)]
    public string? ConnectionString { get; set; }
}
