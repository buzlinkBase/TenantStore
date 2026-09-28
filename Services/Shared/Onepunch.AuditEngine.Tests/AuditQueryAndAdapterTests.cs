using System.Security.Claims;
using FluentAssertions;
using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Onepunch.AuditEngine.AspNetCore;
using Onepunch.AuditEngine.MassTransit;

namespace Onepunch.AuditEngine.Tests;

public class AuditQueryTests
{
    private static async Task SeedAsync(AuditHost host)
    {
        await host.InScopeAsync(async sp =>
        {
            var writer = sp.GetRequiredService<IAuditWriter>();
            var start = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
            for (var i = 0; i < 25; i++)
            {
                await writer.WriteAsync(new AuditEvent
                {
                    OccurredAtUtc = start.AddHours(i),
                    EventType = i % 5 == 0 ? AuditEventTypes.Login : AuditEventTypes.Update,
                    Outcome = i == 10 ? AuditOutcome.Failure : AuditOutcome.Success,
                    EntityType = "Employee",
                    EntityId = $"emp-{i}",
                    Description = i == 7 ? "Updated salary grade" : "Updated employee",
                    Actor = i < 20 ? AuditHost.Alice : AuditActor.User("user-2", "Bob"),
                });
            }
            await writer.WriteAsync(new AuditEvent { EventType = AuditEventTypes.Update, OrganizationId = "other-org" });
            await sp.GetRequiredService<TestDbContext>().SaveChangesAsync();
        });
    }

    private static Task<AuditPage> QueryAsync(AuditHost host, AuditQuery query) =>
        host.InScopeAsync(sp => sp.GetRequiredService<IAuditQueryService>().QueryAsync(query with { OrganizationId = AuditHost.Org }));

    [Fact]
    public async Task Pages_NewestFirst_WithTheTotalForTheOrganizationOnly()
    {
        using var host = new AuditHost();
        await SeedAsync(host);

        var page = await QueryAsync(host, new AuditQuery { Page = 2, PageSize = 10 });

        page.Total.Should().Be(25);
        page.Items.Should().HaveCount(10);
        page.Items.First().EntityId.Should().Be("emp-14");
        page.Items.Should().BeInDescendingOrder(x => x.OccurredAtUtc);
    }

    [Fact]
    public async Task Filters_Combine()
    {
        using var host = new AuditHost();
        await SeedAsync(host);

        (await QueryAsync(host, new AuditQuery { EventType = AuditEventTypes.Login })).Total.Should().Be(5);
        (await QueryAsync(host, new AuditQuery { Outcome = AuditOutcome.Failure })).Items.Should().ContainSingle().Which.EntityId.Should().Be("emp-10");
        (await QueryAsync(host, new AuditQuery { ActorId = "user-2" })).Total.Should().Be(5);
        (await QueryAsync(host, new AuditQuery { Search = "salary" })).Items.Should().ContainSingle().Which.EntityId.Should().Be("emp-7");
        (await QueryAsync(host, new AuditQuery
        {
            FromUtc = new DateTime(2026, 9, 1, 2, 0, 0, DateTimeKind.Utc),
            ToUtc = new DateTime(2026, 9, 1, 5, 0, 0, DateTimeKind.Utc),
        })).Total.Should().Be(3, "the upper bound is exclusive");
    }

    [Fact]
    public async Task Get_IsScopedToTheOrganization()
    {
        using var host = new AuditHost();
        await SeedAsync(host);
        var any = (await QueryAsync(host, new AuditQuery { PageSize = 1 })).Items.Single();

        var service = (Func<string, Task<AuditEvent?>>)(org =>
            host.InScopeAsync(sp => sp.GetRequiredService<IAuditQueryService>().GetAsync(any.Id, org)));

        (await service(AuditHost.Org)).Should().NotBeNull();
        (await service("other-org")).Should().BeNull();
    }
}

public class AspNetCoreAdapterTests
{
    private static (ClaimsPrincipalAuditActorResolver Resolver, HttpContextAuditMetadataEnricher Enricher) Build(
        HttpContext? context, ClaimsAuditActorOptions? claimOptions = null)
    {
        var accessor = new HttpContextAccessor { HttpContext = context };
        var services = new ServiceCollection().AddSingleton<ITenantHolder>(new TenantHolder("t-from-provider")).BuildServiceProvider();
        return (new ClaimsPrincipalAuditActorResolver(accessor, new AuditEngineOptions { SourceName = "hrms-api" },
                claimOptions ?? new ClaimsAuditActorOptions(), services),
            new HttpContextAuditMetadataEnricher(accessor));
    }

    private interface ITenantHolder { string? TenantId { get; } }
    private sealed record TenantHolder(string? TenantId) : ITenantHolder;

