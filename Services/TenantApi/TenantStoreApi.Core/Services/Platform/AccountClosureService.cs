using System.Security.Cryptography;
using System.Text;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Core.Services.Platform;

/// <summary>What the owner sees of their closure request.</summary>
public sealed record AccountClosureView(
    Guid Id,
    AccountClosureStatus Status,
    DateTime RequestedAtUtc,
    DateTime? ConfirmExpiresAtUtc,
    DateTime? CoolingOffEndsAtUtc,
    string? DecisionReason,
    DateTime? PurgeAfterUtc);

/// <summary>A closure request as the console shows it.</summary>
public sealed record AccountClosureReviewView(
    Guid Id,
    Guid OwnerUserId,
    string OwnerEmail,
    IReadOnlyList<string> CompanyNames,
    string? Reason,
    AccountClosureStatus Status,
    DateTime RequestedAtUtc,
    DateTime? CoolingOffEndsAtUtc,
    DateTime? PaidThroughUtc,
    bool DataPurgeEnabled);

/// <summary>
/// An account owner closing their own account from the app. It never deletes anything itself:
/// <list type="number">
/// <item>The owner asks (typing their email). A confirmation link goes to that email, so a hijacked session alone can't do it.</item>
/// <item>Confirming starts a cooling-off period; the owner can cancel in the app or from the email until an operator approves.</item>
/// <item>When it ends, an operator reviews it. Approval locks the account (read-only) and sets the purge date to the
/// minimum notice from now, so <see cref="DataPurgeService"/> archives and purges it the usual way and the
/// compliance archive is kept for its legal period. Paying again before that date undoes it.</item>
/// </list>
/// Doesn't commit.
/// </summary>
public class AccountClosureService
{
    private readonly IUnitOfWorkService _uow;
    private readonly PlatformDirectoryService _directory;
    private readonly BillingEventService _events;
    private readonly SubscriptionChangeNotifierService _notifier;
    private readonly IPublishEndpoint _publisher;
    private readonly SubscriptionOptions _options;
    private readonly TimeProvider _clock;

    public AccountClosureService(
        IUnitOfWorkService uow,
        PlatformDirectoryService directory,
        BillingEventService events,
        SubscriptionChangeNotifierService notifier,
        IPublishEndpoint publisher,
        IOptions<SubscriptionOptions> options,
        TimeProvider clock)
    {
        _uow = uow;
        _directory = directory;
        _events = events;
        _notifier = notifier;
        _publisher = publisher;
        _options = options.Value;
        _clock = clock;
    }

    private TenantContext Context => _uow.Context;
    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public Task<bool> CommitChangesAsync(CancellationToken token = default) => _uow.CommitChangesAsync("", token);

    // ── The owner ────────────────────────────────────────────────────────────

