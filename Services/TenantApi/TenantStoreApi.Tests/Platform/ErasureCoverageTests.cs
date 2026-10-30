using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using TenantStoreApi.Core;
using TenantStoreApi.Core.Services.Platform;
using TenantStoreApi.Domain.Entities;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Infrastructure;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Platform;

/// <summary>
/// The safety net for the console's permanent deletion in Tenant Service: one row of EVERY table is
/// seeded for each of two accounts (read from the EF model, on a real SQLite database), the real
/// set-based eraser runs, and every row of the erased account or company must be gone while the
/// other account's rows stay.
/// <para>If one of these fails after you added an entity: teach <see cref="TenantStoreAccountEraser"/>
/// to delete it -- or, if it truly holds no account data (a catalog every account shares), add it to
/// <see cref="GlobalEntities"/> with a reason.</para>
/// </summary>
public class ErasureCoverageTests : IDisposable
{
    /// <summary>Shared by every account; never erased. Each needs a reason to be here.</summary>
    private static readonly Type[] GlobalEntities =
    [
        typeof(Plan),                 // the plan catalog
        typeof(PlanEntitlement),      // what each plan includes
        typeof(Industry),             // signup choices
        typeof(SubIndustry),
        typeof(StorageAddOnOffering), // the storage catalog
        typeof(PlatformSetting),      // console settings
        typeof(LegalDocument),        // the Terms of Service versions every company is asked to accept
        typeof(Permission),           // the permission catalog
    ];

    /// <summary>MassTransit's inbox and outbox: transient, not account data.</summary>
    private static readonly HashSet<string> MessagingTables = ["InboxState", "OutboxMessage", "OutboxState"];

    private readonly SqliteConnection _keepAlive;
    private readonly DbContextOptions<TenantContext> _options;

    private static readonly Account A = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
    private static readonly Account B = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

    private sealed record Account(Guid Owner, Guid Subscription, Guid Company)
    {
        public SeedUnit Unit => new(
            new Dictionary<string, object>
            {
                ["OwnerUserId"] = Owner,
                ["UserId"] = Owner,
                ["SubscriptionId"] = Subscription,
                ["TenantId"] = Company,
                ["HostTenantId"] = Company,
                ["GuestTenantId"] = Company,
                ["OrganizationId"] = Company.ToString(),
            },
            new Dictionary<string, object>
            {
                ["Tenants"] = Company,
                ["AccountSubscriptions"] = Subscription,
            });
    }

