using MassTransit;
using Microsoft.EntityFrameworkCore.Design;

namespace OnePunch.Notification.Infrastructure;

/// <summary>
/// Used only by the EF tools (migrations / database update). Reads the connection string the
/// same places the service does, never from the committed appsettings.json, so no real database
/// credentials have to live in the repo:
///   1. appsettings.Development.json (local database)
///   2. this project's user secrets (dotnet user-secrets ...)
///   3. environment variables (ConnectionStrings__NotifConnection)
/// Later sources win.
/// </summary>
public class NotifContextFactory : IDesignTimeDbContextFactory<NotifContext>
{
    public NotifContext CreateDbContext(string[] args)
    {
        var configBuilder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.Development.json", optional: true);
        // Same project as the service itself, so its own user secrets are reachable directly.
        configBuilder.AddUserSecrets(typeof(Program).Assembly, optional: true);
        var configuration = configBuilder.AddEnvironmentVariables().Build();

        var connectionString = configuration.GetConnectionString("NotifConnection");
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException(
                "Could not find 'NotifConnection'. Set it in appsettings.Development.json, the user secrets " +
                "(ConnectionStrings:NotifConnection) or the ConnectionStrings__NotifConnection env var.");
        }

        var optionsBuilder = new DbContextOptionsBuilder<NotifContext>();
        var sqlVersion = new MySqlServerVersion(new Version(8, 4, 8));
        optionsBuilder.UseMySql(connectionString, sqlVersion);
        return new NotifContext(optionsBuilder.Options);
    }
}
