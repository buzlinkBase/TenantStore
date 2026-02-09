using Adms.api.Domain.Entties;
using Bogus;
using Microsoft.EntityFrameworkCore;

namespace Adms.api.Infrastructure;

public class AdmsContext : DbContext
{
    public AdmsContext(DbContextOptions<AdmsContext> options) : base(options)
    {
    }
    public DbSet<Attendance> Attendances { get; set; }


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {

        var faker = new Faker<Attendance>()
      .RuleFor(x => x.Id, f => Guid.NewGuid())
      .RuleFor(x => x.BioId, f => 1)
      .RuleFor(x => x.IP, f => "127.0.0.1")
      .RuleFor(x => x.Workstate, f => 1)
      .RuleFor(x => x.Status, f => "Active")
      .RuleFor(x => x.TenantId, f => Guid.Parse("F2A115BB-C35F-48D7-B42A-0D5DC5F181A4"))
      .RuleFor(x => x.WorkDateTime, f =>
      {
          // Pick a random date (e.g., within the past month)
          var date = f.Date.Past(1);

          // Randomly decide if this is a morning or evening log
          bool isMorning = f.Random.Bool();

          if (isMorning)
          {
              // Morning log: between 5:00 AM and 6:00 AM
              var hour = f.Random.Int(5, 6);
              var minute = f.Random.Int(0, 59);
              var second = f.Random.Int(0, 59);
              return new DateTime(date.Year, date.Month, date.Day, hour, minute, second);
          }
          else
          {
              // Evening log: between 5:00 PM and 6:59 PM
              var hour = f.Random.Int(17, 18); // 17 = 5 PM, 18 = 6 PM
              var minute = f.Random.Int(0, 59);
              var second = f.Random.Int(0, 59);
              return new DateTime(date.Year, date.Month, date.Day, hour, minute, second);
          }
      });
        ;

        var fakeAttendances = faker.Generate(10);
        modelBuilder.Entity<Attendance>().HasData(fakeAttendances);

        base.OnModelCreating(modelBuilder);

    }

}
