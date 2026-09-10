using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Onepunch.Auth.Infrastructure;
using OnePunch.Auth.Core;

namespace Onepunch.Auth.Api.Tests.TestSupport;

public static class AuthTestContextFactory
{
    public static AuthContext CreateContext(string? databaseName = null)
    {
        var options = new DbContextOptionsBuilder<AuthContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString())
            // The InMemory provider doesn't support transactions — UnitOfWork.CommitChangesAsync
            // wraps its SaveChanges in one, which EF otherwise escalates to a hard error here.
            // This is an InMemory-provider-only accommodation (the real SQL-backed provider
            // genuinely uses the transaction), not a suppressed defect.
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AuthContext(options, new FakeTenantProvider());
    }

    public static IUnitOfWorkService CreateUnitOfWork(AuthContext? context = null) =>
        new UnitOfWorkService(context ?? CreateContext());
}
