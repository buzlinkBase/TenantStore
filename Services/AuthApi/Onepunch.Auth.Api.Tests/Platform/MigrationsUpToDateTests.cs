using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Onepunch.Auth.Infrastructure;
using Xunit;

namespace Onepunch.Auth.Api.Tests.Platform;

/// <summary>
/// A table or field change must ship with its migration: until it does, the live database doesn't
/// have it, and the console's permanent deletion would run statements against tables or columns that
/// aren't there. Built the way the EF tools build the contexts (MySQL 8.4); no database is contacted.
/// </summary>
public class MigrationsUpToDateTests
{
    private const string NoDatabase = "server=localhost;port=1;database=none;user=none;pwd=none";
    private static readonly MySqlServerVersion Version = new(new Version(8, 4, 8));

    [Fact]
    public void TheAuthModel_HasNoChangesWithoutAMigration()
    {
        var options = new DbContextOptionsBuilder<AuthContext>().UseMySql(NoDatabase, Version).Options;
        using var db = new AuthContext(options, null!);

        db.Database.HasPendingModelChanges().Should().BeFalse(
            "the AuthContext model changed without a migration -- add one (dotnet ef migrations add) before it ships");
    }
}
