using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Onepunch.AuditEngine.EntityFrameworkCore;

namespace Onepunch.AuditEngine.Tests;

/// <summary>
/// Automatic capture through the EF Core interceptor: only [Audited] entities, only real
/// changes, written in the same save as the change itself.
/// </summary>
public class AuditCaptureTests
{
    private static readonly Guid CustomerId = Guid.NewGuid();

    private static async Task AddCustomerAsync(AuditHost host) =>
        await host.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            db.Customers.Add(new Customer { Id = CustomerId, Name = "Acme", BankAccountNo = "1234-5678", Notes = "vip" });
            await db.SaveChangesAsync();
        });

    private static async Task EditCustomerAsync(AuditHost host, Action<Customer> edit) =>
        await host.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            edit(await db.Customers.SingleAsync(x => x.Id == CustomerId));
            await db.SaveChangesAsync();
        });

    [Fact]
    public async Task Create_RecordsTheSetFields_InTheSameSave()
    {
        using var host = new AuditHost();

        await AddCustomerAsync(host);

        var record = (await host.RecordsAsync()).Should().ContainSingle().Subject;
        record.EventType.Should().Be(AuditEventTypes.Create);
        record.EntityType.Should().Be(nameof(Customer));
        record.EntityId.Should().Be(CustomerId.ToString());
        record.ActorId.Should().Be("user-1");
        record.ActorDisplayName.Should().Be("Alice");
        record.OrganizationId.Should().Be(AuditHost.Org);
        record.Source.Should().Be("test-api");
        record.Changes.Should().Contain("Acme");
    }

    [Fact]
    public async Task IgnoredAndRedactedProperties_AreNotRecordedInTheClear()
    {
        using var host = new AuditHost();

        await AddCustomerAsync(host);

        var changes = (await host.RecordsAsync()).Single().Changes;
        changes.Should().NotContain("vip", "[AuditIgnore] properties are left out");
        changes.Should().NotContain("1234-5678", "configured sensitive properties are masked");
        changes.Should().Contain("BankAccountNo").And.Contain("***");
    }

    [Fact]
    public async Task Update_RecordsOnlyTheChangedFields_WithBeforeAndAfter()
    {
        using var host = new AuditHost();
        await AddCustomerAsync(host);

        await EditCustomerAsync(host, c => { c.Name = "Acme Corp"; c.UpdatedAt = DateTime.UtcNow; });

        var update = (await host.RecordsAsync()).Should().ContainSingle(x => x.EventType == AuditEventTypes.Update).Subject;
        var ev = await host.InScopeAsync(sp => sp.GetRequiredService<IAuditQueryService>().GetAsync(update.Id, AuditHost.Org));
        ev!.Changes.Should().ContainSingle()
            .Which.Should().Be(new AuditChange(nameof(Customer.Name), "Acme", "Acme Corp"),
                "bookkeeping columns like UpdatedAt are ignored by default");
    }

    [Fact]
    public async Task SavingWithoutRealChanges_RecordsNothing()
    {
        using var host = new AuditHost();
        await AddCustomerAsync(host);

        await EditCustomerAsync(host, c => c.Name = c.Name);
        await EditCustomerAsync(host, c => c.Notes = "ignored field only");

        (await host.RecordsAsync()).Should().ContainSingle(x => x.EventType == AuditEventTypes.Create);
    }

    [Fact]
    public async Task SameValueInADifferentForm_IsNotAChange()
    {
        using var host = new AuditHost();
        var hired = new DateTime(2026, 1, 5, 8, 0, 0, DateTimeKind.Utc);
        await host.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            db.Customers.Add(new Customer { Id = CustomerId, Name = "Acme", DailyRate = 1538.460000m, HiredAt = hired });
            await db.SaveChangesAsync();
        });

        // What an edit-form save looks like: the same amount without the column's scale, the same
        // instant with a different Kind -- and, like the host's ModifyAsync, Update() marking every
        // property modified (EF's own change detection would otherwise hide the problem).
        await host.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            var customer = await db.Customers.SingleAsync(x => x.Id == CustomerId);
            customer.DailyRate = 1538.46m;
            customer.HiredAt = DateTime.SpecifyKind(hired, DateTimeKind.Unspecified);
            db.Customers.Update(customer);
            await db.SaveChangesAsync();
        });

        (await host.RecordsAsync()).Select(x => x.EventType).Should().Equal([AuditEventTypes.Create],
            "a save that changes no value records nothing");
    }

    [Fact]
    public async Task ARealAmountChange_IsShownWithoutTrailingZeros()
    {
        using var host = new AuditHost();
        await host.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            db.Customers.Add(new Customer { Id = CustomerId, Name = "Acme", DailyRate = 435.000000m });
            await db.SaveChangesAsync();
        });

        await EditCustomerAsync(host, c => c.DailyRate = 450.50m);

        var update = (await host.RecordsAsync()).Single(x => x.EventType == AuditEventTypes.Update);
        var ev = await host.InScopeAsync(sp => sp.GetRequiredService<IAuditQueryService>().GetAsync(update.Id, AuditHost.Org));
        ev!.Changes.Should().ContainSingle().Which.Should().Be(new AuditChange(nameof(Customer.DailyRate), "435", "450.5"));
    }

    [Fact]
    public async Task SoftDelete_IsRecordedAsADelete()
    {
        using var host = new AuditHost();
        await AddCustomerAsync(host);

        await EditCustomerAsync(host, c => c.DeletedAt = DateTime.UtcNow);

        (await host.RecordsAsync()).Select(x => x.EventType).Should().Equal(AuditEventTypes.Create, AuditEventTypes.Delete);
    }

    [Fact]
    public async Task HardDelete_IsRecordedAsADelete()
    {
        using var host = new AuditHost();
        await AddCustomerAsync(host);

        await host.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            db.Customers.Remove(await db.Customers.SingleAsync());
            await db.SaveChangesAsync();
        });

        (await host.RecordsAsync()).Should().Contain(x => x.EventType == AuditEventTypes.Delete && x.EntityId == CustomerId.ToString());
    }

    [Fact]
    public async Task UnmarkedEntities_AreNeverRecorded_AndEntityTypeCanBeRenamed()
    {
        using var host = new AuditHost();

        await host.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            db.Cache.Add(new CacheRow { Id = Guid.NewGuid(), Value = "x" });
            db.Invoices.Add(new InvoiceRow { Id = Guid.NewGuid(), Amount = 12.5m });
            await db.SaveChangesAsync();
        });

        var record = (await host.RecordsAsync()).Should().ContainSingle().Subject;
        record.EntityType.Should().Be("Invoice");
        record.Changes.Should().Contain("12.5");
    }

    [Fact]
    public async Task Capture_RecordsOnlyTheSelectedKindsOfChange()
    {
        using var host = new AuditHost();
        var id = Guid.NewGuid();

        await host.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            db.Commands.Add(new QueuedCommand { Id = id, Command = "ENROLL_FP 12" });
            await db.SaveChangesAsync();
        });
        await host.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            (await db.Commands.SingleAsync()).Status = "Done"; // the device acknowledging it
            await db.SaveChangesAsync();
        });
        await host.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            db.Commands.Remove(await db.Commands.SingleAsync());
            await db.SaveChangesAsync();
        });

        (await host.RecordsAsync()).Select(x => x.EventType)
            .Should().Equal(AuditEventTypes.Create, AuditEventTypes.Delete);
    }

    [Fact]
    public async Task ExplicitWrite_IsPersistedWithTheHostsSave_AndSeenBySinks()
    {
        using var host = new AuditHost();

        await host.InScopeAsync(async sp =>
        {
            await sp.GetRequiredService<IAuditWriter>().WriteAsync(new AuditEvent
            {
                EventType = AuditEventTypes.Approve,
                EntityType = "Leave",
                EntityId = "42",
                Description = "Approved leave 42",
            });
            var db = sp.GetRequiredService<TestDbContext>();
            db.Customers.Add(new Customer { Id = Guid.NewGuid(), Name = "B" });
            await db.SaveChangesAsync();
        });

        (await host.RecordsAsync()).Select(x => x.EventType).Should().BeEquivalentTo([AuditEventTypes.Approve, AuditEventTypes.Create]);
        host.Sink.Seen.Select(x => x.EventType).Should().BeEquivalentTo([AuditEventTypes.Approve, AuditEventTypes.Create]);
        host.Sink.Seen.Should().OnlyContain(x => x.Hash != null && x.Actor == AuditHost.Alice);
    }

    [Fact]
    public async Task ExplicitWrite_WithoutASave_IsNotPersisted()
    {
        using var host = new AuditHost();

        await host.InScopeAsync(sp => sp.GetRequiredService<IAuditWriter>()
            .WriteAsync(new AuditEvent { EventType = AuditEventTypes.Login }));

        (await host.RecordsAsync()).Should().BeEmpty("events commit with the host's unit of work, never on their own");
    }

    [Fact]
    public async Task Disabled_CapturesAndWritesNothing()
    {
        using var host = new AuditHost(enabled: false);

        await AddCustomerAsync(host);
        await host.InScopeAsync(async sp =>
        {
            await sp.GetRequiredService<IAuditWriter>().WriteAsync(new AuditEvent { EventType = AuditEventTypes.Login });
            await sp.GetRequiredService<TestDbContext>().SaveChangesAsync();
        });

        (await host.RecordsAsync()).Should().BeEmpty();
        host.Sink.Seen.Should().BeEmpty();
    }

    [Fact]
    public void AuditTable_CanBeTrimmedByDateAcrossOrganizations()
    {
        using var host = new AuditHost();
        using var scope = host.Scope();
        var entity = scope.ServiceProvider.GetRequiredService<TestDbContext>().Model.FindEntityType(typeof(AuditEventRecord))!;

        entity.GetIndexes().Should().Contain(i =>
            i.Properties.Count == 1 && i.Properties[0].Name == nameof(AuditEventRecord.OccurredAtUtc),
            "a retention job selects old events by date alone");
    }

    [Fact]
    public void AddAuditEngine_WithoutStorage_FailsFast()
    {
        var act = () => new ServiceCollection().AddAuditEngine(_ => { });

        act.Should().Throw<InvalidOperationException>().WithMessage("*storage adapter*");
    }
}
