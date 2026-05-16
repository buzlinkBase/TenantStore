using Microsoft.Extensions.Configuration;
using Onepunch.Auth.Domain.Entities;
using OnePunch.Auth.Core;
using OnePunch.Auth.Core.Services;

namespace Onepunch.Auth.Core.Services
{
    public class TenantRequestService : BaseService<TenantCreationRequestStatus>
    {

        private readonly IConfiguration _configuration;
        public TenantRequestService(IUnitOfWorkService uow,
            IConfiguration configuration) : base(uow)
        { 
            _configuration = configuration;
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
