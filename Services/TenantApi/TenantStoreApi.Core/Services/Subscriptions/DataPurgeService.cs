using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Onepunch.Common.Lib.DTO;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Core.Services.Subscriptions;

/// <summary>
/// When a locked account's data gets a purge date, and what happens to that date as the account is
/// paid up again. Pure rules over the subscription -- <see cref="DataPurgeService"/> acts on them.
/// </summary>
public static class DataPurgeSchedule
{
    public static bool IsLocked(AccountSubscription subscription) =>
        subscription.SubStatus is AccountSubscriptionStatus.Suspended
            or AccountSubscriptionStatus.Expired or AccountSubscriptionStatus.Canceled;

    /// <summary>
    /// A locked account gets <c>PurgeAfterUtc</c> = the day it locked + the retention period, but never
    /// less than <see cref="SubscriptionOptions.MinPurgeNoticeDays"/> from now. An account that got access
    /// back (paid, reinstated) loses a purge that hadn't happened yet. One whose data is already gone
    /// keeps its state: paying again doesn't bring it back.
    /// </summary>
    public static void Reconcile(AccountSubscription subscription, DateTime nowUtc, SubscriptionOptions options)
    {
        if (subscription.PurgeState is DataPurgeState.Purged or DataPurgeState.ArchiveDestroyed) return;

        if (!IsLocked(subscription))
        {
            if (subscription.PurgeState != DataPurgeState.None || subscription.PurgeAfterUtc != null)
            {
                subscription.PurgeState = DataPurgeState.None;
                subscription.PurgeAfterUtc = null;
            }
            return;
        }

        if (subscription.PurgeAfterUtc == null)
        {
            var lockedAt = subscription.SubStatus switch
            {
                AccountSubscriptionStatus.Canceled => subscription.CanceledAtUtc,
                AccountSubscriptionStatus.Expired => subscription.TrialEndsAtUtc ?? subscription.CurrentPeriodEndUtc,
                _ => subscription.GraceEndsAtUtc,
            } ?? nowUtc;
            var due = lockedAt.AddDays(options.PurgeAfterDays);
            var earliest = nowUtc.AddDays(options.MinPurgeNoticeDays);
            subscription.PurgeAfterUtc = due > earliest ? due : earliest;
        }
        if (subscription.PurgeState == DataPurgeState.None) subscription.PurgeState = DataPurgeState.Scheduled;
    }
}

/// <summary>
/// Drives the removal of a locked account's data. Tenant Service owns the dates and decides; each
/// company's data lives in hrms-api, which does the deleting on a <see cref="PurgeCompanyData"/>
/// command and reports back (<see cref="CompanyDataPurged"/>). Two stages:
/// <list type="number">
/// <item>PurgeAfterUtc (a year after the lock) -- archive the company's records, then delete them
/// from the live databases. Compliance records survive in the archive.</item>
/// <item>ArchiveRetainUntilUtc (5 years after the last entry) -- destroy the archive.</item>
/// </list>
/// A legal hold stops both. Idempotent -- each request is keyed by day, so an unfinished stage is
/// asked again tomorrow, and hrms-api treats a repeat as a no-op. Doesn't commit.
/// </summary>
public class DataPurgeService
{
    private readonly IUnitOfWorkService _uow;
    private readonly BillingEventService _events;
    private readonly SubscriptionChangeNotifierService _notifier;
    private readonly IPublishEndpoint _publisher;
    private readonly SubscriptionOptions _options;
    private readonly TimeProvider _clock;

    public DataPurgeService(
        IUnitOfWorkService uow,
        BillingEventService events,
        SubscriptionChangeNotifierService notifier,
        IPublishEndpoint publisher,
        IOptions<SubscriptionOptions> options,
        TimeProvider clock)
    {
        _uow = uow;
        _events = events;
        _notifier = notifier;
        _publisher = publisher;
        _options = options.Value;
        _clock = clock;
    }

    private TenantContext Context => _uow.Context;

    /// <summary>Asks for whatever stage is due now, if any. Returns whether a request went out.</summary>
    public async Task<bool> RequestDueAsync(AccountSubscription subscription, DateTime now, CancellationToken token)
    {
        if (!_options.DataPurgeEnabled || subscription.LegalHold) return false;

        if (DataPurgeSchedule.IsLocked(subscription)
            && subscription.PurgeAfterUtc is { } purgeAfter && purgeAfter <= now
            && subscription.PurgeState is DataPurgeState.Scheduled or DataPurgeState.InProgress)
        {
            subscription.PurgeState = DataPurgeState.InProgress;
            await RequestAsync(subscription, CompanyPurgeStage.ArchiveAndPurge, BillingEventTypes.DataPurgeRequested, now, token);
            return true;
        }

        if (subscription.PurgeState == DataPurgeState.Purged
            && subscription.ArchiveRetainUntilUtc is { } retainUntil && retainUntil <= now)
        {
            await RequestAsync(subscription, CompanyPurgeStage.DestroyArchive, BillingEventTypes.ArchiveDestroyRequested, now, token);
            return true;
        }

        return false;
    }

