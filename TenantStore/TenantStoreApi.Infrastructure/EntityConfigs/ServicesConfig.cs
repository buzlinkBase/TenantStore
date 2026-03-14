using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Onepunch.Common.Lib;
using TenantStoreApi.Domain.Entities;

namespace TenantStoreApi.Infrastructure.EntityConfigs;

public class ServicesConfig : IEntityTypeConfiguration<PlanProduct>
{
    public void Configure(EntityTypeBuilder<PlanProduct> builder)
    {
        builder.Property(x => x.ServiceType)
       .HasConversion(
             v => v.ToString(),
             v => EnumParserConfig.SafeParseEnum(v, ServiceType.None)
         );
    }
}
