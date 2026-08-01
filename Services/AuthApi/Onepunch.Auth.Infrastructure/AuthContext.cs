using BuzlinkRepository;
using MassTransit;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
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
        modelBuilder.Entity<User>().HasIndex(x => x.Email).IsUnique();

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
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
