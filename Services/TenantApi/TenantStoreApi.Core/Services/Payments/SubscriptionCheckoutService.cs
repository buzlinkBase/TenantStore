using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Core.Services.Payments;

public sealed record CheckoutStarted(Guid CheckoutId, string CheckoutUrl);

/// <summary>A checkout as the payer's return screen shows it.</summary>
public sealed record CheckoutView(
    Guid Id,
    PaymentCheckoutStatus Status,
    string PlanName,
    BillingCycle BillingCycle,
    decimal Amount,
    string Currency,
    DateTime CreatedAtUtc,
    DateTime? PaidAtUtc,
    PaymentMethod? PaymentMethod,
    PaymentCheckoutKind Kind = PaymentCheckoutKind.Plan,
    /// <summary>It pays the first invoice of an auto renew: once paid, the gateway renews on its own.</summary>
    bool AutoRenew = false);

public enum WebhookOutcome
{
    /// <summary>Not signed by the gateway: refused (401).</summary>
    Rejected,
    /// <summary>Authentic but not ours to act on (another event type, the other mode).</summary>
    Ignored,
    /// <summary>Already processed.</summary>
    Duplicate,
    /// <summary>Paid, but not applied -- no matching checkout, or not the amount quoted. Recorded for an operator.</summary>
    Flagged,
    Settled,
}

public enum ReconcileResult
{
    Unchanged,
    /// <summary>A payment the webhook missed was applied.</summary>
    Settled,
    /// <summary>A refund the webhook missed was recorded.</summary>
    RefundFlagged,
}

/// <summary>
/// Paying for a subscription online. <see cref="StartAsync"/> quotes what's due
/// (<see cref="AccountSubscriptionService.GetPaymentDueAsync"/>), saves it as a
/// <see cref="PaymentCheckout"/> and opens the gateway's hosted page for it. The payment is applied
/// when the gateway says it's paid -- its webhook (<see cref="HandleWebhookAsync"/>), or the gateway
/// itself asked from the return screen (<see cref="RefreshAsync"/>) -- by recording it through
/// <see cref="AccountSubscriptionService.RecordPaymentAsync"/>, for exactly the quoted plan and cycle.
/// Each checkout settles once: its status, and a billing event keyed on it (unique), see to that
/// even when a webhook and the return screen race.
/// Unlike the other subscription services this one commits (once per call -- BuzlinkRepository's
/// unit of work commits only once): what the gateway was asked to do can't be rolled back, so the
/// record of it is saved right here.
/// </summary>
public class SubscriptionCheckoutService
{
    /// <summary>A double click or second tab gets the page already open for the same quote.</summary>
    public static readonly TimeSpan ReuseWindow = TimeSpan.FromHours(1);
    /// <summary>How long after opening an unpaid checkout is still asked about by reconciliation.</summary>
    public static readonly TimeSpan ReconcileOpenWindow = TimeSpan.FromHours(48);
    /// <summary>How long after paying a checkout is still checked for a refund by reconciliation.</summary>
    public static readonly TimeSpan ReconcileRefundWindow = TimeSpan.FromDays(30);

    private const string ReturnPath = "/billing/payment";

    private readonly IUnitOfWorkService _uow;
    private readonly AccountSubscriptionService _subscriptions;
    private readonly BillingEventService _events;
    private readonly IPaymentGateway _gateway;
    private readonly PaymentOptions _options;
    private readonly TimeProvider _clock;
    private readonly StorageAddOnService _storage;
    private readonly StorageSubscriptionService _storagePeriod;

    public SubscriptionCheckoutService(
        IUnitOfWorkService uow,
        AccountSubscriptionService subscriptions,
        BillingEventService events,
        IPaymentGateway gateway,
        IOptions<PaymentOptions> options,
        TimeProvider clock,
        StorageAddOnService storage,
        StorageSubscriptionService storagePeriod)
    {
        _storagePeriod = storagePeriod;
        _uow = uow;
        _subscriptions = subscriptions;
        _events = events;
        _gateway = gateway;
        _options = options.Value;
        _clock = clock;
        _storage = storage;
    }

    private TenantContext Context => _uow.Context;
    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public bool IsEnabled => _gateway.IsEnabled;

    // ── Paying ───────────────────────────────────────────────────────────────────────────────

