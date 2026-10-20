using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Onepunch.Common.Lib.DTO;
using Serilog;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Core.Services.Payments;

/// <summary>Auto renew is being set up: pay <see cref="Amount"/> now (which saves the card or Maya account).</summary>
/// <param name="PublicKey">For the browser to hand card details straight to the gateway; null: card isn't offered.</param>
public sealed record AutoRenewStarted(Guid CheckoutId, decimal Amount, string Currency, BillingCycle BillingCycle, string? PublicKey);

/// <summary>Where the first payment stands; <see cref="RedirectUrl"/> is the page the payer must authorize it on.</summary>
public sealed record AutoRenewConfirmed(Guid CheckoutId, string? RedirectUrl, PaymentCheckoutStatus Status);

/// <summary>How the owner pays the first invoice: a card (tokenized in the browser) or their Maya account.</summary>
public sealed record ConfirmAutoRenewCommand
{
    /// <summary>"card" or "maya".</summary>
    public string Method { get; init; } = "card";
    /// <summary>Card: the gateway payment method the browser created (pm_...). Never card details.</summary>
    public string? PaymentMethodId { get; init; }
}

/// <summary>
/// Auto renew through the gateway's recurring billing (PayMongo Subscriptions) -- the only way it
/// charges a saved card or Maya account without the owner present. One flow for both targets, the
/// differences in <see cref="IAutoRenewTarget"/> (Strategy):
/// <list type="number">
/// <item>Turning it on is paying what's due now: a gateway customer, plan (priced at that charge, on the
/// target's cycle) and subscription are created; the owner pays its first invoice, which saves the card or
/// wallet, and is settled through the ordinary checkout paths. Our period and the gateway's billing start together.</item>
/// <item>A day before each charge the gateway raises a draft invoice: it is corrected to this account's
/// price (storage blocks bought or dropped, discounts, a scheduled downgrade) -- or, when nothing should
/// renew, the gateway subscription is cancelled.</item>
/// <item>Each paid invoice is recorded as an ordinary renewal; a failed one is left to the gateway's retries
/// and our own past-due and grace, and the owner is told.</item>
/// </list>
/// Our billing stays the source of truth: anything that would make the gateway's schedule wrong (a
/// cycle switch, a cancellation, a suspension) turns auto renew off rather than charging by a stale plan.
/// </summary>
public class AutoRenewService
{
    public const string Actor = "paymongo-autorenew";
    /// <summary>The gateway's smallest charge.</summary>
    private const decimal MinimumCharge = 1m;
    /// <summary>An unpaid first invoice is abandoned by the gateway after a day.</summary>
    private static readonly TimeSpan SetupLifetime = TimeSpan.FromHours(25);

    private readonly IUnitOfWorkService _uow;
    private readonly IRecurringBillingGateway _billing;
    private readonly IPaymentGateway _gateway;
    private readonly SubscriptionCheckoutService _checkouts;
    private readonly AccountSubscriptionService _subscriptions;
    private readonly BillingEventService _events;
    private readonly SubscriptionChangeNotifierService _notifier;
    private readonly IPublishEndpoint _publisher;
    private readonly PaymentOptions _options;
    private readonly TimeProvider _clock;
    private readonly Dictionary<AutoRenewTarget, IAutoRenewTarget> _targets;

    public AutoRenewService(
        IUnitOfWorkService uow,
        IRecurringBillingGateway billing,
        IPaymentGateway gateway,
        SubscriptionCheckoutService checkouts,
        AccountSubscriptionService subscriptions,
        StorageSubscriptionService storagePeriod,
        StorageAddOnService storage,
        BillingEventService events,
        SubscriptionChangeNotifierService notifier,
        IPublishEndpoint publisher,
        IOptions<PaymentOptions> options,
        TimeProvider clock)
    {
        _uow = uow;
        _billing = billing;
        _gateway = gateway;
        _checkouts = checkouts;
        _subscriptions = subscriptions;
        _events = events;
        _notifier = notifier;
        _publisher = publisher;
        _options = options.Value;
        _clock = clock;
        _targets = new IAutoRenewTarget[]
        {
            new PlanAutoRenewTarget(subscriptions),
            new StorageAutoRenewTarget(storagePeriod, storage),
        }.ToDictionary(t => t.Target);
    }

