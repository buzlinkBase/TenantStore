using MassTransit;
using Microsoft.EntityFrameworkCore;
namespace TenantStoreApi.Infrastructure;

public class MContext : DbContext
{
    public MContext(DbContextOptions<TenantContext> options) : base(options) { }
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
    }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }  
}
