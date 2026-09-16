using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Onepunch.Auth.Infrastructure.Data;

public class AuthContextFactory : IDesignTimeDbContextFactory<AuthContext>
{
    public AuthContext CreateDbContext(string[] args)
    {
        string basePath = Path.Combine(Directory.GetCurrentDirectory(), "..", "hrms.auth.api");
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json")
            //.AddJsonFile("appsettings.Development.json")
            .Build(); 
        var connectionString = configuration.GetConnectionString("AuthConnection");
        var optionsBuilder = new DbContextOptionsBuilder<AuthContext>();
        var serverVersion = ServerVersion.AutoDetect(connectionString);//new MySqlServerVersion(new Version(9, 2, 0)
        optionsBuilder.UseMySql(connectionString, serverVersion);
        return new AuthContext(optionsBuilder.Options, null);
    }
}
