using AutoMapper;
using Microsoft.Extensions.Options;
using Onepunch.Common.Lib.Exceptions;
using TenantStoreApi.Core.Utilities;
using TenantStoreApi.Core.Validations;

namespace TenantStoreApi.Core.Services;

public class TenantService : BaseService<Tenant>
{
    private readonly IMapper _mapper;
    private readonly IProducerService _producerService;
    private readonly AuthService _authClient;
    private readonly PasswordCrypto _crypto;
    public TenantService(IUnitOfWorkService service,
        IMapper mapper,
        IProducerService producerService,
        AuthService authClient, PasswordCrypto crypto) : base(service)
    {
        _mapper = mapper;
        _producerService = producerService;
        _authClient = authClient;
        _crypto = crypto;
    }
    protected override async Task<EvaluationResult> CreateValidatorAsync(Tenant model, CancellationToken token)
    {
        await base.CreateValidatorAsync(model, token);
        Guard.ThrowIfNull(model, "Organization");
        var fluentValResult = await new TenantValidator(UoW).ValidateAsync(model, token);
        var result = EvaluationResult.Check(fluentValResult);
        Guard.ThrowIfError(result);
        var checkEmailResult = await _authClient.CheckEmailAsync(model.Email, token);
        Guard.EnsureFalse(checkEmailResult.Exists, "Email is already used");
        return result;
    }

    public async Task<TenantModel> RegisterAsync(CreateTenant payload, CancellationToken token)
    {
        var tenant = _mapper.Map<Tenant>(payload);
        tenant.Status = "Pending";
        tenant.Token = TokenGenerator.GenerateRandomToken();
        await CreateAsync(tenant, token);
        await UoW.SaveChangesAsync(token);
        var msgPayloadDto = ComposePayload(tenant, payload);
        await _producerService.ProduceAsync(msgPayloadDto);
        await CommitChangesAsync(token);
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
    internal async Task DeleteAsync(IEnumerable<Tenant> tenants, CancellationToken token)
    {
        await RemoveRangeAsync(tenants, token);
    }

    private TenantCreatedPayload ComposePayload(Tenant tenant, CreateTenant payload)
    {
        var userPassword = _crypto.Encrypt(payload.Password);
        return new TenantCreatedPayload
        {
            TenantId = tenant.Id,
            Email = payload.Email,
            Password = userPassword,
        };
    }
}

