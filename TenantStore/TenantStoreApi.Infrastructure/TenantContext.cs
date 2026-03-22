using BuzlinkRepository;
using MassTransit;
using Microsoft.EntityFrameworkCore; 
using TenantStoreApi.Domain.Entities;
using TenantStoreApi.Domain.Entities.Subs;

namespace TenantStoreApi.Infrastructure;

public class TenantContext : DbContext
{
    public TenantContext(DbContextOptions<TenantContext> options) : base(options) { }
    public DbSet<ConnectionStringStore> Connections { get; set; }
    public DbSet<Branch> Branches { get; set; }
    public DbSet<Tenant> Tenants { get; set; }
    public DbSet<SchemaVersion> SchemaVersions { get; set; } 
    public DbSet<UserMembership> Memberships  { get; set; }
    public DbSet<TenantDelegation> TenantDelegations { get; set; }

    //plan
    public DbSet<TenantSubscription> TenantSubscriptions { get; set; }
    public DbSet<Plan> Plans { get; set; }
    public DbSet<PlanProduct> PlanServices { get; set; }
    public DbSet<ExtraService> ExtraServices { get; set; }


    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.AddInterceptors(new SoftDeleteInterceptor());
        optionsBuilder.UseLazyLoadingProxies(true);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly);
        modelBuilder.UseDateFilter();

        modelBuilder.Entity<Branch>().HasIndex(x => x.TenantId);
        modelBuilder.Entity<Branch>().HasIndex(x => x.Status);

        modelBuilder.Entity<Tenant>().HasIndex(x => x.UserId);
        modelBuilder.Entity<Tenant>().HasIndex(x => x.Status);

        modelBuilder.Entity<TenantSubscription>().HasIndex(x => x.TenantId);
        modelBuilder.Entity<TenantSubscription>().HasIndex(x => x.PlanId);
        modelBuilder.Entity<TenantSubscription>().HasIndex(x => x.SubStatus);

        modelBuilder.Entity<Plan>().HasIndex(x => x.Name);
        modelBuilder.Entity<PlanProduct>().HasIndex(x => x.PlanId);

        modelBuilder.Entity<UserMembership>().HasIndex(x => x.TenantId);
        modelBuilder.Entity<UserMembership>().HasIndex(x => x.UserId);
        modelBuilder.Entity<UserMembership>().HasIndex(x => x.Role);

        modelBuilder.Entity<TenantDelegation>().HasIndex(x => x.HostTenantId);
        modelBuilder.Entity<TenantDelegation>().HasIndex(x => x.GuestTenantId);


        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();

    }
}
