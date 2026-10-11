using Microsoft.EntityFrameworkCore;
using Onepunch.Common.Lib.Subscriptions;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Core.Services.Platform;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

/// <summary>Page request, clamped so a console typo can't ask for the whole table.</summary>
public sealed record PageRequest(int Page = 1, int PageSize = 25)
{
    public const int MaxPageSize = 100;
    public int SafePage => Math.Max(1, Page);
    public int SafePageSize => Math.Clamp(PageSize, 1, MaxPageSize);
    public int Skip => (SafePage - 1) * SafePageSize;
}

public sealed record AccountQuery(
    string? Search = null,
    AccountSubscriptionStatus? Status = null,
    string? PlanCode = null,
    int Page = 1,
    int PageSize = 25)
{
    public PageRequest Paging => new(Page, PageSize);
}

public sealed record CompanyQuery(
    string? Search = null,
    bool? Activated = null,
    Guid? OwnerUserId = null,
    int Page = 1,
    int PageSize = 25)
{
    public PageRequest Paging => new(Page, PageSize);
}

public sealed record PaymentQuery(
    Guid? OwnerUserId = null,
    PaymentMethod? Method = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    /// <summary>Null: every payment. True: only voided ones. False: only live ones.</summary>
    bool? Voided = null,
    int Page = 1,
    int PageSize = 25)
{
    public PageRequest Paging => new(Page, PageSize);
}

/// <summary>An owner account: who they are, what they own, where their subscription stands.</summary>
public sealed record AccountListItem(
    Guid OwnerUserId,
    string? OwnerEmail,
    string? OwnerName,
    int Companies,
    int ActivatedCompanies,
    string? PlanCode,
    string? PlanName,
    AccountSubscriptionStatus? Status,
    BillingCycle? BillingCycle,
    DateTime? TrialEndsAtUtc,
    DateTime? CurrentPeriodEndUtc,
    DateTime? GraceEndsAtUtc,
    bool CancelAtPeriodEnd,
    DateTime? SubscribedAtUtc);

public sealed record CompanyListItem(
    Guid TenantId,
    string? Name,
    Guid OwnerUserId,
    string? OwnerEmail,
    bool Activated,
    DateTime? ActivatedAtUtc,
    DateTime CreatedAtUtc,
    string? PlanName,
    AccountSubscriptionStatus? Status,
    SubscriptionAccessMode? AccessMode);

public sealed record DiscountView(Guid Id, decimal? Percent, decimal? FixedAmount, string? Reason, DateTime? ValidUntilUtc);

/// <summary>The account header the console shows above its billing tabs.</summary>
public sealed record AccountDetail(
    AccountListItem Account,
    SubscriptionAccessMode? AccessMode,
    bool TrialExtended,
    DateTime? CurrentPeriodStartUtc,
    DateTime? CanceledAtUtc,
    DateTime? PurgeAfterUtc,
    int OverrideCount,
    IReadOnlyList<CompanyListItem> Companies,
    IReadOnlyList<DiscountView> Discounts,
    DataPurgeState PurgeState = DataPurgeState.None,
    DateTime? DataPurgedAtUtc = null,
    DateTime? ArchiveRetainUntilUtc = null,
    bool LegalHold = false,
    string? LegalHoldReason = null);

