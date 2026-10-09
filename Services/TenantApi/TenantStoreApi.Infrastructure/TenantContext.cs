using BuzlinkRepository;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Onepunch.AuditEngine.EntityFrameworkCore;
using TenantStoreApi.Domain.Entities;
using TenantStoreApi.Domain.Entities.Subs;

namespace TenantStoreApi.Infrastructure;

public class TenantContext : DbContext
{
    public TenantContext(DbContextOptions<TenantContext> options) : base(options) { }
    public DbSet<ConnectionStringStore> Connections { get; set; }
    public DbSet<Tenant> Tenants { get; set; }
    public DbSet<SchemaVersion> SchemaVersions { get; set; }
    public DbSet<UserMembership> Memberships { get; set; }
    public DbSet<MembershipRole> MembershipRoles { get; set; }
    public DbSet<TenantDelegation> TenantDelegations { get; set; }
    public DbSet<Plan> Plans { get; set; }
    public DbSet<PlanEntitlement> PlanEntitlements { get; set; }
    public DbSet<PlanOffer> PlanOffers { get; set; }
    public DbSet<AccountSubscription> AccountSubscriptions { get; set; }
    public DbSet<SubscriptionOverride> SubscriptionOverrides { get; set; }
    public DbSet<SubscriptionDiscount> SubscriptionDiscounts { get; set; }
    public DbSet<SubscriptionPayment> SubscriptionPayments { get; set; }
    public DbSet<PaymentCheckout> PaymentCheckouts { get; set; }
    public DbSet<StorageAddOnOffering> StorageAddOnOfferings { get; set; }
    public DbSet<StorageAddOnOffer> StorageAddOnOffers { get; set; }
    public DbSet<StorageAddOn> StorageAddOns { get; set; }
    public DbSet<StorageAddOnPayment> StorageAddOnPayments { get; set; }
    public DbSet<BillingEvent> BillingEvents { get; set; }
    public DbSet<PlatformSetting> PlatformSettings { get; set; }
    public DbSet<Permission> Permissions { get; set; }
    public DbSet<Role> Roles { get; set; }
    public DbSet<RolePermission> RolePermissions { get; set; }

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

        modelBuilder.Entity<Tenant>().HasIndex(x => x.UserId);
        modelBuilder.Entity<Tenant>().HasIndex(x => x.Status);
        modelBuilder.Entity<Tenant>().HasIndex(x => new { x.UserId, x.ActivatedAtUtc });

        modelBuilder.Entity<Plan>().HasIndex(x => x.Name);

        modelBuilder.Entity<UserMembership>().HasIndex(x => x.TenantId);
        modelBuilder.Entity<UserMembership>().HasIndex(x => x.UserId);

        modelBuilder.Entity<MembershipRole>()
            .HasOne(x => x.UserMembership)
            .WithMany(x => x.Roles)
            .HasForeignKey(x => x.UserMembershipId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<MembershipRole>().HasIndex(x => new { x.UserMembershipId, x.Role }).IsUnique();

        // Migration A: RoleId is nullable and unconstrained until the backfill (see
        // PermissionCatalogSeederService) is confirmed and a later migration makes it required.
        modelBuilder.Entity<MembershipRole>()
            .HasOne(x => x.RoleRef)
            .WithMany()
            .HasForeignKey(x => x.RoleId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        modelBuilder.Entity<Role>().HasIndex(x => x.TenantId);
        modelBuilder.Entity<Role>().HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
        modelBuilder.Entity<Permission>().HasIndex(x => x.Code).IsUnique();

        modelBuilder.Entity<RolePermission>()
            .HasOne(x => x.Role)
            .WithMany(x => x.RolePermissions)
            .HasForeignKey(x => x.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<RolePermission>()
            .HasOne(x => x.Permission)
            .WithMany()
            .HasForeignKey(x => x.PermissionId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<RolePermission>().HasIndex(x => new { x.RoleId, x.PermissionId }).IsUnique();

        modelBuilder.Entity<TenantDelegation>().HasIndex(x => x.HostTenantId);
        modelBuilder.Entity<TenantDelegation>().HasIndex(x => x.GuestTenantId);


        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();

        // Audit trail of [Audited] entities (memberships, roles, permissions) -- see
        // AddAuditEngine in RegisterSelfServices.
        modelBuilder.ApplyAuditEngineModel();
    }
}
