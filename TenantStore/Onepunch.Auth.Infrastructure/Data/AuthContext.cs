using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Onepunch.Auth.Domain.Entities;
using Onepunch.Common.Lib;
using Onepunch.Common.Lib.Entities;
using OnePunch.Auth.Domain.Entities;

namespace Onepunch.Auth.Infrastructure.Data;

public class AuthContext : IdentityDbContext<User, Role, Guid>
{
    public AuthContext(DbContextOptions<AuthContext> options)
       : base(options) { }

    public DbSet<EmailToken> EmailTokens { get; set; }
    public DbSet<OutboxMessage> OutboxMessages { get; set; }
    public DbSet<RefreshToken> RefreshTokens { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>()
        .HasIndex(x => x.Email)
        .IsUnique();

        modelBuilder.Entity<User>().HasIndex(x => x.TenantId);
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

public class TenantContextFactory : IDesignTimeDbContextFactory<AuthContext>
{
    public AuthContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AuthContext>();
        //var constr = "server=178.128.122.114;port=3306;database=tenantstore;user=admin;pwd=admin";
        var constr = "server=127.0.0.1;port=3316;database=auth;user=oneuser;password=Pokemon67584321";
        optionsBuilder.UseMySql(constr, ServerVersion.AutoDetect(constr));
        return new AuthContext(optionsBuilder.Options);
    }
}