    private async Task RequestAsync(
        AccountSubscription subscription, CompanyPurgeStage stage, string eventType, DateTime now, CancellationToken token)
    {
        var tenantIds = await OwnerTenantIdsAsync(subscription.OwnerUserId, token);
        foreach (var tenantId in tenantIds)
        {
            // Once per company per day: an unfinished stage is asked again the next run.
            var key = $"{eventType}:{subscription.Id:N}:{tenantId:N}:{now:yyyyMMdd}";
            if (await _events.ExistsAsync(key, token)) continue;

            await _publisher.Publish(new PurgeCompanyData
            {
                OwnerUserId = subscription.OwnerUserId,
                SubscriptionId = subscription.Id,
                TenantId = tenantId,
                Stage = stage,
                RequestedAtUtc = now,
            }, ctx => ctx.Headers.Set("X-Tenant-ID", tenantId.ToString()), token);
            await _events.AddAsync(subscription, eventType, new { tenantId, stage = stage.ToString() },
                actor: "system", dedupeKey: key, token: token);
        }
    }

    /// <summary>
    /// Records hrms-api's answer for one company. When the last company of the account reports in,
    /// the account moves to the next state: Purged (and its archive's retention date is set from the
    /// latest last-entry date any company reported), or ArchiveDestroyed.
    /// </summary>
    public async Task RecordResultAsync(CompanyDataPurged result, CancellationToken token)
    {
        var subscription = await Context.AccountSubscriptions
            .FirstOrDefaultAsync(s => s.Id == result.SubscriptionId && s.OwnerUserId == result.OwnerUserId, token);
        if (subscription == null) return;
        // A company that isn't this account's can't move its state.
        var tenantIds = await OwnerTenantIdsAsync(subscription.OwnerUserId, token);
        if (!tenantIds.Contains(result.TenantId)) return;

        var now = _clock.GetUtcNow().UtcDateTime;
        var destroying = result.Stage == CompanyPurgeStage.DestroyArchive;

        if (!result.Succeeded)
        {
            await _events.AddAsync(subscription, BillingEventTypes.DataPurgeFailed,
                new { result.TenantId, stage = result.Stage.ToString(), result.Error },
                actor: "system", dedupeKey: $"{BillingEventTypes.DataPurgeFailed}:{subscription.Id:N}:{result.TenantId:N}:{now:yyyyMMdd}", token: token);
            return;
        }

        var doneType = destroying ? BillingEventTypes.ArchiveDestroyed : BillingEventTypes.DataPurged;
        var doneKey = $"{doneType}:{subscription.Id:N}:{result.TenantId:N}";
        if (!await _events.ExistsAsync(doneKey, token))
        {
            await _events.AddAsync(subscription, doneType, new
            {
                result.TenantId,
                result.LastEntryUtc,
                result.ArchiveKey,
                result.ArchiveSha256,
                result.RowsArchived,
                result.RowsDeleted,
                result.FilesDeleted,
            }, actor: "system", dedupeKey: doneKey, token: token);
        }

        if (!destroying && result.LastEntryUtc is { } lastEntry)
        {
            var retainUntil = lastEntry.AddYears(_options.ArchiveRetentionYears);
            if (subscription.ArchiveRetainUntilUtc == null || retainUntil > subscription.ArchiveRetainUntilUtc)
                subscription.ArchiveRetainUntilUtc = retainUntil;
        }

        foreach (var tenantId in tenantIds)
        {
            if (!await _events.ExistsAsync($"{doneType}:{subscription.Id:N}:{tenantId:N}", token)) return;
        }

        if (destroying)
        {
            subscription.PurgeState = DataPurgeState.ArchiveDestroyed;
        }
        else
        {
            subscription.PurgeState = DataPurgeState.Purged;
            subscription.DataPurgedAtUtc = now;
            // No company reported an entry (nothing was ever recorded): the clock starts at the purge.
            subscription.ArchiveRetainUntilUtc ??= now.AddYears(_options.ArchiveRetentionYears);
        }
        subscription.ConcurrencyStamp = Guid.NewGuid();
        await _notifier.PublishAsync(subscription.OwnerUserId, doneType, token);
    }

    private Task<List<Guid>> OwnerTenantIdsAsync(Guid ownerUserId, CancellationToken token) =>
        Context.Tenants.AsNoTracking().Where(t => t.UserId == ownerUserId).Select(t => t.Id).ToListAsync(token);
}
