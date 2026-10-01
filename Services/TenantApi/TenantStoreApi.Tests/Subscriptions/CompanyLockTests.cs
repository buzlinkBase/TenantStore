using System.Security.Claims;
using BuzlinkRepository;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Moq;
using Onepunch.Common.Lib.Subscriptions;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Core.Filters;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Subscriptions;

/// <summary>
/// When an owner's plan lapses: the owner and billing admins keep read-only access, every other
/// member is locked out, and devices' punches are kept only for a while -- never past the point
/// the account stops being valid.
/// </summary>
public class SubscriptionAccessPolicyTests
{
    private static EntitlementSnapshot Snapshot(SubscriptionAccessMode mode) => new() { AccessMode = mode };

    [Theory]
    [InlineData(SubscriptionAccessMode.Full, true, "GET", SubscriptionAccessDecision.Allow)]
    [InlineData(SubscriptionAccessMode.Full, false, "POST", SubscriptionAccessDecision.Allow)]
    [InlineData(SubscriptionAccessMode.ReadOnly, true, "GET", SubscriptionAccessDecision.Allow)]
    [InlineData(SubscriptionAccessMode.ReadOnly, true, "POST", SubscriptionAccessDecision.ReadOnly)]
    [InlineData(SubscriptionAccessMode.ReadOnly, true, "DELETE", SubscriptionAccessDecision.ReadOnly)]
    [InlineData(SubscriptionAccessMode.ReadOnly, false, "GET", SubscriptionAccessDecision.Locked)]
    [InlineData(SubscriptionAccessMode.ReadOnly, false, "POST", SubscriptionAccessDecision.Locked)]
    public void Decide(SubscriptionAccessMode mode, bool billingUser, string method, SubscriptionAccessDecision expected) =>
        SubscriptionAccessPolicy.Decide(Snapshot(mode), method, billingUser).Should().Be(expected);

    private static ClaimsPrincipal User(params Claim[] claims) => new(new ClaimsIdentity(claims, "Bearer"));

    [Fact]
    public void BillingUsers_AreTheOwner_OrHoldersOfBillingView_NotAdmin()
    {
        BillingAccess.IsBillingUser(User(new Claim(ClaimTypes.Role, "Owner"))).Should().BeTrue();
        BillingAccess.IsBillingUser(User(new Claim("permission", "Billing:View"))).Should().BeTrue();
        BillingAccess.IsBillingUser(User(new Claim(ClaimTypes.Role, "Admin"))).Should().BeFalse();
        BillingAccess.IsBillingUser(User(new Claim(ClaimTypes.Role, "Employee"))).Should().BeFalse();
    }
}

public class PunchWindowTests
{
    private static readonly DateTime Lock = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
    private readonly SubscriptionStateMachine _machine = new();

    private EntitlementSnapshot Build(AccountSubscription subscription, int retentionDays = 60) =>
        EntitlementSnapshotBuilder.Build(subscription, null, Guid.NewGuid(), Lock, _machine, retentionDays);

    [Fact]
    public void ExpiredTrial_KeepsPunchesForTheWindow_FromTheTrialEnd()
    {
        var snapshot = Build(new AccountSubscription { SubStatus = AccountSubscriptionStatus.Expired, TrialEndsAtUtc = Lock });

        snapshot.ReadOnlySinceUtc.Should().Be(Lock);
        snapshot.PunchesAcceptedUntilUtc.Should().Be(Lock.AddDays(60));
        snapshot.AcceptsPunches(Lock.AddDays(59)).Should().BeTrue();
        snapshot.AcceptsPunches(Lock.AddDays(61)).Should().BeFalse();
    }

    [Fact]
    public void Suspended_CountsFromTheEndOfGrace()
    {
        var snapshot = Build(new AccountSubscription { SubStatus = AccountSubscriptionStatus.Suspended, GraceEndsAtUtc = Lock });

        snapshot.PunchesAcceptedUntilUtc.Should().Be(Lock.AddDays(60));
    }

    [Fact]
    public void ACustomWindow_IsHonored()
    {
        var snapshot = Build(new AccountSubscription { SubStatus = AccountSubscriptionStatus.Expired, TrialEndsAtUtc = Lock }, retentionDays: 14);

        snapshot.PunchesAcceptedUntilUtc.Should().Be(Lock.AddDays(14));
    }

    [Fact]
    public void Canceled_StopsAtThePurgeDate_WhenThatComesFirst()
    {
        var snapshot = Build(new AccountSubscription
        {
            SubStatus = AccountSubscriptionStatus.Canceled,
            CanceledAtUtc = Lock,
            PurgeAfterUtc = Lock.AddDays(30),
        });

        snapshot.PunchesAcceptedUntilUtc.Should().Be(Lock.AddDays(30));
    }

