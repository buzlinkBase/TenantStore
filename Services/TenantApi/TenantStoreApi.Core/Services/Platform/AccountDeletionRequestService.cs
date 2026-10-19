using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Core.Services.Platform;

/// <summary>The signed-in console operator: their id (token <c>sub</c>) and how they're recorded ("platform:{email}").</summary>
public sealed record PlatformOperator(Guid Id, string Actor);

/// <summary>What an approval started: a whole account's erasure, or one company's (<paramref name="TenantId"/> set).</summary>
public sealed record ApprovedDeletion(Guid OwnerUserId, Guid? TenantId);

/// <summary>A deletion request as the console shows it. <see cref="TenantId"/> is set for one company's deletion.</summary>
public sealed record AccountDeletionRequestView(
    Guid Id,
    Guid OwnerUserId,
    Guid? TenantId,
    string? CompanyName,
    CompanyErasureStatus? Erasure,
    string? ErasureError,
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
    bool SecondApproverRequired);

/// <summary>
/// The two-person rule in front of <see cref="PlatformAccountDeletionService"/> and
/// <see cref="PlatformCompanyDeletionService"/>: one operator asks for an account (or one of its
/// companies) to be erased, and nothing happens until a DIFFERENT operator approves it within
/// <see cref="Lifetime"/>. Both type the owner's email (the company's name, for a company), and the
/// guards (<see cref="IAccountDeletionGuard"/>, <see cref="ICompanyDeletionGuard"/>) are checked at both
/// steps. Doesn't commit.
/// </summary>
public class AccountDeletionRequestService
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);
    private const int MaxReasonLength = 500;

    private readonly IUnitOfWorkService _uow;
    private readonly PlatformDirectoryService _directory;
    private readonly PlatformAccountDeletionService _deletion;
    private readonly PlatformCompanyDeletionService _companyDeletion;
    private readonly BillingEventService _events;
    private readonly IEnumerable<IAccountDeletionGuard> _guards;
    private readonly IEnumerable<ICompanyDeletionGuard> _companyGuards;
    private readonly SubscriptionOptions _options;
    private readonly TimeProvider _clock;

    public AccountDeletionRequestService(
        IUnitOfWorkService uow,
        PlatformDirectoryService directory,
        PlatformAccountDeletionService deletion,
        PlatformCompanyDeletionService companyDeletion,
        BillingEventService events,
        IEnumerable<IAccountDeletionGuard> guards,
        IEnumerable<ICompanyDeletionGuard> companyGuards,
        IOptions<SubscriptionOptions> options,
        TimeProvider clock)
    {
        _options = options.Value;
        _uow = uow;
        _directory = directory;
        _deletion = deletion;
        _companyDeletion = companyDeletion;
        _events = events;
        _guards = guards;
        _companyGuards = companyGuards;
        _clock = clock;
    }

    private TenantContext Context => _uow.Context;
    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public Task<bool> CommitChangesAsync(CancellationToken token = default) => _uow.CommitChangesAsync("", token);

    public async Task<AccountDeletionRequestView> RequestAsync(
        Guid ownerUserId, string? reason, string? confirmEmail, PlatformOperator requester, CancellationToken token = default)
    {
        reason = RequireReason(reason);
        var subscription = await RequireSubscriptionAsync(ownerUserId, token);
        var ownerEmail = await RequireConfirmedEmailAsync(ownerUserId, confirmEmail, token);
        await CheckGuardsAsync(subscription, token);

        var now = Now;
        if (await Context.AccountDeletionRequests.AnyAsync(r => r.OwnerUserId == ownerUserId && r.TenantId == null
                && r.RequestStatus == AccountDeletionRequestStatus.Pending && r.ExpiresAtUtc > now, token))
            throw new SubscriptionRuleException("A deletion request for this account is already waiting for approval.");

        var request = new AccountDeletionRequest
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
        await Context.AccountDeletionRequests.AddAsync(request, token);
        await _events.AddAsync(subscription, BillingEventTypes.DeletionRequested,
            new { requestId = request.Id, reason, expiresAtUtc = request.ExpiresAtUtc }, requester.Actor, token: token);
        return await ToViewAsync(request, token);
    }

    /// <summary>Asks for one company of an account to be erased for good; the account and its other companies stay.</summary>
    public async Task<AccountDeletionRequestView> RequestCompanyAsync(
        Guid tenantId, string? reason, string? confirmName, PlatformOperator requester, CancellationToken token = default)
    {
        reason = RequireReason(reason);
        var company = await RequireCompanyAsync(tenantId, token);
        var subscription = await RequireSubscriptionAsync(company.UserId, token);
        var companyName = RequireConfirmedName(company, confirmName);
        await CheckCompanyGuardsAsync(subscription, company, token);

        var now = Now;
        if (await Context.AccountDeletionRequests.AnyAsync(r => r.TenantId == tenantId
                && r.RequestStatus == AccountDeletionRequestStatus.Pending && r.ExpiresAtUtc > now, token))
            throw new SubscriptionRuleException("A deletion request for this company is already waiting for approval.");

        var identities = await _directory.OwnerIdentitiesAsync([company.UserId], token);
        var request = new AccountDeletionRequest
        {
            Id = Guid.CreateVersion7(),
            OwnerUserId = company.UserId,
            TenantId = tenantId,
            CompanyName = companyName,
            OwnerEmail = identities.GetValueOrDefault(company.UserId).Email ?? string.Empty,
            Reason = reason,
            RequestStatus = AccountDeletionRequestStatus.Pending,
            RequestedById = requester.Id,
            RequestedBy = requester.Actor,
            RequestedAtUtc = now,
            ExpiresAtUtc = now.Add(Lifetime),
        };
        await Context.AccountDeletionRequests.AddAsync(request, token);
        await _events.AddAsync(subscription, BillingEventTypes.DeletionRequested,
            new { requestId = request.Id, tenantId, company = companyName, reason, expiresAtUtc = request.ExpiresAtUtc }, requester.Actor, token: token);
        return await ToViewAsync(request, token);
    }

    /// <summary>Only the operator who asked can withdraw a request.</summary>
    public async Task CancelAsync(Guid ownerUserId, Guid requestId, PlatformOperator requester, CancellationToken token = default)
    {
        var request = await RequirePendingAsync(requestId, token);
        if (request.OwnerUserId != ownerUserId || request.TenantId != null) throw new KeyNotFoundException("Deletion request not found.");
        await WithdrawAsync(request, requester, token);
    }

    /// <summary>Withdraws a company deletion request; only the operator who asked can.</summary>
    public async Task CancelCompanyAsync(Guid tenantId, Guid requestId, PlatformOperator requester, CancellationToken token = default)
    {
        var request = await RequirePendingAsync(requestId, token);
        if (request.TenantId != tenantId) throw new KeyNotFoundException("Deletion request not found.");
        await WithdrawAsync(request, requester, token);
    }

    private async Task WithdrawAsync(AccountDeletionRequest request, PlatformOperator requester, CancellationToken token)
    {
        var requestId = request.Id;
        if (request.RequestedById != requester.Id)
            throw new SubscriptionRuleException("Only the administrator who asked for this deletion can withdraw it. Reject it instead.");

        Decide(request, AccountDeletionRequestStatus.Canceled, requester, null);
        await _events.AddAsync(null, BillingEventTypes.DeletionCanceled, new { requestId }, requester.Actor, ownerUserId: request.OwnerUserId, token: token);
    }

    public async Task RejectAsync(Guid requestId, string? reason, PlatformOperator approver, CancellationToken token = default)
    {
        reason = RequireReason(reason);
        var request = await RequirePendingAsync(requestId, token);
        Decide(request, AccountDeletionRequestStatus.Rejected, approver, reason);
        await _events.AddAsync(null, BillingEventTypes.DeletionRejected, new { requestId, reason }, approver.Actor, ownerUserId: request.OwnerUserId, token: token);
    }

    /// <summary>
    /// The second operator's go-ahead: re-checks everything (the account or company may have changed
    /// since it was asked for), then starts the erasure. <paramref name="confirmation"/> is the owner's
    /// email for an account, the company's name for a company.
    /// </summary>
    public async Task<ApprovedDeletion> ApproveAsync(Guid requestId, string? confirmation, PlatformOperator approver, CancellationToken token = default)
    {
        var request = await RequirePendingAsync(requestId, token);
        // A console setting can drop the two-person rule for testing; on by default.
        if (_options.RequireSecondDeletionApprover && request.RequestedById == approver.Id)
            throw new SubscriptionRuleException("A second administrator must approve this deletion. You can't approve your own request.");

        var subscription = await RequireSubscriptionAsync(request.OwnerUserId, token);
        if (request.TenantId is { } tenantId)
        {
            var company = await RequireCompanyAsync(tenantId, token);
            RequireConfirmedName(company, confirmation);
            await CheckCompanyGuardsAsync(subscription, company, token);
        }
        else
        {
            await RequireConfirmedEmailAsync(request.OwnerUserId, confirmation, token);
            await CheckGuardsAsync(subscription, token);
        }

        Decide(request, AccountDeletionRequestStatus.Approved, approver, null);
        await _events.AddAsync(subscription, BillingEventTypes.DeletionApproved, new { requestId, tenantId = request.TenantId }, approver.Actor, token: token);
        if (request.TenantId != null) await _companyDeletion.StartAsync(request, token);
        else await _deletion.StartAsync(request, token);
        return new ApprovedDeletion(request.OwnerUserId, request.TenantId);
    }

    /// <summary>Requests still waiting for a second operator, oldest first.</summary>
    public async Task<IReadOnlyList<AccountDeletionRequestView>> ListPendingAsync(CancellationToken token = default)
    {
        var now = Now;
        var requests = await Context.AccountDeletionRequests.AsNoTracking()
            .Where(r => r.RequestStatus == AccountDeletionRequestStatus.Pending && r.ExpiresAtUtc > now)
            .OrderBy(r => r.RequestedAtUtc)
            .ToListAsync(token);
        var names = await CompanyNamesAsync(requests.Select(r => r.OwnerUserId).Distinct().ToList(), token);
        return requests.Select(r => ToView(r, names.GetValueOrDefault(r.OwnerUserId) ?? [])).ToList();
    }

    /// <summary>The account's latest request to erase the whole account (whatever its outcome), or null when it never had one.</summary>
    public async Task<AccountDeletionRequestView?> GetLatestAsync(Guid ownerUserId, CancellationToken token = default)
    {
        var request = await Context.AccountDeletionRequests.AsNoTracking()
            .Where(r => r.OwnerUserId == ownerUserId && r.TenantId == null)
            .OrderByDescending(r => r.RequestedAtUtc)
            .FirstOrDefaultAsync(token);
        return request is null ? null : await ToViewAsync(request, token);
    }

    /// <summary>The company's latest deletion request (whatever its outcome), or null when it never had one.</summary>
    public async Task<AccountDeletionRequestView?> GetLatestForCompanyAsync(Guid tenantId, CancellationToken token = default)
    {
        var request = await Context.AccountDeletionRequests.AsNoTracking()
            .Where(r => r.TenantId == tenantId)
            .OrderByDescending(r => r.RequestedAtUtc)
            .FirstOrDefaultAsync(token);
        return request is null ? null : await ToViewAsync(request, token);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void Decide(AccountDeletionRequest request, AccountDeletionRequestStatus status, PlatformOperator by, string? reason)
    {
        request.RequestStatus = status;
        request.DecidedById = by.Id;
        request.DecidedBy = by.Actor;
        request.DecidedAtUtc = Now;
        request.DecisionReason = reason;
    }

    private async Task<AccountDeletionRequest> RequirePendingAsync(Guid requestId, CancellationToken token)
    {
        var request = await Context.AccountDeletionRequests.FirstOrDefaultAsync(r => r.Id == requestId, token)
            ?? throw new KeyNotFoundException("Deletion request not found.");
        if (request.RequestStatus != AccountDeletionRequestStatus.Pending)
            throw new SubscriptionRuleException($"This deletion request was already {request.RequestStatus.ToString().ToLowerInvariant()}.");
        if (request.ExpiresAtUtc <= Now)
            throw new SubscriptionRuleException("This deletion request has expired. Ask for the deletion again.");
        return request;
    }

    private async Task<AccountSubscription> RequireSubscriptionAsync(Guid ownerUserId, CancellationToken token) =>
        await Context.AccountSubscriptions.FirstOrDefaultAsync(s => s.OwnerUserId == ownerUserId, token)
        ?? throw new KeyNotFoundException("This account has no subscription.");

    private async Task CheckGuardsAsync(AccountSubscription subscription, CancellationToken token)
    {
        foreach (var guard in _guards) await guard.CheckAsync(Context, subscription, token);
    }

    private async Task CheckCompanyGuardsAsync(AccountSubscription subscription, Tenant company, CancellationToken token)
    {
        foreach (var guard in _companyGuards) await guard.CheckAsync(Context, subscription, company, token);
    }

    private async Task<Tenant> RequireCompanyAsync(Guid tenantId, CancellationToken token) =>
        await Context.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, token)
        ?? throw new KeyNotFoundException("Company not found.");

    /// <summary>The company's name, which the operator must have typed exactly (case and surrounding spaces aside).</summary>
    private static string RequireConfirmedName(Tenant company, string? confirmName)
    {
        var name = company.TenantName?.Trim();
        if (string.IsNullOrEmpty(name))
            throw new SubscriptionRuleException("This company has no name, so the deletion can't be confirmed.");
        if (!string.Equals(name, confirmName?.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new SubscriptionRuleException("The name typed doesn't match the company's name.");
        return name;
    }

    /// <summary>The owner's email, which the operator must have typed exactly (case aside).</summary>
    private async Task<string> RequireConfirmedEmailAsync(Guid ownerUserId, string? confirmEmail, CancellationToken token)
    {
        var identities = await _directory.OwnerIdentitiesAsync([ownerUserId], token);
        var email = identities.GetValueOrDefault(ownerUserId).Email
            ?? throw new SubscriptionRuleException("This account's owner email is unknown, so the deletion can't be confirmed.");
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

    private async Task<Dictionary<Guid, List<string>>> CompanyNamesAsync(List<Guid> ownerIds, CancellationToken token)
    {
        var rows = await Context.Tenants.AsNoTracking()
            .Where(t => ownerIds.Contains(t.UserId))
            .Select(t => new { t.UserId, t.TenantName })
            .ToListAsync(token);
        return rows.GroupBy(r => r.UserId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.TenantName ?? "(unnamed)").OrderBy(n => n).ToList());
    }

    private async Task<AccountDeletionRequestView> ToViewAsync(AccountDeletionRequest request, CancellationToken token)
    {
        var names = await CompanyNamesAsync([request.OwnerUserId], token);
        return ToView(request, names.GetValueOrDefault(request.OwnerUserId) ?? []);
    }

    private AccountDeletionRequestView ToView(AccountDeletionRequest r, IReadOnlyList<string> companyNames) => new(
        r.Id, r.OwnerUserId, r.TenantId, r.CompanyName, r.Erasure, r.ErasureError, r.OwnerEmail,
        r.TenantId != null && r.CompanyName != null ? [r.CompanyName] : companyNames, r.Reason,
        // Nobody marks a request expired: it simply ran out.
        r.RequestStatus == AccountDeletionRequestStatus.Pending && r.ExpiresAtUtc <= Now ? AccountDeletionRequestStatus.Expired : r.RequestStatus,
        r.RequestedById, r.RequestedBy, r.RequestedAtUtc, r.ExpiresAtUtc, r.DecidedBy, r.DecidedAtUtc, r.DecisionReason,
        _options.RequireSecondDeletionApprover);
}
