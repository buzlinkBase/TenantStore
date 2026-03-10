using AutoMapper;

namespace TenantStoreApi
{
    public class MapperProfileConfig :Profile
    {
        public MapperProfileConfig()
        {
            CreateMap<UserCreated, Tenant>();
            CreateMap<UpdateTenant, Tenant>();
            CreateMap<Tenant, TenantModel>();

            CreateMap<CreateBranch, Branch>();
            CreateMap<UpdateBranch, Branch>();
            CreateMap<Branch, BranchModel>();

        }
    }
}
