using Onepunch.Auth.Domain.Entities;
using OnePunch.Auth.Core;
using OnePunch.Auth.Core.Services;

namespace Onepunch.Auth.Core.Services
{
    public class TenantRequestService : BaseService<TenantCreationRequestStatus>
    {
        public TenantRequestService(IUnitOfWorkService uow) : base(uow)
        {
        }
        public async Task<TenantCreationRequestStatus?> FindOne(Guid tenantId)
        {
            return await Context.TenantCreationRequests.FirstOrDefaultAsync(x => x.TenantId == tenantId);
        }

        public async Task Store(TenantCreationRequestStatus model)
        {
            await CreateAsync(model);
        }
    }
}
