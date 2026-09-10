using MessagePack;

namespace Onepunch.Common.Lib
{
    [MessagePackObject]
    public class AccountMemberShipQuery
    {
        [Key(0)]
        public Guid TenantId { get; set; }
        [Key(1)]
        public Guid UserId { get; set; }
        [Key(3)]
        public string? TenantName { get; set; }
        [Key(4)]
        public List<string> Roles { get; set; } = new();
        [Key(5)]
        public string Status { get; set; }
        // 6, not 2 -- Key(2) is a retired gap from a removed field, don't reuse it.
        [Key(6)]
        public List<string> Permissions { get; set; } = new();
    }
}
