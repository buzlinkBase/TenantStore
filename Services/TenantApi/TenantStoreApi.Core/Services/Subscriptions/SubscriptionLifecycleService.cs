using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Quartz;
using Onepunch.Common.Lib.Subscriptions;
using Serilog;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Core.Services.Subscriptions;

/// <summary>
/// One pass of the subscription lifecycle for one account: applies the transitions now due
/// (<see cref="SubscriptionStateMachine"/>) and sends the emails now due
/// (<see cref="SubscriptionReminderSchedule"/>). Idempotent -- transitions are decided by dates
/// alone and each email by its dedupe key -- so running it late, twice, or on two instances at
/// once does no harm.
/// </summary>
public class SubscriptionLifecycleService
{
    private readonly IUnitOfWorkService _uow;
    private readonly SubscriptionStateMachine _stateMachine;
    private readonly BillingEventService _events;
    private readonly SubscriptionChangeNotifierService _notifier;
    private readonly SubscriptionUsageService _usage;
    private readonly DataPurgeService _purge;
    private readonly IPublishEndpoint _publisher;
    private readonly SubscriptionOptions _options;
    private readonly TimeProvider _clock;
    private readonly IEnumerable<ISubscriptionReminderRule> _rules;
    private readonly StorageAddOnService _storage;
    private readonly StorageSubscriptionService _storagePeriod;
    private readonly IEnumerable<IStorageReminderRule> _storageRules;
    private readonly Payments.AutoRenewService _autoRenew;

    public SubscriptionLifecycleService(
        IUnitOfWorkService uow,
        SubscriptionStateMachine stateMachine,
        BillingEventService events,
        SubscriptionChangeNotifierService notifier,
        SubscriptionUsageService usage,
        DataPurgeService purge,
        IPublishEndpoint publisher,
        IOptions<SubscriptionOptions> options,
        TimeProvider clock,
        StorageAddOnService storage,
        StorageSubscriptionService storagePeriod,
        Payments.AutoRenewService autoRenew)
    {
        _autoRenew = autoRenew;
        _storage = storage;
        _storagePeriod = storagePeriod;
        _storageRules = StorageReminderSchedule.Rules;
        _uow = uow;
        _stateMachine = stateMachine;
        _events = events;
        _notifier = notifier;
        _usage = usage;
        _purge = purge;
        _publisher = publisher;
        _options = options.Value;
        _clock = clock;
        _rules = SubscriptionReminderSchedule.Rules;
    }

    private TenantContext Context => _uow.Context;

    public Task<List<Guid>> ListSubscriptionIdsAsync(CancellationToken token) =>
        Context.AccountSubscriptions.AsNoTracking().Select(s => s.Id).ToListAsync(token);

    /// <summary>Processes one subscription and commits. Returns the transitions applied.</summary>
    public async Task<List<SubscriptionTransition>> ProcessAsync(Guid subscriptionId, CancellationToken token)
    {
        var subscription = await Context.AccountSubscriptions
            .Include(s => s.Plan)
            .Include(s => s.StorageAddOns)
            .Include(s => s.StorageSubscription)
            .FirstOrDefaultAsync(s => s.Id == subscriptionId, token);
        if (subscription == null) return [];

        var now = _clock.GetUtcNow().UtcDateTime;

        // Storage runs on its own period: scheduled reductions whose storage period has ended become
        // permanent, and the period moves on by date (past due, then lapsed). Entitlements already follow
        // the dates, so this keeps the stored rows, the next charge and the emails honest.
        await _storagePeriod.AdvanceAsync(subscription, now, token);

        var transitions = _stateMachine.Advance(subscription, now, _options);
        if (transitions.Count > 0)
        {
            subscription.ConcurrencyStamp = Guid.NewGuid();
            foreach (var transition in transitions)
            {
                await _events.AddAsync(subscription, transition.EventType,
                    new { from = transition.From.ToString(), to = transition.To.ToString() }, actor: "system", token: token);
            }
            await _notifier.PublishAsync(subscription.OwnerUserId, transitions[^1].EventType, token);
        }

        // A locked account gets its purge date (and loses it if it paid up), then, if the date has
        // come, hrms-api is told to archive and delete. The emails below warn ahead of it.
        DataPurgeSchedule.Reconcile(subscription, now, _options);
        await _purge.RequestDueAsync(subscription, now, token);

        // Auto renews whose target no longer renews (cancelled, suspended, a cycle switch) are stopped at the
        // gateway, and a charge whose webhook never came is recorded.
        await _autoRenew.ReconcileAsync(subscription, token);

        await SendDueEmailsAsync(subscription, now, token);
        await SendStorageAlertsAsync(subscription, now, token);
        await SendStorageBillingEmailsAsync(subscription, now, token);
        await _uow.CommitChangesAsync("", token);
        return transitions;
    }

