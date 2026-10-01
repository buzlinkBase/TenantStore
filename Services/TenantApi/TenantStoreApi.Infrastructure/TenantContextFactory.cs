using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace TenantStoreApi.Infrastructure;

/// <summary>
/// Used only by the EF tools (migrations / database update). Reads the connection string the
/// same places the API does, never from the committed appsettings.json, so no real database
/// credentials have to live in the repo:
///   1. TenantStoreApi/appsettings.Development.json (local database)
///   2. the API project's user secrets (dotnet user-secrets --project TenantStoreApi ...)
///   3. environment variables (ConnectionStrings__TenantConnection)
/// Later sources win.
/// </summary>
public class TenantContextFactory : IDesignTimeDbContextFactory<TenantContext>
{
    // TenantStoreApi.csproj's <UserSecretsId>. Infrastructure can't reference the API project
    // (the API references Infrastructure), so its secrets are loaded by id.
    private const string ApiUserSecretsId = "4c1f8e2a-9b37-4d6e-a5c2-7f0e3b9d1a64";
    private const string ApiFolder = "TenantStoreApi";

    public TenantContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(ApiProjectDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddUserSecrets(ApiUserSecretsId)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("TenantConnection");
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException(
                "Could not find 'TenantConnection'. Set it in TenantStoreApi/appsettings.Development.json, the API's " +
                "user secrets (ConnectionStrings:TenantConnection) or the ConnectionStrings__TenantConnection env var.");
        }

        var optionsBuilder = new DbContextOptionsBuilder<TenantContext>();
        var sqlVersion = new MySqlServerVersion(new Version(8, 4, 8));
        optionsBuilder.UseMySql(connectionString, sqlVersion);
        return new TenantContext(optionsBuilder.Options);
    }

    // The EF tools run from this project's or the API project's directory; either way the API's
    // settings sit in the sibling API folder.
    private static string ApiProjectDirectory()
    {
        var currentDirectory = Directory.GetCurrentDirectory();
        var sibling = Path.GetFullPath(Path.Combine(currentDirectory, "..", ApiFolder));
        return Directory.Exists(sibling) ? sibling : currentDirectory;
    }
}
