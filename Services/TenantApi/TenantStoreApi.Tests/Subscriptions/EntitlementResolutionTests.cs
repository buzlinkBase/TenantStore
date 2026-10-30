using FluentAssertions;
using Onepunch.Common.Lib.Subscriptions;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Subscriptions;

/// <summary>
/// Plan, then account overrides, then company overrides: later layers win, expired ones don't
/// count, and an override can take a feature away as well as give one.
/// </summary>
public class EntitlementResolutionTests
{
    [Fact]
    public async Task Trial_GetsFullPremium()
    {
        var host = new SubscriptionTestHost();
        var (_, tenantId) = await host.TrialAccountAsync();

        var snapshot = await host.Resolver.ResolveForTenantAsync(tenantId);

        snapshot.Status.Should().Be(nameof(AccountSubscriptionStatus.Trialing));
        snapshot.AccessMode.Should().Be(SubscriptionAccessMode.Full);
        snapshot.PlanCode.Should().Be("legacy-free-trial");
        snapshot.PlanName.Should().Be("Free Trial");
        snapshot.Features.Where(f => !EntitlementKeys.SeparateSystems.Contains(f.Key)).Select(f => f.Value)
            .Should().AllBeEquivalentTo(true);
        snapshot.Features.Where(f => EntitlementKeys.SeparateSystems.Contains(f.Key)).Select(f => f.Value)
            .Should().AllBeEquivalentTo(false, "the separate systems are sold on top of a plan, a trial included");
        snapshot.LimitOf(EntitlementKeys.EmployeesMax).Should().BeNull("Premium has no employee limit");
        snapshot.LimitOf(EntitlementKeys.CompaniesMax).Should().Be(5);
    }

    [Fact]
    public async Task AccountOverride_CanSwitchOn_ASeparateSystem_ForOneAccount()
    {
        var host = new SubscriptionTestHost();
        var (owner, tenantId) = await host.TrialAccountAsync();

        await host.Subscriptions.AddOverrideAsync(owner, new AddOverrideCommand { Key = EntitlementKeys.ProcurementCore, Value = "true" }, "platform");
        await host.SaveAsync();

        var snapshot = await host.Resolver.ResolveForTenantAsync(tenantId);
        snapshot.Features[EntitlementKeys.ProcurementCore].Should().BeTrue();
        snapshot.Features[EntitlementKeys.AccountingCore].Should().BeFalse();
        snapshot.Features[EntitlementKeys.BillingCore].Should().BeFalse();
    }

    [Fact]
    public async Task AccountOverride_CanRevoke_AFeatureThePlanIncludes()
    {
        var host = new SubscriptionTestHost();
        var (owner, tenantId) = await host.TrialAccountAsync();

        await host.Subscriptions.AddOverrideAsync(owner, new AddOverrideCommand { Key = EntitlementKeys.Analytics, Value = "false" }, "platform");
        await host.SaveAsync();

        var snapshot = await host.Resolver.ResolveForTenantAsync(tenantId);
        snapshot.HasFeature(EntitlementKeys.Analytics).Should().BeFalse();
        snapshot.Sources[EntitlementKeys.Analytics].Should().Be(EntitlementSources.AccountOverride);
        snapshot.HasFeature(EntitlementKeys.AuditTrail).Should().BeTrue("only the overridden key changes");
    }

    [Fact]
    public async Task CompanyOverride_BeatsAccountOverride_ForThatCompanyOnly()
    {
        var host = new SubscriptionTestHost();
        var (owner, firstCompany) = await host.TrialAccountAsync();
        var secondCompany = host.AddCompany(owner, "Beta");

        await host.Subscriptions.AddOverrideAsync(owner, new AddOverrideCommand { Key = EntitlementKeys.Analytics, Value = "false" }, "platform");
        await host.Subscriptions.AddOverrideAsync(owner, new AddOverrideCommand { TenantId = secondCompany, Key = EntitlementKeys.Analytics, Value = "true" }, "platform");
        await host.SaveAsync();

        (await host.Resolver.ResolveForTenantAsync(firstCompany)).HasFeature(EntitlementKeys.Analytics).Should().BeFalse();
        var second = await host.Resolver.ResolveForTenantAsync(secondCompany);
        second.HasFeature(EntitlementKeys.Analytics).Should().BeTrue();
        second.Sources[EntitlementKeys.Analytics].Should().Be(EntitlementSources.CompanyOverride);
    }

