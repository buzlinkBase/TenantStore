using BuzlinkRepository;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Onepunch.Auth.Domain.Entities;
using Onepunch.Common.Lib;
using Onepunch.Common.Lib.Entities;
using OnePunch.Auth.Domain.Entities;

namespace Onepunch.Auth.Infrastructure.Data;

public class AuthContext : IdentityDbContext<User, Role, Guid>
{
    public Guid? TenantId { get; private set; }
    public AuthContext(DbContextOptions<AuthContext> options) : base(options)
    {
    }

    public AuthContext(DbContextOptions<AuthContext> options, ITenantProvider provider) : base(options)
    {
        TenantId = provider.TenantId;
    }

    public DbSet<EmailToken> EmailTokens { get; set; }
    public DbSet<OutboxMessage> OutboxMessages { get; set; }
    public DbSet<RefreshToken> RefreshTokens { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RefreshToken>().HasIndex(x => x.TenantId);
        modelBuilder.Entity<User>().HasIndex(x => x.TenantId);
        modelBuilder.Entity<User>().HasIndex(x => x.Email).IsUnique();
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
        base.OnModelCreating(modelBuilder);
    }
}
