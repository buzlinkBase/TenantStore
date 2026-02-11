 
namespace OnePunch.Notification.Infrastructure.Data
{
    public class NotifContext : DbContext
    {
        public NotifContext(DbContextOptions<NotifContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
        }
    }
}
