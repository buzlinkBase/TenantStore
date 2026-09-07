using Microsoft.EntityFrameworkCore;
using OnePunch.Auth.Core;
using Onepunch.Auth.Infrastructure;

namespace Onepunch.Auth.Api.Tests.TestSupport;

public static class AuthTestContextFactory
{
    public static AuthContext CreateContext(string? databaseName = null)
    {
        var options = new DbContextOptionsBuilder<AuthContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString())
            .Options;
        return new AuthContext(options, new FakeTenantProvider());
    }

    public static IUnitOfWorkService CreateUnitOfWork(AuthContext? context = null) =>
        new UnitOfWorkService(context ?? CreateContext());
}
