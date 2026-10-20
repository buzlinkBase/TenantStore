using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantStoreApi.Domain.Entities.Subs;

namespace TenantStoreApi.Infrastructure.EntityConfigs;

public class AutoRenewMandateConfig : IEntityTypeConfiguration<AutoRenewMandate>
{
    public void Configure(EntityTypeBuilder<AutoRenewMandate> builder)
    {
        builder.Ignore(x => x.IsLive);
        builder.Property(x => x.Target).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.BillingCycle).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Provider).HasMaxLength(32);
        builder.Property(x => x.ProviderCustomerId).HasMaxLength(128);
        builder.Property(x => x.ProviderSubscriptionId).HasMaxLength(128);
        builder.Property(x => x.ProviderPlanId).HasMaxLength(128);
        builder.Property(x => x.PaymentMethodLabel).HasMaxLength(64);
        builder.Property(x => x.CancelReason).HasMaxLength(512);
        builder.Property(x => x.CanceledBy).HasMaxLength(128);
        builder.Property(x => x.LastFailureReason).HasMaxLength(512);
        builder.Property(x => x.CreatedBy).HasMaxLength(128);
        // Webhooks find their mandate by the gateway subscription.
        builder.HasIndex(x => new { x.Provider, x.ProviderSubscriptionId }).IsUnique();
        builder.HasIndex(x => new { x.SubscriptionId, x.Target, x.Status });
        builder.HasIndex(x => x.OwnerUserId);
    }
}
