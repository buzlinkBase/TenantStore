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
    public DbSet<Industry> Industries { get; set; }
    public DbSet<SubIndustry> SubIndustries { get; set; }
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
    public DbSet<AccountDeletionRequest> AccountDeletionRequests { get; set; }
    public DbSet<PlatformSetting> PlatformSettings { get; set; }
    public DbSet<Permission> Permissions { get; set; }
    public DbSet<Role> Roles { get; set; }
    public DbSet<RolePermission> RolePermissions { get; set; }
    public DbSet<ClientEntity> ClientEntities { get; set; }
    public DbSet<ClientMembershipRole> ClientMembershipRoles { get; set; }
    public DbSet<LegalDocument> LegalDocuments { get; set; }
    public DbSet<TermsAcceptance> TermsAcceptances { get; set; }

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
        modelBuilder.Entity<Tenant>().Property(x => x.IndustryCode).HasMaxLength(64);
        modelBuilder.Entity<Tenant>().Property(x => x.SubIndustryCode).HasMaxLength(64);
        modelBuilder.Entity<Tenant>().Property(x => x.BusinessType).HasMaxLength(32);
        modelBuilder.Entity<Tenant>().Property(x => x.CompanySize).HasMaxLength(32);
        modelBuilder.Entity<Tenant>().Property(x => x.AcquisitionSource).HasMaxLength(32);
        modelBuilder.Entity<Tenant>().Property(x => x.AcquisitionSourceOther).HasMaxLength(200);
        modelBuilder.Entity<Tenant>().HasIndex(x => x.IndustryCode);

        modelBuilder.Entity<Plan>().HasIndex(x => x.Name);

        modelBuilder.Entity<UserMembership>().HasIndex(x => x.TenantId);
        modelBuilder.Entity<UserMembership>().HasIndex(x => x.UserId);
        modelBuilder.Entity<UserMembership>().Property(x => x.Category)
            .HasMaxLength(32)
            .HasDefaultValue(MembershipCategory.InternalEmployee);
        modelBuilder.Entity<UserMembership>().HasIndex(x => new { x.TenantId, x.Category });

        modelBuilder.Entity<ClientEntity>().Property(x => x.Name).HasMaxLength(256);
        modelBuilder.Entity<ClientEntity>().Property(x => x.ExternalId).HasMaxLength(64);
        modelBuilder.Entity<ClientEntity>().HasIndex(x => x.TenantId);
        modelBuilder.Entity<ClientEntity>().HasIndex(x => new { x.TenantId, x.ExternalId });

        modelBuilder.Entity<ClientMembershipRole>()
            .HasOne(x => x.UserMembership)
            .WithMany(x => x.ClientRoles)
            .HasForeignKey(x => x.UserMembershipId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ClientMembershipRole>()
            .HasOne(x => x.ClientEntity)
            .WithMany()
            .HasForeignKey(x => x.ClientEntityId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ClientMembershipRole>()
            .HasOne(x => x.Role)
            .WithMany()
            .HasForeignKey(x => x.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ClientMembershipRole>()
            .HasIndex(x => new { x.UserMembershipId, x.ClientEntityId, x.RoleId }).IsUnique();
        modelBuilder.Entity<ClientMembershipRole>().HasIndex(x => x.ClientEntityId);

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
        modelBuilder.Entity<Role>().Property(x => x.Audience)
            .HasMaxLength(16)
            .HasDefaultValue(RoleAudience.Staff);
        modelBuilder.Entity<Role>().HasIndex(x => x.ClientEntityId);
        // A client's custom roles live in their own namespace: "Finance" for one client doesn't
        // collide with the agency's "Finance" or another client's.
        modelBuilder.Entity<Role>().HasIndex(x => new { x.TenantId, x.ClientEntityId, x.Name }).IsUnique();
        modelBuilder.Entity<Permission>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<Permission>().Property(x => x.Product).HasMaxLength(50);
        modelBuilder.Entity<Permission>().HasIndex(x => x.Product);
        modelBuilder.Entity<Role>().Property(x => x.Product).HasMaxLength(50);

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
