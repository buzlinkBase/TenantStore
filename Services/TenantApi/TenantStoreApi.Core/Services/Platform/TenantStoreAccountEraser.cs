using Microsoft.EntityFrameworkCore;
using Onepunch.AuditEngine.EntityFrameworkCore;
using TenantStoreApi.Domain.Entities;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Core.Services.Platform;

/// <summary>
/// Physically deletes the rows a query selects -- no soft delete, no audit rows about the deletion.
/// Strategy seam: production deletes in the database (<see cref="RelationalRowEraser"/>); tests over
/// the InMemory provider, which can't run set-based deletes, plug in their own.
/// </summary>
public interface IRowEraser
{
    Task<int> EraseAsync<T>(IQueryable<T> rows, CancellationToken token) where T : class;
}

/// <summary>One DELETE statement per call (<c>ExecuteDeleteAsync</c>): bypasses the change tracker, so
/// neither the soft-delete nor the audit interceptor sees it. Runs inside the unit of work's
/// transaction when one is open.</summary>
public sealed class RelationalRowEraser : IRowEraser
{
    public Task<int> EraseAsync<T>(IQueryable<T> rows, CancellationToken token) where T : class =>
        rows.ExecuteDeleteAsync(token);
}

/// <summary>What one account's erasure covers in Tenant Service.</summary>
public sealed record AccountErasureScope(Guid OwnerUserId, Guid SubscriptionId, IReadOnlyCollection<Guid> TenantIds);

/// <summary>What one company's erasure covers in Tenant Service; the account keeps everything else.</summary>
public sealed record CompanyErasureScope(Guid OwnerUserId, Guid SubscriptionId, Guid TenantId, Guid RequestId);

/// <summary>
/// Deletes what Tenant Service holds for one account -- subscription and billing history, companies,
/// memberships, client entities, custom roles, audit trail -- or for just one of its companies,
/// children before parents so no foreign key objects. Soft-deleted rows go too (every query ignores
/// the filters). Rows that belong to anyone else are never selected: everything is keyed by the
/// owner, the subscription or the companies being erased. Idempotent: a second run finds nothing.
/// </summary>
public class TenantStoreAccountEraser
{
    private readonly IUnitOfWorkService _uow;
    private readonly IRowEraser _rows;

    public TenantStoreAccountEraser(IUnitOfWorkService uow, IRowEraser rows)
    {
        _uow = uow;
        _rows = rows;
    }

    private TenantContext Context => _uow.Context;

    private IQueryable<T> All<T>() where T : class => Context.Set<T>().IgnoreQueryFilters();

    public async Task<long> EraseAsync(AccountErasureScope scope, CancellationToken token)
    {
        var owner = scope.OwnerUserId;
        var subscriptionId = scope.SubscriptionId;

        var paymentIds = await All<SubscriptionPayment>().Where(p => p.SubscriptionId == subscriptionId).Select(p => p.Id).ToListAsync(token);
        var overrideIds = await All<SubscriptionOverride>().Where(o => o.SubscriptionId == subscriptionId).Select(o => o.Id).ToListAsync(token);
        var discountIds = await All<SubscriptionDiscount>().Where(d => d.SubscriptionId == subscriptionId).Select(d => d.Id).ToListAsync(token);
        // Audit rows about the account's own records written outside any company (an operator's change to the subscription).
        var entityIds = new[] { subscriptionId }.Concat(paymentIds).Concat(overrideIds).Concat(discountIds).Select(id => id.ToString()).ToList();

        long deleted = await _rows.EraseAsync(All<AuditEventRecord>().Where(a => entityIds.Contains(a.EntityId!)), token);

        // Billing: history, checkouts and payments (Restrict FKs) before the subscription.
        deleted += await _rows.EraseAsync(All<BillingEvent>().Where(e => e.OwnerUserId == owner || e.SubscriptionId == subscriptionId), token);
        deleted += await _rows.EraseAsync(All<AccountDeletionRequest>().Where(r => r.OwnerUserId == owner), token);
        deleted += await _rows.EraseAsync(All<PaymentCheckout>().Where(c => c.SubscriptionId == subscriptionId || c.OwnerUserId == owner), token);
        deleted += await _rows.EraseAsync(All<SubscriptionPayment>().Where(p => p.SubscriptionId == subscriptionId), token);
        deleted += await _rows.EraseAsync(All<StorageAddOnPayment>().Where(p => p.SubscriptionId == subscriptionId), token);
        deleted += await _rows.EraseAsync(All<StorageAddOn>().Where(a => a.SubscriptionId == subscriptionId), token);
        deleted += await _rows.EraseAsync(All<StorageAddOnOffer>().Where(o => o.OwnerUserId == owner), token);
        deleted += await _rows.EraseAsync(All<PlanOffer>().Where(o => o.OwnerUserId == owner), token);
        deleted += await _rows.EraseAsync(All<SubscriptionDiscount>().Where(d => d.SubscriptionId == subscriptionId), token);
        deleted += await _rows.EraseAsync(All<SubscriptionOverride>().Where(o => o.SubscriptionId == subscriptionId), token);
        deleted += await _rows.EraseAsync(All<AccountSubscription>().Where(s => s.Id == subscriptionId), token);

        deleted += await EraseCompaniesAsync(scope.TenantIds.ToList(), token);
        return deleted;
    }

