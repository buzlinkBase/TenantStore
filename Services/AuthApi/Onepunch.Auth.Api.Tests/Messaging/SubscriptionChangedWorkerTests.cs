using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Moq;
using Onepunch.Auth.Core.Services;
using Onepunch.Common.Lib.DTO;
using OnePunch.Auth.Core.Hubs;
using OnePunch.Auth.Core.Messaging;
using OnePunch.Auth.Core.Services;
using Xunit;

namespace Onepunch.Auth.Api.Tests.Messaging;

/// <summary>
/// An account's subscription changed: every member of its companies (and the owner) gets one
/// "SubscriptionChanged" push so their sessions refetch entitlements -- nothing to re-mint, since
/// entitlements aren't in the token.
/// </summary>
public class SubscriptionChangedWorkerTests
{
    private static (SubscriptionChangedWorker Worker, Mock<ITenantNotificationClient> Client, List<string> Addressed) CreateSut()
    {
        var addressed = new List<string>();
        var client = new Mock<ITenantNotificationClient>();
        var hubClients = new Mock<IHubClients<ITenantNotificationClient>>();
        hubClients.Setup(c => c.Users(It.IsAny<IReadOnlyList<string>>()))
            .Callback<IReadOnlyList<string>>(ids => addressed.AddRange(ids))
            .Returns(client.Object);
        var hubContext = new Mock<IHubContext<TenantHub, ITenantNotificationClient>>();
        hubContext.Setup(h => h.Clients).Returns(hubClients.Object);
        return (new SubscriptionChangedWorker(new TenantNotificationService(hubContext.Object)), client, addressed);
    }

    private static MassTransit.ConsumeContext<SubscriptionChanged> ContextFor(SubscriptionChanged message)
    {
        var context = new Mock<MassTransit.ConsumeContext<SubscriptionChanged>>();
        context.SetupGet(c => c.Message).Returns(message);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return context.Object;
    }

    [Fact]
    public async Task Consume_PushesOnce_ToEveryMemberAndTheOwner()
    {
        var (worker, client, addressed) = CreateSut();
        var owner = Guid.NewGuid();
        var member = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        await worker.Consume(ContextFor(new SubscriptionChanged
        {
            OwnerUserId = owner,
            TenantIds = [tenantId],
            MemberUserIds = [member, owner, Guid.Empty],
            Reason = "trial.expired",
        }));

        addressed.Should().BeEquivalentTo([owner.ToString(), member.ToString()]);
        client.Verify(c => c.SubscriptionChanged(It.Is<SubscriptionChangedNotification>(n =>
            n.TenantIds.Contains(tenantId) && n.Reason == "trial.expired")), Times.Once);
    }

    [Fact]
    public async Task Consume_NoOneToTell_PushesNothing()
    {
        var (worker, client, _) = CreateSut();

        await worker.Consume(ContextFor(new SubscriptionChanged()));

        client.Verify(c => c.SubscriptionChanged(It.IsAny<SubscriptionChangedNotification>()), Times.Never);
    }
}

/// <summary>Which invitation roles take one of the plan's admin seats (admins.max).</summary>
public class InvitationAdminSeatTests
{
    [Theory]
    [InlineData(new[] { "Employee" }, false)]
    [InlineData(new[] { "Client" }, false)]
    [InlineData(new[] { " employee " }, false)]
    [InlineData(new[] { "Admin" }, true)]
    [InlineData(new[] { "None" }, true)]
    [InlineData(new[] { "Payroll Officer" }, true)]
    [InlineData(new[] { "Employee", "Admin" }, true)]
    public void TakesAdminSeat_AnyRoleBeyondThePortalOnes(string[] roles, bool expected)
    {
        InvitationPolicy.TakesAdminSeat(roles).Should().Be(expected);
    }
}
