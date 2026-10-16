using BuzlinkRepository;
using MassTransit;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Onepunch.AuditEngine.EntityFrameworkCore;
using Onepunch.Auth.Domain.Entities;
using OnePunch.Auth.Domain.Entities;

namespace Onepunch.Auth.Infrastructure;

public class AuthContext : IdentityDbContext<User, Role, Guid>
{
    public AuthContext(DbContextOptions<AuthContext> options, ITenantProvider provider) : base(options)
    {
    }
    public DbSet<EmailToken> EmailTokens { get; set; }
    public DbSet<RefreshToken> RefreshTokens { get; set; }
    public DbSet<Invitation> Invitations { get; set; }
    public DbSet<InvitationBatch> InvitationBatches { get; set; }
    public DbSet<TenantCreationRequestStatus> TenantCreationRequests { get; set; }
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
    }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly);
        modelBuilder.UseDateFilter();
        modelBuilder.Entity<RefreshToken>().HasIndex(x => x.RefreshTokenHash);
        // "When was this company last signed in to" (Platform Console) groups sessions by company, newest first.
        modelBuilder.Entity<RefreshToken>().HasIndex(x => new { x.TenantId, x.CreatedAt });
        modelBuilder.Entity<User>().HasIndex(x => x.Email).IsUnique();
        modelBuilder.Entity<User>().Property(x => x.Origin).HasMaxLength(24).HasDefaultValue(AccountOrigins.Unknown);
        modelBuilder.Entity<User>().Property(x => x.OriginDetail).HasMaxLength(128);
        // Read by the platform console's acquisition page: accounts created in a date range.
        modelBuilder.Entity<User>().HasIndex(x => x.CreatedAtUtc);
        // One stored result per company + idempotency key (see InvitationService.SendBatchAsync).
        modelBuilder.Entity<InvitationBatch>().HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
        modelBuilder.Entity<InvitationBatch>().Property(x => x.IdempotencyKey).HasMaxLength(100);

        var stringListConverter = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<List<string>, string>(
            v => string.Join(',', v),
            v => string.IsNullOrEmpty(v) ? new List<string>() : v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList());
        var stringListComparer = new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<string>>(
            (a, b) => (a ?? new()).SequenceEqual(b ?? new()),
            v => v.Aggregate(0, (hash, s) => HashCode.Combine(hash, s.GetHashCode())),
            v => v.ToList());

        modelBuilder.Entity<User>()
            .Property(x => x.DefaultTenantRoles)
            .HasConversion(stringListConverter, stringListComparer);
        modelBuilder.Entity<Invitation>()
            .Property(x => x.Roles)
            .HasConversion(stringListConverter, stringListComparer);

        var guidListConverter = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<List<Guid>, string>(
            v => string.Join(',', v),
            v => string.IsNullOrEmpty(v)
                ? new List<Guid>()
                : v.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToList());
        var guidListComparer = new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<Guid>>(
            (a, b) => (a ?? new()).SequenceEqual(b ?? new()),
            v => v.Aggregate(0, (hash, g) => HashCode.Combine(hash, g.GetHashCode())),
            v => v.ToList());
        modelBuilder.Entity<Invitation>()
            .Property(x => x.ClientRoleIds)
            .HasConversion(guidListConverter, guidListComparer);
        modelBuilder.Entity<Invitation>().Property(x => x.Category).HasMaxLength(16).HasDefaultValue(InvitationCategory.Staff);
        modelBuilder.Entity<Invitation>().Property(x => x.ClientName).HasMaxLength(256);
        modelBuilder.Entity<Invitation>().HasIndex(x => new { x.ClientEntityId, x.Status });
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();

        // Audit trail of sign-ins/sign-outs -- see AuthAuditRecorder.
        modelBuilder.ApplyAuditEngineModel();
    }
}
