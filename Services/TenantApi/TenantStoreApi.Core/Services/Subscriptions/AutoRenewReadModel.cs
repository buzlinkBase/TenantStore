using Microsoft.EntityFrameworkCore;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Core.Services.Subscriptions;

/// <summary>An auto renew as the billing screens show it -- never more of the card than its label.</summary>
public sealed record AutoRenewView(
    AutoRenewTarget Target,
    AutoRenewMandateStatus Status,
    BillingCycle BillingCycle,
    string? PaymentMethodLabel,
    DateTime? ActivatedAtUtc,
    DateTime? NextChargeAtUtc,
    DateTime? LastFailureAtUtc,
    string? LastFailureReason,
    DateTime? CanceledAtUtc,
    string? CancelReason)
{
    public static AutoRenewView From(AutoRenewMandate m) => new(
        m.Target, m.Status, m.BillingCycle, m.PaymentMethodLabel, m.ActivatedAtUtc, m.NextChargeAtUtc,
        m.LastFailureAtUtc, m.LastFailureReason, m.CanceledAtUtc, m.CancelReason);
}

/// <summary>Reads an account's auto renews for the billing views (no gateway calls).</summary>
public static class AutoRenewReadModel
{
    /// <summary>The live (on, or being set up) auto renew of each target; a target without one is missing.</summary>
    public static async Task<List<AutoRenewView>> LiveAsync(TenantContext context, Guid subscriptionId, CancellationToken token = default) =>
        (await context.AutoRenewMandates.AsNoTracking()
            .Where(m => m.SubscriptionId == subscriptionId && m.Status != AutoRenewMandateStatus.Canceled)
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync(token))
        .GroupBy(m => m.Target)
        .Select(g => AutoRenewView.From(g.First()))
        .ToList();

    /// <summary>Every auto renew, newest first -- the console's history.</summary>
    public static async Task<List<AutoRenewView>> AllAsync(TenantContext context, Guid subscriptionId, CancellationToken token = default) =>
        (await context.AutoRenewMandates.AsNoTracking()
            .Where(m => m.SubscriptionId == subscriptionId)
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync(token))
        .Select(AutoRenewView.From)
        .ToList();

    /// <summary>The live auto renew that charges by itself (active or retrying), if any.</summary>
    public static Task<AutoRenewMandate?> ChargingAsync(TenantContext context, Guid subscriptionId, AutoRenewTarget target, CancellationToken token = default) =>
        context.AutoRenewMandates.AsNoTracking()
            .Where(m => m.SubscriptionId == subscriptionId && m.Target == target
                && (m.Status == AutoRenewMandateStatus.Active || m.Status == AutoRenewMandateStatus.PastDue))
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefaultAsync(token);
}
