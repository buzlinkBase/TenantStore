using FluentAssertions;
using TenantStoreApi.Core.Services.Platform;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Platform;

public class PlatformAuditServiceTests
{
    private static PlatformAuditService Audit(SubscriptionTestHost host) =>
        new(host.Uow, new PlatformDirectoryService(host.Uow, host.StateMachine));

    private static async Task AddEventAsync(SubscriptionTestHost host, Guid? owner, string type, string? actor, TimeSpan after, object? payload = null)
    {
        host.Clock.UtcNow = SubscriptionTestHost.Start + after;
        var subscription = owner == null ? null : host.Uow.Context.AccountSubscriptions.FirstOrDefault(s => s.OwnerUserId == owner);
        await host.Events.AddAsync(subscription, type, payload, actor, ownerUserId: owner);
        await host.SaveAsync();
    }

    private static async Task<(SubscriptionTestHost Host, Guid A, Guid B)> SeedAsync()
    {
        var host = new SubscriptionTestHost();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        host.AddCompany(a, "Acme", "a@acme.ph");
        host.AddCompany(b, "Globex", "b@globex.ph");
        await AddEventAsync(host, a, BillingEventTypes.PaymentRecorded, "platform:ana@onepunch.test", TimeSpan.FromHours(1), new { amount = 100 });
        await AddEventAsync(host, a, BillingEventTypes.PastDue, null, TimeSpan.FromHours(2));
        await AddEventAsync(host, b, BillingEventTypes.SuspendedByAdmin, "platform:ben@onepunch.test", TimeSpan.FromHours(3), new { reason = "Chargeback" });
        await AddEventAsync(host, null, "plan.updated", "platform:ana@onepunch.test", TimeSpan.FromHours(4));
        await AddEventAsync(host, b, BillingEventTypes.TrialStarted, "owner", TimeSpan.FromHours(5));
        return (host, a, b);
    }

    [Fact]
    public async Task List_ShowsEverythingNewestFirst_WithTheOwnersEmail()
    {
        var (host, a, _) = await SeedAsync();

        var page = await Audit(host).ListAsync(new AuditQuery());

        page.Total.Should().Be(5);
        page.Items.Select(e => e.Type).Should().Equal(
            BillingEventTypes.TrialStarted, "plan.updated", BillingEventTypes.SuspendedByAdmin, BillingEventTypes.PastDue, BillingEventTypes.PaymentRecorded);
        page.Items.Single(e => e.Type == BillingEventTypes.PaymentRecorded).OwnerEmail.Should().Be("a@acme.ph");
        page.Items.Single(e => e.Type == "plan.updated").OwnerUserId.Should().BeNull("plan edits belong to no account");
        page.Items.Single(e => e.Type == BillingEventTypes.PaymentRecorded).PayloadJson.Should().Contain("100");
        page.Items.Single(e => e.Type == BillingEventTypes.PastDue).OwnerUserId.Should().Be(a);
    }

    [Fact]
    public async Task Filters_ByOwner_Actor_OperatorsOnly_AndDates()
    {
        var (host, a, _) = await SeedAsync();
        var audit = Audit(host);

        (await audit.ListAsync(new AuditQuery(OwnerUserId: a))).Items.Select(e => e.Type)
            .Should().BeEquivalentTo([BillingEventTypes.PaymentRecorded, BillingEventTypes.PastDue]);
        (await audit.ListAsync(new AuditQuery(Actor: "ANA@"))).Total.Should().Be(2, "actor search ignores case");
        (await audit.ListAsync(new AuditQuery(OperatorsOnly: true))).Total.Should().Be(3);
        (await audit.ListAsync(new AuditQuery(OperatorsOnly: false))).Items.Select(e => e.Type)
            .Should().BeEquivalentTo([BillingEventTypes.PastDue, BillingEventTypes.TrialStarted]);
        (await audit.ListAsync(new AuditQuery(
            FromUtc: SubscriptionTestHost.Start.AddHours(2), ToUtc: SubscriptionTestHost.Start.AddHours(3)))).Items.Select(e => e.Type)
            .Should().BeEquivalentTo([BillingEventTypes.PastDue, BillingEventTypes.SuspendedByAdmin]);
    }

    [Fact]
    public async Task Filters_ByExactTypeOrPrefix()
    {
        var (host, _, _) = await SeedAsync();
        var audit = Audit(host);

        (await audit.ListAsync(new AuditQuery(Type: BillingEventTypes.PastDue))).Total.Should().Be(1);
        (await audit.ListAsync(new AuditQuery(Type: "subscription.*"))).Items.Select(e => e.Type)
            .Should().BeEquivalentTo([BillingEventTypes.PastDue, BillingEventTypes.SuspendedByAdmin]);
        (await audit.ListAsync(new AuditQuery(Type: "plan."))).Total.Should().Be(1);
    }

    [Fact]
    public async Task Pages_AndClampsThePageSize()
    {
        var (host, _, _) = await SeedAsync();

        var second = await Audit(host).ListAsync(new AuditQuery(Page: 2, PageSize: 2));

        second.Total.Should().Be(5);
        second.Items.Should().HaveCount(2);
        second.Page.Should().Be(2);
        (await Audit(host).ListAsync(new AuditQuery(PageSize: 100000))).PageSize.Should().Be(PageRequest.MaxPageSize);
    }

    [Fact]
    public async Task Types_ListsEachEventTypeOnce_Sorted()
    {
        var (host, _, _) = await SeedAsync();
        await AddEventAsync(host, null, "plan.updated", "platform:x", TimeSpan.FromHours(6));

        var types = await Audit(host).ListTypesAsync();

        types.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems().And.Contain("plan.updated");
    }
}
