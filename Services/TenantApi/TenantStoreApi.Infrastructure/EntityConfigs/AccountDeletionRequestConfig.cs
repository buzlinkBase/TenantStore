using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantStoreApi.Domain.Entities.Subs;

namespace TenantStoreApi.Infrastructure.EntityConfigs;

public class AccountDeletionRequestConfig : IEntityTypeConfiguration<AccountDeletionRequest>
{
    public void Configure(EntityTypeBuilder<AccountDeletionRequest> builder)
    {
        builder.Property(x => x.OwnerEmail).HasMaxLength(256);
        builder.Property(x => x.Reason).HasMaxLength(500);
        builder.Property(x => x.RequestStatus).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.RequestedBy).HasMaxLength(256);
        builder.Property(x => x.DecidedBy).HasMaxLength(256);
        builder.Property(x => x.DecisionReason).HasMaxLength(500);
        builder.Property(x => x.CompanyName).HasMaxLength(256);
        builder.Property(x => x.Erasure).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.ErasureError).HasMaxLength(500);
        builder.HasIndex(x => new { x.OwnerUserId, x.RequestStatus });
        builder.HasIndex(x => new { x.TenantId, x.RequestStatus });
        builder.HasIndex(x => new { x.RequestStatus, x.ExpiresAtUtc });
    }
}
