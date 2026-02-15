using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace TenantStoreApi.Infrastructure;

public class TenantContextFactory : IDesignTimeDbContextFactory<TenantContext>
{
    public TenantContext CreateDbContext(string[] args)
    {
        string basePath = Path.Combine(Directory.GetCurrentDirectory(), "..", "TenantStoreApi", "TenantStoreApi");
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.Development.json")
            .Build();
        var connectionString = configuration.GetConnectionString("DbConnection");
        //var connectionString = "server=127.0.0.1;port=3316;database=tenantstore;user=oneuser;pwd=Pokemon67584321";
        var optionsBuilder = new DbContextOptionsBuilder<TenantContext>();
        optionsBuilder.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
        return new TenantContext(optionsBuilder.Options);
    }
}
