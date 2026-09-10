using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TenantStoreApi.Core;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Tests.TestSupport;

// Mirrors AuthApi's AuthTestContextFactory idiom -- TenantContext has no auto-tenant-scoping
// query filter and takes no ITenantProvider dependency (every service filters by TenantId
// explicitly), so this factory is simpler than the AuthApi one: just an InMemory TenantContext.
public static class TenantTestContextFactory
{
    public static TenantContext CreateContext(string? databaseName = null)
    {
        var options = new DbContextOptionsBuilder<TenantContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new TenantContext(options);
    }

    public static IUnitOfWorkService CreateUnitOfWork(TenantContext? context = null) =>
        new UnitOfWorkService(context ?? CreateContext());
}
