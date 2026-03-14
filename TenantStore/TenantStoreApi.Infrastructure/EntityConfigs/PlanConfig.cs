using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantStoreApi.Domain.Entities;

namespace TenantStoreApi.Infrastructure.EntityConfigs;

public class PlanConfig : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        builder.HasData(
            new Plan()
            {
                Id=Guid.Parse("D8CB5F8B-8E4E-4352-B41C-C311F73B5ED5"),
                Name = "Free Trial",
                Description = "Free Trial",
            });
    }
}

