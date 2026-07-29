
using BuzlinkRepository;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Onepunch.Common.Lib.Exceptions;

namespace TenantStoreApi.Core.Services;

public class TenantService : BaseService<Tenant>
{
    private readonly IMapper _mapper;
    private readonly ITenantProvider _tenantProvider;
    public TenantService(IUnitOfWorkService service,
        IMapper mapper,
        ITenantProvider tenantProvider,
        IPublishEndpoint publisher,

        PasswordCrypto crypto) : base(service)
    {
        _mapper = mapper;
        _tenantProvider = tenantProvider;
    }

    protected override async Task<EvaluationResult> CreateValidatorAsync(Tenant model, CancellationToken token)
    {
        await base.CreateValidatorAsync(model, token);
        Guard.ThrowIfNull(model, "Account Payload");
        return EvaluationResult.OK;
    }

    public async Task<TenantModel> CreateTenant(TenantCreationRequested payload, CancellationToken token)
    {
        var tenant = _mapper.Map<Tenant>(payload);
        tenant.Id = payload.TenantId;
        await CreateAsync(tenant, token);
        await Uow.SaveChangesAsync(token);
        _tenantProvider.SetTenantId(tenant.Id);
        return _mapper.Map<TenantModel>(tenant);
    }
    public async Task UpdateAsync(Guid Id, UpdateTenant payload, CancellationToken token)
    {
        var tenant = _mapper.Map<Tenant>(payload);
        tenant.Id = Id;
        await CreateOrUpdateAsync(tenant, token);
        CommitChanges();
    }

    public async Task UpdateAsync(Tenant tenant, CancellationToken token)
    {
        await CreateOrUpdateAsync(tenant, token);
    }

    public async Task<Tenant?> FindTenantAsync(Guid Id, CancellationToken token)
    {
        var tenant = await Repository.FindOneAsync<Tenant>(Id, token);
        return tenant;
    }

    public async Task<List<Tenant>> FindAlltenants(Guid userId, CancellationToken token)
    {
        return await GetQueryable(x => x.UserId == userId)
            .ToListAsync(token);
    }

    public async Task DeleteAsync(IEnumerable<Tenant> tenants, CancellationToken token)
    {
        await RemoveRangeAsync(tenants, token);
    }
}