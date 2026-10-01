using BuzlinkRepository;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using TenantStoreApi.Core.Services.Subscriptions;

namespace TenantStoreApi.Core.Messaging;

/// <summary>
/// A company finished setting up (hrms-api's HrisOrgProvisioned; AuthApi's HrDbCreatedWorker
/// gets its own copy). Only now does it count as activated: it starts its owner's one trial if
/// the account has no subscription yet, and counts toward the companies the account owns. A
/// failed setup activates nothing, so an abandoned workspace never uses up the trial.
/// Idempotent: a redelivery finds the company already activated and the trial already started.
/// </summary>
public class TenantActivatedWorker : IConsumer<HrisOrgProvisionedPayload>
{
    private readonly IUnitOfWorkService _uow;
    private readonly ITenantProvider _tenantProvider;
    private readonly AccountSubscriptionService _subscriptions;
    private readonly SubscriptionChangeNotifierService _notifier;
    private readonly TimeProvider _clock;

    public TenantActivatedWorker(
        IUnitOfWorkService uow,
        ITenantProvider tenantProvider,
        AccountSubscriptionService subscriptions,
        SubscriptionChangeNotifierService notifier,
        TimeProvider clock)
    {
        _uow = uow;
        _tenantProvider = tenantProvider;
        _subscriptions = subscriptions;
        _notifier = notifier;
        _clock = clock;
    }

    public static bool Succeeded(string? status) =>
        !string.IsNullOrWhiteSpace(status)
        && !status.Contains("fail", StringComparison.OrdinalIgnoreCase)
        && !status.Contains("error", StringComparison.OrdinalIgnoreCase);

    public async Task Consume(ConsumeContext<HrisOrgProvisionedPayload> context)
    {
        var message = context.Message;
        if (!Succeeded(message.Status)) return;

        var token = context.CancellationToken;
        var tenant = await _uow.Context.Tenants.FirstOrDefaultAsync(t => t.Id == message.TenantId, token);
        if (tenant == null || tenant.ActivatedAtUtc != null) return;

        _tenantProvider.SetTenantId(tenant.Id);
        tenant.ActivatedAtUtc = _clock.GetUtcNow().UtcDateTime;

        if (tenant.UserId != Guid.Empty)
        {
            var ownerEmail = await _uow.Context.Memberships.AsNoTracking()
                .Where(m => m.TenantId == tenant.Id && m.UserId == tenant.UserId && m.IsHidden)
                .Select(m => m.InvitedEmail)
                .FirstOrDefaultAsync(token);
            await _subscriptions.StartTrialIfFirstCompanyAsync(tenant.UserId, ownerEmail, token);
            await _notifier.PublishAsync(tenant.UserId, "company.activated", token);
        }

        await _uow.CommitChangesAsync("", token);
    }
}