    public ErasureCoverageTests()
    {
        var connectionString = $"Data Source=file:{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(connectionString);
        _keepAlive.Open();
        _options = new DbContextOptionsBuilder<TenantContext>().UseSqlite(connectionString).Options;
        using var db = NewContext();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _keepAlive.Dispose();

    private TenantContext NewContext() => new(_options);

    private HashSet<string> GlobalTables(TenantContext db) =>
        GlobalEntities.Select(t => db.Model.FindEntityType(t)!.GetTableName()!).ToHashSet();

    private SeededRows Seed()
    {
        using var db = NewContext();
        var rows = ModelRowSeeder.Seed(db, [A.Unit, B.Unit], MessagingTables);
        rows.Tables.Should().HaveCountGreaterThan(20, "every Tenant Service table is seeded");
        return rows;
    }

    private static async Task RunAsync(TenantContext db, Func<TenantStoreAccountEraser, Task> erase)
    {
        var uow = new UnitOfWorkService(db);
        await erase(new TenantStoreAccountEraser(uow, new RelationalRowEraser()));
        await uow.CommitChangesAsync("", CancellationToken.None);
    }

    // The tables an account's rows reach: its own columns, or a foreign key to such a row.
    private static HashSet<string> TablesReaching(TenantContext db, IReadOnlySet<string> columns, IReadOnlySet<string> roots)
    {
        var tables = db.Model.GetRelationalModel().Tables.ToList();
        var reached = tables.Where(t => roots.Contains(t.Name) || t.Columns.Any(c => columns.Contains(c.Name))).Select(t => t.Name).ToHashSet();
        bool grew;
        do
        {
            grew = false;
            foreach (var table in tables.Where(t => !reached.Contains(t.Name)))
            {
                if (ModelRowSeeder.Parents(table).Any(reached.Contains)) grew = reached.Add(table.Name) || grew;
            }
        } while (grew);
        return reached;
    }

    [Fact]
    public async Task AccountErasure_DeletesTheAccountsRowInEveryTable_AndNoOneElses()
    {
        var rows = Seed();

        await using (var db = NewContext())
            await RunAsync(db, eraser => eraser.EraseAsync(new AccountErasureScope(A.Owner, A.Subscription, [A.Company], [A.Owner]), CancellationToken.None));

        await using var check = NewContext();
        var global = GlobalTables(check);
        var survived = rows.Tables.Where(t => !global.Contains(t) && ModelRowSeeder.Exists(check, rows, t, 0)).ToList();
        survived.Should().BeEmpty("nothing of an erased account may be left -- teach the eraser, or declare the entity global");
        rows.Tables.Where(t => !ModelRowSeeder.Exists(check, rows, t, 1)).Should().BeEmpty("another account's rows are never touched");
        rows.Tables.Where(global.Contains).Where(t => !ModelRowSeeder.Exists(check, rows, t, 0))
            .Should().BeEmpty("the shared catalogs stay");
    }

    [Fact]
    public async Task CompanyErasure_DeletesTheCompanysRowInEveryTable_AndKeepsTheAccountsOwnRecords()
    {
        var rows = Seed();

        await using (var db = NewContext())
            await RunAsync(db, eraser => eraser.EraseCompanyAsync(
                new CompanyErasureScope(A.Owner, A.Subscription, A.Company, Guid.NewGuid(), [A.Owner]), CancellationToken.None));

        await using var check = NewContext();
        var global = GlobalTables(check);
        var company = TablesReaching(check, new HashSet<string> { "TenantId", "HostTenantId", "GuestTenantId", "OrganizationId" }, new HashSet<string> { "Tenants" });
        company.ExceptWith(global);

        rows.Tables.Where(t => company.Contains(t) && ModelRowSeeder.Exists(check, rows, t, 0))
            .Should().BeEmpty("nothing of an erased company may be left");
        rows.Tables.Where(t => !company.Contains(t) && !ModelRowSeeder.Exists(check, rows, t, 0))
            .Should().BeEmpty("the account's subscription, payments and billing history stay when one company goes");
        rows.Tables.Where(t => !ModelRowSeeder.Exists(check, rows, t, 1)).Should().BeEmpty("another account's rows are never touched");
    }

    [Fact]
    public async Task Erasure_TwiceInARow_IsHarmless()
    {
        Seed();
        var scope = new AccountErasureScope(A.Owner, A.Subscription, [A.Company], [A.Owner]);
        await using (var db = NewContext()) await RunAsync(db, eraser => eraser.EraseAsync(scope, CancellationToken.None));

        long second = -1;
        await using (var db = NewContext())
            await RunAsync(db, async eraser => second = await eraser.EraseAsync(scope, CancellationToken.None));

        second.Should().Be(0);
    }

    [Fact]
    public async Task AccountErasure_TakesTheErasedPeoplesRemovedMembershipsElsewhere_ButNotLiveOnes()
    {
        var other = Guid.NewGuid();
        var gone = Guid.NewGuid();
        var kept = Guid.NewGuid();
        await using (var db = NewContext())
        {
            db.Tenants.Add(new Tenant { Id = A.Company, TenantName = "Acme", UserId = A.Owner });
            db.Tenants.Add(new Tenant { Id = other, TenantName = "Other", UserId = Guid.NewGuid() });
            db.Memberships.AddRange(
                new UserMembership { Id = Guid.NewGuid(), TenantId = A.Company, UserId = gone, Status = "Active" },
                new UserMembership { Id = Guid.NewGuid(), TenantId = other, UserId = gone, Status = "Removed", DeletedAt = DateTime.UtcNow },
                new UserMembership { Id = Guid.NewGuid(), TenantId = other, UserId = kept, Status = "Active" });
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
            await RunAsync(db, eraser => eraser.EraseAsync(new AccountErasureScope(A.Owner, A.Subscription, [A.Company], [A.Owner, gone]), CancellationToken.None));

        await using var check = NewContext();
        (await check.Memberships.IgnoreQueryFilters().CountAsync(m => m.UserId == gone)).Should().Be(0);
        (await check.Memberships.IgnoreQueryFilters().CountAsync(m => m.UserId == kept)).Should().Be(1);
    }

    [Fact]
    public async Task AccountErasure_TakesTheAuditTrailOfItsStorageAddOnsAndTheirPayments()
    {
        var addOn = Guid.NewGuid();
        var payment = Guid.NewGuid();
        await using (var db = NewContext())
        {
            // Only the rows this test is about; the plan and the rest are left out.
            await db.Database.OpenConnectionAsync();
            await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");
            var offering = new StorageAddOnOffering { Id = Guid.NewGuid(), Code = "s50", Name = "50 GB", BlockMb = 51200 };
            db.StorageAddOnOfferings.Add(offering);
            db.AccountSubscriptions.Add(new AccountSubscription { Id = A.Subscription, OwnerUserId = A.Owner });
            db.StorageAddOns.Add(new StorageAddOn { Id = addOn, SubscriptionId = A.Subscription, OfferingId = offering.Id, Blocks = 1, BlockMb = 51200 });
            db.StorageAddOnPayments.Add(new StorageAddOnPayment { Id = payment, SubscriptionId = A.Subscription, AddOnId = addOn, Blocks = 1 });
            foreach (var id in new[] { addOn, payment })
                db.Set<Onepunch.AuditEngine.EntityFrameworkCore.AuditEventRecord>().Add(new()
                {
                    Id = Guid.NewGuid(), OccurredAtUtc = DateTime.UtcNow, EventType = "Update", EntityId = id.ToString(),
                });
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
            await RunAsync(db, eraser => eraser.EraseAsync(new AccountErasureScope(A.Owner, A.Subscription, []), CancellationToken.None));

        await using var check = NewContext();
        (await check.Set<Onepunch.AuditEngine.EntityFrameworkCore.AuditEventRecord>().CountAsync()).Should().Be(0);
    }
}
