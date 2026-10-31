using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantStoreApi.Domain.Entities.Subs;

namespace TenantStoreApi.Infrastructure.EntityConfigs;

public class PurgedCompanyArchiveConfig : IEntityTypeConfiguration<PurgedCompanyArchive>
{
    public void Configure(EntityTypeBuilder<PurgedCompanyArchive> builder)
    {
        builder.Property(x => x.CompanyName).HasMaxLength(256);
        builder.Property(x => x.ArchiveKey).HasMaxLength(512);
        builder.Property(x => x.ArchiveSha256).HasMaxLength(64);
        builder.HasIndex(x => new { x.SubscriptionId, x.TenantId }).IsUnique();
        builder.HasIndex(x => x.OwnerUserId);
        builder.HasIndex(x => x.PurgedAtUtc);
    }
}

public class AccountClosureRequestConfig : IEntityTypeConfiguration<AccountClosureRequest>
{
    public void Configure(EntityTypeBuilder<AccountClosureRequest> builder)
    {
        builder.Property(x => x.OwnerEmail).HasMaxLength(256);
        builder.Property(x => x.Reason).HasMaxLength(500);
        builder.Property(x => x.RequestStatus).HasConversion<string>().HasMaxLength(24);
        builder.Property(x => x.ConfirmTokenHash).HasMaxLength(64);
        builder.Property(x => x.CancelTokenHash).HasMaxLength(64);
        builder.Property(x => x.DecidedBy).HasMaxLength(256);
        builder.Property(x => x.DecisionReason).HasMaxLength(500);
        builder.HasIndex(x => new { x.OwnerUserId, x.RequestStatus });
        builder.HasIndex(x => x.ConfirmTokenHash);
        builder.HasIndex(x => x.CancelTokenHash);
    }
}

public class ArchiveDestroyRequestConfig : IEntityTypeConfiguration<ArchiveDestroyRequest>
{
    public void Configure(EntityTypeBuilder<ArchiveDestroyRequest> builder)
    {
        builder.Property(x => x.OwnerEmail).HasMaxLength(256);
        builder.Property(x => x.Reason).HasMaxLength(500);
        builder.Property(x => x.RequestStatus).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.RequestedBy).HasMaxLength(256);
        builder.Property(x => x.DecidedBy).HasMaxLength(256);
        builder.Property(x => x.DecisionReason).HasMaxLength(500);
        builder.HasIndex(x => new { x.OwnerUserId, x.RequestStatus });
    }
}
