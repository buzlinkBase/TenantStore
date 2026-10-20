using FluentAssertions;
using TenantStoreApi.Core.Services.Platform;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Platform;

/// <summary>A client on a custom plan can be kept from seeing the public plans and storage offerings.</summary>
public class PublicOfferingVisibilityTests
{
    private const string Operator = "platform:ops@onepunch.test";
    private const string PrivateCode = "acme-custom";

    private static PlatformAccountActionsService Actions(SubscriptionTestHost host) => new(
        host.Uow, host.Subscriptions, host.Events, host.Notifier, host.StateMachine,
        Microsoft.Extensions.Options.Options.Create(host.Options), host.Gateway, host.Clock, host.StoragePeriod);

    private static async Task AddPrivatePlanAsync(SubscriptionTestHost host)
    {
        var premium = host.Uow.Context.Plans.Single(p => p.Code == "premium");
        var plan = new Plan
        {
            Id = Guid.NewGuid(), Code = PrivateCode, Name = "Acme Custom", SortOrder = 50, IsPublic = false,
            MonthlyPrice = 7000, AnnualPrice = 70000, Currency = "PHP",
        };
        host.Uow.Context.Plans.Add(plan);
        host.Uow.Context.PlanEntitlements.AddRange(host.Uow.Context.PlanEntitlements.Where(e => e.PlanId == premium.Id).ToList()
            .Select(e => new PlanEntitlement { Id = Guid.NewGuid(), PlanId = plan.Id, Key = e.Key, Value = e.Value }));
        await host.SaveAsync();
    }

    private static async Task<List<string>> VisiblePlansAsync(SubscriptionTestHost host, Guid owner) =>
        (await host.Subscriptions.GetVisiblePlansAsync(owner)).Select(p => p.Code).ToList();

    private static async Task<Guid> CustomClientAsync(SubscriptionTestHost host)
    {
        await AddPrivatePlanAsync(host);
        var (owner, _) = await host.TrialAccountAsync();
        await host.Subscriptions.OfferPlanAsync(PrivateCode, owner, null, "platform");
        await host.SaveAsync();
        return owner;
    }

    [Fact]
    public async Task ByDefault_EveryAccountSeesThePublicPlans()
    {
        var host = new SubscriptionTestHost();
        var owner = await CustomClientAsync(host);

        host.SubscriptionOf(owner).ShowPublicOffering.Should().BeTrue();
        (await VisiblePlansAsync(host, owner)).Should().Contain(["timekeeping", "payroll", "premium", PrivateCode]);
    }

    [Fact]
    public async Task Hidden_TheAccountSeesOnlyItsCustomPlan_AndCannotPickAPublicOne()
    {
        var host = new SubscriptionTestHost();
        var owner = await CustomClientAsync(host);

        await Actions(host).SetPublicOfferingVisibilityAsync(owner, show: false, "Custom contract", Operator);
        await host.SaveAsync();

        (await VisiblePlansAsync(host, owner)).Should().Equal(PrivateCode);
        var pickPublic = () => host.Subscriptions.RequestPlanChangeAsync(owner, "payroll", BillingCycle.Monthly, "owner");
        await pickPublic.Should().ThrowAsync<SubscriptionRuleException>();
        await host.Subscriptions.RequestPlanChangeAsync(owner, PrivateCode, BillingCycle.Monthly, "owner");
    }

    [Fact]
    public async Task Hidden_OnlyThatAccountIsAffected_AndTheAnonymousCatalogStaysPublic()
    {
        var host = new SubscriptionTestHost();
        var owner = await CustomClientAsync(host);
        var (other, _) = await host.TrialAccountAsync("other@acme.ph");

        await Actions(host).SetPublicOfferingVisibilityAsync(owner, show: false, "Custom contract", Operator);
        await host.SaveAsync();

        (await VisiblePlansAsync(host, other)).Should().Contain(["timekeeping", "payroll", "premium"]);
        (await host.Subscriptions.GetVisiblePlansAsync(null)).Select(p => p.Code).Should().Contain(["timekeeping", "payroll", "premium"]);
    }

