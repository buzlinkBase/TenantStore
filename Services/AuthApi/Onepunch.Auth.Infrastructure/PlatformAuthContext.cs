using Microsoft.EntityFrameworkCore;
using Onepunch.Auth.Domain.Entities;

namespace Onepunch.Auth.Infrastructure;

/// <summary>
/// The Platform Admin Console's identity store -- operators, their MFA state and sessions. A
/// separate context (and migrations history) from <see cref="AuthContext"/> on purpose: it can live
/// in its own database via <c>ConnectionStrings:PlatformAuthConnection</c>, and it never shares a
/// table with tenant users.
///
/// Entities are configured inline rather than with IEntityTypeConfiguration classes:
/// AuthContext applies every configuration in this assembly, so a platform configuration class
/// would silently leak these tables into the tenant model.
/// </summary>
public class PlatformAuthContext : DbContext
{
    /// <summary>Own history table, so these migrations don't collide with AuthContext's when both share a database.</summary>
    public const string MigrationsHistoryTable = "__PlatformAuthMigrationsHistory";

    public PlatformAuthContext(DbContextOptions<PlatformAuthContext> options) : base(options)
    {
    }

    public DbSet<PlatformUser> PlatformUsers { get; set; }
    public DbSet<PlatformRefreshToken> PlatformRefreshTokens { get; set; }
    public DbSet<PlatformAdminAudit> PlatformAdminAudits { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<PlatformUser>(user =>
        {
            user.ToTable("PlatformUsers");
            user.HasKey(x => x.Id);
            user.HasIndex(x => x.NormalizedEmail).IsUnique();
            user.Property(x => x.Email).HasMaxLength(256).IsRequired();
            user.Property(x => x.NormalizedEmail).HasMaxLength(256).IsRequired();
            user.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            user.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired();
            user.Property(x => x.Role).HasMaxLength(50).IsRequired();
            user.Property(x => x.SecurityStamp).HasMaxLength(64).IsRequired();
            user.Property(x => x.MfaSecretProtected).HasMaxLength(1000);
            user.Property(x => x.CreatedBy).HasMaxLength(256);
        });

        modelBuilder.Entity<PlatformAdminAudit>(audit =>
        {
            audit.ToTable("PlatformAdminAudits");
            audit.HasKey(x => x.Id);
            audit.HasIndex(x => x.CreatedUtc);
            audit.HasIndex(x => x.TargetId);
            audit.Property(x => x.ActorEmail).HasMaxLength(256).IsRequired();
            audit.Property(x => x.Action).HasMaxLength(64).IsRequired();
            audit.Property(x => x.TargetEmail).HasMaxLength(256);
            audit.Property(x => x.Detail).HasMaxLength(500);
        });

        modelBuilder.Entity<PlatformRefreshToken>(token =>
        {
            token.ToTable("PlatformRefreshTokens");
            token.HasKey(x => x.Id);
            token.HasIndex(x => x.TokenHash).IsUnique();
            token.HasIndex(x => x.UserId);
            token.Property(x => x.TokenHash).HasMaxLength(100).IsRequired();
            token.Property(x => x.RevokedReason).HasMaxLength(100);
            token.HasOne<PlatformUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