    /// <summary>
    /// One company: its own rows, its company-specific overrides, the account's billing entries that
    /// are about it (they name it in their payload) and its deletion request. The account's
    /// subscription, payments and other companies are untouched.
    /// </summary>
    public async Task<long> EraseCompanyAsync(CompanyErasureScope scope, CancellationToken token)
    {
        var tenant = scope.TenantId.ToString();
        var request = scope.RequestId.ToString();
        var overrideIds = await All<SubscriptionOverride>()
            .Where(o => o.SubscriptionId == scope.SubscriptionId && o.TenantId == scope.TenantId)
            .Select(o => o.Id.ToString())
            .ToListAsync(token);

        long deleted = await _rows.EraseAsync(All<AuditEventRecord>().Where(a => overrideIds.Contains(a.EntityId!)), token);
        deleted += await _rows.EraseAsync(All<SubscriptionOverride>()
            .Where(o => o.SubscriptionId == scope.SubscriptionId && o.TenantId == scope.TenantId), token);
        deleted += await _rows.EraseAsync(All<BillingEvent>()
            .Where(e => (e.OwnerUserId == scope.OwnerUserId || e.SubscriptionId == scope.SubscriptionId)
                && ((e.PayloadJson != null && (e.PayloadJson.Contains(tenant) || e.PayloadJson.Contains(request)))
                    || (e.DedupeKey != null && e.DedupeKey.Contains(scope.TenantId.ToString("N"))))), token);
        deleted += await _rows.EraseAsync(All<AccountDeletionRequest>().Where(r => r.TenantId == scope.TenantId), token);
        deleted += await EraseCompaniesAsync([scope.TenantId], token);
        return deleted;
    }

    /// <summary>The companies themselves and everything Tenant Service keeps per company.</summary>
    private async Task<long> EraseCompaniesAsync(List<Guid> tenantIds, CancellationToken token)
    {
        var membershipIds = await All<UserMembership>().Where(m => tenantIds.Contains(m.TenantId)).Select(m => m.Id).ToListAsync(token);
        var roleIds = await All<Role>().Where(r => r.TenantId != null && tenantIds.Contains(r.TenantId.Value)).Select(r => r.Id).ToListAsync(token);
        var clientIds = await All<ClientEntity>().Where(c => tenantIds.Contains(c.TenantId)).Select(c => c.Id).ToListAsync(token);

        // Audit rows of the companies (OrganizationId = company), plus rows about their records written elsewhere.
        var organizations = tenantIds.Select(id => id.ToString()).ToList();
        var entityIds = tenantIds.Concat(membershipIds).Concat(roleIds).Concat(clientIds).Select(id => id.ToString()).ToList();

        long deleted = await _rows.EraseAsync(All<AuditEventRecord>()
            .Where(a => organizations.Contains(a.OrganizationId!) || entityIds.Contains(a.EntityId!)), token);

        // Access: role assignments, memberships, the companies' own roles and clients.
        deleted += await _rows.EraseAsync(All<ClientMembershipRole>()
            .Where(r => membershipIds.Contains(r.UserMembershipId) || clientIds.Contains(r.ClientEntityId) || roleIds.Contains(r.RoleId)), token);
        deleted += await _rows.EraseAsync(All<MembershipRole>()
            .Where(r => membershipIds.Contains(r.UserMembershipId) || (r.RoleId != null && roleIds.Contains(r.RoleId.Value))), token);
        deleted += await _rows.EraseAsync(All<UserMembership>().Where(m => membershipIds.Contains(m.Id)), token);
        deleted += await _rows.EraseAsync(All<RolePermission>().Where(p => roleIds.Contains(p.RoleId)), token);
        deleted += await _rows.EraseAsync(All<Role>().Where(r => roleIds.Contains(r.Id)), token);
        deleted += await _rows.EraseAsync(All<ClientEntity>().Where(c => clientIds.Contains(c.Id)), token);

        // The companies themselves.
        deleted += await _rows.EraseAsync(All<TenantDelegation>()
            .Where(d => tenantIds.Contains(d.HostTenantId) || tenantIds.Contains(d.GuestTenantId)), token);
        deleted += await _rows.EraseAsync(All<ConnectionStringStore>().Where(c => tenantIds.Contains(c.TenantId)), token);
        deleted += await _rows.EraseAsync(All<SchemaVersion>().Where(v => tenantIds.Contains(v.TenantId)), token);
        deleted += await _rows.EraseAsync(All<Tenant>().Where(t => tenantIds.Contains(t.Id)), token);
        return deleted;
    }
}