    [Fact]
    public async Task Hidden_TheHeldCustomPlanStaysRenewable_AfterItsOfferExpires()
    {
        var host = new SubscriptionTestHost();
        await AddPrivatePlanAsync(host);
        var (owner, _) = await host.TrialAccountAsync();
        await host.Subscriptions.OfferPlanAsync(PrivateCode, owner, SubscriptionTestHost.Start.AddDays(10), "platform");
        await host.Subscriptions.SetPlanAsync(owner, new SetPlanCommand { PlanCode = PrivateCode, PeriodEndUtc = host.Clock.UtcNow.AddMonths(1) }, "platform");
        await host.SaveAsync();
        await Actions(host).SetPublicOfferingVisibilityAsync(owner, show: false, "Custom contract", Operator);
        await host.SaveAsync();

        host.Clock.Advance(TimeSpan.FromDays(20));

        (await VisiblePlansAsync(host, owner)).Should().Equal(PrivateCode);
    }

    [Fact]
    public async Task Hidden_PublicStorageOfferingsAreHidden_TooButOfferedOnesStay()
    {
        var host = new SubscriptionTestHost();
        var owner = await CustomClientAsync(host);
        await host.StorageCatalog.CreateAsync(new CreateStorageOfferingCommand
        {
            Code = "storage-200gb", Name = "200 GB block", BlockMb = 200 * 1024, MonthlyPrice = 1400m, AnnualPrice = 14000m,
        }, "platform");
        await host.SaveAsync();
        await host.StorageCatalog.OfferAsync("storage-200gb", owner, null, "platform");
        await host.SaveAsync();
        (await host.StorageCatalog.GetVisibleAsync(owner)).Should().HaveCountGreaterThan(1);

        await Actions(host).SetPublicOfferingVisibilityAsync(owner, show: false, "Custom contract", Operator);
        await host.SaveAsync();

        (await host.StorageCatalog.GetVisibleAsync(owner)).Select(o => o.Code).Should().Equal("storage-200gb");
        var buyPublic = () => host.StorageCatalog.RequireVisibleAsync(host.Uow.Context.StorageAddOnOfferings.First(o => o.IsPublic).Code, owner);
        await buyPublic.Should().ThrowAsync<SubscriptionRuleException>();
    }

    [Fact]
    public async Task Hiding_IsRefused_WhenTheAccountHasNoPrivatePlanToRenewWith()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();

        var act = () => Actions(host).SetPublicOfferingVisibilityAsync(owner, show: false, "Custom contract", Operator);

        (await act.Should().ThrowAsync<SubscriptionRuleException>()).Which.Message.Should().Contain("Offer this account a plan first");
        host.SubscriptionOf(owner).ShowPublicOffering.Should().BeTrue();
    }

    [Fact]
    public async Task Showing_RestoresThePublicPlans_AndEachChangeIsRecordedWithItsReason()
    {
        var host = new SubscriptionTestHost();
        var owner = await CustomClientAsync(host);
        var actions = Actions(host);

        await actions.SetPublicOfferingVisibilityAsync(owner, show: false, "  Custom contract ", Operator);
        await host.SaveAsync();
        await actions.SetPublicOfferingVisibilityAsync(owner, show: true, "Contract ended", Operator);
        await host.SaveAsync();

        (await VisiblePlansAsync(host, owner)).Should().Contain(["timekeeping", "payroll", "premium", PrivateCode]);
        var events = host.Uow.Context.BillingEvents.Where(e => e.Type == BillingEventTypes.PublicOfferingVisibilityChanged).ToList();
        events.Should().HaveCount(2);
        events.Should().OnlyContain(e => e.Actor == Operator);
        events.Select(e => e.PayloadJson).Should().Contain(p => p!.Contains("Custom contract") && p.Contains("false"));
    }

    [Fact]
    public async Task ChangingToTheCurrentSetting_NeedsAReason_AndIsRejected()
    {
        var host = new SubscriptionTestHost();
        var owner = await CustomClientAsync(host);

        var noReason = () => Actions(host).SetPublicOfferingVisibilityAsync(owner, show: false, " ", Operator);
        var already = () => Actions(host).SetPublicOfferingVisibilityAsync(owner, show: true, "Nothing to change", Operator);

        await noReason.Should().ThrowAsync<SubscriptionRuleException>();
        await already.Should().ThrowAsync<SubscriptionRuleException>();
    }

    [Fact]
    public async Task TheAccountDetail_ReportsTheSetting()
    {
        var host = new SubscriptionTestHost();
        var owner = await CustomClientAsync(host);
        var directory = new PlatformDirectoryService(host.Uow, host.StateMachine);
        (await directory.GetAccountAsync(owner)).ShowPublicOffering.Should().BeTrue();

        await Actions(host).SetPublicOfferingVisibilityAsync(owner, show: false, "Custom contract", Operator);
        await host.SaveAsync();

        (await directory.GetAccountAsync(owner)).ShowPublicOffering.Should().BeFalse();
    }
}
