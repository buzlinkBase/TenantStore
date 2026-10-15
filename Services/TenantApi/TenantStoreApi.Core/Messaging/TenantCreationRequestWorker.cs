using BuzlinkRepository;
using MassTransit;
using TenantStoreApi.Core.Services;

namespace TenantStoreApi.Core.Messaging;

public class TenantCreationRequestWorker : IConsumer<TenantCreationRequested>
{
    private readonly TenantService _tenantService;
    private readonly UserMembershipService _userMembershipService;
    private readonly IUnitOfWorkService _uow;
    private readonly ITenantProvider _tenantProvider;
    private readonly IPublishEndpoint _publisher;

    public TenantCreationRequestWorker(
        TenantService tenantService,
        UserMembershipService userMembershipService,
        IUnitOfWorkService uow,
        ITenantProvider tenantProvider,
        IPublishEndpoint publisher)
    {
        _tenantService = tenantService;
        _userMembershipService = userMembershipService;
        _uow = uow;
        _tenantProvider = tenantProvider;
        _publisher = publisher;
    }

    public async Task Consume(ConsumeContext<TenantCreationRequested> context)
    {
        var message = context.Message;
        string role = "Owner";
        var tenant = await _tenantService.CreateTenant(message, context.CancellationToken);
        _tenantProvider.SetTenantId(tenant.Id);
        await CreateMembershipAsync(tenant, message, role, context.CancellationToken);
        // No trial yet: it starts when the company finishes setting up (TenantActivatedWorker),
        // so a workspace whose setup fails or is abandoned never uses up the account's one trial.

        await _publisher.Publish(new TenantCreationCompleted
        {
            Event = "tenant.created",
            TenantId = tenant.Id,
            UserId = tenant.UserId,
            TenantName = tenant.TenantName,
            Roles = [role],
            Profile = CompanyProfile.Normalize(message.Profile)
        });
        await _uow.CommitChangesAsync();
    }

    private async Task CreateMembershipAsync(TenantModel tenant, TenantCreationRequested msg, string role, CancellationToken token)
    {
        await _userMembershipService.AddAsync(new UserMembership
        {
            TenantId = tenant.Id,
            TenantName = tenant.TenantName,
            FullName = msg.FullName,
            InvitedEmail = msg.Email,
            IsHidden = true,
            UserId = tenant.UserId,
        }, [role], token);
    }


}
