using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Onepunch.AuditEngine.Integrity;

namespace Onepunch.AuditEngine.Tests;

/// <summary>Hash chain: each event links to its predecessor, and tampering is detectable.</summary>
public class AuditIntegrityTests
{
    private static async Task<List<AuditEvent>> WriteAsync(AuditHost host, params string[][] batches)
    {
        foreach (var batch in batches)
        {
            await host.InScopeAsync(async sp =>
            {
                var writer = sp.GetRequiredService<IAuditWriter>();
                foreach (var eventType in batch)
                    await writer.WriteAsync(new AuditEvent { EventType = eventType });
                await sp.GetRequiredService<TestDbContext>().SaveChangesAsync();
            });
        }

        var page = await host.InScopeAsync(sp => sp.GetRequiredService<IAuditQueryService>()
            .QueryAsync(new AuditQuery { OrganizationId = AuditHost.Org, PageSize = 100 }));
        return page.Items.OrderBy(x => x.OccurredAtUtc).ToList();
    }

    [Fact]
    public async Task Events_ChainAcrossOneSaveAndAcrossSaves()
    {
        using var host = new AuditHost();

        var events = await WriteAsync(host, [AuditEventTypes.Create, AuditEventTypes.Update], [AuditEventTypes.Delete]);

        events.Should().HaveCount(3);
        events[0].PreviousEventHash.Should().BeNull("the first event starts the chain");
        events[1].PreviousEventHash.Should().Be(events[0].Hash, "events staged in one save chain onto each other");
        events[2].PreviousEventHash.Should().Be(events[1].Hash, "a later save continues from the stored head");
    }

    [Fact]
    public async Task StoredChain_VerifiesClean_AfterARoundTripThroughStorage()
    {
        using var host = new AuditHost();

        var events = await WriteAsync(host, [AuditEventTypes.Create, AuditEventTypes.Update], [AuditEventTypes.Delete]);

        AuditChainVerifier.Verify(events).Should().BeEmpty();
    }

    [Fact]
    public async Task EditingAStoredEvent_IsDetected()
    {
        using var host = new AuditHost();
        var events = await WriteAsync(host, [AuditEventTypes.Create, AuditEventTypes.Update]);

        var tampered = events.Select(e => e.Id == events[0].Id ? e with { Description = "nothing to see" } : e);

        AuditChainVerifier.Verify(tampered).Should().ContainSingle()
            .Which.Should().Be(new AuditIntegrityFinding(events[0].Id, AuditIntegrityIssue.ContentAltered));
    }

    [Fact]
    public async Task DeletingAStoredEvent_IsDetected()
    {
        using var host = new AuditHost();
        var events = await WriteAsync(host, [AuditEventTypes.Create, AuditEventTypes.Update, AuditEventTypes.Delete]);

        var withGap = events.Where(e => e.Id != events[1].Id);

        AuditChainVerifier.Verify(withGap).Should().ContainSingle()
            .Which.Should().Be(new AuditIntegrityFinding(events[2].Id, AuditIntegrityIssue.PredecessorMissing));
    }

    [Fact]
    public async Task Organizations_KeepSeparateChains()
    {
        using var host = new AuditHost();

        await host.InScopeAsync(async sp =>
        {
            var writer = sp.GetRequiredService<IAuditWriter>();
            await writer.WriteAsync(new AuditEvent { EventType = AuditEventTypes.Create, OrganizationId = "org-a" });
            await writer.WriteAsync(new AuditEvent { EventType = AuditEventTypes.Create, OrganizationId = "org-b" });
            await sp.GetRequiredService<TestDbContext>().SaveChangesAsync();
        });

        (await host.RecordsAsync()).Should().OnlyContain(x => x.PreviousEventHash == null);
    }

    [Fact]
    public async Task Replicas_AreStoredAsReceived_Once_AndNeverJoinTheLocalChain()
    {
        using var host = new AuditHost();
        var foreign = new AuditEvent
        {
            Id = Guid.NewGuid(),
            OccurredAtUtc = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc),
            EventType = AuditEventTypes.Login,
            OrganizationId = AuditHost.Org,
            Actor = AuditActor.User("u-9", "Bob"),
            Source = "auth-api",
            Hash = "ABC",
        };

        for (var i = 0; i < 2; i++)
        {
            await host.InScopeAsync(async sp =>
            {
                await sp.GetRequiredService<IAuditReplicaWriter>().WriteReplicaAsync(foreign);
                await sp.GetRequiredService<TestDbContext>().SaveChangesAsync();
            });
        }
        var local = await WriteAsync(host, [AuditEventTypes.Create]);

        var records = await host.RecordsAsync();
        records.Should().ContainSingle(x => x.Id == foreign.Id).Which.Hash.Should().Be("ABC");
        local.Single(x => x.Source == "test-api").PreviousEventHash.Should().BeNull("another service's chain is not ours");
    }
}