    private TenantContext Context => _uow.Context;
    private DateTime Now => _clock.GetUtcNow().UtcDateTime;
    public bool IsEnabled => _billing.IsEnabled;

    // ── Turning it on ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sets auto renew up for <see cref="StartAutoRenewCommand.Target"/>: prices what's due now, creates the
    /// gateway subscription, and opens a checkout for its first invoice. Any earlier auto renew of the
    /// same target is stopped first. Commits.
    /// </summary>
    public async Task<AutoRenewStarted> StartAsync(Guid ownerUserId, Guid actorUserId, StartAutoRenewCommand command, CancellationToken token = default)
    {
        if (!_billing.IsEnabled)
            throw new SubscriptionRuleException("Auto renew isn't available yet -- please pay by the other options.");
        var subscription = await _subscriptions.FindAsync(ownerUserId, token)
            ?? throw new KeyNotFoundException("This account has no subscription.");
        if (AccountSubscriptionService.IsSuspendedByOperator(subscription))
            throw new SubscriptionRuleException("This account was suspended by Onepunch support. Please contact support.");

        var first = await _targets[command.Target].FirstChargeAsync(subscription, command, token);
        var charge = first.Charge;
        if (charge.Amount < MinimumCharge)
            throw new SubscriptionRuleException("There's nothing to pay now, so auto renew can't start yet. Turn it on with your next payment.");

        foreach (var live in await LiveMandatesAsync(subscription.Id, command.Target, token))
            await StopAsync(subscription, live, "Replaced by a new auto renew", ownerUserId.ToString(), notify: false, token);

        var owner = await OwnerContactAsync(ownerUserId, token);
        var customerId = await Context.AutoRenewMandates.AsNoTracking()
            .Where(m => m.OwnerUserId == ownerUserId && m.Provider == _billing.Provider && m.ProviderCustomerId != "")
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => m.ProviderCustomerId)
            .FirstOrDefaultAsync(token)
            ?? await _billing.CreateCustomerAsync(owner, token);

        var cycleLabel = StorageSubscriptionService.CycleLabel(charge.Cycle);
        var planId = await _billing.CreatePlanAsync(
            $"{_options.ProductName} {charge.Name} ({cycleLabel})",
            $"{charge.Name}, renewed automatically every {(charge.Cycle == BillingCycle.Annual ? "year" : "month")}",
            charge.Amount, charge.Currency, charge.Cycle,
            new Dictionary<string, string> { ["owner_user_id"] = ownerUserId.ToString(), ["target"] = command.Target.ToString() },
            token);
        var gatewaySubscription = await _billing.CreateSubscriptionAsync(customerId, planId, token);
        if (gatewaySubscription.PaymentIntentId is not { } intentId)
            throw new PaymentGatewayException("The gateway didn't raise a first invoice to pay.");

        var now = Now;
        var mandate = new AutoRenewMandate
        {
            Id = Guid.CreateVersion7(),
            SubscriptionId = subscription.Id,
            OwnerUserId = ownerUserId,
            Target = command.Target,
            Status = AutoRenewMandateStatus.Pending,
            Provider = _billing.Provider,
            ProviderCustomerId = customerId,
            ProviderSubscriptionId = gatewaySubscription.SubscriptionId,
            ProviderPlanId = planId,
            BillingCycle = charge.Cycle,
            CreatedBy = actorUserId.ToString(),
        };
        var checkout = first.Checkout;
        checkout.Id = Guid.CreateVersion7();
        checkout.SubscriptionId = subscription.Id;
        checkout.OwnerUserId = ownerUserId;
        checkout.Provider = _billing.Provider;
        checkout.ProviderSessionId = gatewaySubscription.SubscriptionId;
        checkout.ProviderIntentId = intentId;
        checkout.MandateId = mandate.Id;
        checkout.CheckoutStatus = PaymentCheckoutStatus.Open;
        checkout.CreatedByUserId = actorUserId;
        checkout.CreatedAtUtc = now;
        if (checkout.Kind == PaymentCheckoutKind.StorageAddOn && command.OfferingCode is { } code)
            checkout.StorageOfferingId = await Context.StorageAddOnOfferings.Where(o => o.Code == code).Select(o => o.Id).FirstAsync(token);
        mandate.SetupCheckoutId = checkout.Id;