    /// <summary>
    /// Tells the owner when the account's storage reaches 95% of its limit -- once per billing period,
    /// so the hourly run doesn't repeat it. Only for accounts still using the product; usage that
    /// couldn't be measured (unknown, not zero) never alerts.
    /// </summary>
    private async Task SendStorageAlertsAsync(AccountSubscription subscription, DateTime now, CancellationToken token)
    {
        if (subscription.SubStatus is not (AccountSubscriptionStatus.Trialing or AccountSubscriptionStatus.Active or AccountSubscriptionStatus.PastDue))
            return;

        StorageSummary summary;
        try
        {
            summary = await _storage.GetSummaryAsync(subscription.OwnerUserId, token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Logger.Warning(ex, "Subscription {SubscriptionId}: couldn't read storage usage", subscription.Id);
            return;
        }
        if (summary.Level != StorageLevel.Critical || summary.UsedMb == null || summary.LimitMb == null) return;

        // One email per account per billing period (storage is one pool for all its companies); only the owner.
        // The storage period when there is one, since storage is billed on its own.
        var period = (subscription.StorageSubscription?.CurrentPeriodEndUtc ?? subscription.CurrentPeriodEndUtc
            ?? subscription.TrialEndsAtUtc ?? now).ToString("yyyyMMdd");
        var dedupeKey = $"email:{SubscriptionEmailTemplates.StorageAlmostFull}:{subscription.Id:N}:{period}";
        if (await _events.ExistsAsync(dedupeKey, token)) return;

        var recipient = await Context.Memberships.AsNoTracking()
            .Where(m => m.UserId == subscription.OwnerUserId && m.IsHidden && m.InvitedEmail != null)
            .Select(m => new { m.InvitedEmail, m.FullName })
            .FirstOrDefaultAsync(token);
        if (recipient?.InvitedEmail == null)
        {
            Log.Logger.Warning("Subscription {SubscriptionId}: no owner email on file, storage alert not sent", subscription.Id);
            return;
        }

        await _publisher.Publish(new SubscriptionReminderEmail
        {
            Email = recipient.InvitedEmail,
            FullName = recipient.FullName,
            Template = SubscriptionEmailTemplates.StorageAlmostFull,
            PlanName = subscription.Plan?.Name,
            StoragePercent = summary.PercentUsed,
            StorageUsedMb = summary.UsedMb,
            StorageLimitMb = summary.LimitMb,
        }, token);
        await _events.AddAsync(subscription, BillingEventTypes.StorageThresholdCrossed,
            new { percent = summary.PercentUsed }, actor: "system", dedupeKey: dedupeKey, token: token);
    }

    /// <summary>Storage's own billing emails -- its renewal reminder, past due and lapsed -- apart from the plan's.</summary>
    private async Task SendStorageBillingEmailsAsync(AccountSubscription subscription, DateTime now, CancellationToken token)
    {
        if (subscription.StorageSubscription is not { } storage) return;
        var due = new List<ReminderDecision>();
        foreach (var rule in _storageRules)
        {
            if (rule.Evaluate(subscription, storage, now, _options) is { } decision && !await _events.ExistsAsync(decision.DedupeKey, token))
                due.Add(decision);
        }
        if (due.Count == 0) return;

        var recipient = await Context.Memberships.AsNoTracking()
            .Where(m => m.UserId == subscription.OwnerUserId && m.IsHidden && m.InvitedEmail != null)
            .Select(m => new { m.InvitedEmail, m.FullName })
            .FirstOrDefaultAsync(token);
        if (recipient?.InvitedEmail == null)
        {
            Log.Logger.Warning("Subscription {SubscriptionId}: no owner email on file, {Count} storage email(s) not sent", subscription.Id, due.Count);
            return;
        }

        foreach (var decision in due)
        {
            await _publisher.Publish(new SubscriptionReminderEmail
            {
                Email = recipient.InvitedEmail,
                FullName = recipient.FullName,
                Template = decision.Template,
                PlanName = subscription.Plan?.Name,
                PeriodEndUtc = storage.CurrentPeriodEndUtc,
                GraceEndsAtUtc = storage.GraceEndsAtUtc,
                PaymentMethodLabel = decision.Template == SubscriptionEmailTemplates.StorageRenewalDue
                    ? (await AutoRenewReadModel.ChargingAsync(Context, subscription.Id, AutoRenewTarget.Storage, token))?.PaymentMethodLabel
                    : null,
            }, token);
            await _events.AddAsync(subscription, BillingEventTypes.EmailSent,
                new { template = decision.Template }, actor: "system", dedupeKey: decision.DedupeKey, token: token);
        }
    }

    private async Task SendDueEmailsAsync(AccountSubscription subscription, DateTime now, CancellationToken token)
    {
        var due = new List<ReminderDecision>();
        foreach (var rule in _rules)
        {
            if (rule.Evaluate(subscription, now, _options) is { } decision && !await _events.ExistsAsync(decision.DedupeKey, token))
                due.Add(decision);
        }
        if (due.Count == 0) return;

        var recipient = await Context.Memberships.AsNoTracking()
            .Where(m => m.UserId == subscription.OwnerUserId && m.IsHidden && m.InvitedEmail != null)
            .Select(m => new { m.InvitedEmail, m.FullName })
            .FirstOrDefaultAsync(token);
        if (recipient?.InvitedEmail == null)
        {
            Log.Logger.Warning("Subscription {SubscriptionId}: no owner email on file, {Count} email(s) not sent", subscription.Id, due.Count);
            return;
        }

        int? employees = null;
        int? companies = null;
        if (due.Any(d => d.Template == SubscriptionEmailTemplates.TrialUsageSummary))
        {
            var usage = await _usage.GetCompanyUsageAsync(subscription.OwnerUserId, token);
            companies = usage.Count;
            employees = usage.Sum(c => c.Used.GetValueOrDefault(EntitlementKeys.EmployeesMax) ?? 0);
        }

        foreach (var decision in due)
        {
            await _publisher.Publish(new SubscriptionReminderEmail
            {
                Email = recipient.InvitedEmail,
                FullName = recipient.FullName,
                Template = decision.Template,
                PlanName = subscription.Plan?.Name,
                TrialEndsAtUtc = subscription.TrialEndsAtUtc,
                PeriodEndUtc = subscription.CurrentPeriodEndUtc,
                GraceEndsAtUtc = subscription.GraceEndsAtUtc,
                PurgeAfterUtc = subscription.PurgeAfterUtc,
                EmployeeCount = employees,
                CompanyCount = companies,
                PaymentMethodLabel = decision.Template == SubscriptionEmailTemplates.PaymentDue
                    ? (await AutoRenewReadModel.ChargingAsync(Context, subscription.Id, AutoRenewTarget.Plan, token))?.PaymentMethodLabel
                    : null,
            }, token);
            await _events.AddAsync(subscription, BillingEventTypes.EmailSent,
                new { template = decision.Template }, actor: "system", dedupeKey: decision.DedupeKey, token: token);
        }
    }
}

/// <summary>
/// The hourly Quartz job running <see cref="SubscriptionLifecycleService"/> for every account
/// (schedule: Subscriptions:LifecycleCron, see SubscriptionRegistrations). Quartz's clustered job
/// store makes each fire run on exactly one TenantApi instance, and runs a fire missed while every
/// instance was down as soon as one is back. Each run first catches up on activations and the
/// legacy backfill, then gives every account its own scope (and unit of work), so one failing --
/// e.g. a concurrent payment winning the optimistic-concurrency race -- is retried on the next fire
/// without holding up the rest.
/// </summary>
[DisallowConcurrentExecution]
public sealed class SubscriptionLifecycleJob(IServiceScopeFactory scopes) : IJob
{
    public static readonly JobKey Key = new("subscription-lifecycle", "tenantstore");
    public static readonly TriggerKey TriggerKey = new("subscription-lifecycle-trigger", "tenantstore");