/// <summary>One row of the combined payments list: a plan payment or a storage-block payment.</summary>
internal sealed class PaymentRow
{
    public Guid Id { get; set; }
    public Guid OwnerUserId { get; set; }
    public string Kind { get; set; } = PaymentKinds.Plan;
    public DateTime PaidAtUtc { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "PHP";
    public PaymentMethod Method { get; set; }
    public string? Reference { get; set; }
    public Guid PlanId { get; set; }
    public BillingCycle BillingCycle { get; set; }
    public DateTime PeriodStartUtc { get; set; }
    public DateTime PeriodEndUtc { get; set; }
    public string? RecordedBy { get; set; }
    public string? Notes { get; set; }
    public DateTime? VoidedAtUtc { get; set; }
    public string? VoidedBy { get; set; }
    public string? VoidReason { get; set; }
    public Guid? AddOnId { get; set; }
    public int Blocks { get; set; }
}

public sealed record PlatformPaymentView(
    Guid Id,
    Guid OwnerUserId,
    string? OwnerEmail,
    DateTime PaidAtUtc,
    decimal Amount,
    string Currency,
    PaymentMethod Method,
    string? Reference,
    string? PlanName,
    BillingCycle BillingCycle,
    DateTime PeriodStartUtc,
    DateTime PeriodEndUtc,
    string? RecordedBy,
    string? Notes,
    DateTime? VoidedAtUtc,
    string? VoidedBy,
    string? VoidReason,
    /// <summary>"Plan" or "Storage" (<see cref="PaymentKinds"/>): storage blocks bought mid-period are listed too.</summary>
    string Kind = PaymentKinds.Plan,
    /// <summary>Storage payments: what was bought, e.g. "2 x 50 GB Storage".</summary>
    string? Description = null,
    /// <summary>"Refunded" or "Disputed" when the gateway reported one against this payment; nothing is
    /// changed by it -- it is for the operator to act on.</summary>
    string? GatewayIssue = null);

/// <summary>
/// The platform console's cross-account read side: owner accounts, companies and payments,
/// searchable and paged. Read-only. An account's owner is the user in <c>Tenant.UserId</c>; their
/// email and name come from the hidden owner membership of one of their companies.
/// </summary>
public class PlatformDirectoryService
{
    private readonly IUnitOfWorkService _uow;
    private readonly SubscriptionStateMachine _states;

    public PlatformDirectoryService(IUnitOfWorkService uow, SubscriptionStateMachine states)
    {
        _uow = uow;
        _states = states;
    }

    private TenantContext Context => _uow.Context;

    // ── Accounts ─────────────────────────────────────────────────────────────

    public async Task<PagedResult<AccountListItem>> ListAccountsAsync(AccountQuery query, CancellationToken token = default)
    {
        var tenants = Context.Tenants.AsNoTracking();

        var term = query.Search?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(term))
        {
            var ownersByIdentity = Context.Memberships.AsNoTracking()
                .Where(m => m.IsHidden
                    && ((m.InvitedEmail != null && m.InvitedEmail.ToLower().Contains(term))
                        || (m.FullName != null && m.FullName.ToLower().Contains(term))))
                .Select(m => m.UserId);
            tenants = tenants.Where(t => (t.TenantName != null && t.TenantName.ToLower().Contains(term))
                || ownersByIdentity.Contains(t.UserId));
        }

        var status = query.Status;
        var planCode = string.IsNullOrWhiteSpace(query.PlanCode) ? null : query.PlanCode.Trim();
        if (status != null || planCode != null)
        {
            var ownersBySubscription = Context.AccountSubscriptions.AsNoTracking()
                .Where(s => (status == null || s.SubStatus == status) && (planCode == null || s.Plan.Code == planCode))
                .Select(s => s.OwnerUserId);
            tenants = tenants.Where(t => ownersBySubscription.Contains(t.UserId));
        }

        // Newest account first: an owner is as new as their most recently created company.
        var owners = tenants
            .GroupBy(t => t.UserId)
            .Select(g => new { OwnerUserId = g.Key, Latest = g.Max(t => t.CreatedAt) });
        var total = await owners.CountAsync(token);
        var paging = query.Paging;
        var pageOwners = await owners
            .OrderByDescending(o => o.Latest).ThenBy(o => o.OwnerUserId)
            .Skip(paging.Skip).Take(paging.SafePageSize)
            .Select(o => o.OwnerUserId)
            .ToListAsync(token);

        var items = await BuildAccountItemsAsync(pageOwners, token);
        return new PagedResult<AccountListItem>(items, total, paging.SafePage, paging.SafePageSize);
    }

