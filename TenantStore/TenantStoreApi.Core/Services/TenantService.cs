using AutoMapper;
using BuzlinkRepository;
using MassTransit;
using Onepunch.Common.Lib.Exceptions;
using TenantStoreApi.Core.Validations;

namespace TenantStoreApi.Core.Services;

public class TenantService : BaseService<Tenant>
{
    private readonly IMapper _mapper;
    private readonly ITenantProvider _tenantProvider;
    private readonly IPublishEndpoint _publisher;
    private readonly BranchService _branchService;
    private readonly PasswordCrypto _crypto;
    public TenantService(IUnitOfWorkService service,
        IMapper mapper,
        ITenantProvider tenantProvider,
        IPublishEndpoint publisher,
        BranchService branchService,

        PasswordCrypto crypto) : base(service)
    {
        _mapper = mapper;
        _tenantProvider = tenantProvider;
        _publisher = publisher;
        _branchService = branchService;
        _crypto = crypto;
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
        await UoW.SaveChangesAsync(token);
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

    //public async Task<bool> SendUserInvitation(InvitationPayload payload, CancellationToken token)
    //{
    //    var exp = DateTime.UtcNow.AddDays(2);
    //    var emailToken = await _emailTokenService.CreateModelAsync("user.invitation", exp, payload.Email);
    //    var message = new UserInvitionNotificationPayload
    //    {
    //        Email = payload.Email,
    //        Name = payload.Name,
    //        InviteLink = $"{_domains.FrontEndDomain}/invitations-list?token={emailToken.TokenValue}",
    //        AppName = _configuration["AppName"],
    //        TenantName = emailToken.TenantName,
    //        Expiry = exp,
    //        Token = emailToken.TokenValue,
    //    };
    //    await _emailTokenService.StoreToken(emailToken, token);
    //    await _publisher.Publish(message, token);
    //    return await CommitChangesAsync(token);
    //} 
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
    internal async Task DeleteAsync(IEnumerable<Tenant> tenants, CancellationToken token)
    {
        await RemoveRangeAsync(tenants, token);
    }
}