    /// <summary>Opens the hosted payment page for what the account owes now.</summary>
    public async Task<CheckoutStarted> StartAsync(Guid ownerUserId, Guid actorUserId, CancellationToken token = default)
    {
        if (!_gateway.IsEnabled)
            throw new SubscriptionRuleException("Online payment isn't available yet -- please use the payment instructions.");

        var subscription = await _subscriptions.FindAsync(ownerUserId, token)
            ?? throw new KeyNotFoundException("This account has no subscription.");
        if (AccountSubscriptionService.IsSuspendedByOperator(subscription))
            throw new SubscriptionRuleException("This account was suspended by Onepunch support, so it can't be reactivated by paying. Please contact support.");
        var due = await _subscriptions.GetPaymentDueAsync(subscription, token)
            ?? throw new SubscriptionRuleException("There's nothing to pay right now.");
        if (due.Reason == PaymentDueReason.Renewal
            && await AutoRenewReadModel.ChargingAsync(Context, subscription.Id, AutoRenewTarget.Plan, token) != null)
            throw new SubscriptionRuleException("Your plan renews automatically. Turn auto renew off first to pay by hand.");
        if (due.Amount <= 0)
            throw new SubscriptionRuleException("There's nothing to pay online for this plan -- please contact billing to activate it.");

        var now = Now;
        var open = await Context.PaymentCheckouts
            .Where(c => c.SubscriptionId == subscription.Id && c.CheckoutStatus == PaymentCheckoutStatus.Open
                && c.Kind == PaymentCheckoutKind.Plan && c.MandateId == null)
            .ToListAsync(token);

        var reusable = open.FirstOrDefault(c =>
            c.Provider == _gateway.Provider
            && c.PlanId == due.Plan.Id
            && c.BillingCycle == due.BillingCycle
            && c.Amount == due.Amount
            && c.CheckoutUrl != null
            && now - c.CreatedAtUtc < ReuseWindow);
        if (reusable?.ProviderSessionId is { } reusableSessionId)
        {
            var session = await _gateway.GetCheckoutAsync(reusableSessionId, token);
            if (session.Metadata.TryGetValue(MetadataKeys.CheckoutFormat, out var format)
                && format == MetadataKeys.CurrentCheckoutFormat)
            {
                return new CheckoutStarted(reusable.Id, reusable.CheckoutUrl!);
            }
        }

        // Any other open page is for a quote that no longer applies: close it so it can't be paid too.
        foreach (var stale in open) await ExpireAsync(stale, token);

        var checkout = new PaymentCheckout
        {
            Id = Guid.CreateVersion7(),
            SubscriptionId = subscription.Id,
            OwnerUserId = ownerUserId,
            Provider = _gateway.Provider,
            PlanId = due.Plan.Id,
            BillingCycle = due.BillingCycle,
            Amount = due.Amount,
            Currency = due.Plan.Currency,
            LineItemsJson = JsonSerializer.Serialize(BuildLineItems(due)),
            CreatedByUserId = actorUserId,
            CreatedAtUtc = now,
        };

        return await OpenSessionAsync(checkout, BuildRequest(checkout, due), subscription,
            new { plan = due.Plan.Code, cycle = due.BillingCycle, amount = due.Amount, reason = due.Reason },
            actorUserId, token);
    }

    /// <summary>Opens the hosted payment page for blocks of storage the owner chose for the account
    /// (priced by <see cref="StorageAddOnService.QuoteAsync"/>: prorated to the storage due date, or a full
    /// storage period on <paramref name="cycle"/> when none is running).</summary>
    public async Task<CheckoutStarted> StartStorageAsync(
        Guid ownerUserId, string offeringCode, int blocks, Guid actorUserId, BillingCycle? cycle = null, CancellationToken token = default)
    {
        if (!_gateway.IsEnabled)
            throw new SubscriptionRuleException("Online payment isn't available yet -- please contact billing to add storage.");

        var subscription = await _subscriptions.FindAsync(ownerUserId, token)
            ?? throw new KeyNotFoundException("This account has no subscription.");
        var quote = await _storage.QuoteAsync(ownerUserId, offeringCode, blocks, cycle, token);
        if (quote.AmountDue <= 0)
            throw new SubscriptionRuleException("There's nothing to pay online for this purchase -- please contact billing.");
        var offering = await Context.StorageAddOnOfferings.AsNoTracking().FirstAsync(o => o.Code == quote.OfferingCode, token);

        var now = Now;
        var open = await Context.PaymentCheckouts
            .Where(c => c.SubscriptionId == subscription.Id && c.CheckoutStatus == PaymentCheckoutStatus.Open
                && c.Kind == PaymentCheckoutKind.StorageAddOn && c.MandateId == null)
            .ToListAsync(token);

        var reusable = open.FirstOrDefault(c =>
            c.Provider == _gateway.Provider
            && c.StorageOfferingId == offering.Id
            && c.StorageBlocks == blocks
            && c.BillingCycle == quote.BillingCycle
            && c.Amount == quote.AmountDue
            && c.CheckoutUrl != null
            && now - c.CreatedAtUtc < ReuseWindow);
        if (reusable != null) return new CheckoutStarted(reusable.Id, reusable.CheckoutUrl!);

        foreach (var stale in open) await ExpireAsync(stale, token);

        var checkout = new PaymentCheckout
        {
            Id = Guid.CreateVersion7(),
            SubscriptionId = subscription.Id,
            OwnerUserId = ownerUserId,
            Kind = PaymentCheckoutKind.StorageAddOn,
            Provider = _gateway.Provider,
            PlanId = subscription.PlanId,
            BillingCycle = quote.BillingCycle,
            StorageOfferingId = offering.Id,
            StorageBlocks = blocks,
            Amount = quote.AmountDue,
            Currency = quote.Currency,
            CreatedByUserId = actorUserId,
            CreatedAtUtc = now,
        };
        var request = new GatewayCheckoutRequest
        {
            CheckoutId = checkout.Id,
            ItemName = $"{blocks} x {offering.Name}",
            Description = quote.StartsNewPeriod
                ? $"{_options.ProductName} extra storage, {StorageSubscriptionService.CycleLabel(quote.BillingCycle)} billing"
                : $"{_options.ProductName} extra storage, prorated to the end of the current storage period",
            Amount = quote.AmountDue,
            Currency = quote.Currency,
            SuccessUrl = ReturnUrl(checkout),
            CancelUrl = $"{ReturnUrl(checkout)}&canceled=1",
            Metadata = CheckoutMetadata(checkout),
        };
        return await OpenSessionAsync(checkout, request, subscription,
            new { kind = checkout.Kind, offering = offering.Code, blocks, amount = quote.AmountDue, cycle = quote.BillingCycle },
            actorUserId, token);
    }

