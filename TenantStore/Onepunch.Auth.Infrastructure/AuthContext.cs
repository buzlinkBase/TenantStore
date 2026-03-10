using BuzlinkRepository;
using MassTransit;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Onepunch.Auth.Domain.Entities;
using Onepunch.Common.Lib;
using OnePunch.Auth.Domain.Entities;

namespace Onepunch.Auth.Infrastructure;

public class AuthContext : IdentityDbContext<User, Role, Guid>
{
    public AuthContext(DbContextOptions<AuthContext> options, ITenantProvider provider) : base(options)
    {
    }
    public DbSet<EmailToken> EmailTokens { get; set; }
    public DbSet<RefreshToken> RefreshTokens { get; set; }
    public DbSet<UserTenant> UserTenants { get; set; }
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
    }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ApiToken>()
        .Property(x => x.Status)
        .HasConversion(
                v => v.ToString(),
                v => EnumParserConfig.SafeParseEnum(v, Domain.TokenStatus.Revoke)
            );
        modelBuilder.UseSoftDelete(Guid.Empty);
        modelBuilder.Entity<RefreshToken>().HasIndex(x => x.RefreshTokenHash);
        modelBuilder.Entity<User>().HasIndex(x => x.Email).IsUnique();
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
