using MassTransit;
using Microsoft.EntityFrameworkCore.Design;
using System.Diagnostics;

namespace OnePunch.Notification.Infrastructure;

public class NotifContextFactory : IDesignTimeDbContextFactory<NotifContext>
{
    public NotifContext CreateDbContext(string[] args)
    {
        string basePath = Path.Combine(Directory.GetCurrentDirectory());
        Debug.WriteLine(basePath);
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json")
            //.AddJsonFile("appsettings.Development.json")
            .Build();
        var connectionString = configuration.GetConnectionString("NotifConnection");
        var optionsBuilder = new DbContextOptionsBuilder<NotifContext>();
        optionsBuilder.UseMySql(connectionString, new MySqlServerVersion(new Version(9, 2, 0)));
        return new NotifContext(optionsBuilder.Options);
    }
}