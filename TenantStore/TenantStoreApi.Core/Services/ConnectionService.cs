using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TenantStoreApi.Core.Services;

public class ConnectionService : BaseService<TenantConnection>
{
    public ConnectionService(IUnitOfWorkService service) : base(service)
    {
    }

    public async Task<string> GetConnection(Guid tenantId)
    {
        return GetQueryable(x => x.TenantId == tenantId)
            .FirstOrDefault()?.ConnetionString ?? "";
    }

}