    public Task Execute(IJobExecutionContext context) => RunOnceAsync(context.CancellationToken);
    public async Task RunOnceAsync(CancellationToken token)
    {
        // First catch up on companies that finished setting up without us hearing about it (and
        // give any still-unbilled existing owner the legacy plan) -- see CompanyActivationSyncService.
        try
        {
            using var syncScope = scopes.CreateScope();
            await syncScope.ServiceProvider.GetRequiredService<SubscriptionBackfillService>().EnsureBackfilledAsync(token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Logger.Error(ex, "Subscription lifecycle: activation sync/backfill failed");
        }

        List<Guid> ids;
        try
        {
            using var listScope = scopes.CreateScope();
            ids = await listScope.ServiceProvider.GetRequiredService<SubscriptionLifecycleService>().ListSubscriptionIdsAsync(token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Logger.Error(ex, "Subscription lifecycle: couldn't list subscriptions");
            return;
        }

        foreach (var id in ids)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<SubscriptionLifecycleService>().ProcessAsync(id, token);
            }
            catch (DbUpdateConcurrencyException)
            {
                Log.Logger.Information("Subscription lifecycle: {SubscriptionId} changed concurrently, retrying next run", id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Logger.Error(ex, "Subscription lifecycle: {SubscriptionId} failed", id);
            }
        }
    }
}
