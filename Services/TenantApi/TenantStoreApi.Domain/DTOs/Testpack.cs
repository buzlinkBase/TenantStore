using MessagePack;

namespace TenantStoreApi.Domain.DTOs
{
    [MessagePackObject]
    public partial class Testpack
    {
        [Key(0)]
        public Guid Id { get; set; }
    }
}
