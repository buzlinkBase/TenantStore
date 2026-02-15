using AutoMapper;
using Microsoft.Extensions.Options;
using TenantStoreApi.Core.Utilities;
using TenantStoreApi.Core.Validations;

namespace TenantStoreApi.Core.Services;

public class TenantService : BaseService<Tenant>
{
    private readonly IMapper _mapper;
    private readonly AuthService _authClient;
    private readonly KafkaSettings _kafkaSettings;
    private readonly OutBoxService _outBoxService;
    private readonly PasswordCrypto _crypto;
    public TenantService(IUnitOfWorkService service, IMapper mapper,
        AuthService authClient ,
        IOptions<KafkaSettings> kafkaSettings,
        OutBoxService outBoxService, PasswordCrypto crypto) : base(service)
    {
        _mapper = mapper;
        _authClient = authClient;
        _kafkaSettings = kafkaSettings.Value;
        _outBoxService = outBoxService;
        _crypto = crypto;
    }
    protected override async Task<ValidationResponse> CreateValidator(Tenant tenant)
    {
        await base.CreateValidator(tenant);
        Guard.ThrowIfNull(tenant, nameof(tenant));
        var fluentValResult = await new TenantValidator(UoW).ValidateAsync(tenant);
        var result = ValidationResponse.Check(fluentValResult);
        Guard.ThrowIfError(result);
        // Check email existence via external service
        var emailExists = await _authClient.CheckEmailAsync(tenant.Email);
        Guard.EnsureFalse(emailExists.Valid, "Email is already used");
        return result;
    }

    public async Task<TenantModel> RegisterAsync(CreateTenant payload)
    {

        var tenant = _mapper.Map<Tenant>(payload);
        tenant.Status = "Pending";
        tenant.Token = TokenGenerator.GenerateRandomToken();
        await CreateOrUpdateAsync(tenant);

        var msgPayloadDto = ComposePayload(tenant, payload);
        await CreateOutBoxAysnc(tenant, msgPayloadDto);
        await CommitChangesAsync();
        var message = ObjectSerializer.Serialize(msgPayloadDto);
        return _mapper.Map<TenantModel>(tenant);


    }

    public async Task UpdateAsync(Guid Id, UpdateTenant payload)
    {
        var tenant = _mapper.Map<Tenant>(payload);
        tenant.Id = Id;
        await CreateOrUpdateAsync(tenant);
        CommitChanges();
    }

    public async Task UpdateAsync(Tenant tenant)
    {
        await CreateOrUpdateAsync(tenant);
    }
    public async Task<List<Tenant>> FindAll()
    {
        var tenant = Repository.FindAll<Tenant>();
        return tenant.ToList();
    }

    public async Task<Tenant?> FindTenant(Guid Id)
    {
        var tenant = await Repository.FindOneAsync<Tenant>(Id);
        return tenant;
    }

    private MessagePayload<TenantCreatedPayload> ComposePayload(Tenant tenant, CreateTenant payload)
    {
        var userPassword = _crypto.Encrypt(payload.Password);
        return new MessagePayload<TenantCreatedPayload>
        {
            Data = new TenantCreatedPayload
            {
                TenantId = tenant.Id,
                Email = payload.Email,
                Password = userPassword,
            },
        };
    }

    private async Task CreateOutBoxAysnc(Tenant tenant, MessagePayload<TenantCreatedPayload> payload)
    {
        var message = ObjectSerializer.Serialize(payload);
        var outbox = _outBoxService.CreateModel(tenant.Id,
            tenant.Id.ToString(),
            _kafkaSettings.Topics.TenantCreated,
            message);
        await _outBoxService.AddAsync(outbox);
    }
}

