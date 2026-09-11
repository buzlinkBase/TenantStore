using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace TenantStoreApi.Infrastructure;

public class TenantContextFactory : IDesignTimeDbContextFactory<TenantContext>
{
    public TenantContext CreateDbContext(string[] args)
    {
        string basePath = Path.Combine(Directory.GetCurrentDirectory(), "..", "TenantStoreApi");
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json")
            //.AddJsonFile("appsettings.Development.json")
            .Build();
        var connectionString = configuration.GetConnectionString("TenantConnection");
        var optionsBuilder = new DbContextOptionsBuilder<TenantContext>();
        optionsBuilder.UseMySql(connectionString, new MySqlServerVersion(new Version(9, 2, 0)));
        return new TenantContext(optionsBuilder.Options);
    }
}
