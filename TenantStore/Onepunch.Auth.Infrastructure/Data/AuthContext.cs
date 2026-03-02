using BuzlinkRepository;
using MassTransit;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Onepunch.Auth.Domain.Entities;
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
    public DbSet<RefreshToken> RefreshTokens { get; set; }
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<RefreshToken>().HasIndex(x => x.TenantId);
        modelBuilder.Entity<User>().HasIndex(x => x.TenantId);
        modelBuilder.Entity<User>().HasIndex(x => x.Email).IsUnique(); 
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
