using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantStoreApi.Domain.Entities.Subs;

namespace TenantStoreApi.Infrastructure.EntityConfigs;

public class AccountSubscriptionConfig : IEntityTypeConfiguration<AccountSubscription>
{
    public void Configure(EntityTypeBuilder<AccountSubscription> builder)
    {
        // One subscription per account -- also what makes the trial once-only.
        builder.HasIndex(x => x.OwnerUserId).IsUnique();
        builder.HasIndex(x => x.SubStatus);
        builder.Property(x => x.SubStatus).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.BillingCycle).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.PendingBillingCycle).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.PendingChangeTiming).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.LockedMonthlyPrice).HasPrecision(18, 2);
        builder.Property(x => x.LockedAnnualPrice).HasPrecision(18, 2);
        builder.Property(x => x.ConcurrencyStamp).IsConcurrencyToken();
        builder.HasOne(x => x.Plan)
            .WithMany()
            .HasForeignKey(x => x.PlanId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class SubscriptionOverrideConfig : IEntityTypeConfiguration<SubscriptionOverride>
{
    public void Configure(EntityTypeBuilder<SubscriptionOverride> builder)
    {
        builder.Property(x => x.Key).HasMaxLength(64);
        builder.Property(x => x.Value).HasMaxLength(64);
        builder.Property(x => x.Reason).HasMaxLength(512);
        builder.Property(x => x.CreatedBy).HasMaxLength(128);
        builder.HasOne(x => x.Subscription)
            .WithMany(x => x.Overrides)
            .HasForeignKey(x => x.SubscriptionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.SubscriptionId, x.TenantId, x.Key });
    }
}

public class SubscriptionDiscountConfig : IEntityTypeConfiguration<SubscriptionDiscount>
{
    public void Configure(EntityTypeBuilder<SubscriptionDiscount> builder)
    {
        builder.Property(x => x.Percent).HasPrecision(5, 2);
        builder.Property(x => x.FixedAmount).HasPrecision(18, 2);
        builder.Property(x => x.Reason).HasMaxLength(512);
        builder.Property(x => x.CreatedBy).HasMaxLength(128);
        builder.HasOne(x => x.Subscription)
            .WithMany()
            .HasForeignKey(x => x.SubscriptionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.SubscriptionId);
    }
}

public class PlanOfferConfig : IEntityTypeConfiguration<PlanOffer>
{
    public void Configure(EntityTypeBuilder<PlanOffer> builder)
    {
        builder.Property(x => x.CreatedBy).HasMaxLength(128);
        builder.HasOne(x => x.Plan)
            .WithMany()
            .HasForeignKey(x => x.PlanId)
            .OnDelete(DeleteBehavior.Cascade);
        // Not unique: revoking soft-deletes, and the same plan can be offered again afterwards.
        builder.HasIndex(x => new { x.PlanId, x.OwnerUserId });
        builder.HasIndex(x => x.OwnerUserId);
    }
}

public class SubscriptionPaymentConfig: IEntityTypeConfiguration<SubscriptionPayment>
{
    public void Configure(EntityTypeBuilder<SubscriptionPayment> builder)
    {
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.Currency).HasMaxLength(8);
        builder.Property(x => x.BillingCycle).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Method).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Reference).HasMaxLength(128);
        builder.Property(x => x.RecordedBy).HasMaxLength(128);
        builder.Property(x => x.Notes).HasMaxLength(1024);
        builder.HasOne(x => x.Subscription)
            .WithMany()
            .HasForeignKey(x => x.SubscriptionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.SubscriptionId, x.PaidAtUtc });
    }
}

public class BillingEventConfig : IEntityTypeConfiguration<BillingEvent>
{
    public void Configure(EntityTypeBuilder<BillingEvent> builder)
    {
        builder.Property(x => x.Type).HasMaxLength(64);
        builder.Property(x => x.DedupeKey).HasMaxLength(191);
        builder.Property(x => x.Actor).HasMaxLength(128);
        builder.HasIndex(x => x.DedupeKey).IsUnique();
        builder.HasIndex(x => new { x.SubscriptionId, x.OccurredAtUtc });
        builder.HasIndex(x => x.OwnerUserId);
    }
}
