using BuzlinkRepository;
using Microsoft.EntityFrameworkCore;
using Onepunch.Common.Lib;
using Onepunch.Common.Lib.Entities;
using TenantStoreApi.Domain.Entities;

namespace TenantStoreApi.Infrastructure;

public class TenantContext : DbContext
{
    public Guid? TenantId { get; private set; }
    public TenantContext(DbContextOptions<TenantContext> options) : base(options)
    {
    }

    public TenantContext(DbContextOptions<TenantContext> options, ITenantProvider provider) : base(options)
    {
        TenantId = provider.TenantId;
    }
   

    public DbSet<TenantConnection> Connections { get; set; }
    public DbSet<Branch> Branches { get; set; }
    public DbSet<Tenant> Tenants { get; set; }
    public DbSet<ApiToken> Tokens { get; set; }
    public DbSet<OutboxMessage> OutboxMessages { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly);
        if (TenantId != null)
        {
            modelBuilder.UseSoftDelete(TenantId.Value);
        }
        modelBuilder.Entity<Tenant>().HasIndex(x => x.Email);
        modelBuilder.Entity<Tenant>().HasIndex(x => x.Token);
        modelBuilder.Entity<Tenant>().HasIndex(x => x.Status);
        modelBuilder.Entity<Branch>().HasIndex(x => x.TenantId);
        modelBuilder.Entity<Branch>().HasIndex(x => x.Status);

        modelBuilder.Entity<OutboxMessage>().HasIndex(x => x.TenantId);
        modelBuilder.Entity<OutboxMessage>().HasIndex(x => x.ProcessedOn);
        modelBuilder.Entity<OutboxMessage>().HasIndex(x => x.Status);
        modelBuilder.Entity<OutboxMessage>().HasIndex(x => x.RetryCount);
        modelBuilder.Entity<OutboxMessage>().HasIndex(x => x.NextRetryOn);
        modelBuilder.Entity<OutboxMessage>().HasIndex(x => x.Topic);
        modelBuilder.Entity<OutboxMessage>().HasIndex(x => x.Key);
        modelBuilder.Entity<OutboxMessage>()
       .Property(x => x.Status)
        .HasConversion(
              v => v.ToString(),
              v => EnumParserConfig.SafeParseEnum(v, OutBoxState.PENDING)
          );

        modelBuilder.Entity<ApiToken>()
      .Property(x => x.Status)
       .HasConversion(
             v => v.ToString(),
             v => EnumParserConfig.SafeParseEnum(v, Domain.TokenStatus.Revoke)
         );
    } 
}
