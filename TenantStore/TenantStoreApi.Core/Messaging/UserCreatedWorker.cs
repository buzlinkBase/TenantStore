using MassTransit;
using TenantStoreApi.Core.Services;

namespace TenantStoreApi.Core.Messaging;

public class UserCreatedWorker : IConsumer<UserCreated>
{
    private readonly TenantService _tenantService;
    private readonly UserMembershipService _userMembershipService;
    private readonly SubscriptionService _subscriptionService;
    private readonly DigitalOceanDbService _digitalOceanDbService;
    private readonly PlanService _planService;
    private readonly IPublishEndpoint _publisher;

    public UserCreatedWorker(TenantService tenantService,
        UserMembershipService userMembershipService,
        SubscriptionService subscriptionService,
        DigitalOceanDbService digitalOceanDbService,
        PlanService planService,
        IPublishEndpoint publisher)
    {
        _tenantService = tenantService;
        _userMembershipService = userMembershipService;
        _subscriptionService = subscriptionService;
        _digitalOceanDbService = digitalOceanDbService;
        _planService = planService;
        _publisher = publisher;
    }

    public async Task Consume(ConsumeContext<UserCreated> context)
    {
        var message = context.Message;
        //create tenant
        var tenant = await _tenantService.CreateTenant(message, context.CancellationToken);
        //create db
        var hrisdbName = $"hris-{tenant.Id}";
        var result = await _digitalOceanDbService.CreateTenantDatabaseAsync("hrms", tenant.Id);
        //store connectionstring
        //var constr = new TenantConnection
        //{
        //    ConnetionString = result
        //};
        await _userMembershipService.AddAsync(new UserMembership
        {
            TenantId = tenant.Id,
            UserId = message.UserId,
            Role = "Owner"
        }, context.CancellationToken);

        //trial plan
        var freePlan = await _planService.FindFreeTrialAsync(context.CancellationToken);
        if (freePlan == null)
        {
            freePlan = new Plan
            {
                Description = "Free Trial",
                Name = "Free Trial"
            };
            _planService.Repository.Add(freePlan);
            await _planService.SaveChangesAsync(context.CancellationToken);
        }
        var sub = new TenantSubscription
        {
            TenantId = tenant.Id,
            PlanId = freePlan.Id,
            SubStatus = SubscriptionStatus.Trialing,
            StartDate = DateTime.UtcNow,
            EndDate = DateTime.UtcNow.AddDays(30)
        };
        await _subscriptionService.AddAsync(sub, context.CancellationToken);
        //publish created tenant to set defaultTenantId
        var createdTenant = new TenantCreatedPayload
        {
            TenantId = tenant.Id,
            UserId = message.UserId,
            TenantName = tenant.TenantName,
        };
        await _publisher.Publish(createdTenant);
        await _tenantService.CommitChangesAsync(context.CancellationToken);
    }
}
