using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TenantStoreApi.Domain.DTOs
{
    [MessagePackObject]
    public partial class Testpack
    {
        [Key(0)]
        public Guid Id { get; set; }
    }
}
