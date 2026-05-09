using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantStoreApi.Domain.Entities;

namespace TenantStoreApi.Infrastructure.EntityConfigs;

public class ConnectionStringConfig : IEntityTypeConfiguration<ConnectionStringStore>
{
    public void Configure(EntityTypeBuilder<ConnectionStringStore> builder)
    {
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => x.ServiceOwner);
        builder.HasIndex(x => x.IsActive);
    }
}