    public async Task<AccountDetail> GetAccountAsync(Guid ownerUserId, CancellationToken token = default)
    {
        var item = (await BuildAccountItemsAsync([ownerUserId], token)).Single();
        if (item.Companies == 0 && item.Status == null)
            throw new KeyNotFoundException("No account with this owner.");

        var subscription = await Context.AccountSubscriptions.AsNoTracking()
            .Include(s => s.Overrides)
            .FirstOrDefaultAsync(s => s.OwnerUserId == ownerUserId, token);

        var companies = await ListCompaniesAsync(new CompanyQuery(OwnerUserId: ownerUserId, PageSize: PageRequest.MaxPageSize), token);
        var discounts = subscription == null
            ? []
            : await Context.SubscriptionDiscounts.AsNoTracking()
                .Where(d => d.SubscriptionId == subscription.Id)
                .Select(d => new DiscountView(d.Id, d.Percent, d.FixedAmount, d.Reason, d.ValidUntilUtc))
                .ToListAsync(token);

        return new AccountDetail(
            item,
            subscription == null ? null : _states.AccessModeOf(subscription),
            subscription?.TrialExtended ?? false,
            subscription?.CurrentPeriodStartUtc,
            subscription?.CanceledAtUtc,
            subscription?.PurgeAfterUtc,
            subscription?.Overrides.Count ?? 0,
            companies.Items,
            discounts,
            subscription?.PurgeState ?? DataPurgeState.None,
            subscription?.DataPurgedAtUtc,
            subscription?.ArchiveRetainUntilUtc,
            subscription?.LegalHold ?? false,
            subscription?.LegalHoldReason);
    }

    private async Task<List<AccountListItem>> BuildAccountItemsAsync(List<Guid> ownerIds, CancellationToken token)
    {
        if (ownerIds.Count == 0) return [];

        var companies = (await Context.Tenants.AsNoTracking()
                .Where(t => ownerIds.Contains(t.UserId))
                .Select(t => new { t.UserId, Activated = t.ActivatedAtUtc != null })
                .ToListAsync(token))
            .GroupBy(t => t.UserId)
            .ToDictionary(g => g.Key, g => (Total: g.Count(), Activated: g.Count(c => c.Activated)));
        var subscriptions = (await Context.AccountSubscriptions.AsNoTracking()
                .Include(s => s.Plan)
                .Where(s => ownerIds.Contains(s.OwnerUserId))
                .ToListAsync(token))
            .ToDictionary(s => s.OwnerUserId);
        var identities = await OwnerIdentitiesAsync(ownerIds, token);

        return ownerIds.Select(owner =>
        {
            companies.TryGetValue(owner, out var counts);
            subscriptions.TryGetValue(owner, out var subscription);
            identities.TryGetValue(owner, out var identity);
            return new AccountListItem(
                owner, identity.Email, identity.Name, counts.Total, counts.Activated,
                subscription?.Plan.Code, subscription?.Plan.Name, subscription?.SubStatus, subscription?.BillingCycle,
                subscription?.TrialEndsAtUtc, subscription?.CurrentPeriodEndUtc, subscription?.GraceEndsAtUtc,
                subscription?.CancelAtPeriodEnd ?? false, subscription?.CreatedAt);
        }).ToList();
    }

