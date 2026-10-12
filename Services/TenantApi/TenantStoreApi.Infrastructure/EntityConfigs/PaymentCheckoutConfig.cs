using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantStoreApi.Domain.Entities.Subs;

namespace TenantStoreApi.Infrastructure.EntityConfigs;

public class PaymentCheckoutConfig : IEntityTypeConfiguration<PaymentCheckout>
{
    public void Configure(EntityTypeBuilder<PaymentCheckout> builder)
    {
        builder.Property(x => x.Provider).HasMaxLength(32);
        builder.Property(x => x.ProviderSessionId).HasMaxLength(128);
        builder.Property(x => x.CheckoutUrl).HasMaxLength(1024);
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16).HasDefaultValue(PaymentCheckoutKind.Plan);
        builder.Property(x => x.CheckoutStatus).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.BillingCycle).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.Currency).HasMaxLength(8);
        builder.Property(x => x.LineItemsJson).HasColumnType("longtext");
        builder.Property(x => x.ProviderPaymentId).HasMaxLength(128);
        builder.Property(x => x.ProviderPaymentMethod).HasMaxLength(32);
        builder.Property(x => x.FailureReason).HasMaxLength(512);
        builder.HasOne(x => x.Subscription)
            .WithMany()
            .HasForeignKey(x => x.SubscriptionId)
            .OnDelete(DeleteBehavior.Restrict);
        // A gateway session settles exactly one checkout.
        builder.HasIndex(x => new { x.Provider, x.ProviderSessionId }).IsUnique();
        builder.HasIndex(x => new { x.SubscriptionId, x.CheckoutStatus });
    }
}