    [Fact]
    public async Task ExpiredOverride_IsIgnored()
    {
        var host = new SubscriptionTestHost();
        var (owner, tenantId) = await host.TrialAccountAsync();

        await host.Subscriptions.AddOverrideAsync(owner, new AddOverrideCommand
        {
            Key = EntitlementKeys.Analytics, Value = "false", ExpiresAtUtc = host.Clock.UtcNow.AddDays(1),
        }, "platform");
        await host.SaveAsync();
        host.Clock.Advance(TimeSpan.FromDays(2));

        (await host.Resolver.ResolveForTenantAsync(tenantId)).HasFeature(EntitlementKeys.Analytics).Should().BeTrue();
    }

    [Fact]
    public async Task LimitOverride_ChangesTheLimit()
    {
        var host = new SubscriptionTestHost();
        var (owner, tenantId) = await host.TrialAccountAsync();
        await host.Subscriptions.SetPlanAsync(owner, new SetPlanCommand { PlanCode = "timekeeping", PeriodEndUtc = host.Clock.UtcNow.AddMonths(1) }, "platform");
        await host.Subscriptions.AddOverrideAsync(owner, new AddOverrideCommand { Key = EntitlementKeys.EmployeesMax, Value = "500" }, "platform");
        await host.SaveAsync();

        var snapshot = await host.Resolver.ResolveForTenantAsync(tenantId);

        snapshot.LimitOf(EntitlementKeys.EmployeesMax).Should().Be(500);
        snapshot.HasFeature(EntitlementKeys.PayrollCore).Should().BeFalse("Timekeeping doesn't include payroll");
    }

    [Theory]
    [InlineData(EntitlementKeys.Analytics, "maybe")]
    [InlineData(EntitlementKeys.EmployeesMax, "-5")]
    [InlineData("made.up", "true")]
    public async Task InvalidOverride_IsRefused(string key, string value)
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();

        var act = () => host.Subscriptions.AddOverrideAsync(owner, new AddOverrideCommand { Key = key, Value = value }, "platform");

        await act.Should().ThrowAsync<SubscriptionRuleException>();
    }

    [Fact]
    public async Task ExpiredTrial_IsReadOnly_ButKeepsItsFeatures_ForViewing()
    {
        var host = new SubscriptionTestHost();
        var (owner, tenantId) = await host.TrialAccountAsync();
        host.SubscriptionOf(owner).SubStatus = AccountSubscriptionStatus.Expired;
        await host.SaveAsync();

        var snapshot = await host.Resolver.ResolveForTenantAsync(tenantId);

        snapshot.IsReadOnly.Should().BeTrue();
        snapshot.HasFeature(EntitlementKeys.PayrollCore).Should().BeTrue();
    }

    [Fact]
    public async Task EnforcementOff_AllowsEverything()
    {
        var host = new SubscriptionTestHost();
        var (owner, tenantId) = await host.TrialAccountAsync();
        host.SubscriptionOf(owner).SubStatus = AccountSubscriptionStatus.Suspended;
        await host.SaveAsync();
        host.Options.EnforcementEnabled = false;

        var snapshot = await host.Resolver.ResolveForTenantAsync(tenantId);

        snapshot.IsReadOnly.Should().BeFalse();
        snapshot.LimitOf(EntitlementKeys.CompaniesMax).Should().BeNull();
        (await host.Resolver.ResolveActualForTenantAsync(tenantId)).IsReadOnly.Should().BeTrue("the billing screen still shows the real state");
    }

    [Fact]
    public async Task CompanyWithoutOwner_IsLeftOpen()
    {
        var host = new SubscriptionTestHost();
        var tenantId = host.AddCompany(Guid.Empty);

        var snapshot = await host.Resolver.ResolveForTenantAsync(tenantId);

        snapshot.Status.Should().Be("Unowned");
        snapshot.IsReadOnly.Should().BeFalse();
    }
}
