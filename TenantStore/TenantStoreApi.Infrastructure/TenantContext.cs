using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Onepunch.Common.Lib;
using Onepunch.Common.Lib.Entities;
using TenantStoreApi.Domain.Entities;

namespace TenantStoreApi.Infrastructure;

public class TenantContext : DbContext
{
    public TenantContext(DbContextOptions<TenantContext> options) : base(options) { }
    public DbSet<TenantConnection> Connections { get; set; }
    public DbSet<Branch> Branches { get; set; }
    public DbSet<Tenant> Tenants { get; set; }
    public DbSet<ApiToken> Tokens  { get; set; }
    public DbSet<OutboxMessage> OutboxMessages { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>().HasIndex(x => x.Email);
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
        base.OnModelCreating(modelBuilder);
    }
}

public class TenantContextFactory : IDesignTimeDbContextFactory<TenantContext>
{
    public TenantContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<TenantContext>();
        //var constr = "server=178.128.122.114;port=3306;database=tenantstore;user=admin;pwd=admin";
        var constr = "server=127.0.0.1;port=3316;database=tenantstore;user=oneuser;password=Pokemon67584321";
        optionsBuilder.UseMySql(constr, ServerVersion.AutoDetect(constr));
        return new TenantContext(optionsBuilder.Options);
    }
}