    [Fact]
    public void OrganizationResolver_IsConsultedFirst_EvenWithoutARequest()
    {
        var options = new ClaimsAuditActorOptions { OrganizationResolver = sp => sp.GetRequiredService<ITenantHolder>().TenantId };

        Build(null, options).Resolver.ResolveOrganizationId().Should().Be("t-from-provider");
    }

    [Fact]
    public void AuthenticatedRequest_ResolvesTheUserAndOrganizationFromClaims()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("sub", "u-1"), new Claim("email", "ana@acme.com"), new Claim("tenantId", "t-1")], "jwt")),
        };

        var (resolver, _) = Build(context);

        resolver.ResolveActor().Should().Be(AuditActor.User("u-1", "ana@acme.com"));
        resolver.ResolveOrganizationId().Should().Be("t-1");
    }

    [Fact]
    public void AnonymousRequest_AndNoRequest_AreDistinguished()
    {
        Build(new DefaultHttpContext()).Resolver.ResolveActor().Should().Be(AuditActor.Anonymous);
        Build(null).Resolver.ResolveActor().Should().Be(AuditActor.System("hrms-api"));
    }

    [Fact]
    public void Metadata_RecordsTheClientBehindAProxy()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "PUT";
        context.Request.Path = "/employees/1";
        context.Request.Headers.UserAgent = "Mozilla/5.0";
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.7, 10.0.0.1";

        var metadata = new Dictionary<string, string?>();
        Build(context).Enricher.Enrich(metadata);

        metadata[HttpContextAuditMetadataEnricher.IpAddress].Should().Be("203.0.113.7");
        metadata[HttpContextAuditMetadataEnricher.UserAgent].Should().Be("Mozilla/5.0");
        metadata[HttpContextAuditMetadataEnricher.HttpMethod].Should().Be("PUT");
        metadata[HttpContextAuditMetadataEnricher.Path].Should().Be("/employees/1");
    }
}

public class MassTransitForwarderTests
{
    private static (MassTransitAuditEventForwarder Forwarder, List<(AuditEventRecorded Message, Dictionary<string, object> Headers)> Published) Build()
    {
        var published = new List<(AuditEventRecorded, Dictionary<string, object>)>();
        var publisher = new Mock<IPublishEndpoint>();
        publisher
            .Setup(p => p.Publish(It.IsAny<AuditEventRecorded>(), It.IsAny<IPipe<PublishContext<AuditEventRecorded>>>(), It.IsAny<CancellationToken>()))
            .Returns(async (AuditEventRecorded message, IPipe<PublishContext<AuditEventRecorded>> pipe, CancellationToken _) =>
            {
                var headers = new Dictionary<string, object>();
                var sendHeaders = new Mock<SendHeaders>();
                sendHeaders.Setup(h => h.Set(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<bool>()))
                    .Callback((string key, object value, bool _) => headers[key] = value);
                sendHeaders.Setup(h => h.Set(It.IsAny<string>(), It.IsAny<string>()))
                    .Callback((string key, string value) => headers[key] = value);
                var context = new Mock<PublishContext<AuditEventRecorded>>();
                context.SetupGet(c => c.Headers).Returns(sendHeaders.Object);
                context.SetupGet(c => c.Message).Returns(message);
                await pipe.Send(context.Object);
                published.Add((message, headers));
            });

        var forwarder = new MassTransitAuditEventForwarder(
            publisher.Object,
            new AuditEngineOptions { SourceName = "auth-api" },
            new MassTransitAuditForwardingOptions { OrganizationHeaderName = "X-Tenant-ID" });
        return (forwarder, published);
    }

    [Fact]
    public async Task ForwardsOwnEvents_WithTheOrganizationHeader()
    {
        var (forwarder, published) = Build();
        var own = new AuditEvent { Id = Guid.NewGuid(), EventType = AuditEventTypes.Login, OrganizationId = "t-1", Source = "auth-api", Hash = "H" };

        await forwarder.OnStagedAsync([own]);

        var (message, headers) = published.Should().ContainSingle().Subject;
        message.Should().BeEquivalentTo(AuditEventRecorded.From(own));
        headers["X-Tenant-ID"].Should().Be("t-1");
        message.ToEvent().Should().BeEquivalentTo(own, o => o.Excluding(e => e.OccurredAtUtc));
    }

    [Fact]
    public async Task SkipsEventsWithoutAnOrganization_AndReplicasOfOtherServices()
    {
        var (forwarder, published) = Build();

        await forwarder.OnStagedAsync(
        [
            new AuditEvent { EventType = AuditEventTypes.Login, OrganizationId = null, Source = "auth-api" },
            new AuditEvent { EventType = AuditEventTypes.Update, OrganizationId = "t-1", Source = "tenant-api" },
        ]);

        published.Should().BeEmpty();
    }
}
