using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Core.Services.Platform;

/// <summary>An archive-destruction request as the console shows it. <see cref="ConfirmWith"/> is what an approver must type: the owner's email.</summary>
public sealed record ArchiveDestroyRequestView(
    Guid Id,
    Guid OwnerUserId,
    string OwnerEmail,
    IReadOnlyList<string> CompanyNames,
    string Reason,
    AccountDeletionRequestStatus Status,
    Guid RequestedById,
    string RequestedBy,
    DateTime RequestedAtUtc,
    DateTime ExpiresAtUtc,
    string? DecidedBy,
    DateTime? DecidedAtUtc,
    string? DecisionReason,
    bool SecondApproverRequired,
    string? ConfirmWith = null);

/// <summary>
/// Destroying a purged account's archives before their retention date runs out -- the records the law
/// asks us to keep. The two-person rule of an erasure applies: one operator asks, a DIFFERENT one approves
/// within <see cref="Lifetime"/>, and both type the owner's email. Approval doesn't delete anything itself:
/// it makes the retention date "now" and asks for the destroy stage the daily job already runs
/// (<see cref="DataPurgeService.RequestDueAsync"/>), so the account only becomes "archive destroyed" once
/// hrms-api has actually removed the archives. A legal hold refuses it. Doesn't commit.
/// </summary>
public class ArchiveDestroyRequestService
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);
    private const int MaxReasonLength = 500;

    private readonly IUnitOfWorkService _uow;
    private readonly PlatformDirectoryService _directory;
    private readonly DataPurgeService _purge;
    private readonly BillingEventService _events;
    private readonly SubscriptionOptions _options;
    private readonly TimeProvider _clock;

    public ArchiveDestroyRequestService(
        IUnitOfWorkService uow,
        PlatformDirectoryService directory,
        DataPurgeService purge,
        BillingEventService events,
        IOptions<SubscriptionOptions> options,
        TimeProvider clock)
    {
        _uow = uow;
        _directory = directory;
        _purge = purge;
        _events = events;
        _options = options.Value;
        _clock = clock;
    }

    private TenantContext Context => _uow.Context;
    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public Task<bool> CommitChangesAsync(CancellationToken token = default) => _uow.CommitChangesAsync("", token);

    public async Task<ArchiveDestroyRequestView> RequestAsync(
        Guid ownerUserId, string? reason, string? confirmEmail, PlatformOperator requester, CancellationToken token = default)
    {
        reason = RequireReason(reason);
        var subscription = await RequireSubscriptionAsync(ownerUserId, token);
        var ownerEmail = await RequireConfirmedEmailAsync(ownerUserId, confirmEmail, token);
        CheckCanDestroy(subscription);

        var now = Now;
        if (await Context.ArchiveDestroyRequests.AnyAsync(r => r.OwnerUserId == ownerUserId
                && r.RequestStatus == AccountDeletionRequestStatus.Pending && r.ExpiresAtUtc > now, token))
            throw new SubscriptionRuleException("A request to destroy this account's archives is already waiting for approval.");

        var request = new ArchiveDestroyRequest
        {
            Id = Guid.CreateVersion7(),
            OwnerUserId = ownerUserId,
            OwnerEmail = ownerEmail,
            Reason = reason,
            RequestStatus = AccountDeletionRequestStatus.Pending,
            RequestedById = requester.Id,
            RequestedBy = requester.Actor,
            RequestedAtUtc = now,
            ExpiresAtUtc = now.Add(Lifetime),
        };
        await Context.ArchiveDestroyRequests.AddAsync(request, token);
        await _events.AddAsync(subscription, BillingEventTypes.ArchiveDestroyAsked,
            new { requestId = request.Id, reason, expiresAtUtc = request.ExpiresAtUtc }, requester.Actor, token: token);
        return await ToViewAsync(request, token);
    }

    /// <summary>Only the operator who asked can withdraw a request.</summary>
    public async Task WithdrawAsync(Guid ownerUserId, Guid requestId, PlatformOperator requester, CancellationToken token = default)
    {
        var request = await RequirePendingAsync(requestId, token);
        if (request.OwnerUserId != ownerUserId) throw new KeyNotFoundException("Request not found.");
        if (request.RequestedById != requester.Id)
            throw new SubscriptionRuleException("Only the administrator who asked for this can withdraw it. Reject it instead.");

        Decide(request, AccountDeletionRequestStatus.Canceled, requester, null);
        await _events.AddAsync(null, BillingEventTypes.ArchiveDestroyWithdrawn, new { requestId }, requester.Actor, ownerUserId: ownerUserId, token: token);
    }

    public async Task RejectAsync(Guid requestId, string? reason, PlatformOperator approver, CancellationToken token = default)
    {
        reason = RequireReason(reason);
        var request = await RequirePendingAsync(requestId, token);
        Decide(request, AccountDeletionRequestStatus.Rejected, approver, reason);
        await _events.AddAsync(null, BillingEventTypes.ArchiveDestroyRejected, new { requestId, reason }, approver.Actor, ownerUserId: request.OwnerUserId, token: token);
    }

    /// <summary>
    /// The second operator's go-ahead: re-checks everything (the account may have changed since it was asked
    /// for), then makes the archives due now and asks for them to be destroyed.
    /// </summary>
    public async Task ApproveAsync(Guid requestId, string? confirmEmail, PlatformOperator approver, CancellationToken token = default)
    {
        var request = await RequirePendingAsync(requestId, token);
        if (_options.RequireSecondDeletionApprover && request.RequestedById == approver.Id)
            throw new SubscriptionRuleException("A second administrator must approve this. You can't approve your own request.");
        if (!_options.DataPurgeEnabled)
            throw new SubscriptionRuleException("Data removal is switched off, so the archives can't be destroyed now.");

        var subscription = await RequireSubscriptionAsync(request.OwnerUserId, token);
        await RequireConfirmedEmailAsync(request.OwnerUserId, confirmEmail, token);
        CheckCanDestroy(subscription);

        var now = Now;
        Decide(request, AccountDeletionRequestStatus.Approved, approver, null);
        subscription.ArchiveRetainUntilUtc = now;
        subscription.ConcurrencyStamp = Guid.NewGuid();
        await _events.AddAsync(subscription, BillingEventTypes.ArchiveDestroyApproved, new { requestId, reason = request.Reason }, approver.Actor, token: token);
        await _purge.RequestDueAsync(subscription, now, token);
    }

    /// <summary>Requests still waiting for a second operator, oldest first.</summary>
    public async Task<IReadOnlyList<ArchiveDestroyRequestView>> ListPendingAsync(CancellationToken token = default)
    {
        var now = Now;
        var requests = await Context.ArchiveDestroyRequests.AsNoTracking()
            .Where(r => r.RequestStatus == AccountDeletionRequestStatus.Pending && r.ExpiresAtUtc > now)
            .OrderBy(r => r.RequestedAtUtc)
            .ToListAsync(token);
        var views = new List<ArchiveDestroyRequestView>();
        foreach (var request in requests) views.Add(await ToViewAsync(request, token));
        return views;
    }

    /// <summary>The account's latest request (whatever its outcome), or null when it never had one.</summary>
    public async Task<ArchiveDestroyRequestView?> GetLatestAsync(Guid ownerUserId, CancellationToken token = default)
    {
        var request = await Context.ArchiveDestroyRequests.AsNoTracking()
            .Where(r => r.OwnerUserId == ownerUserId)
            .OrderByDescending(r => r.RequestedAtUtc)
            .FirstOrDefaultAsync(token);
        return request is null ? null : await ToViewAsync(request, token);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void CheckCanDestroy(AccountSubscription subscription)
    {
        if (subscription.LegalHold)
            throw new SubscriptionRuleException("This account is on legal hold. Release the hold before destroying its archives.");
        if (subscription.PurgeState == DataPurgeState.ArchiveDestroyed)
            throw new SubscriptionRuleException("This account's archives have already been destroyed.");
        if (subscription.PurgeState != DataPurgeState.Purged)
            throw new SubscriptionRuleException("Only the archives of a purged account can be destroyed.");
    }

    private void Decide(ArchiveDestroyRequest request, AccountDeletionRequestStatus status, PlatformOperator by, string? reason)
    {
        request.RequestStatus = status;
        request.DecidedById = by.Id;
        request.DecidedBy = by.Actor;
        request.DecidedAtUtc = Now;
        request.DecisionReason = reason;
    }

    private async Task<ArchiveDestroyRequest> RequirePendingAsync(Guid requestId, CancellationToken token)
    {
        var request = await Context.ArchiveDestroyRequests.FirstOrDefaultAsync(r => r.Id == requestId, token)
            ?? throw new KeyNotFoundException("Request not found.");
        if (request.RequestStatus != AccountDeletionRequestStatus.Pending)
            throw new SubscriptionRuleException($"This request was already {request.RequestStatus.ToString().ToLowerInvariant()}.");
        if (request.ExpiresAtUtc <= Now)
            throw new SubscriptionRuleException("This request has expired. Ask again.");
        return request;
    }

    private async Task<AccountSubscription> RequireSubscriptionAsync(Guid ownerUserId, CancellationToken token) =>
        await Context.AccountSubscriptions.FirstOrDefaultAsync(s => s.OwnerUserId == ownerUserId, token)
        ?? throw new KeyNotFoundException("This account has no subscription.");

    /// <summary>The owner's email, which the operator must have typed exactly (case aside).</summary>
    private async Task<string> RequireConfirmedEmailAsync(Guid ownerUserId, string? confirmEmail, CancellationToken token)
    {
        var identities = await _directory.OwnerIdentitiesAsync([ownerUserId], token);
        var email = identities.GetValueOrDefault(ownerUserId).Email
            ?? throw new SubscriptionRuleException("This account's owner email is unknown, so this can't be confirmed.");
        if (!string.Equals(email.Trim(), confirmEmail?.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new SubscriptionRuleException("The email typed doesn't match the account owner's email.");
        return email.Trim();
    }

    private static string RequireReason(string? reason)
    {
        var trimmed = reason?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length < 3)
            throw new SubscriptionRuleException("Give a reason (at least 3 characters).");
        if (trimmed.Length > MaxReasonLength)
            throw new SubscriptionRuleException($"The reason is too long (max {MaxReasonLength} characters).");
        return trimmed;
    }

    private async Task<ArchiveDestroyRequestView> ToViewAsync(ArchiveDestroyRequest r, CancellationToken token)
    {
        var names = await Context.PurgedCompanyArchives.AsNoTracking()
            .Where(a => a.OwnerUserId == r.OwnerUserId).Select(a => a.CompanyName).OrderBy(n => n).ToListAsync(token);
        var identities = await _directory.OwnerIdentitiesAsync([r.OwnerUserId], token);
        var confirmWith = identities.GetValueOrDefault(r.OwnerUserId).Email?.Trim();
        return new ArchiveDestroyRequestView(
            r.Id, r.OwnerUserId, r.OwnerEmail, names, r.Reason,
            // Nobody marks a request expired: it simply ran out.
            r.RequestStatus == AccountDeletionRequestStatus.Pending && r.ExpiresAtUtc <= Now ? AccountDeletionRequestStatus.Expired : r.RequestStatus,
            r.RequestedById, r.RequestedBy, r.RequestedAtUtc, r.ExpiresAtUtc, r.DecidedBy, r.DecidedAtUtc, r.DecisionReason,
            _options.RequireSecondDeletionApprover,
            string.IsNullOrEmpty(confirmWith) ? null : confirmWith);
    }
}
