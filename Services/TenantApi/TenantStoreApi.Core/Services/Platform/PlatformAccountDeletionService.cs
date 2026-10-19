using MassTransit;
using Microsoft.EntityFrameworkCore;
using Onepunch.Common.Lib.DTO;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Core.Services.Platform;

/// <summary>
/// Erases an approved account everywhere, as if it never existed. Tenant Service drives it:
/// <list type="number">
/// <item><see cref="StartAsync"/> locks the account (read-only, sign-ins refreshed) and asks every
/// service holding company data to delete it outright (<see cref="PurgeCompanyData"/>, stage
/// <see cref="CompanyPurgeStage.HardDelete"/>). hrms-api answers per company; adms and the client
/// portal delete on the same message without answering.</item>
/// <item><see cref="RecordResultAsync"/> collects hrms-api's answers. A failure parks the account in
/// <see cref="DataPurgeState.HardDeleteFailed"/> for an operator to retry; once every company is gone,
/// <see cref="FinalizeAsync"/> deletes Tenant Service's own rows and hands the logins over to Auth
/// Service (<see cref="AccountHardDeleted"/>).</item>
/// </list>
/// Every step deletes by key, so repeating one is harmless. Doesn't commit: the caller owns the unit
/// of work, whose transaction also covers the set-based deletes.
/// </summary>
public class PlatformAccountDeletionService
{
    private readonly IUnitOfWorkService _uow;
    private readonly BillingEventService _events;
    private readonly SubscriptionChangeNotifierService _notifier;
    private readonly IPublishEndpoint _publisher;
    private readonly TenantStoreAccountEraser _eraser;
    private readonly TimeProvider _clock;

    public PlatformAccountDeletionService(
        IUnitOfWorkService uow,
        BillingEventService events,
        SubscriptionChangeNotifierService notifier,
        IPublishEndpoint publisher,
        TenantStoreAccountEraser eraser,
        TimeProvider clock)
    {
        _uow = uow;
        _events = events;
        _notifier = notifier;
        _publisher = publisher;
        _eraser = eraser;
        _clock = clock;
    }

    private TenantContext Context => _uow.Context;
    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public Task<bool> CommitChangesAsync(CancellationToken token = default) => _uow.CommitChangesAsync("", token);

    /// <summary>Starts erasing the account of an approved <paramref name="request"/>.</summary>
    public async Task StartAsync(AccountDeletionRequest request, CancellationToken token)
    {
        var subscription = await RequireAsync(request.OwnerUserId, token);
        var tenantIds = await OwnerTenantIdsAsync(subscription.OwnerUserId, token);
        if (tenantIds.Count == 0)
        {
            // No company data anywhere: nothing to wait for.
            await FinalizeAsync(subscription.OwnerUserId, token, request);
            return;
        }

        subscription.PurgeState = DataPurgeState.HardDeleting;
        // Read-only straight away, so nobody keeps adding records while the companies are emptied.
        if (subscription.SubStatus is AccountSubscriptionStatus.Trialing or AccountSubscriptionStatus.Active or AccountSubscriptionStatus.PastDue)
        {
            subscription.SubStatus = AccountSubscriptionStatus.Suspended;
            subscription.SuspensionReason = "This account is being deleted.";
        }
        subscription.ConcurrencyStamp = Guid.NewGuid();

        await _events.AddAsync(subscription, BillingEventTypes.HardDeleteRequested,
            new { requestId = request.Id, tenantIds }, request.DecidedBy, token: token);
        await _notifier.PublishAsync(subscription.OwnerUserId, BillingEventTypes.HardDeleteRequested, token);
        await RequestCompaniesAsync(subscription, tenantIds, token);
    }

    /// <summary>Asks again for the companies that haven't reported their data deleted.</summary>
    public async Task RetryAsync(Guid ownerUserId, string actor, CancellationToken token)
    {
        var subscription = await RequireAsync(ownerUserId, token);
        if (subscription.PurgeState is not (DataPurgeState.HardDeleting or DataPurgeState.HardDeleteFailed))
            throw new SubscriptionRuleException("This account isn't being deleted.");

        var tenantIds = await OwnerTenantIdsAsync(ownerUserId, token);
        var pending = new List<Guid>();
        foreach (var tenantId in tenantIds)
        {
            if (!await _events.ExistsAsync(DoneKey(subscription.Id, tenantId), token)) pending.Add(tenantId);
        }
        if (pending.Count == 0)
        {
            await FinalizeAsync(ownerUserId, token);
            return;
        }

        subscription.PurgeState = DataPurgeState.HardDeleting;
        subscription.ConcurrencyStamp = Guid.NewGuid();
        await _events.AddAsync(subscription, BillingEventTypes.HardDeleteRequested, new { retry = true, tenantIds = pending }, actor, token: token);
        await RequestCompaniesAsync(subscription, pending, token);
    }

