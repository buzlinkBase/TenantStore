using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantStoreApi.Domain.Entities;

namespace TenantStoreApi.Infrastructure.EntityConfigs
{
    public class TenantConfig : IEntityTypeConfiguration<SchemaVersion>
    {
        public void Configure(EntityTypeBuilder<SchemaVersion> builder)
        {
            builder.HasOne(d => d.Tenant)
                    .WithMany(p => p.SchemaVersions)
                    .HasForeignKey(d => d.TenantId)
                    .OnDelete(DeleteBehavior.Cascade);
            builder.HasIndex(e => new { e.TenantId, e.System }).IsUnique();
        }
    }
}