    /// <summary>Opens the hosted payment page for the storage renewal now due -- billed on storage's own
    /// cycle, apart from the plan (see <see cref="StorageSubscriptionService.GetRenewalDueAsync"/>).</summary>
    public async Task<CheckoutStarted> StartStorageRenewalAsync(Guid ownerUserId, Guid actorUserId, CancellationToken token = default)
    {
        if (!_gateway.IsEnabled)
            throw new SubscriptionRuleException("Online payment isn't available yet -- please contact billing to pay for storage.");

        var subscription = await _subscriptions.FindAsync(ownerUserId, token)
            ?? throw new KeyNotFoundException("This account has no subscription.");
        if (AccountSubscriptionService.IsSuspendedByOperator(subscription))
            throw new SubscriptionRuleException("This account was suspended by Onepunch support. Please contact support.");
        var due = await _storagePeriod.GetRenewalDueAsync(subscription, token)
            ?? throw new SubscriptionRuleException("There's no storage renewal to pay right now.");
        if (due.Reason == StorageDueReason.Renewal
            && await AutoRenewReadModel.ChargingAsync(Context, subscription.Id, AutoRenewTarget.Storage, token) != null)
            throw new SubscriptionRuleException("Your storage renews automatically. Turn auto renew off first to pay by hand.");
        if (due.Amount <= 0)
            throw new SubscriptionRuleException("There's nothing to pay online for this storage -- please contact billing.");

        var now = Now;
        var open = await Context.PaymentCheckouts
            .Where(c => c.SubscriptionId == subscription.Id && c.CheckoutStatus == PaymentCheckoutStatus.Open
                && c.Kind == PaymentCheckoutKind.StorageRenewal && c.MandateId == null)
            .ToListAsync(token);
        var reusable = open.FirstOrDefault(c =>
            c.Provider == _gateway.Provider
            && c.BillingCycle == due.BillingCycle
            && c.Amount == due.Amount
            && c.CheckoutUrl != null
            && now - c.CreatedAtUtc < ReuseWindow);
        if (reusable != null) return new CheckoutStarted(reusable.Id, reusable.CheckoutUrl!);

        foreach (var stale in open) await ExpireAsync(stale, token);

        var checkout = new PaymentCheckout
        {
            Id = Guid.CreateVersion7(),
            SubscriptionId = subscription.Id,
            OwnerUserId = ownerUserId,
            Kind = PaymentCheckoutKind.StorageRenewal,
            Provider = _gateway.Provider,
            PlanId = subscription.PlanId,
            BillingCycle = due.BillingCycle,
            StorageBlocks = due.Blocks,
            Amount = due.Amount,
            Currency = due.Currency,
            LineItemsJson = JsonSerializer.Serialize(due.LineItems),
            CreatedByUserId = actorUserId,
            CreatedAtUtc = now,
        };
        var cycleLabel = StorageSubscriptionService.CycleLabel(due.BillingCycle);
        var request = new GatewayCheckoutRequest
        {
            CheckoutId = checkout.Id,
            ItemName = $"Extra storage ({cycleLabel}), {due.Blocks} {(due.Blocks == 1 ? "block" : "blocks")}",
            Description = due.Reason == StorageDueReason.Reactivation
                ? $"{_options.ProductName} extra storage reactivation"
                : $"{_options.ProductName} extra storage renewal",
            Amount = due.Amount,
            Currency = due.Currency,
            SuccessUrl = ReturnUrl(checkout),
            CancelUrl = $"{ReturnUrl(checkout)}&canceled=1",
            Metadata = CheckoutMetadata(checkout),
        };
        return await OpenSessionAsync(checkout, request, subscription,
            new { kind = checkout.Kind, cycle = due.BillingCycle, due.Blocks, amount = due.Amount, reason = due.Reason },
            actorUserId, token);
    }

