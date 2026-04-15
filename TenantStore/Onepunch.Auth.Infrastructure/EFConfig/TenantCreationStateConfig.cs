using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Onepunch.Auth.Domain.Entities;
using Onepunch.Common.Lib;

namespace Onepunch.Auth.Infrastructure.EFConfig;

internal class TenantCreationStateConfig : IEntityTypeConfiguration<TenantCreationRequestStatus>
{
    public void Configure(EntityTypeBuilder<TenantCreationRequestStatus> builder)
    {
        builder
        .Property(x => x.Status)
        .HasConversion(
                v => v.ToString(),
                v => EnumParserConfig.SafeParseEnum(v, TenantCreationStatus.Pending)
            );
    }
}

