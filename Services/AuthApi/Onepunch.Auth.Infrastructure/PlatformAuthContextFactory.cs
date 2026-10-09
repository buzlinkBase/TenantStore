using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Onepunch.Auth.Infrastructure.Data;

/// <summary>
/// Used only by the EF tools, to create migrations for <see cref="PlatformAuthContext"/>:
///   dotnet ef migrations add Initial --context PlatformAuthContext --output-dir Migrations/Platform
/// Reads the connection string like <see cref="AuthContextFactory"/> does (appsettings.Development.json,
/// the API's user secrets, then environment variables). <c>PlatformAuthConnection</c> wins; without it
/// the platform tables go in the same database as <c>AuthConnection</c>, which is what the API does too.
/// </summary>
public class PlatformAuthContextFactory : IDesignTimeDbContextFactory<PlatformAuthContext>
{
    private const string ApiUserSecretsId = "6ab3ffce-21b1-49ae-b1ea-b5cc937f2e9b";
    private const string ApiFolder = "hrms.auth.api";

    public PlatformAuthContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(ApiProjectDirectory())
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddUserSecrets(ApiUserSecretsId)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("PlatformAuthConnection")
            ?? configuration.GetConnectionString("AuthConnection");
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException(
                "Could not find 'PlatformAuthConnection' or 'AuthConnection'. Set one in hrms.auth.api/appsettings.Development.json, " +
                "the API's user secrets or the ConnectionStrings__PlatformAuthConnection env var.");
        }

        var optionsBuilder = new DbContextOptionsBuilder<PlatformAuthContext>();
        optionsBuilder.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 4, 8)),
            mysql => mysql.MigrationsHistoryTable(PlatformAuthContext.MigrationsHistoryTable));
        return new PlatformAuthContext(optionsBuilder.Options);
    }

    private static string ApiProjectDirectory()
    {
        var currentDirectory = Directory.GetCurrentDirectory();
        var sibling = Path.GetFullPath(Path.Combine(currentDirectory, "..", ApiFolder));
        return Directory.Exists(sibling) ? sibling : currentDirectory;
    }
}
