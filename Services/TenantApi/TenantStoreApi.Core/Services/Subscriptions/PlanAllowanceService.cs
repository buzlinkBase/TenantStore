using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Onepunch.Common.Lib.Subscriptions;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Core.Services.Subscriptions;

/// <summary>The answer to "may this account/company take one more of X?".</summary>
public sealed record PlanAllowance(bool Allowed, string? ProblemType, string? Error, int? Max, int Used)
{
    public static PlanAllowance Yes(int used, int? max) => new(true, null, null, max, used);
}

/// <summary>
/// Limit checks Tenant Service owns: companies per account (asked by Auth Service before creating
/// a workspace) and admin seats per company (asked by Auth Service before inviting, and checked
/// here before a role or status change). Employees and devices are checked by the services that
/// own them, against the same snapshot.
/// </summary>
public class PlanAllowanceService
{
    private static readonly string[] PortalOnlyRoles = [TenantRoles.Employee];

    private readonly IUnitOfWorkService _uow;
    private readonly EntitlementResolverService _resolver;
    private readonly SubscriptionUsageService _usage;
    private readonly SubscriptionOptions _options;

    public PlanAllowanceService(
        IUnitOfWorkService uow,
        EntitlementResolverService resolver,
        SubscriptionUsageService usage,
        IOptions<SubscriptionOptions> options)
    {
        _uow = uow;
        _resolver = resolver;
        _usage = usage;
        _options = options.Value;
    }

    private TenantContext Context => _uow.Context;

    /// <summary>Whether <paramref name="ownerUserId"/> may create another company. An account's
    /// first company is always allowed -- creating it is what starts the trial.</summary>
    public async Task<PlanAllowance> CheckCompanyAsync(Guid ownerUserId, CancellationToken token = default)
    {
        var used = await _usage.CountOwnedCompaniesAsync(ownerUserId, token);
        if (!_options.EnforcementEnabled) return PlanAllowance.Yes(used, null);

        var snapshot = await _resolver.ResolveForAccountAsync(ownerUserId, token);
        if (snapshot.Status == EntitlementSnapshot.NoSubscription) return PlanAllowance.Yes(used, null);
        if (snapshot.IsReadOnly)
            return new(false, SubscriptionProblemTypes.ReadOnly,
                "Your subscription is read-only. Choose a plan to add another company.", snapshot.LimitOf(EntitlementKeys.CompaniesMax), used);

        var max = snapshot.LimitOf(EntitlementKeys.CompaniesMax);
        return snapshot.Allows(EntitlementKeys.CompaniesMax, used)
            ? PlanAllowance.Yes(used, max)
            : new(false, SubscriptionProblemTypes.PlanLimitReached,
                $"Your plan allows {max} {(max == 1 ? "company" : "companies")}. Upgrade to add another.", max, used);
    }

    /// <summary>Whether a company may give <paramref name="adding"/> more people an admin seat.</summary>
    public async Task<PlanAllowance> CheckAdminSeatsAsync(Guid tenantId, int adding = 1, CancellationToken token = default)
    {
        var used = (await AdminSeatUsageProvider.CountAsync(Context, [tenantId], token)).GetValueOrDefault(tenantId);
        if (adding <= 0 || !_options.EnforcementEnabled) return PlanAllowance.Yes(used, null);

        var snapshot = await _resolver.ResolveForTenantAsync(tenantId, token);
        if (snapshot.IsReadOnly)
            return new(false, SubscriptionProblemTypes.ReadOnly,
                "Your subscription is read-only. Choose a plan to add users.", snapshot.LimitOf(EntitlementKeys.AdminsMax), used);

        var max = snapshot.LimitOf(EntitlementKeys.AdminsMax);
        return snapshot.Allows(EntitlementKeys.AdminsMax, used, adding)
            ? PlanAllowance.Yes(used, max)
            : new(false, SubscriptionProblemTypes.PlanLimitReached,
                $"Your plan allows {max} admin user{(max == 1 ? "" : "s")} per company. Upgrade to add more.", max, used);
    }

    /// <summary>
    /// Whether a company may be changed at all -- the same rule SubscriptionAccessFilter applies to
    /// this service's own endpoints, decided for the company rather than the caller (who may change
    /// it is the caller's permissions' business). Refused while read-only or stopped by an operator.
    /// </summary>
    public async Task<PlanAllowance> CheckWritableAsync(Guid tenantId, CancellationToken token = default)
    {
        if (!_options.EnforcementEnabled) return PlanAllowance.Yes(0, null);

        var snapshot = await _resolver.ResolveForTenantAsync(tenantId, token);
        var decision = SubscriptionAccessPolicy.Decide(snapshot, "POST", isBillingUser: true);
        return SubscriptionAccessPolicy.Refusal(decision) is { } refusal
            ? new(false, refusal.ProblemType, refusal.Message, null, 0)
            : PlanAllowance.Yes(0, null);
    }

    /// <summary>Whether giving <paramref name="roles"/> to someone currently holding none of them
    /// would take a new admin seat.</summary>
    public static bool TakesAdminSeat(IEnumerable<string> roles) =>
        roles.Any(r => !PortalOnlyRoles.Contains(r) && r != TenantRoles.Owner);

    /// <summary>
    /// Checks a member role/status change before it's made: one that turns a portal-only (or
    /// inactive) member into a seated admin needs a free seat. Throws <see cref="PlanAllowanceException"/>.
    /// </summary>
    public async Task EnsureAdminSeatForChangeAsync(
        Guid tenantId, Guid membershipId, IEnumerable<string>? newRoles, string? newStatus, CancellationToken token = default)
    {
        var member = await Context.Memberships.AsNoTracking()
            .InternalOnly()
            .Include(m => m.Roles)
            .FirstOrDefaultAsync(m => m.Id == membershipId && m.TenantId == tenantId, token);
        if (member == null || member.IsHidden) return;

        var currentRoles = member.RoleNames();
        var holdsSeat = TakesAdminSeat(currentRoles) && IsSeatStatus(member.Status);
        var rolesAfter = newRoles?.ToList() ?? currentRoles;
        var statusAfter = newStatus ?? member.Status;
        var willHoldSeat = TakesAdminSeat(rolesAfter) && IsSeatStatus(statusAfter);
        if (holdsSeat || !willHoldSeat) return;

        var allowance = await CheckAdminSeatsAsync(tenantId, 1, token);
        if (!allowance.Allowed) throw new PlanAllowanceException(allowance, EntitlementKeys.AdminsMax);
    }

    private static bool IsSeatStatus(string? status) => status is "Active" or "Invited";
}

/// <summary>A limit or read-only subscription refused the change. 403 with ProblemDetails.Type
/// <see cref="PlanAllowance.ProblemType"/>.</summary>
public sealed class PlanAllowanceException(PlanAllowance allowance, string limitKey)
    : InvalidOperationException(allowance.Error)
{
    public PlanAllowance Allowance { get; } = allowance;
    public string LimitKey { get; } = limitKey;
}