    /// <summary>The owner's latest request (any outcome), or null when they never made one.</summary>
    public async Task<AccountClosureView?> GetForOwnerAsync(Guid ownerUserId, CancellationToken token)
    {
        var request = await Context.AccountClosureRequests.AsNoTracking()
            .Where(r => r.OwnerUserId == ownerUserId)
            .OrderByDescending(r => r.RequestedAtUtc)
            .FirstOrDefaultAsync(token);
        if (request is null) return null;
        var purgeAfter = request.RequestStatus == AccountClosureStatus.Approved
            ? (await Context.AccountSubscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.OwnerUserId == ownerUserId, token))?.PurgeAfterUtc
            : null;
        return ToOwnerView(request, purgeAfter);
    }

    public async Task<AccountClosureView> RequestAsync(Guid ownerUserId, string? reason, string? confirmEmail, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(_options.AppBaseUrl))
            throw new SubscriptionRuleException("Closing an account isn't available yet. Please contact support.");
        reason = TidyReason(reason);
        var subscription = await RequireSubscriptionAsync(ownerUserId, token);
        var ownerEmail = await RequireConfirmedEmailAsync(ownerUserId, confirmEmail, token);
        CheckCanClose(subscription);

        var now = Now;
        var open = await Context.AccountClosureRequests.AnyAsync(r => r.OwnerUserId == ownerUserId
            && (r.RequestStatus == AccountClosureStatus.CoolingOff || r.RequestStatus == AccountClosureStatus.ReadyForReview
                || (r.RequestStatus == AccountClosureStatus.PendingConfirmation && r.ConfirmExpiresAtUtc > now)), token);
        if (open) throw new SubscriptionRuleException("You already asked to close this account. Cancel that request first if you want to start over.");

        var confirmToken = NewToken();
        var cancelToken = NewToken();
        var request = new AccountClosureRequest
        {
            Id = Guid.CreateVersion7(),
            OwnerUserId = ownerUserId,
            OwnerEmail = ownerEmail,
            Reason = reason,
            RequestStatus = AccountClosureStatus.PendingConfirmation,
            RequestedAtUtc = now,
            ConfirmTokenHash = Hash(confirmToken),
            CancelTokenHash = Hash(cancelToken),
            ConfirmExpiresAtUtc = now.AddHours(_options.ClosureConfirmHours),
        };
        await Context.AccountClosureRequests.AddAsync(request, token);
        await _events.AddAsync(subscription, BillingEventTypes.ClosureRequested, new { requestId = request.Id, reason }, $"owner:{ownerEmail}", token: token);
        if (AccountClosureEmailComposer.Confirm(ownerEmail, Link("confirm", confirmToken), Link("cancel", cancelToken), request.ConfirmExpiresAtUtc) is { } email)
            await _publisher.Publish(email, token);
        return ToOwnerView(request, null);
    }

    /// <summary>Follows the confirmation link in the email. Returns false when the link is unknown, used or expired.</summary>
    public async Task<bool> ConfirmAsync(string? confirmToken, CancellationToken token)
    {
        var hash = HashOrNull(confirmToken);
        if (hash is null) return false;
        var now = Now;
        var request = await Context.AccountClosureRequests
            .FirstOrDefaultAsync(r => r.ConfirmTokenHash == hash && r.RequestStatus == AccountClosureStatus.PendingConfirmation, token);
        if (request is null || request.ConfirmExpiresAtUtc <= now) return false;

        request.RequestStatus = AccountClosureStatus.CoolingOff;
        request.ConfirmedAtUtc = now;
        request.CoolingOffEndsAtUtc = now.AddDays(_options.ClosureCoolingOffDays);
        var subscription = await Context.AccountSubscriptions.FirstOrDefaultAsync(s => s.OwnerUserId == request.OwnerUserId, token);
        await _events.AddAsync(subscription, BillingEventTypes.ClosureConfirmed, new { requestId = request.Id, request.CoolingOffEndsAtUtc },
            $"owner:{request.OwnerEmail}", ownerUserId: request.OwnerUserId, token: token);
        // The cancel link went out with the first email (its token can't be read back), so this note points to the app.
        if (AccountClosureEmailComposer.Scheduled(request.OwnerEmail, request.CoolingOffEndsAtUtc.Value) is { } email)
            await _publisher.Publish(email, token);
        return true;
    }

    /// <summary>The owner changes their mind from the app.</summary>
    public async Task CancelAsync(Guid ownerUserId, CancellationToken token)
    {
        var request = await Context.AccountClosureRequests
            .Where(r => r.OwnerUserId == ownerUserId
                && (r.RequestStatus == AccountClosureStatus.PendingConfirmation || r.RequestStatus == AccountClosureStatus.CoolingOff
                    || r.RequestStatus == AccountClosureStatus.ReadyForReview))
            .OrderByDescending(r => r.RequestedAtUtc)
            .FirstOrDefaultAsync(token)
            ?? throw new SubscriptionRuleException("There is no closure request to cancel.");
        await CancelRequestAsync(request, "the app", token);
    }

    /// <summary>The owner follows the "this wasn't me" link. Returns false when the link is unknown or there is nothing left to cancel.</summary>
    public async Task<bool> CancelByTokenAsync(string? cancelToken, CancellationToken token)
    {
        var hash = HashOrNull(cancelToken);
        if (hash is null) return false;
        var request = await Context.AccountClosureRequests.FirstOrDefaultAsync(r => r.CancelTokenHash == hash, token);
        if (request is null || !OpenStatuses.Contains(request.RequestStatus)) return false;
        await CancelRequestAsync(request, "the email link", token);
        return true;
    }

    private async Task CancelRequestAsync(AccountClosureRequest request, string via, CancellationToken token)
    {
        request.RequestStatus = AccountClosureStatus.Canceled;
        request.DecidedAtUtc = Now;
        var subscription = await Context.AccountSubscriptions.FirstOrDefaultAsync(s => s.OwnerUserId == request.OwnerUserId, token);
        await _events.AddAsync(subscription, BillingEventTypes.ClosureCanceled, new { requestId = request.Id, via },
            $"owner:{request.OwnerEmail}", ownerUserId: request.OwnerUserId, token: token);
    }

    // The hourly lifecycle job moves requests along by date (SubscriptionLifecycleService): an unconfirmed
    // one expires, one whose cooling-off is over becomes ready for an operator.

    // ── The console ──────────────────────────────────────────────────────────

    /// <summary>Requests an operator should look at (ready for review), plus those still cooling off so they can see what's coming.</summary>
    public async Task<IReadOnlyList<AccountClosureReviewView>> ListForReviewAsync(CancellationToken token)
    {
        var requests = await Context.AccountClosureRequests.AsNoTracking()
            .Where(r => r.RequestStatus == AccountClosureStatus.ReadyForReview || r.RequestStatus == AccountClosureStatus.CoolingOff)
            .OrderBy(r => r.RequestedAtUtc)
            .ToListAsync(token);
        var owners = requests.Select(r => r.OwnerUserId).Distinct().ToList();
        var companies = (await Context.Tenants.AsNoTracking().Where(t => owners.Contains(t.UserId))
                .Select(t => new { t.UserId, t.TenantName }).ToListAsync(token))
            .GroupBy(t => t.UserId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(t => t.TenantName ?? "(unnamed)").OrderBy(n => n).ToList());
        var subscriptions = await Context.AccountSubscriptions.AsNoTracking()
            .Where(s => owners.Contains(s.OwnerUserId)).ToDictionaryAsync(s => s.OwnerUserId, token);

        return requests.Select(r => new AccountClosureReviewView(
            r.Id, r.OwnerUserId, r.OwnerEmail, companies.GetValueOrDefault(r.OwnerUserId) ?? [], r.Reason, r.RequestStatus,
            r.RequestedAtUtc, r.CoolingOffEndsAtUtc, subscriptions.GetValueOrDefault(r.OwnerUserId)?.CurrentPeriodEndUtc,
            _options.DataPurgeEnabled)).ToList();
    }

    /// <summary>
    /// Locks the account as closed at the owner's request and sets its purge date to the minimum notice from
    /// now; the usual purge then archives and removes the data. Needs the owner's email typed again.
    /// </summary>
    public async Task ApproveAsync(Guid requestId, string? confirmEmail, PlatformOperator approver, CancellationToken token)
    {
        var request = await RequireReviewableAsync(requestId, token);
        var subscription = await RequireSubscriptionAsync(request.OwnerUserId, token);
        await RequireConfirmedEmailAsync(request.OwnerUserId, confirmEmail, token);
        CheckCanClose(subscription);

        var now = Now;
        if (subscription.SubStatus is AccountSubscriptionStatus.Trialing or AccountSubscriptionStatus.Active or AccountSubscriptionStatus.PastDue)
        {
            subscription.SubStatus = AccountSubscriptionStatus.Suspended;
            subscription.SuspensionReason = "This account was closed at the owner's request.";
        }
        var earliest = now.AddDays(_options.MinPurgeNoticeDays);
        if (subscription.PurgeAfterUtc is not { } purgeAfter || purgeAfter > earliest) subscription.PurgeAfterUtc = earliest;
        if (subscription.PurgeState == DataPurgeState.None) subscription.PurgeState = DataPurgeState.Scheduled;
        subscription.ConcurrencyStamp = Guid.NewGuid();

        request.RequestStatus = AccountClosureStatus.Approved;
        request.DecidedAtUtc = now;
        request.DecidedBy = approver.Actor;
        await _events.AddAsync(subscription, BillingEventTypes.ClosureApproved, new { requestId, purgeAfterUtc = subscription.PurgeAfterUtc }, approver.Actor, token: token);
        await _notifier.PublishAsync(subscription.OwnerUserId, BillingEventTypes.ClosureApproved, token);
        if (AccountClosureEmailComposer.Approved(request.OwnerEmail, subscription.PurgeAfterUtc!.Value) is { } email)
            await _publisher.Publish(email, token);
    }

    public async Task RejectAsync(Guid requestId, string? reason, PlatformOperator approver, CancellationToken token)
    {
        var trimmed = reason?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length < 3) throw new SubscriptionRuleException("Give a reason (at least 3 characters).");
        if (trimmed.Length > 500) throw new SubscriptionRuleException("The reason is too long (max 500 characters).");
        var request = await RequireReviewableAsync(requestId, token);

        request.RequestStatus = AccountClosureStatus.Rejected;
        request.DecidedAtUtc = Now;
        request.DecidedBy = approver.Actor;
        request.DecisionReason = trimmed;
        var subscription = await Context.AccountSubscriptions.FirstOrDefaultAsync(s => s.OwnerUserId == request.OwnerUserId, token);
        await _events.AddAsync(subscription, BillingEventTypes.ClosureRejected, new { requestId, reason = trimmed }, approver.Actor,
            ownerUserId: request.OwnerUserId, token: token);
        if (AccountClosureEmailComposer.Rejected(request.OwnerEmail, trimmed) is { } email)
            await _publisher.Publish(email, token);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static readonly AccountClosureStatus[] OpenStatuses =
        [AccountClosureStatus.PendingConfirmation, AccountClosureStatus.CoolingOff, AccountClosureStatus.ReadyForReview];

    private async Task<AccountClosureRequest> RequireReviewableAsync(Guid requestId, CancellationToken token)
    {
        var request = await Context.AccountClosureRequests.FirstOrDefaultAsync(r => r.Id == requestId, token)
            ?? throw new KeyNotFoundException("Closure request not found.");
        return request.RequestStatus == AccountClosureStatus.ReadyForReview
            ? request
            : throw new SubscriptionRuleException(request.RequestStatus == AccountClosureStatus.CoolingOff
                ? "The owner can still change their mind; this can be decided once the cooling-off is over."
                : $"This request is {request.RequestStatus.ToString().ToLowerInvariant()}.");
    }

    /// <summary>An account already purged, destroyed or being erased has nothing left to close.</summary>
    private static void CheckCanClose(AccountSubscription subscription)
    {
        if (subscription.PurgeState is DataPurgeState.Purged or DataPurgeState.ArchiveDestroyed or DataPurgeState.HardDeleting or DataPurgeState.HardDeleteFailed)
            throw new SubscriptionRuleException("This account's data has already been removed or is being removed.");
    }

    private async Task<AccountSubscription> RequireSubscriptionAsync(Guid ownerUserId, CancellationToken token) =>
        await Context.AccountSubscriptions.FirstOrDefaultAsync(s => s.OwnerUserId == ownerUserId, token)
        ?? throw new KeyNotFoundException("This account has no subscription.");

    /// <summary>The owner's email, which they must have typed exactly (case aside).</summary>
    private async Task<string> RequireConfirmedEmailAsync(Guid ownerUserId, string? confirmEmail, CancellationToken token)
    {
        var identities = await _directory.OwnerIdentitiesAsync([ownerUserId], token);
        var email = identities.GetValueOrDefault(ownerUserId).Email
            ?? throw new SubscriptionRuleException("This account's owner email is unknown, so this can't be confirmed.");
        if (!string.Equals(email.Trim(), confirmEmail?.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new SubscriptionRuleException("The email typed doesn't match the account owner's email.");
        return email.Trim();
    }

    private static string? TidyReason(string? reason)
    {
        var trimmed = reason?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        return trimmed.Length > 500 ? throw new SubscriptionRuleException("The reason is too long (max 500 characters).") : trimmed;
    }

    private string Link(string action, string token) =>
        $"{_options.AppBaseUrl.TrimEnd('/')}/account-closure?action={action}&token={Uri.EscapeDataString(token)}";

    private static AccountClosureView ToOwnerView(AccountClosureRequest r, DateTime? purgeAfter) => new(
        r.Id, r.RequestStatus, r.RequestedAtUtc,
        r.RequestStatus == AccountClosureStatus.PendingConfirmation ? r.ConfirmExpiresAtUtc : null,
        r.CoolingOffEndsAtUtc, r.DecisionReason, purgeAfter);

    private static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static string? HashOrNull(string? token) => string.IsNullOrWhiteSpace(token) || token.Length > 128 ? null : Hash(token.Trim());
}
