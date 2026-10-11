using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Serilog;

namespace TenantStoreApi.Core.Services.Payments;

/// <summary>What one reconciliation pass found.</summary>
public sealed record ReconcileSummary(int Checked, int Settled, int RefundsFlagged);

/// <summary>
/// Asks the payment gateway about checkouts whose webhook may never have arrived (see
/// <see cref="SubscriptionCheckoutService.ReconcileAsync"/>). Each checkout gets a scope -- and so a
/// unit of work -- of its own, because the checkout service commits once per instance; one failing
/// doesn't hold up the rest.
/// </summary>
public sealed class PaymentReconcileService(IServiceScopeFactory scopes)
{
    public async Task<ReconcileSummary> RunAsync(Guid? ownerUserId, CancellationToken token)
    {
        IReadOnlyList<Guid> ids;
        using (var listScope = scopes.CreateScope())
        {
            ids = await listScope.ServiceProvider.GetRequiredService<SubscriptionCheckoutService>()
                .ListReconcilableAsync(ownerUserId, token);
        }

        int settled = 0, refunds = 0;
        foreach (var id in ids)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var scope = scopes.CreateScope();
                var result = await scope.ServiceProvider.GetRequiredService<SubscriptionCheckoutService>().ReconcileAsync(id, token);
                if (result == ReconcileResult.Settled) settled++;
                else if (result == ReconcileResult.RefundFlagged) refunds++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Logger.Error(ex, "Payment reconcile: checkout {CheckoutId} failed", id);
            }
        }
        if (settled + refunds > 0)
            Log.Logger.Warning("Payment reconcile: {Settled} missed payment(s) applied, {Refunds} missed refund(s) flagged", settled, refunds);
        return new ReconcileSummary(ids.Count, settled, refunds);
    }
}

/// <summary>The Quartz job running <see cref="PaymentReconcileService"/> every few minutes (see SubscriptionRegistrations).</summary>
[DisallowConcurrentExecution]
public sealed class PaymentReconcileJob(PaymentReconcileService reconcile) : IJob
{
    public static readonly JobKey Key = new("payment-reconcile", "tenantstore");
    public static readonly TriggerKey TriggerKey = new("payment-reconcile-trigger", "tenantstore");
    public const string DefaultCron = "0 0/15 * * * ?";

    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            await reconcile.RunAsync(null, context.CancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Logger.Error(ex, "Payment reconcile: run failed");
        }
    }
}
