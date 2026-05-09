using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Onepunch.Auth.Domain.Entities;
using Onepunch.Common.Lib;

namespace Onepunch.Auth.Infrastructure.EFConfig;

internal class ApiTokenConfig : IEntityTypeConfiguration<ApiToken>
{
    public void Configure(EntityTypeBuilder<ApiToken> builder)
    {
        builder 
        .Property(x => x.Status)
        .HasConversion(
                v => v.ToString(),
                v => EnumParserConfig.SafeParseEnum(v, Domain.TokenStatus.Revoke)
            );
    }
}

