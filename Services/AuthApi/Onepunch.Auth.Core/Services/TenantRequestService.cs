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
        public async Task<TenantCreationRequestStatus?> FindByTenant(Guid tenantId)
        {
            return await Context.TenantCreationRequests.FirstOrDefaultAsync(x => x.TenantId == tenantId);
        }

        /// <summary>
        /// The user's most recent still-in-flight tenant creation request (not yet Created, not
        /// expired) -- lets WorkspaceService.Create resume an existing request instead of firing
        /// another TenantCreationRequested for the same user, which is what turns a page refresh
        /// during "Setting up workspace..." into an extra dormant/stuck-Provisioning tenant.
        /// </summary>
        public async Task<TenantCreationRequestStatus?> FindPendingByUser(Guid userId)
        {
            return await Context.TenantCreationRequests
                .Where(x => x.UserId == userId
                    && x.Status != TenantCreationStatus.Created
                    && x.RequestExpiry > DateTime.UtcNow)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync();
        }
        public async Task Store(TenantCreationRequestStatus model)
        {
            await CreateAsync(model);
        }

        public async Task<List<TenantCreationRequestStatus>> FindExpired()
        {
            return Uow.Context.TenantCreationRequests
                .Where(x => x.RequestExpiry < DateTime.UtcNow &&
                x.Status != TenantCreationStatus.Created)
                .ToList();
            ;
        }

    }
}
