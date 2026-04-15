

using Mapster;

namespace TenantStoreApi
{
    public class MapperProfileConfig : IRegister
    {
        public void Register(TypeAdapterConfig config)
        {
            config.NewConfig<TenantCreationRequest, Tenant>();
            config.NewConfig<UpdateTenant, Tenant>();
            config.NewConfig<Tenant, TenantModel>();
            config.NewConfig<CreateBranch, Branch>();
            config.NewConfig<UpdateBranch, Branch>();
            config.NewConfig<Branch, BranchModel>();
            config.NewConfig<ConnectionStringPayload,ConnectionStringStore >().TwoWays();
        }
    }
}
