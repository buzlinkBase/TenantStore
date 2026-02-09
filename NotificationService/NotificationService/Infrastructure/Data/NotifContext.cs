 
using Onepunch.Common.Lib.Entities;

namespace OnePunch.Notification.Infrastructure.Data
{
    public class NotifContext : DbContext
    {
        public NotifContext(DbContextOptions<NotifContext> options) : base(options) { }

        public DbSet<OutboxMessage> OutboxMessages { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            //outbox
            modelBuilder.Entity<OutboxMessage>()
             .HasIndex(x => x.TenantId)
             ;

            modelBuilder.Entity<OutboxMessage>()
              .HasIndex(x => x.EventId)
              ;

            modelBuilder.Entity<OutboxMessage>()
              .HasIndex(x => x.Status)
              ;

            modelBuilder.Entity<OutboxMessage>()
           .Property(x => x.Status)
            .HasConversion(
                  v => v.ToString(),
                  v => EnumParserConfig.SafeParseEnum(v, OutBoxState.PENDING)
              );

            modelBuilder.Entity<OutboxMessage>()
               .Property(x => x.Status)
            .HasConversion(
                  v => v.ToString(),
                  v => EnumParserConfig.SafeParseEnum(v, OutBoxState.PENDING)
              );

            base.OnModelCreating(modelBuilder);
        }
    }
}
