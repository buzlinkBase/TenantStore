using MassTransit;
using Microsoft.EntityFrameworkCore;
using Onepunch.Common.Lib.DTO;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Core.Services.Platform;

/// <summary>
/// Erases ONE approved company of an account everywhere, as if it never existed; the account, its
/// subscription, payments and other companies stay. Same shape as <see cref="PlatformAccountDeletionService"/>:
/// <list type="number">
/// <item><see cref="StartAsync"/> asks every service holding the company's data to delete it
/// (<see cref="PurgeCompanyData"/>, stage <see cref="CompanyPurgeStage.HardDelete"/>); the request
/// tracks progress (<see cref="CompanyErasureStatus"/>).</item>
/// <item><see cref="RecordResultAsync"/> takes hrms-api's answer. A failure parks the request for an
/// operator to retry; success deletes Tenant Service's rows for the company and hands its logins to
/// Auth Service (<see cref="CompanyHardDeleted"/>), in one transaction.</item>
/// </list>
/// Every step deletes by key, so repeating one is harmless. Doesn't commit: the caller owns the unit of work.
/// </summary>
public class PlatformCompanyDeletionService
{
    private readonly IUnitOfWorkService _uow;
    private readonly BillingEventService _events;
    private readonly IPublishEndpoint _publisher;
    private readonly TenantStoreAccountEraser _eraser;
    private readonly TimeProvider _clock;

    public PlatformCompanyDeletionService(
        IUnitOfWorkService uow,
        BillingEventService events,
        IPublishEndpoint publisher,
        TenantStoreAccountEraser eraser,
        TimeProvider clock)
    {
        _uow = uow;
        _events = events;
        _publisher = publisher;
        _eraser = eraser;
        _clock = clock;
    }

    private TenantContext Context => _uow.Context;
    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public Task<bool> CommitChangesAsync(CancellationToken token = default) => _uow.CommitChangesAsync("", token);

    /// <summary>Starts erasing the company of an approved <paramref name="request"/>.</summary>
    public async Task StartAsync(AccountDeletionRequest request, CancellationToken token)
    {
        var tenantId = request.TenantId ?? throw new InvalidOperationException("Not a company deletion request.");
        var subscription = await RequireSubscriptionAsync(request.OwnerUserId, token);
        request.Erasure = CompanyErasureStatus.Running;
        request.ErasureError = null;
        await _events.AddAsync(subscription, BillingEventTypes.HardDeleteRequested,
            new { requestId = request.Id, tenantId }, request.DecidedBy, token: token);
        await RequestAsync(subscription, tenantId, token);
    }

    /// <summary>Asks again for a company whose erasure failed (or never reported back).</summary>
    public async Task RetryAsync(Guid tenantId, string actor, CancellationToken token)
    {
        var request = await Context.AccountDeletionRequests
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Erasure != null, token)
            ?? throw new SubscriptionRuleException("This company isn't being deleted.");
        var subscription = await RequireSubscriptionAsync(request.OwnerUserId, token);
        request.Erasure = CompanyErasureStatus.Running;
        request.ErasureError = null;
        await _events.AddAsync(subscription, BillingEventTypes.HardDeleteRequested,
            new { requestId = request.Id, tenantId, retry = true }, actor, token: token);
        await RequestAsync(subscription, tenantId, token);
    }

    /// <summary>
    /// Records hrms-api's answer if it is for a company being erased on its own. Returns false when it
    /// isn't (then it belongs to a whole account's erasure, or to the yearly purge).
    /// </summary>
    public async Task<bool> RecordResultAsync(CompanyDataPurged result, CancellationToken token)
    {
        var request = await Context.AccountDeletionRequests
            .FirstOrDefaultAsync(r => r.TenantId == result.TenantId && r.OwnerUserId == result.OwnerUserId && r.Erasure != null, token);
        if (request is null) return false;

        if (!result.Succeeded)
        {
            request.Erasure = CompanyErasureStatus.Failed;
            request.ErasureError = result.Error is { Length: > 500 } error ? error[..500] : result.Error;
            var subscription = await RequireSubscriptionAsync(request.OwnerUserId, token);
            await _events.AddAsync(subscription, BillingEventTypes.HardDeleteFailed,
                new { requestId = request.Id, result.TenantId, result.Error }, actor: "system", token: token);
            return true;
        }

        await FinalizeAsync(request, token);
        return true;
    }

    /// <summary>
    /// Deletes Tenant Service's rows for the company and publishes <see cref="CompanyHardDeleted"/> in the
    /// same transaction. Pending tracked changes are dropped first: nothing about this company may be
    /// written after its rows are gone.
    /// </summary>
    private async Task FinalizeAsync(AccountDeletionRequest request, CancellationToken token)
    {
        var tenantId = request.TenantId!.Value;
        Context.ChangeTracker.Clear();

        var subscription = await Context.AccountSubscriptions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.OwnerUserId == request.OwnerUserId, token);
        var companyName = await Context.Tenants.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.Id == tenantId).Select(t => t.TenantName).FirstOrDefaultAsync(token);
        var (deleteUserIds, detachUserIds) = await ErasureUsers.PartitionAsync(Context, [tenantId], alsoInclude: null, token);

        await _eraser.EraseCompanyAsync(new CompanyErasureScope(request.OwnerUserId, subscription?.Id ?? Guid.Empty, tenantId, request.Id), token);

        await _publisher.Publish(new CompanyHardDeleted
        {
            RequestId = request.Id,
            OwnerUserId = request.OwnerUserId,
            OwnerEmail = request.OwnerEmail,
            TenantId = tenantId,
            CompanyName = companyName ?? request.CompanyName ?? tenantId.ToString(),
            DeleteUserIds = deleteUserIds,
            DetachUserIds = detachUserIds,
            RequestedBy = request.RequestedBy,
            ApprovedBy = request.DecidedBy ?? string.Empty,
            Reason = request.Reason,
            DeletedAtUtc = Now,
        }, token);
    }

    private Task RequestAsync(AccountSubscription subscription, Guid tenantId, CancellationToken token) =>
        _publisher.Publish(new PurgeCompanyData
        {
            OwnerUserId = subscription.OwnerUserId,
            SubscriptionId = subscription.Id,
            TenantId = tenantId,
            Stage = CompanyPurgeStage.HardDelete,
            RequestedAtUtc = Now,
        }, ctx => ctx.Headers.Set("X-Tenant-ID", tenantId.ToString()), token);

    private async Task<AccountSubscription> RequireSubscriptionAsync(Guid ownerUserId, CancellationToken token) =>
        await Context.AccountSubscriptions.FirstOrDefaultAsync(s => s.OwnerUserId == ownerUserId, token)
        ?? throw new KeyNotFoundException("This company's account has no subscription.");
}
