using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantStoreApi.Domain.Entities;

namespace TenantStoreApi.Infrastructure.EntityConfigs;

public class LegalDocumentConfig : IEntityTypeConfiguration<LegalDocument>
{
    public void Configure(EntityTypeBuilder<LegalDocument> builder)
    {
        builder.Property(x => x.DocType).HasMaxLength(32).IsRequired();
        builder.Property(x => x.LegalStatus).HasMaxLength(16).IsRequired();
        builder.Property(x => x.BodyMarkdown).HasColumnType("longtext").IsRequired();
        builder.Property(x => x.RenderedMarkdown).HasColumnType("longtext");
        builder.Property(x => x.ChangeSummary).HasMaxLength(2000);
        builder.Property(x => x.CreatedBy).HasMaxLength(128);
        builder.Property(x => x.UpdatedBy).HasMaxLength(128);
        builder.Property(x => x.PublishedBy).HasMaxLength(128);
        builder.HasIndex(x => new { x.DocType, x.Version }).IsUnique();
        builder.HasIndex(x => new { x.DocType, x.LegalStatus });
    }
}

public class TermsAcceptanceConfig : IEntityTypeConfiguration<TermsAcceptance>
{
    public void Configure(EntityTypeBuilder<TermsAcceptance> builder)
    {
        builder.Property(x => x.UserEmail).HasMaxLength(256);
        builder.Property(x => x.UserName).HasMaxLength(256);
        builder.Property(x => x.IpAddress).HasMaxLength(64);
        builder.Property(x => x.UserAgent).HasMaxLength(512);
        builder.HasIndex(x => new { x.TenantId, x.Version });
        builder.HasIndex(x => x.Version);
    }
}
