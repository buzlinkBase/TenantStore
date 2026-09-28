using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Onepunch.AuditEngine.EntityFrameworkCore;

public static class EntityFrameworkCoreAuditBuilderExtensions
{
    /// <summary>
    /// Stores the trail in <typeparamref name="TContext"/>'s database and captures its
    /// <see cref="AuditedAttribute"/> entities automatically. The context's model must include the
    /// audit table -- call <see cref="AuditModelBuilderExtensions.ApplyAuditEngineModel"/> from
    /// its <c>OnModelCreating</c>.
    /// </summary>
    public static AuditEngineBuilder UseEntityFrameworkCore<TContext>(this AuditEngineBuilder builder)
        where TContext : DbContext =>
        builder.UseStorage(services =>
        {
            services.AddScoped<EfCoreAuditStore<TContext>>();
            services.AddKeyedScoped<IAuditWriter>(AuditEngineServiceKeys.StorageWriter,
                (sp, _) => sp.GetRequiredService<EfCoreAuditStore<TContext>>());
            services.AddScoped<IAuditChainHead>(sp => sp.GetRequiredService<EfCoreAuditStore<TContext>>());
            services.AddScoped<IAuditReplicaWriter>(sp => sp.GetRequiredService<EfCoreAuditStore<TContext>>());
            services.AddScoped<IAuditQueryService, EfCoreAuditQueryService<TContext>>();

            // Appends to whatever the host's own AddDbContext configures (interceptors accumulate).
            services.ConfigureDbContext<TContext>((sp, options) => options.AddInterceptors(new AuditSaveChangesInterceptor(sp)));
        });
}
