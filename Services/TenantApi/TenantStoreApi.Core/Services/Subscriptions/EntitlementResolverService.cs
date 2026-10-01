using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Onepunch.Common.Lib.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Core.Services.Subscriptions;

/// <summary>
/// Resolves what a company may do right now: its owner account's subscription, plan and overrides
/// (see <see cref="EntitlementSnapshotBuilder"/>). The one place entitlements are computed --
/// hrms-api, AuthApi and the frontend all receive the snapshot built here.
/// </summary>
public class EntitlementResolverService
{
    private readonly IUnitOfWorkService _uow;
    private readonly SubscriptionStateMachine _stateMachine;
    private readonly SubscriptionOptions _options;
    private readonly TimeProvider _clock;

    public EntitlementResolverService(
        IUnitOfWorkService uow,
        SubscriptionStateMachine stateMachine,
        IOptions<SubscriptionOptions> options,
        TimeProvider clock)
    {
        _uow = uow;
        _stateMachine = stateMachine;
        _options = options.Value;
        _clock = clock;
    }

    private TenantContext Context => _uow.Context;

    /// <summary>What to enforce -- everything allowed while enforcement is switched off.</summary>
    public async Task<EntitlementSnapshot> ResolveForTenantAsync(Guid tenantId, CancellationToken token = default) =>
        _options.EnforcementEnabled
            ? await ResolveActualForTenantAsync(tenantId, token)
            : EntitlementSnapshot.Unrestricted(tenantId);

    /// <summary>The subscription's real entitlements, even while enforcement is off -- what the
    /// billing screen shows.</summary>
    public async Task<EntitlementSnapshot> ResolveActualForTenantAsync(Guid tenantId, CancellationToken token = default)
    {
        var ownerUserId = await Context.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => (Guid?)t.UserId)
            .FirstOrDefaultAsync(token);

        // A company that predates ownership tracking has no account to bill. Leave it open
        // rather than lock a live customer out; the backfill can't reach it either.
        if (ownerUserId is not { } owner || owner == Guid.Empty)
            return EntitlementSnapshot.Unrestricted(tenantId) with { Status = "Unowned" };

        var subscription = await LoadAsync(owner, token);
        return EntitlementSnapshotBuilder.Build(subscription, subscription?.Plan, tenantId, _clock.GetUtcNow().UtcDateTime, _stateMachine);
    }

    /// <summary>Account-level entitlements (e.g. companies.max), without any company's overrides.</summary>
    public async Task<EntitlementSnapshot> ResolveForAccountAsync(Guid ownerUserId, CancellationToken token = default)
    {
        if (!_options.EnforcementEnabled) return EntitlementSnapshot.Unrestricted(Guid.Empty);

        var subscription = await LoadAsync(ownerUserId, token);
        return EntitlementSnapshotBuilder.Build(subscription, subscription?.Plan, Guid.Empty, _clock.GetUtcNow().UtcDateTime, _stateMachine);
    }

    /// <summary>What a company would get on <paramref name="plan"/>, keeping its overrides.</summary>
    public EntitlementSnapshot ResolveAgainstPlan(AccountSubscription subscription, Plan plan, Guid tenantId) =>
        EntitlementSnapshotBuilder.Build(subscription, plan, tenantId, _clock.GetUtcNow().UtcDateTime, _stateMachine);

    private Task<AccountSubscription?> LoadAsync(Guid ownerUserId, CancellationToken token) =>
        Context.AccountSubscriptions
            .AsNoTracking()
            .Include(s => s.Plan).ThenInclude(p => p.Entitlements)
            .Include(s => s.Overrides)
            .FirstOrDefaultAsync(s => s.OwnerUserId == ownerUserId, token);
}
