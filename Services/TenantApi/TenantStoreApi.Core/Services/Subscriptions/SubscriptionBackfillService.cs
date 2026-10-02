using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Core.Services.Subscriptions;

/// <summary>
/// Gives every account that already owns a company, but has no subscription yet, the legacy plan
/// as Active until <see cref="SubscriptionOptions.LegacyPeriodEndUtc"/> -- so turning
/// subscriptions on doesn't lock live customers out. Runs at startup; a no-op once every owner
/// has a subscription (new accounts get theirs when they create their first company).
/// </summary>
public class SubscriptionBackfillService
{
    private readonly IUnitOfWorkService _uow;
    private readonly BillingEventService _events;
    private readonly SubscriptionOptions _options;
    private readonly TimeProvider _clock;
    private readonly CompanyActivationSyncService? _activationSync;

    public SubscriptionBackfillService(
        IUnitOfWorkService uow,
        BillingEventService events,
        IOptions<SubscriptionOptions> options,
        TimeProvider clock,
        CompanyActivationSyncService? activationSync = null)
    {
        _uow = uow;
        _events = events;
        _options = options.Value;
        _clock = clock;
        _activationSync = activationSync;
    }

    private TenantContext Context => _uow.Context;

    public async Task<int> EnsureBackfilledAsync(CancellationToken token)
    {
        // Existing companies are only "activated" once AuthApi confirms they finished setting up;
        // without this the backfill below would find no owners at all.
        // One unit of work for both: the sync only stages its changes, committed below.
        var activated = _activationSync != null ? await _activationSync.SyncAsync(token) : 0;

        // Only owners of an activated company: an account that only joined others' companies, or
        // whose own workspace never finished setting up, needs no plan of its own.
        var owners = await Context.Tenants.AsNoTracking()
            .Where(t => t.UserId != Guid.Empty && t.ActivatedAtUtc != null)
            .Select(t => t.UserId)
            .Distinct()
            .Where(owner => !Context.AccountSubscriptions.Any(s => s.OwnerUserId == owner))
            .ToListAsync(token);
        if (owners.Count == 0)
        {
            if (activated > 0) await _uow.CommitChangesAsync("", token);
            return 0;
        }

        var plan = await Context.Plans.FirstOrDefaultAsync(p => p.Code == _options.LegacyPlanCode, token);
        if (plan == null)
        {
            Log.Logger.Error("Subscription backfill: legacy plan '{Plan}' not found; {Count} account(s) left without a subscription",
                _options.LegacyPlanCode, owners.Count);
            if (activated > 0) await _uow.CommitChangesAsync("", token);
            return 0;
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var periodEnd = _options.LegacyPeriodEndUtc is { } configured && configured > now ? configured : now.AddDays(90);
        if (_options.LegacyPeriodEndUtc == null)
        {
            Log.Logger.Warning("Subscription backfill: Subscriptions:LegacyPeriodEndUtc isn't set; existing accounts are active until {PeriodEnd:u}", periodEnd);
        }

        foreach (var owner in owners)
        {
            var subscription = new AccountSubscription
            {
                Id = Guid.CreateVersion7(),
                OwnerUserId = owner,
                PlanId = plan.Id,
                SubStatus = AccountSubscriptionStatus.Active,
                BillingCycle = BillingCycle.Monthly,
                CurrentPeriodStartUtc = now,
                CurrentPeriodEndUtc = periodEnd,
                LockedMonthlyPrice = plan.MonthlyPrice,
                LockedAnnualPrice = plan.AnnualPrice,
            };
            await Context.AccountSubscriptions.AddAsync(subscription, token);
            await _events.AddAsync(subscription, BillingEventTypes.LegacyBackfill,
                new { plan = plan.Code, periodEnd }, actor: "system", token: token);
        }

        await _uow.CommitChangesAsync("", token);
        Log.Logger.Information("Subscription backfill: {Count} existing account(s) put on {Plan} until {PeriodEnd:u}", owners.Count, plan.Code, periodEnd);
        return owners.Count;
    }
}