    private async Task<CheckoutStarted> OpenSessionAsync(
        PaymentCheckout checkout, GatewayCheckoutRequest request, AccountSubscription subscription,
        object quoted, Guid actorUserId, CancellationToken token)
    {
        GatewayCheckoutSession session;
        try
        {
            session = await _gateway.CreateCheckoutAsync(request, token);
        }
        catch (PaymentGatewayException ex)
        {
            checkout.CheckoutStatus = PaymentCheckoutStatus.Failed;
            checkout.FailureReason = Truncate(ex.Message, 512);
            await Context.PaymentCheckouts.AddAsync(checkout, token);
            await _uow.CommitChangesAsync("", token);
            throw;
        }

        // The page's address is handed out only once this is saved, so a payment made on it always
        // finds its quote. (Saved after the gateway call, not before: a unit of work commits once.)
        checkout.ProviderSessionId = session.SessionId;
        checkout.CheckoutUrl = session.CheckoutUrl;
        checkout.CheckoutStatus = PaymentCheckoutStatus.Open;
        await Context.PaymentCheckouts.AddAsync(checkout, token);
        await _events.AddAsync(subscription, BillingEventTypes.CheckoutStarted,
            new { checkoutId = checkout.Id, quoted, provider = _gateway.Provider, sessionId = session.SessionId },
            actorUserId.ToString(), token: token);
        await _uow.CommitChangesAsync("", token);
        return new CheckoutStarted(checkout.Id, session.CheckoutUrl);
    }

    /// <summary>
    /// A checkout as it stands -- first asking the gateway, while it's unpaid, whether it has been
    /// paid since (the webhook may be late, or not reach this environment at all).
    /// </summary>
    public async Task<CheckoutView> RefreshAsync(Guid ownerUserId, Guid checkoutId, CancellationToken token = default)
    {
        var checkout = await Context.PaymentCheckouts
            .FirstOrDefaultAsync(c => c.Id == checkoutId && c.OwnerUserId == ownerUserId, token)
            ?? throw new KeyNotFoundException("Payment not found.");

        if (checkout.CheckoutStatus is PaymentCheckoutStatus.Open or PaymentCheckoutStatus.Expired
            && checkout.ProviderSessionId is { } sessionId
            && checkout.MandateId == null
            && checkout.Provider == _gateway.Provider
            && _gateway.IsEnabled)
        {
            try
            {
                var session = await _gateway.GetCheckoutAsync(sessionId, token);
                if (session.IsPaid)
                {
                    await SettleAsync(session, token);
                    await _uow.CommitChangesAsync("", token);
                }
            }
            catch (PaymentGatewayException ex)
            {
                Log.Logger.Warning(ex, "Checkout {CheckoutId}: couldn't ask the gateway for its status", checkoutId);
            }
            catch (DbUpdateException ex)
            {
                // Usually the webhook settling it at the same moment: show what was saved.
                Log.Logger.Information(ex, "Checkout {CheckoutId}: settled concurrently", checkoutId);
                await Context.Entry(checkout).ReloadAsync(token);
            }
        }

        var isStorage = checkout.Kind is PaymentCheckoutKind.StorageAddOn or PaymentCheckoutKind.StorageRenewal;
        var name = checkout.Kind == PaymentCheckoutKind.StorageRenewal
            ? StorageAddOnService.DescribeRenewal(checkout.StorageBlocks ?? 0, checkout.BillingCycle)
            : isStorage
            ? $"{checkout.StorageBlocks} x " + (await Context.StorageAddOnOfferings.AsNoTracking()
                .Where(o => o.Id == checkout.StorageOfferingId)
                .Select(o => o.Name)
                .FirstOrDefaultAsync(token) ?? "storage")
            : await Context.Plans.AsNoTracking()
                .Where(p => p.Id == checkout.PlanId)
                .Select(p => p.Name)
                .FirstOrDefaultAsync(token) ?? string.Empty;
        PaymentMethod? method = null;
        if (isStorage && checkout.StorageAddOnPaymentId is { } storagePaymentId)
            method = await Context.StorageAddOnPayments.AsNoTracking()
                .Where(p => p.Id == storagePaymentId)
                .Select(p => (PaymentMethod?)p.Method)
                .FirstOrDefaultAsync(token);
        else if (checkout.SubscriptionPaymentId is { } paymentId)
            method = await Context.SubscriptionPayments.AsNoTracking()
                .Where(p => p.Id == paymentId)
                .Select(p => (PaymentMethod?)p.Method)
                .FirstOrDefaultAsync(token);
        return new CheckoutView(
            checkout.Id, checkout.CheckoutStatus, name, checkout.BillingCycle, checkout.Amount, checkout.Currency,
            checkout.CreatedAtUtc, checkout.PaidAtUtc, method, checkout.Kind, checkout.MandateId != null);
    }

