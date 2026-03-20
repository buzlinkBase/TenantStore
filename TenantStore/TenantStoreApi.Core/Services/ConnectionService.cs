using Microsoft.EntityFrameworkCore;

namespace TenantStoreApi.Core.Services;

public class ConnectionService : BaseService<ConnectionStringStore>
{
    public ConnectionService(IUnitOfWorkService service) : base(service)
    {
    }

    public async Task<ConnectionQueryResponse?> GetConnection(Guid tenantId,string service)
    {
        var data = await GetQueryable(x =>
                        x.TenantId == tenantId &&
                        x.ServiceOwner== service &&
                        x.IsActive)
            .FirstOrDefaultAsync();

        if (data == null) return null;

        return new ConnectionQueryResponse
        {
            ConnectionString = data.ConnetionString,
            TenantId = data.TenantId,
            Success = true,
        };
    }
    public async Task AddAsync(ConnectionStringStore tenantConnection, CancellationToken token = default) => await Repository.AddAsync(tenantConnection, token);

}
