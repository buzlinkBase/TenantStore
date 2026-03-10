using MassTransit;
using Microsoft.EntityFrameworkCore;
using TenantStoreApi.Domain.Entities;

namespace TenantStoreApi.Infrastructure;

public class TenantContext : DbContext
{
    public TenantContext(DbContextOptions<TenantContext> options) : base(options) { }
    public DbSet<TenantConnection> Connections { get; set; }
    public DbSet<Branch> Branches { get; set; }
    public DbSet<Tenant> Tenants { get; set; }
    public DbSet<UserMembership> UserMemberships { get; set; }
    public DbSet<TenantDelegation> TenantDelegations { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly);
        modelBuilder.Entity<Tenant>().HasQueryFilter(x => x.DeletedAt == null);
        modelBuilder.Entity<Branch>().HasQueryFilter(x => x.DeletedAt == null);
        modelBuilder.Entity<Branch>().HasIndex(x => x.TenantId);
        modelBuilder.Entity<Branch>().HasIndex(x => x.Status);
        modelBuilder.Entity<Tenant>().HasIndex(x => x.UserId);
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
        base.OnModelCreating(modelBuilder);
    }
}
