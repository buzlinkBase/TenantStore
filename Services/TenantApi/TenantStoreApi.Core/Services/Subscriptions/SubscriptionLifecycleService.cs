using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
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
    private readonly IPublishEndpoint _publisher;
    private readonly SubscriptionOptions _options;
    private readonly TimeProvider _clock;
    private readonly IEnumerable<ISubscriptionReminderRule> _rules;

    public SubscriptionLifecycleService(
        IUnitOfWorkService uow,
        SubscriptionStateMachine stateMachine,
        BillingEventService events,
        SubscriptionChangeNotifierService notifier,
        SubscriptionUsageService usage,
        IPublishEndpoint publisher,
        IOptions<SubscriptionOptions> options,
        TimeProvider clock)
    {
        _uow = uow;
        _stateMachine = stateMachine;
        _events = events;
        _notifier = notifier;
        _usage = usage;
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
            .FirstOrDefaultAsync(s => s.Id == subscriptionId, token);
        if (subscription == null) return [];

        var now = _clock.GetUtcNow().UtcDateTime;
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

        await SendDueEmailsAsync(subscription, now, token);
        await _uow.CommitChangesAsync("", token);
        return transitions;
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
            }, token);
            await _events.AddAsync(subscription, BillingEventTypes.EmailSent,
                new { template = decision.Template }, actor: "system", dedupeKey: decision.DedupeKey, token: token);
        }
    }
}

/// <summary>
/// Runs <see cref="SubscriptionLifecycleService"/> for every account shortly after startup and
/// then every <see cref="SubscriptionOptions.LifecycleIntervalMinutes"/>. Each account gets its
/// own scope (and unit of work), so one failing -- e.g. a concurrent payment winning the
/// optimistic-concurrency race -- is retried on the next run without holding up the rest.
/// </summary>
public sealed class SubscriptionLifecycleJob(IServiceScopeFactory scopes, IOptions<SubscriptionOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, options.Value.LifecycleIntervalMinutes)));
            do
            {
                await RunOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public async Task RunOnceAsync(CancellationToken token)
    {
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
