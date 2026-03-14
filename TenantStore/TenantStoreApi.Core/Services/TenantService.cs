using AutoMapper;
using BuzlinkRepository;
using MassTransit;
using Onepunch.Common.Lib.Exceptions;

namespace TenantStoreApi.Core.Services;

public class TenantService : BaseService<Tenant>
{
    private readonly IMapper _mapper;
    private readonly ITenantProvider _tenantProvider;
    private readonly BranchService _branchService;
    public TenantService(IUnitOfWorkService service,
        IMapper mapper,
        ITenantProvider tenantProvider,
        IPublishEndpoint publisher,
        BranchService branchService,

        PasswordCrypto crypto) : base(service)
    {
        _mapper = mapper;
        _tenantProvider = tenantProvider;
        _branchService = branchService;
    }

    protected override async Task<EvaluationResult> CreateValidatorAsync(Tenant model, CancellationToken token)
    {
        await base.CreateValidatorAsync(model, token);
        Guard.ThrowIfNull(model, "Account Payload");
        return EvaluationResult.OK;
    }

    public async Task<TenantModel> CreateTenant(UserCreated payload, CancellationToken token)
    {
        var tenant = _mapper.Map<Tenant>(payload);
        await CreateAsync(tenant, token);
        await Uow.SaveChangesAsync(token);

        _tenantProvider.SetTenantId(tenant.Id);
        await _branchService.AddAsync(new CreateBranch
        {
            Code = "Main",
            Name = "Main Branch",
            TenantId = tenant.Id,
            Status = "Active"
        }, token);
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
    public async Task<List<Tenant>> FindAll()
    {
        var tenant = Repository.FindAll<Tenant>();
        return tenant.ToList();
    }

    public async Task<Tenant?> FindTenantAsync(Guid Id, CancellationToken token)
    {
        var tenant = await Repository.FindOneAsync<Tenant>(Id, token);
        return tenant;
    }
    public async Task DeleteAsync(IEnumerable<Tenant> tenants, CancellationToken token)
    {
        await RemoveRangeAsync(tenants, token);
    }
}