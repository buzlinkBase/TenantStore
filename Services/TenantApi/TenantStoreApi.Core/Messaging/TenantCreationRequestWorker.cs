using BuzlinkRepository;
using MassTransit;
using Newtonsoft.Json;
using Serilog;
using TenantStoreApi.Core.Services;
using TenantStoreApi.Domain.Entities.Subs;

namespace TenantStoreApi.Core.Messaging;

public class TenantCreationRequestWorker : IConsumer<TenantCreationRequest>
{
    private readonly TenantService _tenantService;
    private readonly UserMembershipService _userMembershipService;
    private readonly IUnitOfWorkService _uow;
    private readonly ITenantProvider _tenantProvider;
    private readonly IPublishEndpoint _publisher;
    private readonly PlanService _planService;
    private readonly SubscriptionService _subscriptionService;

    public TenantCreationRequestWorker(
        TenantService tenantService,
        UserMembershipService userMembershipService,
        IUnitOfWorkService uow,
        ITenantProvider tenantProvider,
        IPublishEndpoint publisher,
        PlanService planService,
        SubscriptionService subscriptionService)
    {
        _tenantService = tenantService;
        _userMembershipService = userMembershipService;
        _uow = uow;
        _tenantProvider = tenantProvider;
        _publisher = publisher;
        _planService = planService;
        _subscriptionService = subscriptionService;
    }

    public async Task Consume(ConsumeContext<TenantCreationRequest> context)
    {
        Log.Logger.Information("TenantCreationRequest Consumed");
        var message = context.Message;
        string role = "Owner";
        var tenant = await _tenantService.CreateTenant(message, context.CancellationToken);
        _tenantProvider.SetTenantId(tenant.Id);
        await CreateMembershipAsync(tenant, role, context.CancellationToken);
        await CreateFreeTrialAsync(tenant, context.CancellationToken);
        await _publisher.Publish(new TenantCreatedPayload
        {
            Event = "tenant.created",
            TenantId = tenant.Id,
            UserId = tenant.UserId,
            TenantName = tenant.TenantName,
            Role = role
        });
        Log.Logger.Information("TenantCreationRequest published {0}", JsonConvert.SerializeObject(_publisher));
        await _uow.CommitChangesAsync();
        Log.Logger.Information("TenantCreationRequest Consumer committed");
    }

    private async Task CreateMembershipAsync(TenantModel tenant, string role, CancellationToken token)
    {
        await _userMembershipService.AddAsync(new UserMembership
        {
            TenantId = tenant.Id,
            TenantName = tenant.TenantName,
            UserId = tenant.UserId,
            Role = role
        }, token);
    }

    private async Task CreateFreeTrialAsync(TenantModel tenant, CancellationToken token)
    {
        var freeTrial = await _planService.FindFreeTrialAsync(token);
        if (freeTrial == null) return;

        await _subscriptionService.AddAsync(new TenantSubscription
        {
            TenantId = tenant.Id,
            PlanId = freeTrial.Id,
            SubStatus = SubscriptionStatus.Trialing,
            StartDate = DateTime.UtcNow,
            EndDate = DateTime.UtcNow.AddDays(freeTrial.Days > 0 ? freeTrial.Days : 14),
        }, token);
    }
}
