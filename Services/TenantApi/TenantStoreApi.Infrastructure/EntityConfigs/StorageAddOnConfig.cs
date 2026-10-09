using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantStoreApi.Domain.Entities.Subs;

namespace TenantStoreApi.Infrastructure.EntityConfigs;

/// <summary>The storage offering every environment starts with: 50 GB for 400 PHP a month, public.
/// Edited through the platform console afterwards; a change here only reaches the database through a migration.</summary>
public static class StorageAddOnSeed
{
    public static readonly Guid Block50GbId = Guid.Parse("5A0E1C3D-2B4F-4A6C-8D7E-0F1A2B3C4D01");

    public static readonly StorageAddOnOffering[] Offerings =
    [
        new()
        {
            Id = Block50GbId,
            Code = "storage-50gb",
            Name = "50 GB storage block",
            Description = "Adds 50 GB of file storage to a company, billed monthly with your plan.",
            SortOrder = 1,
            BlockMb = 50 * 1024,
            MonthlyPrice = 400m,
            Currency = "PHP",
            PricesIncludeVat = true,
            IsPublic = true,
            IsActive = true,
        },
    ];
}

public class StorageAddOnOfferingConfig : IEntityTypeConfiguration<StorageAddOnOffering>
{
    public void Configure(EntityTypeBuilder<StorageAddOnOffering> builder)
    {
        builder.Property(x => x.Code).HasMaxLength(64);
        builder.HasIndex(x => x.Code).IsUnique();
        builder.Property(x => x.Name).HasMaxLength(128);
        builder.Property(x => x.Description).HasMaxLength(512);
        builder.Property(x => x.Currency).HasMaxLength(8);
        builder.Property(x => x.MonthlyPrice).HasPrecision(18, 2);
        builder.HasData(StorageAddOnSeed.Offerings);
    }
}

public class StorageAddOnOfferConfig : IEntityTypeConfiguration<StorageAddOnOffer>
{
    public void Configure(EntityTypeBuilder<StorageAddOnOffer> builder)
    {
        builder.Property(x => x.CreatedBy).HasMaxLength(128);
        builder.HasOne(x => x.Offering)
            .WithMany()
            .HasForeignKey(x => x.OfferingId)
            .OnDelete(DeleteBehavior.Cascade);
        // Not unique: revoking soft-deletes, and the same offering can be offered again afterwards.
        builder.HasIndex(x => new { x.OfferingId, x.OwnerUserId });
        builder.HasIndex(x => x.OwnerUserId);
    }
}

public class StorageAddOnConfig : IEntityTypeConfiguration<StorageAddOn>
{
    public void Configure(EntityTypeBuilder<StorageAddOn> builder)
    {
        builder.Property(x => x.UnitPrice).HasPrecision(18, 2);
        builder.Property(x => x.Reason).HasMaxLength(512);
        builder.Property(x => x.CreatedBy).HasMaxLength(128);
        builder.HasOne(x => x.Subscription)
            .WithMany(x => x.StorageAddOns)
            .HasForeignKey(x => x.SubscriptionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Offering)
            .WithMany()
            .HasForeignKey(x => x.OfferingId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.SubscriptionId, x.TenantId });
    }
}

public class StorageAddOnPaymentConfig : IEntityTypeConfiguration<StorageAddOnPayment>
{
    public void Configure(EntityTypeBuilder<StorageAddOnPayment> builder)
    {
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.Currency).HasMaxLength(8);
        builder.Property(x => x.Method).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Reference).HasMaxLength(128);
        builder.Property(x => x.RecordedBy).HasMaxLength(128);
        builder.HasIndex(x => new { x.SubscriptionId, x.PaidAtUtc });
    }
}