        await Context.AutoRenewMandates.AddAsync(mandate, token);
        await Context.PaymentCheckouts.AddAsync(checkout, token);
        await _events.AddAsync(subscription, BillingEventTypes.AutoRenewSetupStarted,
            new { mandateId = mandate.Id, target = command.Target.ToString(), charge.Amount, cycle = charge.Cycle.ToString(), checkoutId = checkout.Id },
            actorUserId.ToString(), token: token);
        await _uow.CommitChangesAsync("", token);
        return new AutoRenewStarted(checkout.Id, charge.Amount, charge.Currency, charge.Cycle, _billing.PublicKey);
    }

    /// <summary>
    /// Pays the first invoice with the method the owner chose. Usually the payer must then authorize it
    /// (3-D Secure, the Maya app): <see cref="AutoRenewConfirmed.RedirectUrl"/> is that page, and they come
    /// back to the payment return screen. Commits when it's paid at once.
    /// </summary>
    public async Task<AutoRenewConfirmed> ConfirmAsync(Guid ownerUserId, Guid checkoutId, ConfirmAutoRenewCommand command, CancellationToken token = default)
    {
        var checkout = await Context.PaymentCheckouts
            .FirstOrDefaultAsync(c => c.Id == checkoutId && c.OwnerUserId == ownerUserId && c.MandateId != null, token)
            ?? throw new KeyNotFoundException("Payment not found.");
        if (checkout.CheckoutStatus != PaymentCheckoutStatus.Open || checkout.ProviderIntentId is not { } intentId)
            throw new SubscriptionRuleException("This payment is no longer open. Start again from Billing.");

        string paymentMethodId;
        if (string.Equals(command.Method, "maya", StringComparison.OrdinalIgnoreCase))
        {
            var owner = await OwnerContactAsync(ownerUserId, token);
            paymentMethodId = await _billing.CreateWalletPaymentMethodAsync("paymaya", $"{owner.FirstName} {owner.LastName}".Trim(), owner.Email, token);
        }
        else
        {
            // Only the gateway's token is accepted -- card details never come here.
            if (command.PaymentMethodId is not { } pm || !pm.StartsWith("pm_", StringComparison.Ordinal))
                throw new SubscriptionRuleException("Enter your card details again.");
            paymentMethodId = pm;
        }

        var attached = await _billing.AttachPaymentMethodAsync(intentId, paymentMethodId, ReturnUrl(checkout), token);
        if (attached.RedirectUrl == null) await RefreshSetupAsync(checkout, token);
        return new AutoRenewConfirmed(checkout.Id, attached.RedirectUrl, checkout.CheckoutStatus);
    }

    /// <summary>
    /// The return screen asking after a payment: an auto renew's first invoice is checked with the gateway
    /// (the webhook may be late), then the checkout is shown as usual.
    /// </summary>
    public async Task<CheckoutView> RefreshAsync(Guid ownerUserId, Guid checkoutId, CancellationToken token = default)
    {
        var checkout = await Context.PaymentCheckouts
            .FirstOrDefaultAsync(c => c.Id == checkoutId && c.OwnerUserId == ownerUserId, token);
        if (checkout is { MandateId: not null, CheckoutStatus: PaymentCheckoutStatus.Open } && _billing.IsEnabled)
            await RefreshSetupAsync(checkout, token);
        return await _checkouts.RefreshAsync(ownerUserId, checkoutId, token);
    }

    private async Task RefreshSetupAsync(PaymentCheckout checkout, CancellationToken token)
    {
        try
        {
            var intent = await _billing.GetPaymentIntentAsync(checkout.ProviderIntentId!, token);
            if (intent.Payment is not { } payment) return;
            await SettleSetupAsync(checkout, payment, intent.MethodLabel, token);
            await _uow.CommitChangesAsync("", token);
        }
        catch (PaymentGatewayException ex)
        {
            Log.Logger.Warning(ex, "Auto renew checkout {CheckoutId}: couldn't ask the gateway for its status", checkout.Id);
        }
        catch (DbUpdateException ex)
        {
            Log.Logger.Information(ex, "Auto renew checkout {CheckoutId}: settled concurrently", checkout.Id);
            await Context.Entry(checkout).ReloadAsync(token);
        }
    }

    /// <summary>The first invoice was paid: settle it as its checkout and switch auto renew on.</summary>
    private async Task SettleSetupAsync(PaymentCheckout checkout, GatewayPayment payment, string? label, CancellationToken token)
    {
        var mandate = await Context.AutoRenewMandates.FirstAsync(m => m.Id == checkout.MandateId, token);
        var session = new GatewaySessionState(
            checkout.ProviderSessionId!, payment,
            new Dictionary<string, string> { [SubscriptionCheckoutService.MetadataKeys.CheckoutId] = checkout.Id.ToString() },
            _gateway.LiveMode);
        var outcome = await _checkouts.SettlePaidAsync(session, token);
        var subscription = await _subscriptions.FindAsync(mandate.OwnerUserId, token);
        if (outcome == WebhookOutcome.Flagged)
        {
            // Paid, but not what was quoted: an operator sorts out the money; nothing renews by itself.
            await StopAsync(subscription!, mandate, "The first payment didn't match the amount due", "system", notify: false, token);
            return;
        }
        if (mandate.Status != AutoRenewMandateStatus.Pending) return;

        mandate.Status = AutoRenewMandateStatus.Active;
        mandate.ActivatedAtUtc = Now;
        mandate.PaymentMethodLabel = label ?? payment.MethodName;
        await RefreshNextChargeAsync(mandate, token);
        await _events.AddAsync(subscription, BillingEventTypes.AutoRenewActivated,
            new { mandateId = mandate.Id, target = mandate.Target.ToString(), mandate.PaymentMethodLabel, checkoutId = checkout.Id },
            Actor, $"{mandate.Provider}:autorenew:{mandate.Id:N}:activated", token: token);
        await _notifier.PublishAsync(mandate.OwnerUserId, BillingEventTypes.AutoRenewActivated, token);
        await EmailAsync(mandate, SubscriptionEmailTemplates.AutoRenewOn, null, token);
    }

    /// <summary>Every auto renew of the account, newest first (the console's history).</summary>
    public async Task<List<AutoRenewView>> HistoryAsync(Guid ownerUserId, CancellationToken token = default)
    {
        var subscription = await _subscriptions.FindAsync(ownerUserId, token)
            ?? throw new KeyNotFoundException("This account has no subscription.");
        return await AutoRenewReadModel.AllAsync(Context, subscription.Id, token);
    }

    // ── Turning it off ───────────────────────────────────────────────────────────────────────

    /// <summary>The owner (or an operator) turns auto renew off: the gateway stops billing at once. Commits.</summary>
    public async Task TurnOffAsync(Guid ownerUserId, AutoRenewTarget target, string? actor, string reason, CancellationToken token = default)
    {
        var subscription = await _subscriptions.FindAsync(ownerUserId, token)
            ?? throw new KeyNotFoundException("This account has no subscription.");
        var live = await LiveMandatesAsync(subscription.Id, target, token);
        if (live.Count == 0) throw new SubscriptionRuleException("Auto renew isn't on.");
        foreach (var mandate in live) await StopAsync(subscription, mandate, reason, actor, notify: false, token);
        await _uow.CommitChangesAsync("", token);
    }

    /// <summary>Stops one auto renew: cancels the gateway subscription (best effort -- an unpaid first
    /// invoice lapses by itself) and closes its open first-payment checkout. Doesn't commit.</summary>
    private async Task StopAsync(AccountSubscription subscription, AutoRenewMandate mandate, string reason, string? actor, bool notify, CancellationToken token)
    {
        if (!mandate.IsLive) return;
        try
        {
            await _billing.CancelSubscriptionAsync(mandate.ProviderSubscriptionId, token);
        }
        catch (PaymentGatewayException ex)
        {
            Log.Logger.Warning(ex, "Auto renew {MandateId}: couldn't cancel gateway subscription {SubscriptionId}", mandate.Id, mandate.ProviderSubscriptionId);
        }
        var wasCharging = mandate.Status is AutoRenewMandateStatus.Active or AutoRenewMandateStatus.PastDue;
        mandate.Status = AutoRenewMandateStatus.Canceled;
        mandate.CanceledAtUtc = Now;
        mandate.CancelReason = reason.Length <= 500 ? reason : reason[..500];
        mandate.CanceledBy = actor;

        if (mandate.SetupCheckoutId is { } checkoutId)
        {
            var open = await Context.PaymentCheckouts.FirstOrDefaultAsync(c => c.Id == checkoutId && c.CheckoutStatus == PaymentCheckoutStatus.Open, token);
            if (open != null) open.CheckoutStatus = PaymentCheckoutStatus.Expired;
        }

        await _events.AddAsync(subscription, BillingEventTypes.AutoRenewCanceled,
            new { mandateId = mandate.Id, target = mandate.Target.ToString(), reason }, actor ?? "system", token: token);
        await _notifier.PublishAsync(mandate.OwnerUserId, BillingEventTypes.AutoRenewCanceled, token);
        if (notify && wasCharging) await EmailAsync(mandate, SubscriptionEmailTemplates.AutoRenewStopped, null, token);
    }

    // ── Webhooks ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A gateway webhook: recurring-billing events are handled here, everything else is passed on to the
    /// hosted checkout (Chain of Responsibility). Commits.
    /// </summary>
    public async Task<WebhookOutcome> HandleWebhookAsync(string rawBody, string? signatureHeader, CancellationToken token = default)
    {
        var received = _gateway.ReadWebhook(rawBody, signatureHeader);
        if (received == null) return WebhookOutcome.Rejected;
        if (received.Kind is not (GatewayEventKind.SubscriptionInvoiceCreated or GatewayEventKind.SubscriptionInvoicePaid
            or GatewayEventKind.SubscriptionInvoiceFailed or GatewayEventKind.SubscriptionChanged))
        {
            return await _checkouts.HandleWebhookAsync(rawBody, signatureHeader, token);
        }
        if (received.LiveMode != _gateway.LiveMode)
        {
            Log.Logger.Warning("Payment webhook {EventId} ({Type}) is from the other mode -- ignored", received.EventId, received.Type);
            return WebhookOutcome.Ignored;
        }

        var outcome = received.Kind switch
        {
            GatewayEventKind.SubscriptionInvoiceCreated when received.Invoice is { } invoice => await HandleInvoiceCreatedAsync(invoice, token),
            GatewayEventKind.SubscriptionInvoicePaid when received.Invoice is { } invoice => await HandleInvoicePaidAsync(invoice, token),
            GatewayEventKind.SubscriptionInvoiceFailed when received.Invoice is { } invoice => await HandleInvoiceFailedAsync(invoice, token),
            GatewayEventKind.SubscriptionChanged when received.Subscription is { } notice => await HandleSubscriptionChangedAsync(notice, token),
            _ => WebhookOutcome.Ignored,
        };
        await _uow.CommitChangesAsync("", token);
        return outcome;
    }

    private Task<AutoRenewMandate?> MandateOfAsync(string? gatewaySubscriptionId, CancellationToken token) =>
        gatewaySubscriptionId == null
            ? Task.FromResult<AutoRenewMandate?>(null)
            : Context.AutoRenewMandates.FirstOrDefaultAsync(
                m => m.Provider == _billing.Provider && m.ProviderSubscriptionId == gatewaySubscriptionId, token);

    /// <summary>A draft invoice, a day before the charge: correct it to our price, or stop when nothing should renew.</summary>
    public async Task<WebhookOutcome> HandleInvoiceCreatedAsync(GatewayInvoice invoice, CancellationToken token)
    {
        var mandate = await MandateOfAsync(invoice.SubscriptionId, token);
        if (mandate is not { Status: AutoRenewMandateStatus.Active or AutoRenewMandateStatus.PastDue } || invoice.Status != "draft")
            return WebhookOutcome.Ignored;
        var key = $"{mandate.Provider}:invoice:{invoice.InvoiceId}:reviewed";
        if (await _events.ExistsAsync(key, token)) return WebhookOutcome.Duplicate;

        var subscription = await _subscriptions.FindAsync(mandate.OwnerUserId, token);
        if (subscription == null) return WebhookOutcome.Ignored;
        var next = await _targets[mandate.Target].NextChargeAsync(subscription, token);
        string? stopReason = next == null ? "Nothing is set to renew"
            : next.Cycle != mandate.BillingCycle ? "The billing cycle changed"
            : next.Amount < MinimumCharge ? "Nothing to charge"
            : null;
        if (stopReason != null)
        {
            await StopAsync(subscription, mandate, stopReason, "system", notify: true, token);
            await _events.AddAsync(subscription, BillingEventTypes.AutoRenewInvoiceAdjusted,
                new { mandateId = mandate.Id, invoice.InvoiceId, stopped = stopReason }, Actor, key, token: token);
            return WebhookOutcome.Settled;
        }

        var difference = next!.Amount - invoice.Amount;
        if (difference != 0m)
        {
            await _billing.AddInvoiceLineItemAsync(invoice.InvoiceId, difference,
                difference > 0 ? "Adjusted to your current price" : "Adjusted to your current price (less)", token);
        }
        await _events.AddAsync(subscription, BillingEventTypes.AutoRenewInvoiceAdjusted,
            new { mandateId = mandate.Id, invoice.InvoiceId, invoiced = invoice.Amount, expected = next.Amount, difference }, Actor, key, token: token);
        return WebhookOutcome.Settled;
    }

    /// <summary>An invoice was paid: the first one turns auto renew on; later ones are recorded as renewals.</summary>
    public async Task<WebhookOutcome> HandleInvoicePaidAsync(GatewayInvoice invoice, CancellationToken token)
    {
        var mandate = await MandateOfAsync(invoice.SubscriptionId, token);
        if (mandate == null)
        {
            Log.Logger.Warning("Paid invoice {InvoiceId} names gateway subscription {SubscriptionId} we don't know -- ignored",
                invoice.InvoiceId, invoice.SubscriptionId);
            return WebhookOutcome.Ignored;
        }
        var key = $"{mandate.Provider}:invoice:{invoice.InvoiceId}:paid";
        if (await _events.ExistsAsync(key, token)) return WebhookOutcome.Duplicate;

        var intent = invoice.PaymentIntentId is { } intentId ? await _billing.GetPaymentIntentAsync(intentId, token) : null;
        var payment = intent?.Payment
            ?? new GatewayPayment(invoice.PaymentIntentId ?? invoice.InvoiceId, invoice.Amount, invoice.Currency, PaymentMethod.Card, "card", Now);
        var subscription = await _subscriptions.FindAsync(mandate.OwnerUserId, token);

        // The first invoice settles the checkout that set auto renew up.
        var setup = mandate.SetupCheckoutId is { } setupId
            ? await Context.PaymentCheckouts.FirstOrDefaultAsync(c => c.Id == setupId, token)
            : null;
        if (setup is { CheckoutStatus: PaymentCheckoutStatus.Open or PaymentCheckoutStatus.Expired }
            && mandate.Status == AutoRenewMandateStatus.Pending)
        {
            await SettleSetupAsync(setup, payment, intent?.MethodLabel, token);
            await _events.AddAsync(subscription, BillingEventTypes.AutoRenewCharged,
                new { mandateId = mandate.Id, invoice.InvoiceId, invoice.Amount, first = true }, Actor, key, token: token);
            return WebhookOutcome.Settled;
        }
        if (subscription == null) return WebhookOutcome.Ignored;

        var target = _targets[mandate.Target];
        var expected = await target.NextChargeAsync(subscription, token);
        try
        {
            await target.SettleRenewalAsync(subscription, payment.Amount, payment, invoice.InvoiceId, Actor, token);
        }
        catch (SubscriptionRuleException ex)
        {
            // Money was taken but there's nothing it can renew: an operator decides (refund, or record by hand).
            await _events.AddAsync(subscription, BillingEventTypes.AutoRenewAmountMismatch,
                new { mandateId = mandate.Id, invoice.InvoiceId, paid = payment.Amount, error = ex.Message }, Actor, key, token: token);
            return WebhookOutcome.Flagged;
        }
        if (expected != null && expected.Amount != payment.Amount)
        {
            await _events.AddAsync(subscription, BillingEventTypes.AutoRenewAmountMismatch,
                new { mandateId = mandate.Id, invoice.InvoiceId, paid = payment.Amount, expected = expected.Amount }, Actor, token: token);
        }

        mandate.Status = mandate.Status == AutoRenewMandateStatus.Canceled ? mandate.Status : AutoRenewMandateStatus.Active;
        mandate.PaymentMethodLabel = intent?.MethodLabel ?? mandate.PaymentMethodLabel;
        mandate.LastFailureAtUtc = null;
        mandate.LastFailureReason = null;
        await RefreshNextChargeAsync(mandate, token);
        await _events.AddAsync(subscription, BillingEventTypes.AutoRenewCharged,
            new { mandateId = mandate.Id, target = mandate.Target.ToString(), invoice.InvoiceId, payment.Amount, payment.PaymentId }, Actor, key, token: token);
        return WebhookOutcome.Settled;
    }

    /// <summary>A charge failed: the gateway retries; our own past-due and grace run by date. The owner is told once per invoice.</summary>
    public async Task<WebhookOutcome> HandleInvoiceFailedAsync(GatewayInvoice invoice, CancellationToken token)
    {
        var mandate = await MandateOfAsync(invoice.SubscriptionId, token);
        if (mandate is not { Status: AutoRenewMandateStatus.Active or AutoRenewMandateStatus.PastDue }) return WebhookOutcome.Ignored;
        var key = $"{mandate.Provider}:invoice:{invoice.InvoiceId}:failed";
        if (await _events.ExistsAsync(key, token)) return WebhookOutcome.Duplicate;

        mandate.Status = AutoRenewMandateStatus.PastDue;
        mandate.LastFailureAtUtc = Now;
        mandate.LastFailureReason = "The automatic payment didn't go through";
        var subscription = await _subscriptions.FindAsync(mandate.OwnerUserId, token);
        await _events.AddAsync(subscription, BillingEventTypes.AutoRenewChargeFailed,
            new { mandateId = mandate.Id, target = mandate.Target.ToString(), invoice.InvoiceId, invoice.Amount }, Actor, key, token: token);
        await _notifier.PublishAsync(mandate.OwnerUserId, BillingEventTypes.AutoRenewChargeFailed, token);
        await EmailAsync(mandate, SubscriptionEmailTemplates.AutoRenewChargeFailed, invoice.Amount, token);
        return WebhookOutcome.Settled;
    }

    /// <summary>The gateway subscription changed state: retries ran out (unpaid), it was cancelled there, or the
    /// first invoice was never paid.</summary>
    public async Task<WebhookOutcome> HandleSubscriptionChangedAsync(GatewaySubscriptionNotice notice, CancellationToken token)
    {
        var mandate = await MandateOfAsync(notice.SubscriptionId, token);
        if (mandate == null || !mandate.IsLive) return WebhookOutcome.Ignored;
        var subscription = await _subscriptions.FindAsync(mandate.OwnerUserId, token);
        if (subscription == null) return WebhookOutcome.Ignored;

        switch (notice.Status)
        {
            case "unpaid":
                await StopAsync(subscription, mandate, "The automatic payment failed after several tries", "system", notify: true, token);
                return WebhookOutcome.Settled;
            case "cancelled":
            case "incomplete_cancelled":
                await StopAsync(subscription, mandate,
                    notice.Status == "incomplete_cancelled" ? "The first payment wasn't completed" : "Cancelled at the payment gateway",
                    "system", notify: notice.Status == "cancelled", token);
                return WebhookOutcome.Settled;
            case "past_due" when mandate.Status == AutoRenewMandateStatus.Active:
                mandate.Status = AutoRenewMandateStatus.PastDue;
                return WebhookOutcome.Settled;
            default:
                if (notice.NextBillingDateUtc is { } next && mandate.Status != AutoRenewMandateStatus.Pending) mandate.NextChargeAtUtc = next;
                return WebhookOutcome.Ignored;
        }
    }

    // ── Hourly reconciliation (SubscriptionLifecycleService) ───────────────────────────────────

    /// <summary>
    /// Keeps one account's auto renews honest, in case a webhook never arrived or the account changed:
    /// an abandoned first payment is closed; an auto renew whose target no longer renews (cancelled,
    /// suspended, a cycle switch, storage given up) is stopped; a charge the gateway made but we didn't
    /// hear about is recorded. Doesn't commit -- the lifecycle run does.
    /// </summary>
    public async Task ReconcileAsync(AccountSubscription subscription, CancellationToken token = default)
    {
        if (!_billing.IsEnabled) return;
        var live = await Context.AutoRenewMandates
            .Where(m => m.SubscriptionId == subscription.Id && m.Status != AutoRenewMandateStatus.Canceled && m.Provider == _billing.Provider)
            .ToListAsync(token);
        var now = Now;
        foreach (var mandate in live)
        {
            try
            {
                if (mandate.Status == AutoRenewMandateStatus.Pending)
                {
                    var setup = mandate.SetupCheckoutId is { } setupId
                        ? await Context.PaymentCheckouts.FirstOrDefaultAsync(c => c.Id == setupId, token)
                        : null;
                    if (setup != null && now - setup.CreatedAtUtc < SetupLifetime) continue;
                    if (setup?.ProviderIntentId is { } intentId
                        && (await _billing.GetPaymentIntentAsync(intentId, token)).Payment is { } paid)
                    {
                        await SettleSetupAsync(setup, paid, null, token);
                        continue;
                    }
                    await StopAsync(subscription, mandate, "The first payment wasn't completed", "system", notify: false, token);
                    continue;
                }

                var next = await _targets[mandate.Target].NextChargeAsync(subscription, token);
                if (next == null || next.Cycle != mandate.BillingCycle)
                {
                    await StopAsync(subscription, mandate, next == null ? "Nothing is set to renew" : "The billing cycle changed",
                        "system", notify: true, token);
                    continue;
                }

                // A charge date has passed: make sure its payment was recorded.
                if (mandate.NextChargeAtUtc is { } due && due <= now)
                {
                    var state = await _billing.GetSubscriptionAsync(mandate.ProviderSubscriptionId, token);
                    if (state.LatestInvoiceStatus == "paid" && state.LatestInvoiceId is { } invoiceId)
                    {
                        await HandleInvoicePaidAsync(new GatewayInvoice(invoiceId, mandate.ProviderSubscriptionId, 0m, "PHP", "paid",
                            null, state.PaymentIntentId), token);
                    }
                    if (state.NextBillingDateUtc is { } nextBilling) mandate.NextChargeAtUtc = nextBilling;
                }
            }
            catch (PaymentGatewayException ex)
            {
                Log.Logger.Warning(ex, "Auto renew {MandateId}: couldn't reconcile with the gateway", mandate.Id);
            }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────

    private Task<List<AutoRenewMandate>> LiveMandatesAsync(Guid subscriptionId, AutoRenewTarget target, CancellationToken token) =>
        Context.AutoRenewMandates
            .Where(m => m.SubscriptionId == subscriptionId && m.Target == target && m.Status != AutoRenewMandateStatus.Canceled)
            .ToListAsync(token);

    private async Task RefreshNextChargeAsync(AutoRenewMandate mandate, CancellationToken token)
    {
        try
        {
            var state = await _billing.GetSubscriptionAsync(mandate.ProviderSubscriptionId, token);
            if (state.NextBillingDateUtc is { } next) mandate.NextChargeAtUtc = next;
        }
        catch (PaymentGatewayException ex)
        {
            Log.Logger.Warning(ex, "Auto renew {MandateId}: couldn't read the next charge date", mandate.Id);
        }
    }

    /// <summary>The owner as the gateway's customer record needs them.</summary>
    private async Task<GatewayCustomerRequest> OwnerContactAsync(Guid ownerUserId, CancellationToken token)
    {
        var owner = await Context.Memberships.AsNoTracking()
            .Where(m => m.UserId == ownerUserId && m.IsHidden && m.InvitedEmail != null)
            .Select(m => new { m.InvitedEmail, m.FullName })
            .FirstOrDefaultAsync(token)
            ?? throw new SubscriptionRuleException("There's no owner email on file to bill.");
        var names = (owner.FullName ?? string.Empty).Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return new GatewayCustomerRequest(
            names.Length > 0 ? names[0] : "Account",
            names.Length > 1 ? names[1] : "Owner",
            owner.InvitedEmail!);
    }

    private async Task EmailAsync(AutoRenewMandate mandate, string template, decimal? amount, CancellationToken token)
    {
        var recipient = await Context.Memberships.AsNoTracking()
            .Where(m => m.UserId == mandate.OwnerUserId && m.IsHidden && m.InvitedEmail != null)
            .Select(m => new { m.InvitedEmail, m.FullName })
            .FirstOrDefaultAsync(token);
        if (recipient?.InvitedEmail == null) return;
        await _publisher.Publish(new SubscriptionReminderEmail
        {
            Email = recipient.InvitedEmail,
            FullName = recipient.FullName,
            Template = template,
            AutoRenewItem = mandate.Target == AutoRenewTarget.Plan ? "plan" : "extra storage",
            PaymentMethodLabel = mandate.PaymentMethodLabel,
            Amount = amount,
        }, token);
    }

    private string ReturnUrl(PaymentCheckout checkout) =>
        $"{_options.ReturnBaseUrl.TrimEnd('/')}/billing/payment?checkout={checkout.Id}";
}