    // ── Webhook ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A webhook delivery. Anything authentic is acknowledged -- even what isn't applied is recorded
    /// for an operator -- so the gateway stops retrying; a failure to save throws, and the gateway's
    /// retry tries again.
    /// </summary>
    public async Task<WebhookOutcome> HandleWebhookAsync(string rawBody, string? signatureHeader, CancellationToken token = default)
    {
        var received = _gateway.ReadWebhook(rawBody, signatureHeader);
        if (received == null) return WebhookOutcome.Rejected;

        if (received.LiveMode != _gateway.LiveMode)
        {
            Log.Logger.Warning("Payment webhook {EventId} ({Type}) is from the other mode (live: {LiveMode}) -- ignored",
                received.EventId, received.Type, received.LiveMode);
            return WebhookOutcome.Ignored;
        }
        if (received.Kind is GatewayEventKind.PaymentRefunded or GatewayEventKind.PaymentDisputed)
        {
            if (received.Issue is not { } issue) return WebhookOutcome.Ignored;
            var flagged = await FlagPaymentIssueAsync(received.Kind, received.Type, issue, token);
            await _uow.CommitChangesAsync("", token);
            return flagged;
        }
        if (received.Kind != GatewayEventKind.CheckoutPaid || received.Session is not { IsPaid: true } session)
            return WebhookOutcome.Ignored;

        var outcome = await SettleAsync(session, token);
        await _uow.CommitChangesAsync("", token);
        return outcome;
    }

    /// <summary>
    /// A refund or dispute against a payment taken here. Recorded as a billing event for the operator --
    /// access, plan and blocks are left alone. Once per gateway payment and kind; an event naming a
    /// payment this system never took is recorded too, with no owner.
    /// </summary>
    private async Task<WebhookOutcome> FlagPaymentIssueAsync(
        GatewayEventKind kind, string gatewayEvent, GatewayPaymentIssue issue, CancellationToken token)
    {
        var type = kind == GatewayEventKind.PaymentRefunded
            ? BillingEventTypes.PaymentRefunded
            : BillingEventTypes.PaymentDisputed;
        var key = $"{_gateway.Provider}:payment:{issue.PaymentId}:{type}";
        if (await _events.ExistsAsync(key, token)) return WebhookOutcome.Duplicate;

        var checkout = await Context.PaymentCheckouts.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Provider == _gateway.Provider && c.ProviderPaymentId == issue.PaymentId, token);
        var subscription = checkout == null ? null : await _subscriptions.FindAsync(checkout.OwnerUserId, token);

