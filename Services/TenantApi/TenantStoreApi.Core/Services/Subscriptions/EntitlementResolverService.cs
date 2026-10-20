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

    /// <summary>Snapshot status of a company still setting up whose owner has no plan yet.</summary>
    public const string ProvisioningStatus = "Provisioning";

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
        // Past the soft-delete filter: a deleted company must come back as Invalid, not "unknown".
        var company = await Context.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.UserId, t.ActivatedAtUtc, t.DeletedAt })
            .FirstOrDefaultAsync(token);

        // Deleted: the account is no longer valid. Closed to everyone, and devices' punches for it
        // are refused from the moment it was deleted.
        if (company?.DeletedAt is { } deletedAt)
        {
            return new EntitlementSnapshot
            {
                TenantId = tenantId,
                OwnerUserId = company.UserId,
                Status = EntitlementSnapshot.InvalidStatus,
                AccessMode = SubscriptionAccessMode.ReadOnly,
                ReadOnlySinceUtc = deletedAt,
                PunchesAcceptedUntilUtc = deletedAt,
            };
        }

        // A company that predates ownership tracking has no account to bill. Leave it open
        // rather than lock a live customer out; the backfill can't reach it either.
        if (company == null || company.UserId == Guid.Empty)
            return EntitlementSnapshot.Unrestricted(tenantId) with { Status = "Unowned" };

        var subscription = await LoadAsync(company.UserId, token);

        // Not activated yet (still setting up, or an existing company the activation backfill
        // hasn't marked) and the owner has no plan of their own: the trial starts on activation,
        // so until then leave it open rather than show a first-time owner as read-only.
        if (subscription == null && company.ActivatedAtUtc == null)
            return EntitlementSnapshot.Unrestricted(tenantId) with { Status = ProvisioningStatus, OwnerUserId = company.UserId };

        var snapshot = EntitlementSnapshotBuilder.Build(subscription, subscription?.Plan, tenantId, _clock.GetUtcNow().UtcDateTime, _stateMachine, _options.LockedPunchRetentionDays);

        // Storage is one pool for the account: tell the enforcing service which companies share it.
        var accountTenantIds = await Context.Tenants.AsNoTracking()
            .Where(t => t.UserId == company.UserId)
            .Select(t => t.Id)
            .ToListAsync(token);
        return snapshot with { AccountTenantIds = accountTenantIds };
    }

    /// <summary>Account-level entitlements (e.g. companies.max), without any company's overrides.</summary>
    public async Task<EntitlementSnapshot> ResolveForAccountAsync(Guid ownerUserId, CancellationToken token = default)
    {
        if (!_options.EnforcementEnabled) return EntitlementSnapshot.Unrestricted(Guid.Empty);

        var subscription = await LoadAsync(ownerUserId, token);
        return EntitlementSnapshotBuilder.Build(subscription, subscription?.Plan, Guid.Empty, _clock.GetUtcNow().UtcDateTime, _stateMachine, _options.LockedPunchRetentionDays);
    }

    /// <summary>What a company would get on <paramref name="plan"/>, keeping its overrides.</summary>
    public EntitlementSnapshot ResolveAgainstPlan(AccountSubscription subscription, Plan plan, Guid tenantId) =>
        EntitlementSnapshotBuilder.Build(subscription, plan, tenantId, _clock.GetUtcNow().UtcDateTime, _stateMachine, _options.LockedPunchRetentionDays);

    /// <summary>
    /// The account's subscription as of now: loaded untracked, then moved through whatever
    /// transitions its dates imply (trial ended → Expired, period ended → PastDue → Suspended, a
    /// scheduled downgrade's date passed → new plan). So access follows the dates the moment they
    /// pass -- or are edited -- instead of waiting for the hourly lifecycle job, which still makes
    /// the same transitions permanent (and sends the emails). Nothing here is saved.
    /// </summary>
    private async Task<AccountSubscription?> LoadAsync(Guid ownerUserId, CancellationToken token)
    {
        var subscription = await Context.AccountSubscriptions
            .AsNoTracking()
            .Include(s => s.Plan).ThenInclude(p => p.Entitlements)
            .Include(s => s.Overrides)
            .Include(s => s.StorageAddOns)
            .Include(s => s.StorageSubscription)
            .FirstOrDefaultAsync(s => s.OwnerUserId == ownerUserId, token);
        if (subscription == null) return null;

        _stateMachine.Advance(subscription, _clock.GetUtcNow().UtcDateTime, _options);
        if (subscription.Plan?.Id != subscription.PlanId)
        {
            subscription.Plan = await Context.Plans.AsNoTracking()
                .Include(p => p.Entitlements)
                .FirstAsync(p => p.Id == subscription.PlanId, token);
        }
        return subscription;
    }
}