    [Theory]
    [InlineData(AccountSubscriptionStatus.Active)]
    [InlineData(AccountSubscriptionStatus.Trialing)]
    [InlineData(AccountSubscriptionStatus.PastDue)]
    public void FullAccess_AlwaysAcceptsPunches(AccountSubscriptionStatus status)
    {
        var snapshot = Build(new AccountSubscription { SubStatus = status, GraceEndsAtUtc = Lock.AddDays(-100) });

        snapshot.PunchesAcceptedUntilUtc.Should().BeNull();
        snapshot.AcceptsPunches(Lock.AddYears(5)).Should().BeTrue();
    }

    [Fact]
    public async Task DeletedCompany_IsInvalid_AndAcceptsNoPunches()
    {
        var host = new SubscriptionTestHost();
        var (_, tenantId) = await host.TrialAccountAsync();
        var deletedAt = host.Clock.UtcNow;
        host.Uow.Context.Tenants.Single(t => t.Id == tenantId).DeletedAt = deletedAt;
        await host.SaveAsync();

        var snapshot = await host.Resolver.ResolveForTenantAsync(tenantId);

        snapshot.Status.Should().Be(EntitlementSnapshot.InvalidStatus);
        snapshot.IsReadOnly.Should().BeTrue();
        snapshot.AcceptsPunches(deletedAt.AddMinutes(1)).Should().BeFalse();
    }

    [Fact]
    public async Task TheConfiguredWindow_ReachesTheResolver()
    {
        var host = new SubscriptionTestHost();
        var (owner, tenantId) = await host.TrialAccountAsync();
        var subscription = host.SubscriptionOf(owner);
        subscription.SubStatus = AccountSubscriptionStatus.Expired;
        await host.SaveAsync();
        host.Options.LockedPunchRetentionDays = 0;

        var snapshot = await host.Resolver.ResolveForTenantAsync(tenantId);

        snapshot.PunchesAcceptedUntilUtc.Should().Be(subscription.TrialEndsAtUtc);
    }
}

public class SubscriptionAccessFilterTests
{
    private static async Task<IActionResult?> RunAsync(SubscriptionTestHost host, Guid tenantId, Claim[] claims, string method, object[]? metadata = null)
    {
        var tenant = new Mock<ITenantProvider>();
        tenant.SetupGet(t => t.TenantId).Returns(tenantId);
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")) };
        http.Request.Method = method;
        var descriptor = new ActionDescriptor { EndpointMetadata = (metadata ?? []).ToList() };
        var context = new ActionExecutingContext(
            new ActionContext(http, new RouteData(), descriptor), [], new Dictionary<string, object?>(), new object());

        await new SubscriptionAccessFilter(host.Resolver, tenant.Object).OnActionExecutionAsync(
            context, () => Task.FromResult(new ActionExecutedContext(context, [], new object())));
        return context.Result;
    }

    private static async Task<(SubscriptionTestHost Host, Guid TenantId)> ExpiredCompanyAsync()
    {
        var host = new SubscriptionTestHost();
        var (owner, tenantId) = await host.TrialAccountAsync();
        host.SubscriptionOf(owner).SubStatus = AccountSubscriptionStatus.Expired;
        await host.SaveAsync();
        return (host, tenantId);
    }

    [Fact]
    public async Task Member_IsLockedOut_OfMembersAndRoles()
    {
        var (host, tenantId) = await ExpiredCompanyAsync();

        var result = await RunAsync(host, tenantId, [new Claim(ClaimTypes.Role, "Admin")], "GET");

        var problem = (ProblemDetails)((ObjectResult)result!).Value!;
        problem.Type.Should().Be(SubscriptionProblemTypes.Locked);
    }

    [Fact]
    public async Task Member_CanStillReachEndpointsMarkedAllowWhenLocked()
    {
        var (host, tenantId) = await ExpiredCompanyAsync();

        var result = await RunAsync(host, tenantId, [new Claim(ClaimTypes.Role, "Employee")], "GET", [new AllowWhenLockedAttribute()]);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Owner_CanRead_ButNotChange()
    {
        var (host, tenantId) = await ExpiredCompanyAsync();
        Claim[] owner = [new Claim(ClaimTypes.Role, "Owner")];

        (await RunAsync(host, tenantId, owner, "GET")).Should().BeNull();
        var write = (ProblemDetails)((ObjectResult)(await RunAsync(host, tenantId, owner, "POST"))!).Value!;
        write.Type.Should().Be(SubscriptionProblemTypes.ReadOnly);
    }
}