        Log.Logger.Warning("Payment {PaymentId}: {Type} ({Amount}) -- needs an operator", issue.PaymentId, type, issue.Amount);
        await _events.AddAsync(subscription, type,
            new
            {
                gatewayPaymentId = issue.PaymentId, issue.Amount, gatewayEvent,
                checkoutId = checkout?.Id, kind = checkout?.Kind.ToString(), matched = checkout != null,
                subscriptionPaymentId = checkout?.SubscriptionPaymentId, storageAddOnPaymentId = checkout?.StorageAddOnPaymentId,
            },
            _gateway.Provider, key, checkout?.OwnerUserId, token);
        return WebhookOutcome.Flagged;
    }

    // ── Reconciliation ───────────────────────────────────────────────────────────────────────

    /// <summary>Checkouts still unpaid (or paid but not yet checked for a refund) recently enough to be worth
    /// asking the gateway about -- what a missed webhook leaves behind. Empty while online payment is off.</summary>
    public async Task<IReadOnlyList<Guid>> ListReconcilableAsync(Guid? ownerUserId, CancellationToken token = default)
    {
        if (!_gateway.IsEnabled) return [];
        var now = Now;
        var openSince = now - ReconcileOpenWindow;
        var paidSince = now - ReconcileRefundWindow;
        var checkouts = Context.PaymentCheckouts.AsNoTracking()
            .Where(c => c.Provider == _gateway.Provider && c.ProviderSessionId != null
                && (((c.CheckoutStatus == PaymentCheckoutStatus.Open || c.CheckoutStatus == PaymentCheckoutStatus.Expired) && c.CreatedAtUtc >= openSince)
                    || (c.CheckoutStatus == PaymentCheckoutStatus.Paid && c.PaidAtUtc >= paidSince && c.ProviderPaymentId != null)));
        if (ownerUserId is { } owner) checkouts = checkouts.Where(c => c.OwnerUserId == owner);
        return await checkouts.OrderBy(c => c.CreatedAtUtc).Select(c => c.Id).ToListAsync(token);
    }

    /// <summary>
    /// Catches up on one checkout whose webhook may never have arrived: an unpaid one the gateway says is
    /// paid is settled (as the webhook would have); a paid one the gateway says was refunded is flagged
    /// (the same event, keyed the same way, so a webhook that did arrive isn't recorded twice). Commits
    /// once, so call it with a unit of work of its own per checkout.
    /// </summary>
    public async Task<ReconcileResult> ReconcileAsync(Guid checkoutId, CancellationToken token = default)
    {
        var checkout = await Context.PaymentCheckouts.FirstOrDefaultAsync(c => c.Id == checkoutId, token);
        if (checkout == null || !_gateway.IsEnabled || checkout.Provider != _gateway.Provider) return ReconcileResult.Unchanged;

        try
        {
            // An auto renew's first payment isn't a hosted session: AutoRenewService reconciles it.
            if (checkout.CheckoutStatus is PaymentCheckoutStatus.Open or PaymentCheckoutStatus.Expired
                && checkout.ProviderSessionId is { } sessionId
                && checkout.MandateId == null)
            {
                var session = await _gateway.GetCheckoutAsync(sessionId, token);
                if (session.IsPaid)
                {
                    var outcome = await SettleAsync(session, token);
                    // Flagged (an amount mismatch, say) is saved too: it's what an operator is shown.
                    if (outcome != WebhookOutcome.Duplicate) await _uow.CommitChangesAsync("", token);
                    return outcome == WebhookOutcome.Settled ? ReconcileResult.Settled : ReconcileResult.Unchanged;
                }
            }
            else if (checkout.CheckoutStatus == PaymentCheckoutStatus.Paid && checkout.ProviderPaymentId is { } paymentId)
            {
                var payment = await _gateway.GetPaymentAsync(paymentId, token);
                if (payment.RefundedAmount > 0m
                    && await FlagPaymentIssueAsync(GatewayEventKind.PaymentRefunded, "reconciled",
                        new GatewayPaymentIssue(paymentId, payment.RefundedAmount), token) == WebhookOutcome.Flagged)
                {
                    await _uow.CommitChangesAsync("", token);
                    return ReconcileResult.RefundFlagged;
                }
            }
        }
        catch (PaymentGatewayException ex)
        {
            Log.Logger.Warning(ex, "Checkout {CheckoutId}: couldn't reconcile with the gateway", checkoutId);
        }
        return ReconcileResult.Unchanged;
    }

    // ── Settlement ───────────────────────────────────────────────────────────────────────────

    /// <summary>Applies a paid session to its checkout. Doesn't commit.</summary>
    /// <summary>
    /// Settles a paid gateway payment against the checkout it names -- exactly as a hosted checkout's
    /// webhook would. Used by auto renew for its first payment, where <paramref name="session"/> stands
    /// for the gateway subscription (<see cref="PaymentCheckout.ProviderSessionId"/>). Doesn't commit.
    /// </summary>
    public Task<WebhookOutcome> SettlePaidAsync(GatewaySessionState session, CancellationToken token) =>
        SettleAsync(session, token);

    private async Task<WebhookOutcome> SettleAsync(GatewaySessionState session, CancellationToken token)
    {
        var payment = session.Payment!;
        var checkout = await FindCheckoutAsync(session, token);
        if (checkout == null)
        {
            return await FlagAsync(null, BillingEventTypes.CheckoutUnmatched, session, OwnerFromMetadata(session),
                new { sessionId = session.SessionId, payment.PaymentId, payment.Amount, session.Metadata }, token);
        }
        if (checkout.CheckoutStatus == PaymentCheckoutStatus.Paid) return WebhookOutcome.Duplicate;

        var settledKey = $"{_gateway.Provider}:checkout:{checkout.Id:N}:paid";
        if (await _events.ExistsAsync(settledKey, token)) return WebhookOutcome.Duplicate;

        var subscription = await _subscriptions.FindAsync(checkout.OwnerUserId, token);
        if (payment.Amount != checkout.Amount || !string.Equals(payment.Currency, checkout.Currency, StringComparison.OrdinalIgnoreCase))
        {
            Log.Logger.Error("Checkout {CheckoutId}: paid {Paid} {PaidCurrency}, quoted {Quoted} {Currency} -- not applied",
                checkout.Id, payment.Amount, payment.Currency, checkout.Amount, checkout.Currency);
            return await FlagAsync(subscription, BillingEventTypes.CheckoutAmountMismatch, session, checkout.OwnerUserId,
                new { checkoutId = checkout.Id, sessionId = session.SessionId, payment.PaymentId, paid = payment.Amount, quoted = checkout.Amount }, token);
        }

        if (checkout.Kind is PaymentCheckoutKind.StorageAddOn or PaymentCheckoutKind.StorageRenewal)
            return await SettleStorageAsync(checkout, session, subscription, settledKey, token);

        var planCode = await Context.Plans.AsNoTracking()
            .Where(p => p.Id == checkout.PlanId)
            .Select(p => p.Code)
            .FirstAsync(token);
        var lineItems = checkout.LineItemsJson == null
            ? null
            : JsonSerializer.Deserialize<List<PaymentLineItemSnapshot>>(checkout.LineItemsJson);
        var recorded = await _subscriptions.RecordPaymentAsync(checkout.OwnerUserId, new RecordPaymentCommand
        {
            Amount = payment.Amount,
            Method = payment.Method,
            Reference = payment.PaymentId,
            PaidAtUtc = payment.PaidAtUtc ?? Now,
            PlanCode = planCode,
            BillingCycle = checkout.BillingCycle,
            Notes = $"Paid online ({checkout.Provider} checkout {session.SessionId})",
            LineItems = lineItems,
            // The credit as quoted on the page that was paid, not as it stands now.
            CreditAmount = lineItems == null ? null : CreditOf(lineItems),
        }, checkout.Provider, token);

        checkout.CheckoutStatus = PaymentCheckoutStatus.Paid;
        checkout.PaidAtUtc = recorded.PaidAtUtc;
        checkout.ProviderPaymentId = payment.PaymentId;
        checkout.ProviderPaymentMethod = payment.MethodName;
        checkout.SubscriptionPaymentId = recorded.Id;

        await _events.AddAsync(subscription, BillingEventTypes.CheckoutPaid,
            new
            {
                checkoutId = checkout.Id, sessionId = session.SessionId, gatewayPaymentId = payment.PaymentId,
                payment.Amount, method = payment.MethodName, subscriptionPaymentId = recorded.Id,
            },
            checkout.Provider, settledKey, checkout.OwnerUserId, token);
        return WebhookOutcome.Settled;
    }

    /// <summary>A paid storage checkout: adds exactly the blocks that were quoted, or renews the storage period.</summary>
    private async Task<WebhookOutcome> SettleStorageAsync(
        PaymentCheckout checkout, GatewaySessionState session, AccountSubscription? subscription, string settledKey, CancellationToken token)
    {
        var payment = session.Payment!;
        StorageAddOnPayment recorded;
        if (checkout.Kind == PaymentCheckoutKind.StorageRenewal)
        {
            recorded = await _storagePeriod.RecordRenewalAsync(
                checkout.OwnerUserId, payment.Amount, payment.Method, payment.PaymentId, checkout.Provider,
                payment.PaidAtUtc, token);
        }
        else
        {
            var offeringCode = await Context.StorageAddOnOfferings.AsNoTracking()
                .Where(o => o.Id == checkout.StorageOfferingId)
                .Select(o => o.Code)
                .FirstAsync(token);
            recorded = await _storage.ActivateAsync(
                checkout.OwnerUserId, offeringCode, checkout.StorageBlocks!.Value,
                payment.Amount, payment.Method, payment.PaymentId, checkout.Provider, checkout.BillingCycle, token);
        }

        checkout.CheckoutStatus = PaymentCheckoutStatus.Paid;
        checkout.PaidAtUtc = recorded.PaidAtUtc;
        checkout.ProviderPaymentId = payment.PaymentId;
        checkout.ProviderPaymentMethod = payment.MethodName;
        checkout.StorageAddOnPaymentId = recorded.Id;

        await _events.AddAsync(subscription, BillingEventTypes.CheckoutPaid,
            new
            {
                checkoutId = checkout.Id, kind = checkout.Kind, sessionId = session.SessionId, gatewayPaymentId = payment.PaymentId,
                payment.Amount, method = payment.MethodName, storageAddOnPaymentId = recorded.Id,
            },
            checkout.Provider, settledKey, checkout.OwnerUserId, token);
        return WebhookOutcome.Settled;
    }

    /// <summary>By the checkout id the session carries back, else by the session id.</summary>
    private async Task<PaymentCheckout?> FindCheckoutAsync(GatewaySessionState session, CancellationToken token)
    {
        if (session.Metadata.TryGetValue(MetadataKeys.CheckoutId, out var raw) && Guid.TryParse(raw, out var checkoutId))
        {
            var byId = await Context.PaymentCheckouts.FirstOrDefaultAsync(c => c.Id == checkoutId && c.Provider == _gateway.Provider, token);
            // A session id that disagrees means the metadata isn't ours to trust.
            if (byId != null && byId.ProviderSessionId == session.SessionId) return byId;
        }
        return await Context.PaymentCheckouts.FirstOrDefaultAsync(
            c => c.Provider == _gateway.Provider && c.ProviderSessionId == session.SessionId, token);
    }

    /// <summary>Records a payment that wasn't applied, once per session and kind.</summary>
    private async Task<WebhookOutcome> FlagAsync(
        AccountSubscription? subscription, string type, GatewaySessionState session, Guid? ownerUserId, object payload, CancellationToken token)
    {
        var key = $"{_gateway.Provider}:session:{session.SessionId}:{type}";
        if (await _events.ExistsAsync(key, token)) return WebhookOutcome.Duplicate;
        Log.Logger.Error("Payment session {SessionId}: {Type} -- needs an operator", session.SessionId, type);
        await _events.AddAsync(subscription, type, payload, _gateway.Provider, key, ownerUserId, token);
        return WebhookOutcome.Flagged;
    }

    private static Guid? OwnerFromMetadata(GatewaySessionState session) =>
        session.Metadata.TryGetValue(MetadataKeys.OwnerUserId, out var raw) && Guid.TryParse(raw, out var owner) ? owner : null;

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────

    private GatewayCheckoutRequest BuildRequest(PaymentCheckout checkout, PaymentDue due)
    {
        var cycle = due.BillingCycle == BillingCycle.Annual ? "annual" : "monthly";
        var returnUrl = ReturnUrl(checkout);
        return new GatewayCheckoutRequest
        {
            CheckoutId = checkout.Id,
            ItemName = $"{due.Plan.Name} plan ({cycle})",
            LineItems = GatewayLineItems(due),
            Description = due.Reason switch
            {
                PaymentDueReason.Renewal => $"{_options.ProductName} subscription renewal",
                PaymentDueReason.Reactivation => $"{_options.ProductName} subscription reactivation",
                _ => $"{_options.ProductName} subscription",
            },
            Amount = due.Amount,
            Currency = due.Plan.Currency,
            SuccessUrl = returnUrl,
            CancelUrl = $"{returnUrl}&canceled=1",
            Metadata = CheckoutMetadata(checkout),
        };
    }

    /// <summary>The itemized lines of a plan charge: the plan, and an upgrade's credit.</summary>
    public static List<PaymentLineItemSnapshot> BuildLineItems(PaymentDue due)
    {
        var cycle = due.BillingCycle == BillingCycle.Annual ? "annual" : "monthly";
        var items = new List<PaymentLineItemSnapshot>
        {
            new($"{due.Plan.Name} plan ({cycle})", due.PlanAmount),
        };
        if (due.CreditAmount > 0)
        {
            items.Add(new(
                AccountSubscriptionService.UpgradeCreditLineName,
                -due.CreditAmount,
                $"{due.UnusedDays} unused days of your current plan"));
        }
        return items;
    }

    /// <summary>
    /// The lines the gateway's page shows. Its lines can't be negative, so an upgrade's credit is
    /// taken off the plan line (and said so); a credit bigger than that line makes one line of the total.
    /// </summary>
    public static List<GatewayCheckoutLineItem> GatewayLineItems(PaymentDue due)
    {
        var items = BuildLineItems(due);
        if (due.CreditAmount <= 0)
            return items.Select(item => new GatewayCheckoutLineItem(item.Name, item.Amount, item.Description)).ToList();

        var credit = $"{due.CreditAmount:N2} {due.Plan.Currency} credit for {due.UnusedDays} unused days of your current plan";
        var plan = items[0];
        if (due.CreditAmount >= plan.Amount)
            return [new GatewayCheckoutLineItem(plan.Name, due.Amount, $"Less {credit}")];

        return items
            .Where(item => item.Amount > 0)
            .Select((item, index) => index == 0
                ? new GatewayCheckoutLineItem(item.Name, item.Amount - due.CreditAmount, $"Less {credit}")
                : new GatewayCheckoutLineItem(item.Name, item.Amount, item.Description))
            .ToList();
    }

    /// <summary>The upgrade credit a saved line-item snapshot carries (its negative lines).</summary>
    public static decimal CreditOf(IEnumerable<PaymentLineItemSnapshot> lineItems) =>
        -lineItems.Where(item => item.Amount < 0).Sum(item => item.Amount);

    private string ReturnUrl(PaymentCheckout checkout) =>
        $"{_options.ReturnBaseUrl.TrimEnd('/')}{ReturnPath}?checkout={checkout.Id}";

    private static Dictionary<string, string> CheckoutMetadata(PaymentCheckout checkout) => new()
    {
        [MetadataKeys.CheckoutId] = checkout.Id.ToString(),
        [MetadataKeys.SubscriptionId] = checkout.SubscriptionId.ToString(),
        [MetadataKeys.OwnerUserId] = checkout.OwnerUserId.ToString(),
        [MetadataKeys.CheckoutFormat] = MetadataKeys.CurrentCheckoutFormat,
    };

    /// <summary>Closes a superseded page. Best effort: if it was paid after all, it's still settled.</summary>
    private async Task ExpireAsync(PaymentCheckout checkout, CancellationToken token)
    {
        checkout.CheckoutStatus = PaymentCheckoutStatus.Expired;
        if (checkout.ProviderSessionId == null || checkout.Provider != _gateway.Provider || checkout.MandateId != null) return;
        try
        {
            await _gateway.ExpireCheckoutAsync(checkout.ProviderSessionId, token);
        }
        catch (PaymentGatewayException ex)
        {
            Log.Logger.Warning(ex, "Checkout {CheckoutId}: couldn't expire session {SessionId}", checkout.Id, checkout.ProviderSessionId);
        }
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    /// <summary>What a checkout session carries back to us.</summary>
    public static class MetadataKeys
    {
        public const string CheckoutId = "checkout_id";
        public const string SubscriptionId = "subscription_id";
        public const string OwnerUserId = "owner_user_id";
        public const string CheckoutFormat = "checkout_format";
        public const string CurrentCheckoutFormat = "itemized-plan-storage-credit-v2";
    }
}
