using Microsoft.EntityFrameworkCore;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Core.Services.Platform;

/// <summary>
/// One reason an account can't be erased. Each guard throws <see cref="SubscriptionRuleException"/>
/// with the reason the operator is shown; a new rule is a new class registered beside these, nothing
/// that runs them changes. Checked when the deletion is requested AND again when it is approved,
/// since the account may have changed in between.
/// </summary>
public interface IAccountDeletionGuard
{
    Task CheckAsync(TenantContext context, AccountSubscription subscription, CancellationToken token);
}

/// <summary>The same, for erasing one company of an account (the account stays).</summary>
public interface ICompanyDeletionGuard
{
    Task CheckAsync(TenantContext context, AccountSubscription subscription, Tenant company, CancellationToken token);
}

/// <summary>Records kept for an audit, protest, refund claim or case are never erased -- not the account, not one of its companies.</summary>
public sealed class LegalHoldDeletionGuard : IAccountDeletionGuard, ICompanyDeletionGuard
{
    public Task CheckAsync(TenantContext context, AccountSubscription subscription, CancellationToken token) =>
        subscription.LegalHold
            ? throw new SubscriptionRuleException("This account is under legal hold. Release the hold before deleting it.")
            : Task.CompletedTask;

    public Task CheckAsync(TenantContext context, AccountSubscription subscription, Tenant company, CancellationToken token) =>
        subscription.LegalHold
            ? throw new SubscriptionRuleException("This company's account is under legal hold. Release the hold before deleting the company.")
            : Task.CompletedTask;
}

/// <summary>
/// An account that paid real money isn't a test account. Every payment must be voided first (the
/// refund flow voids an online payment once the money is back), which is the operator's deliberate
/// statement that the payment doesn't count. Not a company rule: payments belong to the account,
/// which keeps them when one of its companies goes.
/// </summary>
public sealed class LivePaymentsDeletionGuard : IAccountDeletionGuard
{
    public async Task CheckAsync(TenantContext context, AccountSubscription subscription, CancellationToken token)
    {
        var live = await context.SubscriptionPayments.AsNoTracking()
            .CountAsync(p => p.SubscriptionId == subscription.Id && p.VoidedAtUtc == null, token);
        if (live > 0)
            throw new SubscriptionRuleException(live == 1
                ? "This account has a payment that isn't voided. Void it first if this really is a test account."
                : $"This account has {live} payments that aren't voided. Void them first if this really is a test account.");
    }
}

/// <summary>An account erasure already under way is retried, not requested again -- and none of its companies can be singled out meanwhile.</summary>
public sealed class NotErasingDeletionGuard : IAccountDeletionGuard, ICompanyDeletionGuard
{
    public Task CheckAsync(TenantContext context, AccountSubscription subscription, CancellationToken token) =>
        IsErasing(subscription)
            ? throw new SubscriptionRuleException("This account is already being deleted.")
            : Task.CompletedTask;

    public Task CheckAsync(TenantContext context, AccountSubscription subscription, Tenant company, CancellationToken token) =>
        IsErasing(subscription)
            ? throw new SubscriptionRuleException("This company's whole account is already being deleted.")
            : Task.CompletedTask;

    private static bool IsErasing(AccountSubscription subscription) =>
        subscription.PurgeState is DataPurgeState.HardDeleting or DataPurgeState.HardDeleteFailed;
}

/// <summary>
/// One erasure at a time: a company already being erased is retried, not requested again, and the
/// account can't be erased while one of its companies is (the two would race over the same rows).
/// </summary>
public sealed class NoCompanyErasureRunningGuard : IAccountDeletionGuard, ICompanyDeletionGuard
{
    public async Task CheckAsync(TenantContext context, AccountSubscription subscription, CancellationToken token)
    {
        if (await context.AccountDeletionRequests.AsNoTracking()
                .AnyAsync(r => r.OwnerUserId == subscription.OwnerUserId && r.TenantId != null && r.Erasure != null, token))
            throw new SubscriptionRuleException("One of this account's companies is being deleted. Wait for it to finish (or retry it) first.");
    }

    public async Task CheckAsync(TenantContext context, AccountSubscription subscription, Tenant company, CancellationToken token)
    {
        if (await context.AccountDeletionRequests.AsNoTracking()
                .AnyAsync(r => r.TenantId == company.Id && r.Erasure != null, token))
            throw new SubscriptionRuleException("This company is already being deleted.");
    }
}

/// <summary>An account is never left without companies: deleting the last one is deleting the account, which has its own action.</summary>
public sealed class NotLastCompanyDeletionGuard : ICompanyDeletionGuard
{
    public async Task CheckAsync(TenantContext context, AccountSubscription subscription, Tenant company, CancellationToken token)
    {
        var others = await context.Tenants.AsNoTracking()
            .CountAsync(t => t.UserId == subscription.OwnerUserId && t.Id != company.Id, token);
        if (others == 0)
            throw new SubscriptionRuleException("This is the account's only company. Delete the account instead.");
    }
}

/// <summary>The guards every deletion request and approval passes, in order.</summary>
public static class AccountDeletionGuards
{
    private static readonly NotErasingDeletionGuard NotErasing = new();
    private static readonly NoCompanyErasureRunningGuard NoCompanyErasure = new();
    private static readonly LegalHoldDeletionGuard LegalHold = new();

    public static IReadOnlyList<IAccountDeletionGuard> Default { get; } =
    [
        NotErasing,
        NoCompanyErasure,
        LegalHold,
        new LivePaymentsDeletionGuard(),
    ];

    public static IReadOnlyList<ICompanyDeletionGuard> Company { get; } =
    [
        NotErasing,
        NoCompanyErasure,
        LegalHold,
        new NotLastCompanyDeletionGuard(),
    ];
}
