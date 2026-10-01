using BuzlinkRepository;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Options;
using Moq;
using Onepunch.Common.Lib.DTO;
using Onepunch.Common.Lib.Subscriptions;
using TenantStoreApi.Core.Messaging;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Subscriptions;

/// <summary>
/// An account needs a plan only once it created its own workspace AND that workspace finished
/// setting up. Members of other companies (e.g. invited employees) never get one, and what they
/// see follows the company they're in -- not their own account's plan.
/// </summary>
public class WorkspaceActivationTests
{
    private static TenantActivatedWorker Worker(SubscriptionTestHost host) =>
        new(host.Uow, Mock.Of<ITenantProvider>(), host.Subscriptions, host.Notifier, host.Clock);

    private static ConsumeContext<HrisOrgProvisionedPayload> Provisioned(Guid tenantId, string status = "Active")
    {
        var context = new Mock<ConsumeContext<HrisOrgProvisionedPayload>>();
        context.SetupGet(c => c.Message).Returns(new HrisOrgProvisionedPayload { TenantId = tenantId, Status = status });
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return context.Object;
    }

    // ── Activation starts the trial ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SetupFinished_ActivatesTheCompany_AndStartsTheOwnersTrial()
    {
        var host = new SubscriptionTestHost();
        var owner = Guid.NewGuid();
        var tenantId = host.AddCompany(owner, activated: false);

        await Worker(host).Consume(Provisioned(tenantId));

        host.Uow.Context.Tenants.Single(t => t.Id == tenantId).ActivatedAtUtc.Should().Be(host.Clock.UtcNow);
        host.SubscriptionOf(owner).SubStatus.Should().Be(AccountSubscriptionStatus.Trialing);
        host.Publisher.Verify(p => p.Publish(
            It.Is<SubscriptionChanged>(m => m.OwnerUserId == owner), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("Failed")]
    [InlineData("error: database not created")]
    public async Task SetupFailed_ActivatesNothing_AndKeepsTheTrialUnused(string status)
    {
        var host = new SubscriptionTestHost();
        var owner = Guid.NewGuid();
        var tenantId = host.AddCompany(owner, activated: false);

        await Worker(host).Consume(Provisioned(tenantId, status));

        host.Uow.Context.Tenants.Single(t => t.Id == tenantId).ActivatedAtUtc.Should().BeNull();
        host.Uow.Context.AccountSubscriptions.Should().BeEmpty();
    }

    [Fact]
    public async Task ASecondCompany_NeverStartsAnotherTrial()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();
        host.SubscriptionOf(owner).SubStatus = AccountSubscriptionStatus.Expired;
        await host.SaveAsync();
        var second = host.AddCompany(owner, "Second", activated: false);

        await Worker(host).Consume(Provisioned(second));

        host.Uow.Context.AccountSubscriptions.Should().ContainSingle();
        host.SubscriptionOf(owner).SubStatus.Should().Be(AccountSubscriptionStatus.Expired);
    }

    [Fact]
    public async Task Redelivery_IsANoOp()
    {
        var host = new SubscriptionTestHost();
        var owner = Guid.NewGuid();
        var tenantId = host.AddCompany(owner, activated: false);
        await Worker(host).Consume(Provisioned(tenantId));
        var activatedAt = host.Uow.Context.Tenants.Single(t => t.Id == tenantId).ActivatedAtUtc;
        host.Clock.Advance(TimeSpan.FromHours(1));

        await Worker(host).Consume(Provisioned(tenantId));

        host.Uow.Context.Tenants.Single(t => t.Id == tenantId).ActivatedAtUtc.Should().Be(activatedAt);
        host.Uow.Context.AccountSubscriptions.Should().ContainSingle();
    }

    // ── Only activated companies are the account's ───────────────────────────────────────────

    [Fact]
    public async Task UnfinishedCompanies_DontCount()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();
        host.AddCompany(owner, "Abandoned", activated: false);

        (await host.Usage.CountOwnedCompaniesAsync(owner)).Should().Be(1);
        (await host.Usage.GetCompanyUsageAsync(owner)).Should().ContainSingle();
    }

    [Fact]
    public async Task StillSettingUp_WithNoPlanYet_IsOpen_NotReadOnly()
    {
        var host = new SubscriptionTestHost();
        var tenantId = host.AddCompany(Guid.NewGuid(), activated: false);

        var snapshot = await host.Resolver.ResolveForTenantAsync(tenantId);

        snapshot.Status.Should().Be(EntitlementResolverService.ProvisioningStatus);
        snapshot.IsReadOnly.Should().BeFalse();
    }

    [Fact]
    public async Task StillSettingUp_UnderAnExistingPlan_UsesThatPlan()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();
        host.SubscriptionOf(owner).SubStatus = AccountSubscriptionStatus.Expired;
        await host.SaveAsync();
        var second = host.AddCompany(owner, "Second", activated: false);

        (await host.Resolver.ResolveForTenantAsync(second)).IsReadOnly.Should().BeTrue();
    }

    // ── Backfill ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Backfill_OnlyOwnersOfActivatedCompanies()
    {
        var host = new SubscriptionTestHost();
        var activatedOwner = Guid.NewGuid();
        var unfinishedOwner = Guid.NewGuid();
        var activatedCompany = host.AddCompany(activatedOwner, "Live");
        host.AddCompany(unfinishedOwner, "Never finished", activated: false);
        var employee = host.AddMember(activatedCompany, "Employee");

        var backfill = new SubscriptionBackfillService(host.Uow, host.Events, Options.Create(host.Options), host.Clock);
        (await backfill.EnsureBackfilledAsync(CancellationToken.None)).Should().Be(1);

        host.Uow.Context.AccountSubscriptions.Select(s => s.OwnerUserId).Should().Equal(activatedOwner);
        host.Uow.Context.AccountSubscriptions.Should().NotContain(s => s.OwnerUserId == employee || s.OwnerUserId == unfinishedOwner);
    }

    // ── What you see follows the company you're in ───────────────────────────────────────────

    [Fact]
    public async Task OwnTrialEnded_ButAMemberElsewhere_WorksNormallyThere()
    {
        var host = new SubscriptionTestHost();
        var (accountA, companyA) = await host.TrialAccountAsync("a@alpha.ph");
        host.SubscriptionOf(accountA).SubStatus = AccountSubscriptionStatus.Expired;
        var accountB = Guid.NewGuid();
        var companyB = host.AddCompany(accountB, "Beta", "b@beta.ph");
        await host.Subscriptions.SetPlanAsync(accountB,
            new SetPlanCommand { PlanCode = "payroll", PeriodEndUtc = host.Clock.UtcNow.AddMonths(1) }, "platform");
        await host.SaveAsync();
        host.AddMember(companyB, "Admin", userId: accountA);

        var inB = await host.Resolver.ResolveForTenantAsync(companyB);
        var inA = await host.Resolver.ResolveForTenantAsync(companyA);

        inB.IsReadOnly.Should().BeFalse("company B runs on B's plan, whatever A's own trial did");
        inB.OwnerUserId.Should().Be(accountB);
        inA.IsReadOnly.Should().BeTrue("back in A's own company, A's expired trial applies");
        (await host.Allowance.CheckAdminSeatsAsync(companyB)).ProblemType
            .Should().NotBe(SubscriptionProblemTypes.ReadOnly);
    }
}
