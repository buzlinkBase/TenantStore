using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        public string? Role { get; set; }
        [Key(5)]
        public string Status { get; set; }
    }
}
