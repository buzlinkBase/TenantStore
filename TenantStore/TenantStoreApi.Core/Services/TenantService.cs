using AutoMapper;
using Microsoft.AspNetCore.Http.HttpResults;
using TenantStoreApi.Core.Utilities;
using TenantStoreApi.Core.Validations;
using TenantStoreApi.Domain.Entities;

namespace TenantStoreApi.Core.Services;

public class TenantService : BaseService<Tenant>
{
    private readonly IMapper _mapper;
    private readonly AuthHttpClient _authClient;
    private readonly OutBoxService _outBoxService;
    private readonly PasswordCrypto _crypto;
    private readonly IRabbitMQPublisher _publisher;
    private const string _eventType = "tenant.created";

    public TenantService(IUnitOfWorkService service, IMapper mapper,
        AuthHttpClient client, OutBoxService outBoxService, PasswordCrypto crypto,
        IRabbitMQPublisher publisher) : base(service)
    {
        _mapper = mapper;
        _authClient = client;
        _outBoxService = outBoxService;
        _crypto = crypto;
        _publisher = publisher;
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
        Guard.EnsureFalse(emailExists, "Email is already used");
        return result;
    }


    public async Task<TenantModel> RegisterAsync(CreateTenant payload)
    {

        var tenant = _mapper.Map<Tenant>(payload);
        tenant.Status = "Pending";
        await CreateOrUpdateAsync(tenant);

        //save outbox
        var msgPayloadDto = ComposePayload(tenant, payload);
        await CreateOutBoxAysnc(tenant, msgPayloadDto);
        await CommitChangesAsync();

        var message = ObjectSerializer.Serialized(msgPayloadDto);
        await CreateAdminUserAsync(message);

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
        var tenant = Repository.FindOne<Tenant>(Id);
        return tenant;
    }

    private async Task CreateAdminUserAsync(string message)
    {
        await _publisher.PublishAsync(message, _eventType);
    }
    private RMQPayload<TenantCreatedPayload> ComposePayload(Tenant tenant, CreateTenant payload)
    {
        var userPassword = _crypto.Encrypt(payload.Password);
        return new RMQPayload<TenantCreatedPayload>
        {
            EventId = Guid.NewGuid(),
            EventType = _eventType,
            Data = new TenantCreatedPayload
            {
                TenantId = tenant.Id,
                Email = payload.Email,
                Password = userPassword,
            },
        };
    }
    private async Task CreateOutBoxAysnc(Tenant tenant, RMQPayload<TenantCreatedPayload> payload)
    {
        var message = ObjectSerializer.Serialized(payload);
        var outbox = _outBoxService.CreateModel(tenant.Id,
            tenant.Id,
            payload.EventId,
            payload.CausationId,
            payload.CorrelationId,
            "CreateTenant",
            _eventType,
            OutBoxState.PROCESSING, message);
        await _outBoxService.AddAsync(outbox);
    }
}

