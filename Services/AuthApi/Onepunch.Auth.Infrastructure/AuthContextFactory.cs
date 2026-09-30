using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Onepunch.Auth.Infrastructure.Data;

/// <summary>
/// Used only by the EF tools (migrations / database update). Reads the connection string the
/// same places the API does, never from the committed appsettings.json, so no real database
/// credentials have to live in the repo:
///   1. hrms.auth.api/appsettings.Development.json (local database)
///   2. the API project's user secrets (dotnet user-secrets --project hrms.auth.api ...)
///   3. environment variables (ConnectionStrings__AuthConnection)
/// Later sources win.
/// </summary>
public class AuthContextFactory : IDesignTimeDbContextFactory<AuthContext>
{
    // OnePunch.Auth.Api.csproj's <UserSecretsId>. Infrastructure can't reference the API project
    // (the API references Infrastructure), so its secrets are loaded by id.
    private const string ApiUserSecretsId = "6ab3ffce-21b1-49ae-b1ea-b5cc937f2e9b";
    private const string ApiFolder = "hrms.auth.api";

    public AuthContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(ApiProjectDirectory())
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddUserSecrets(ApiUserSecretsId)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("AuthConnection");
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException(
                "Could not find 'AuthConnection'. Set it in hrms.auth.api/appsettings.Development.json, the API's " +
                "user secrets (ConnectionStrings:AuthConnection) or the ConnectionStrings__AuthConnection env var.");
        }

        var optionsBuilder = new DbContextOptionsBuilder<AuthContext>();
        var serverVersion = new MySqlServerVersion(new Version(8, 4, 8));
        optionsBuilder.UseMySql(connectionString, serverVersion);
        return new AuthContext(optionsBuilder.Options, null!);
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
