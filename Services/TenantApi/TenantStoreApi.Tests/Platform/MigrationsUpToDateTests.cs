using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TenantStoreApi.Infrastructure;
using Xunit;

namespace TenantStoreApi.Tests.Platform;

/// <summary>
/// A table or field change must ship with its migration: until it does, the live database doesn't
/// have it, and the console's permanent deletion (which deletes from every table the model knows)
/// would run statements against tables or columns that aren't there. Built the way the EF tools build
/// the context (MySQL 8.4); no database is contacted.
/// </summary>
public class MigrationsUpToDateTests
{
    [Fact]
    public void TheTenantServiceModel_HasNoChangesWithoutAMigration()
    {
        var options = new DbContextOptionsBuilder<TenantContext>()
            .UseMySql("server=localhost;port=1;database=none;user=none;pwd=none", new MySqlServerVersion(new Version(8, 4, 8)))
            .Options;
        using var db = new TenantContext(options);

        db.Database.HasPendingModelChanges().Should().BeFalse(
            "the TenantContext model changed without a migration -- add one (dotnet ef migrations add) before it ships");
    }
}
