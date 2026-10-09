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
    PaymentMethod? PaymentMethod);

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

    private const string ReturnPath = "/billing/payment";

    private readonly IUnitOfWorkService _uow;
    private readonly AccountSubscriptionService _subscriptions;
    private readonly BillingEventService _events;
    private readonly IPaymentGateway _gateway;
    private readonly PaymentOptions _options;
    private readonly TimeProvider _clock;

    public SubscriptionCheckoutService(
        IUnitOfWorkService uow,
        AccountSubscriptionService subscriptions,
        BillingEventService events,
        IPaymentGateway gateway,
        IOptions<PaymentOptions> options,
        TimeProvider clock)
    {
        _uow = uow;
        _subscriptions = subscriptions;
        _events = events;
        _gateway = gateway;
        _options = options.Value;
        _clock = clock;
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
        var due = await _subscriptions.GetPaymentDueAsync(subscription, token)
            ?? throw new SubscriptionRuleException("There's nothing to pay right now.");
        if (due.Amount <= 0)
            throw new SubscriptionRuleException("There's nothing to pay online for this plan -- please contact billing to activate it.");

        var now = Now;
        var open = await Context.PaymentCheckouts
            .Where(c => c.SubscriptionId == subscription.Id && c.CheckoutStatus == PaymentCheckoutStatus.Open)
            .ToListAsync(token);

        var reusable = open.FirstOrDefault(c =>
            c.Provider == _gateway.Provider
            && c.PlanId == due.Plan.Id
            && c.BillingCycle == due.BillingCycle
            && c.Amount == due.Amount
            && c.CheckoutUrl != null
            && now - c.CreatedAtUtc < ReuseWindow);
        if (reusable != null) return new CheckoutStarted(reusable.Id, reusable.CheckoutUrl!);

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
            CreatedByUserId = actorUserId,
            CreatedAtUtc = now,
        };

        GatewayCheckoutSession session;
        try
        {
            session = await _gateway.CreateCheckoutAsync(BuildRequest(checkout, due), token);
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
            new
            {
                checkoutId = checkout.Id, plan = due.Plan.Code, cycle = due.BillingCycle, amount = due.Amount,
                reason = due.Reason, provider = _gateway.Provider, sessionId = session.SessionId,
            },
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

        var planName = await Context.Plans.AsNoTracking()
            .Where(p => p.Id == checkout.PlanId)
            .Select(p => p.Name)
            .FirstOrDefaultAsync(token) ?? string.Empty;
        var method = checkout.SubscriptionPaymentId is { } paymentId
            ? await Context.SubscriptionPayments.AsNoTracking()
                .Where(p => p.Id == paymentId)
                .Select(p => (PaymentMethod?)p.Method)
                .FirstOrDefaultAsync(token)
            : null;
        return new CheckoutView(
            checkout.Id, checkout.CheckoutStatus, planName, checkout.BillingCycle, checkout.Amount, checkout.Currency,
            checkout.CreatedAtUtc, checkout.PaidAtUtc, method);
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
        if (received.Kind != GatewayEventKind.CheckoutPaid || received.Session is not { IsPaid: true } session)
            return WebhookOutcome.Ignored;

        var outcome = await SettleAsync(session, token);
        await _uow.CommitChangesAsync("", token);
        return outcome;
    }

    // ── Settlement ───────────────────────────────────────────────────────────────────────────

    /// <summary>Applies a paid session to its checkout. Doesn't commit.</summary>
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

        var planCode = await Context.Plans.AsNoTracking()
            .Where(p => p.Id == checkout.PlanId)
            .Select(p => p.Code)
            .FirstAsync(token);
        var recorded = await _subscriptions.RecordPaymentAsync(checkout.OwnerUserId, new RecordPaymentCommand
        {
            Amount = payment.Amount,
            Method = payment.Method,
            Reference = payment.PaymentId,
            PaidAtUtc = payment.PaidAtUtc ?? Now,
            PlanCode = planCode,
            BillingCycle = checkout.BillingCycle,
            Notes = $"Paid online ({checkout.Provider} checkout {session.SessionId})",
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
        var returnUrl = $"{_options.ReturnBaseUrl.TrimEnd('/')}{ReturnPath}?checkout={checkout.Id}";
        return new GatewayCheckoutRequest
        {
            CheckoutId = checkout.Id,
            ItemName = $"{due.Plan.Name} plan ({cycle})",
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
            Metadata = new Dictionary<string, string>
            {
                [MetadataKeys.CheckoutId] = checkout.Id.ToString(),
                [MetadataKeys.SubscriptionId] = checkout.SubscriptionId.ToString(),
                [MetadataKeys.OwnerUserId] = checkout.OwnerUserId.ToString(),
            },
        };
    }

    /// <summary>Closes a superseded page. Best effort: if it was paid after all, it's still settled.</summary>
    private async Task ExpireAsync(PaymentCheckout checkout, CancellationToken token)
    {
        checkout.CheckoutStatus = PaymentCheckoutStatus.Expired;
        if (checkout.ProviderSessionId == null || checkout.Provider != _gateway.Provider) return;
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
    }
}
