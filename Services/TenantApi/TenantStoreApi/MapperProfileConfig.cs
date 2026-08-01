

using Mapster;

namespace TenantStoreApi
{
    public class MapperProfileConfig : IRegister
    {
        public void Register(TypeAdapterConfig config)
        {
            config.NewConfig<TenantCreationRequested, Tenant>();
            config.NewConfig<UpdateTenant, Tenant>();
            config.NewConfig<Tenant, TenantModel>();
            config.NewConfig<ConnectionStringPayload, ConnectionStringStore>().TwoWays();
        }
    }
}