    /// <summary>Email and name of each owner, from the hidden owner membership of any of their companies.</summary>
    public async Task<Dictionary<Guid, (string? Email, string? Name)>> OwnerIdentitiesAsync(
        IReadOnlyCollection<Guid> ownerIds, CancellationToken token)
    {
        var rows = await Context.Memberships.AsNoTracking()
            .Where(m => m.IsHidden && ownerIds.Contains(m.UserId))
            .Select(m => new { m.UserId, m.InvitedEmail, m.FullName })
            .ToListAsync(token);
        return rows
            .GroupBy(r => r.UserId)
            .ToDictionary(
                g => g.Key,
                g => (g.Select(r => r.InvitedEmail).FirstOrDefault(e => !string.IsNullOrWhiteSpace(e)),
                      g.Select(r => r.FullName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))));
    }

    // ── Companies ────────────────────────────────────────────────────────────

    public async Task<PagedResult<CompanyListItem>> ListCompaniesAsync(CompanyQuery query, CancellationToken token = default)
    {
        var tenants = Context.Tenants.AsNoTracking();

        if (query.OwnerUserId is { } owner) tenants = tenants.Where(t => t.UserId == owner);
        if (query.Activated is { } activated)
            tenants = activated ? tenants.Where(t => t.ActivatedAtUtc != null) : tenants.Where(t => t.ActivatedAtUtc == null);

        var term = query.Search?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(term))
        {
            var ownersByIdentity = Context.Memberships.AsNoTracking()
                .Where(m => m.IsHidden && m.InvitedEmail != null && m.InvitedEmail.ToLower().Contains(term))
                .Select(m => m.UserId);
            tenants = tenants.Where(t => (t.TenantName != null && t.TenantName.ToLower().Contains(term))
                || ownersByIdentity.Contains(t.UserId));
        }

        var total = await tenants.CountAsync(token);
        var paging = query.Paging;
        var page = await tenants
            .OrderByDescending(t => t.CreatedAt).ThenBy(t => t.Id)
            .Skip(paging.Skip).Take(paging.SafePageSize)
            .Select(t => new { t.Id, t.TenantName, t.UserId, t.ActivatedAtUtc, t.CreatedAt })
            .ToListAsync(token);

        var ownerIds = page.Select(t => t.UserId).Distinct().ToList();
        var identities = await OwnerIdentitiesAsync(ownerIds, token);
        var subscriptions = (await Context.AccountSubscriptions.AsNoTracking()
                .Include(s => s.Plan)
                .Where(s => ownerIds.Contains(s.OwnerUserId))
                .ToListAsync(token))
            .ToDictionary(s => s.OwnerUserId);

        var items = page.Select(t =>
        {
            subscriptions.TryGetValue(t.UserId, out var subscription);
            identities.TryGetValue(t.UserId, out var identity);
            return new CompanyListItem(
                t.Id, t.TenantName, t.UserId, identity.Email,
                t.ActivatedAtUtc != null, t.ActivatedAtUtc, t.CreatedAt,
                subscription?.Plan.Name, subscription?.SubStatus,
                subscription == null ? null : _states.AccessModeOf(subscription));
        }).ToList();
        return new PagedResult<CompanyListItem>(items, total, paging.SafePage, paging.SafePageSize);
    }

    // ── Payments ─────────────────────────────────────────────────────────────

    public async Task<PagedResult<PlatformPaymentView>> ListPaymentsAsync(PaymentQuery query, CancellationToken token = default)
    {
        // Plan payments and storage-block payments live in separate tables; one list shows both.
        var planPayments = from p in Context.SubscriptionPayments.AsNoTracking()
                           join s in Context.AccountSubscriptions.AsNoTracking() on p.SubscriptionId equals s.Id
                           select new PaymentRow
                           {
                               Id = p.Id, OwnerUserId = s.OwnerUserId, Kind = PaymentKinds.Plan,
                               PaidAtUtc = p.PaidAtUtc, Amount = p.Amount, Currency = p.Currency, Method = p.Method,
                               Reference = p.Reference, PlanId = p.PlanId, BillingCycle = p.BillingCycle,
                               PeriodStartUtc = p.PeriodStartUtc, PeriodEndUtc = p.PeriodEndUtc,
                               RecordedBy = p.RecordedBy, Notes = p.Notes,
                               VoidedAtUtc = p.VoidedAtUtc, VoidedBy = p.VoidedBy, VoidReason = p.VoidReason,
                           };
        var storagePayments = from p in Context.StorageAddOnPayments.AsNoTracking()
                              join s in Context.AccountSubscriptions.AsNoTracking() on p.SubscriptionId equals s.Id
                              select new PaymentRow
                              {
                                  Id = p.Id, OwnerUserId = s.OwnerUserId, Kind = PaymentKinds.Storage,
                                  PaidAtUtc = p.PaidAtUtc, Amount = p.Amount, Currency = p.Currency, Method = p.Method,
                                  Reference = p.Reference, PlanId = s.PlanId, BillingCycle = s.BillingCycle,
                                  PeriodStartUtc = p.PeriodStartUtc, PeriodEndUtc = p.PeriodEndUtc,
                                  RecordedBy = p.RecordedBy, AddOnId = p.AddOnId, Blocks = p.Blocks,
                              };

        IQueryable<PaymentRow> Filtered(IQueryable<PaymentRow> payments)
        {
            if (query.OwnerUserId is { } owner) payments = payments.Where(x => x.OwnerUserId == owner);
            if (query.Method is { } method) payments = payments.Where(x => x.Method == method);
            if (query.FromUtc is { } from) payments = payments.Where(x => x.PaidAtUtc >= from);
            if (query.ToUtc is { } to) payments = payments.Where(x => x.PaidAtUtc <= to);
            if (query.Voided is { } voided)
                payments = voided ? payments.Where(x => x.VoidedAtUtc != null) : payments.Where(x => x.VoidedAtUtc == null);
            return payments;
        }

        // Two tables, merged here rather than in SQL: each side contributes its newest (skip + take)
        // rows, and the requested page is cut from those, which is exactly what a combined sort would give.
        var plan = Filtered(planPayments);
        var storage = Filtered(storagePayments);
        var total = await plan.CountAsync(token) + await storage.CountAsync(token);
        var paging = query.Paging;
        var window = paging.Skip + paging.SafePageSize;
        var fromPlan = await plan.OrderByDescending(x => x.PaidAtUtc).ThenBy(x => x.Id).Take(window).ToListAsync(token);
        var fromStorage = await storage.OrderByDescending(x => x.PaidAtUtc).ThenBy(x => x.Id).Take(window).ToListAsync(token);
        var page = fromPlan.Concat(fromStorage)
            .OrderByDescending(x => x.PaidAtUtc).ThenBy(x => x.Id)
            .Skip(paging.Skip).Take(paging.SafePageSize)
            .ToList();

        var ownerIds = page.Select(x => x.OwnerUserId).Distinct().ToList();
        var identities = await OwnerIdentitiesAsync(ownerIds, token);
        var planIds = page.Select(x => x.PlanId).Distinct().ToList();
        var planNames = await Context.Plans.AsNoTracking()
            .Where(p => planIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name, token);
        var addOnIds = page.Where(x => x.AddOnId != null).Select(x => x.AddOnId!.Value).Distinct().ToList();
        var offeringNames = await Context.StorageAddOns.AsNoTracking()
            .Where(a => addOnIds.Contains(a.Id))
            .Select(a => new { a.Id, a.Offering.Name })
            .ToDictionaryAsync(a => a.Id, a => a.Name, token);

        // Refunds and disputes are billing events keyed "{provider}:payment:{gatewayPaymentId}:{type}".
        var issueTypes = new[] { BillingEventTypes.PaymentRefunded, BillingEventTypes.PaymentDisputed };
        var issueEvents = await Context.BillingEvents.AsNoTracking()
            .Where(e => issueTypes.Contains(e.Type) && e.OwnerUserId != null && ownerIds.Contains(e.OwnerUserId.Value))
            .Select(e => new { e.Type, e.DedupeKey })
            .ToListAsync(token);
        var issues = new Dictionary<string, string>();
        foreach (var e in issueEvents)
        {
            var parts = e.DedupeKey?.Split(':');
            if (parts is not { Length: >= 4 }) continue;
            var label = e.Type == BillingEventTypes.PaymentDisputed ? "Disputed" : "Refunded";
            // A dispute outranks a refund when both were reported.
            if (!issues.TryGetValue(parts[2], out var existing) || label == "Disputed" || existing != "Disputed") issues[parts[2]] = label;
        }

        var items = page.Select(x =>
        {
            identities.TryGetValue(x.OwnerUserId, out var identity);
            var issue = x.Reference != null ? issues.GetValueOrDefault(x.Reference) : null;
            var description = x.Kind == PaymentKinds.Storage
                ? StorageAddOnService.DescribePayment(x.Blocks, x.AddOnId is { } id ? offeringNames.GetValueOrDefault(id) : null)
                : null;
            return new PlatformPaymentView(
                x.Id, x.OwnerUserId, identity.Email, x.PaidAtUtc, x.Amount, x.Currency, x.Method, x.Reference,
                planNames.GetValueOrDefault(x.PlanId), x.BillingCycle, x.PeriodStartUtc, x.PeriodEndUtc,
                x.RecordedBy, x.Notes, x.VoidedAtUtc, x.VoidedBy, x.VoidReason, x.Kind, description, issue);
        }).ToList();
        return new PagedResult<PlatformPaymentView>(items, total, paging.SafePage, paging.SafePageSize);
    }
}
