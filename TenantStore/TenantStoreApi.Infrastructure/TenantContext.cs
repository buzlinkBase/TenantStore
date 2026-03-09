using BuzlinkRepository;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Onepunch.Common.Lib;
using TenantStoreApi.Domain.Entities;

namespace TenantStoreApi.Infrastructure;

public class TenantContext : DbContext
{
    private readonly ITenantProvider _tenantProvider;

    public TenantContext(DbContextOptions<TenantContext> options,
        ITenantProvider tenantProvider) : base(options)
    {
        _tenantProvider = tenantProvider;
    }
    public DbSet<TenantConnection> Connections { get; set; }
    public DbSet<Branch> Branches { get; set; }
    public DbSet<Tenant> Tenants { get; set; }
    public DbSet<ApiToken> Tokens { get; set; }
    public DbSet<UserMembership> UserMemberships { get; set; }
    public DbSet<TenantDelegation> TenantDelegations { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {

        var tenantId = _tenantProvider==null ? Guid.Empty :  _tenantProvider.TenantId;
        modelBuilder.UseSoftDelete(tenantId);
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly);
        modelBuilder.Entity<Tenant>().HasQueryFilter(x => x.DeletedAt == null);
        modelBuilder.Entity<Branch>().HasQueryFilter(x => x.DeletedAt == null);

        modelBuilder.Entity<Branch>().HasIndex(x => x.TenantId);
        modelBuilder.Entity<Branch>().HasIndex(x => x.Status);

        modelBuilder.Entity<Tenant>().HasIndex(x => x.AccountId);
        modelBuilder.Entity<Tenant>().HasIndex(x => x.Email);
        modelBuilder.Entity<Tenant>().HasIndex(x => x.ApiToken);
        modelBuilder.Entity<Tenant>().HasIndex(x => x.Status);

        modelBuilder.Entity<UserMembership>().HasIndex(x => x.TenantId);
        modelBuilder.Entity<UserMembership>().HasIndex(x => x.UserId);
        modelBuilder.Entity<UserMembership>().HasIndex(x => x.Status);
        modelBuilder.Entity<TenantDelegation>().HasIndex(x => x.HostTenantId);
        modelBuilder.Entity<TenantDelegation>().HasIndex(x => x.GuestTenantId);
        modelBuilder.Entity<TenantDelegation>().HasIndex(x => x.Status);

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
        modelBuilder.Entity<ApiToken>()
        .Property(x => x.Status)
        .HasConversion(
                v => v.ToString(),
                v => EnumParserConfig.SafeParseEnum(v, Domain.TokenStatus.Revoke)
            );
        base.OnModelCreating(modelBuilder);
    }
}
