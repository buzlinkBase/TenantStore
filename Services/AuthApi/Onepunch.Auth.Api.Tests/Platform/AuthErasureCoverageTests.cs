using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Onepunch.Auth.Api.Tests.TestSupport;
using Onepunch.Auth.Core.Platform;
using Onepunch.Auth.Core.Services;
using Onepunch.Auth.Domain.Entities;
using Onepunch.Auth.Infrastructure;
using Onepunch.Common.Lib.Cache;
using OnePunch.Auth.Core;
using OnePunch.Auth.Core.Hubs;
using OnePunch.Auth.Core.Services;
using OnePunch.Auth.Domain.Entities;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace Onepunch.Auth.Api.Tests.Platform;

/// <summary>
/// The safety net for the console's permanent deletion in Auth Service: one row of EVERY table is
/// seeded for an erased login and for someone else (read from the EF model, on a real SQLite database,
/// so the database's own cascades run too), the real set-based eraser runs, and every row of the
/// erased login and company must be gone while the other's stay.
/// <para>If this fails after you added an entity: teach <see cref="AccountEraserService"/> to delete
/// it -- or, if it truly holds no account data, add it to <see cref="GlobalEntities"/> with a reason.</para>
/// </summary>
public class AuthErasureCoverageTests : IDisposable
{
    /// <summary>Shared by every account; never erased.</summary>
    private static readonly Type[] GlobalEntities =
    [
        typeof(Role),                                                   // the global roles
        typeof(Microsoft.AspNetCore.Identity.IdentityRoleClaim<Guid>), // and their claims
    ];

    private static readonly HashSet<string> MessagingTables = ["InboxState", "OutboxMessage", "OutboxState"];

    private static readonly Login Erased = new(Guid.NewGuid(), Guid.NewGuid(), "gone@acme.ph");
    private static readonly Login Other = new(Guid.NewGuid(), Guid.NewGuid(), "kept@beta.ph");

    private sealed record Login(Guid User, Guid Company, string Email)
    {
        public SeedUnit Unit => new(
            new Dictionary<string, object>
            {
                ["UserId"] = User,
                ["InvitedByUserId"] = User,
                ["TenantId"] = Company,
                ["OrganizationId"] = Company.ToString(),
                ["Email"] = Email,
            },
            new Dictionary<string, object> { ["AspNetUsers"] = User });
    }

    private readonly SqliteConnection _keepAlive;
    private readonly DbContextOptions<AuthContext> _options;

    public AuthErasureCoverageTests()
    {
        var connectionString = $"Data Source=file:{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(connectionString);
        _keepAlive.Open();
        _options = new DbContextOptionsBuilder<AuthContext>().UseSqlite(connectionString).Options;
        using var db = NewContext();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _keepAlive.Dispose();

    private AuthContext NewContext() => new(_options, new FakeTenantProvider());

    private static async Task EraseAsync(AuthContext db, ErasureOrder order)
    {
        var client = new Mock<ITenantNotificationClient>();
        var hubClients = new Mock<IHubClients<ITenantNotificationClient>>();
        hubClients.Setup(c => c.User(It.IsAny<string>())).Returns(client.Object);
        var hub = new Mock<IHubContext<TenantHub, ITenantNotificationClient>>();
        hub.Setup(h => h.Clients).Returns(hubClients.Object);
        var platform = new PlatformAuthContext(new DbContextOptionsBuilder<PlatformAuthContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);
        var eraser = new AccountEraserService(new UnitOfWorkService(db), platform, new RelationalRowEraser(),
            new MembershipCacheService(new Mock<ICacheService>().Object, null!, null!), new TenantNotificationService(hub.Object), TimeProvider.System);
        await eraser.EraseAsync(order, CancellationToken.None);
    }

    private static ErasureOrder Order(IReadOnlyList<Guid> delete, IReadOnlyList<Guid> detach) => new(
        AccountEraserService.HardDeletedAction, Guid.NewGuid(), Erased.User, Erased.Email, [Erased.Company], ["Acme"],
        delete, detach, "platform:alice@onepunch.test", "platform:bob@onepunch.test", "Test account");

    [Fact]
    public async Task Erasure_DeletesTheLoginsAndCompanysRowInEveryTable_AndNoOneElses()
    {
        SeededRows rows;
        using (var db = NewContext()) rows = ModelRowSeeder.Seed(db, [Erased.Unit, Other.Unit], MessagingTables);
        rows.Tables.Should().HaveCountGreaterThan(10, "every Auth Service table is seeded");

        await using (var db = NewContext()) await EraseAsync(db, Order([Erased.User], []));

        await using var check = NewContext();
        var global = GlobalEntities.Select(t => check.Model.FindEntityType(t)!.GetTableName()!).ToHashSet();
        rows.Tables.Where(t => !global.Contains(t) && ModelRowSeeder.Exists(check, rows, t, 0))
            .Should().BeEmpty("nothing of an erased login or company may be left -- teach the eraser, or declare the entity global");
        rows.Tables.Where(t => !ModelRowSeeder.Exists(check, rows, t, 1)).Should().BeEmpty("someone else's rows are never touched");
    }

    [Fact]
    public async Task Erasure_TakesEmailOnlyTokens_SentToAnErasedLogin()
    {
        await using (var db = NewContext())
        {
            db.Users.Add(new User { Id = Erased.User, Email = Erased.Email, UserName = Erased.Email });
            db.EmailTokens.AddRange(
                new EmailToken { Id = Guid.NewGuid(), Email = Erased.Email, TokenType = "reset", TokenValue = "a" },
                new EmailToken { Id = Guid.NewGuid(), Email = Other.Email, TokenType = "reset", TokenValue = "b" });
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext()) await EraseAsync(db, Order([Erased.User], []));

        await using var check = NewContext();
        (await check.EmailTokens.Select(t => t.Email).ToListAsync()).Should().Equal(Other.Email);
    }

    [Fact]
    public async Task Erasure_MovesAnyoneStillPointingAtTheErasedCompanyOffIt_EvenIfTenantServiceDidntListThem()
    {
        var unlisted = Guid.NewGuid();
        await using (var db = NewContext())
        {
            db.Users.Add(new User
            {
                Id = unlisted, Email = "invited@acme.ph", UserName = "invited@acme.ph",
                DefaultTenantId = Erased.Company, DefaultTenantName = "Acme", DefaultTenantRoles = ["HR"],
            });
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext()) await EraseAsync(db, Order([Erased.User], []));

        await using var check = NewContext();
        var kept = await check.Users.SingleAsync(u => u.Id == unlisted);
        kept.DefaultTenantId.Should().BeNull();
        kept.DefaultTenantName.Should().BeNull();
    }
}