    /// <summary>
    /// Records hrms-api's answer for one company of an account being erased. An answer for any other
    /// account is ignored (the archive purge's answers go to <see cref="DataPurgeService"/>).
    /// </summary>
    public async Task RecordResultAsync(CompanyDataPurged result, CancellationToken token)
    {
        var subscription = await Context.AccountSubscriptions
            .FirstOrDefaultAsync(s => s.Id == result.SubscriptionId && s.OwnerUserId == result.OwnerUserId, token);
        if (subscription is null || subscription.PurgeState is not (DataPurgeState.HardDeleting or DataPurgeState.HardDeleteFailed)) return;
        var tenantIds = await OwnerTenantIdsAsync(subscription.OwnerUserId, token);
        if (!tenantIds.Contains(result.TenantId)) return;

        if (!result.Succeeded)
        {
            subscription.PurgeState = DataPurgeState.HardDeleteFailed;
            subscription.ConcurrencyStamp = Guid.NewGuid();
            await _events.AddAsync(subscription, BillingEventTypes.HardDeleteFailed,
                new { result.TenantId, result.Error }, actor: "system", token: token);
            return;
        }

        var stillPending = 0;
        foreach (var tenantId in tenantIds.Where(id => id != result.TenantId))
        {
            if (!await _events.ExistsAsync(DoneKey(subscription.Id, tenantId), token)) stillPending++;
        }
        if (stillPending > 0)
        {
            var key = DoneKey(subscription.Id, result.TenantId);
            if (!await _events.ExistsAsync(key, token))
            {
                await _events.AddAsync(subscription, BillingEventTypes.HardDeleteCompanyDone,
                    new { result.TenantId, result.RowsDeleted, result.FilesDeleted }, actor: "system", dedupeKey: key, token: token);
            }
            return;
        }

        // The last company: nothing of the account is left outside Tenant Service.
        await FinalizeAsync(subscription.OwnerUserId, token);
    }

    /// <summary>
    /// Deletes Tenant Service's rows for the account and publishes <see cref="AccountHardDeleted"/> in
    /// the same transaction. Pending tracked changes are dropped first: nothing about this account may
    /// be written after its rows are gone.
    /// </summary>
    /// <param name="approved">The approved request when it isn't saved yet (an account with no
    /// companies is erased in the approval's own unit of work); otherwise it's read back.</param>
    public async Task FinalizeAsync(Guid ownerUserId, CancellationToken token, AccountDeletionRequest? approved = null)
    {
        Context.ChangeTracker.Clear();

        var subscription = await Context.AccountSubscriptions.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(s => s.OwnerUserId == ownerUserId, token);
        if (subscription is null) return;
        var request = approved ?? await Context.AccountDeletionRequests.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.OwnerUserId == ownerUserId && r.TenantId == null && r.RequestStatus == AccountDeletionRequestStatus.Approved)
            .OrderByDescending(r => r.DecidedAtUtc)
            .FirstOrDefaultAsync(token)
            ?? throw new SubscriptionRuleException("This account has no approved deletion request.");

        var tenants = await Context.Tenants.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.UserId == ownerUserId)
            .Select(t => new { t.Id, t.TenantName })
            .ToListAsync(token);
        var tenantIds = tenants.Select(t => t.Id).ToList();
        var (deleteUserIds, detachUserIds) = await ErasureUsers.PartitionAsync(Context, tenantIds, ownerUserId, token);

        await _eraser.EraseAsync(new AccountErasureScope(ownerUserId, subscription.Id, tenantIds), token);

        await _publisher.Publish(new AccountHardDeleted
        {
            RequestId = request.Id,
            OwnerUserId = ownerUserId,
            OwnerEmail = request.OwnerEmail,
            TenantIds = tenantIds,
            CompanyNames = tenants.Select(t => t.TenantName ?? t.Id.ToString()).ToList(),
            DeleteUserIds = deleteUserIds,
            DetachUserIds = detachUserIds,
            RequestedBy = request.RequestedBy,
            ApprovedBy = request.DecidedBy ?? string.Empty,
            Reason = request.Reason,
            DeletedAtUtc = Now,
        }, token);
    }

    private async Task RequestCompaniesAsync(AccountSubscription subscription, IEnumerable<Guid> tenantIds, CancellationToken token)
    {
        var now = Now;
        foreach (var tenantId in tenantIds)
        {
            await _publisher.Publish(new PurgeCompanyData
            {
                OwnerUserId = subscription.OwnerUserId,
                SubscriptionId = subscription.Id,
                TenantId = tenantId,
                Stage = CompanyPurgeStage.HardDelete,
                RequestedAtUtc = now,
            }, ctx => ctx.Headers.Set("X-Tenant-ID", tenantId.ToString()), token);
        }
    }

    private static string DoneKey(Guid subscriptionId, Guid tenantId) =>
        $"{BillingEventTypes.HardDeleteCompanyDone}:{subscriptionId:N}:{tenantId:N}";

    private async Task<AccountSubscription> RequireAsync(Guid ownerUserId, CancellationToken token) =>
        await Context.AccountSubscriptions.FirstOrDefaultAsync(s => s.OwnerUserId == ownerUserId, token)
        ?? throw new KeyNotFoundException("This account has no subscription.");

    // Soft-deleted companies too: their data is erased like the rest.
    private Task<List<Guid>> OwnerTenantIdsAsync(Guid ownerUserId, CancellationToken token) =>
        Context.Tenants.IgnoreQueryFilters().AsNoTracking().Where(t => t.UserId == ownerUserId).Select(t => t.Id).ToListAsync(token);
}
