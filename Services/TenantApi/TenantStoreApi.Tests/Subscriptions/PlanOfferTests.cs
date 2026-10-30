using FluentAssertions;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Subscriptions;

/// <summary>Private plans: offered to chosen accounts, invisible to everyone else.</summary>
public class PlanOfferTests
{
    private const string PrivateCode = "acme-custom";

    /// <summary>A private plan with Premium's entitlements, so an account fits it.</summary>
    private static async Task<Plan> AddPrivatePlanAsync(SubscriptionTestHost host)
    {
        var premium = host.Uow.Context.Plans.Single(p => p.Code == "premium");
        var premiumEntitlements = host.Uow.Context.PlanEntitlements.Where(e => e.PlanId == premium.Id).ToList();
        var plan = new Plan
        {
            Id = Guid.NewGuid(), Code = PrivateCode, Name = "Acme Custom", SortOrder = 50, IsPublic = false,
            MonthlyPrice = 7000, AnnualPrice = 70000, Currency = "PHP",
        };
        host.Uow.Context.Plans.Add(plan);
        host.Uow.Context.PlanEntitlements.AddRange(premiumEntitlements.Select(e => new PlanEntitlement
        {
            Id = Guid.NewGuid(), PlanId = plan.Id, Key = e.Key, Value = e.Value,
        }));
        await host.SaveAsync();
        return plan;
    }

    private static async Task<List<string>> VisibleCodesAsync(SubscriptionTestHost host, Guid owner) =>
        (await host.Subscriptions.GetVisiblePlansAsync(owner)).Select(p => p.Code).ToList();

    [Fact]
    public async Task PrivatePlan_IsHiddenFromAccountsItWasNotOfferedTo()
    {
        var host = new SubscriptionTestHost();
        await AddPrivatePlanAsync(host);
        var (offered, _) = await host.TrialAccountAsync("a@acme.ph");
        var (other, _) = await host.TrialAccountAsync("b@other.ph");

        await host.Subscriptions.OfferPlanAsync(PrivateCode, offered, null, "platform");
        await host.SaveAsync();

        (await VisibleCodesAsync(host, offered)).Should().Contain(PrivateCode);
        (await VisibleCodesAsync(host, other)).Should().NotContain(PrivateCode);
    }

    [Fact]
    public async Task OfferedAccount_StillSeesThePublicCatalog()
    {
        var host = new SubscriptionTestHost();
        await AddPrivatePlanAsync(host);
        var (owner, _) = await host.TrialAccountAsync();
        await host.Subscriptions.OfferPlanAsync(PrivateCode, owner, null, "platform");
        await host.SaveAsync();

        (await VisibleCodesAsync(host, owner)).Should()
            .Contain(new[] { "timekeeping", "payroll", "premium", PrivateCode })
            .And.NotContain("legacy-free-trial");
    }

    [Fact]
    public async Task TrialingAccount_NeverSeesTheFreeTrialPlan_InPlanSelection()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();

        host.SubscriptionOf(owner).Plan.Code.Should().Be("legacy-free-trial");
        (await VisibleCodesAsync(host, owner)).Should()
            .BeEquivalentTo("timekeeping", "payroll", "premium");
        await Assert.ThrowsAsync<SubscriptionRuleException>(() =>
            host.Subscriptions.RequestPlanChangeAsync(owner, "legacy-free-trial", BillingCycle.Monthly, "owner"));
    }

    [Fact]
    public async Task ExpiredOffer_HidesThePlan()
    {
        var host = new SubscriptionTestHost();
        await AddPrivatePlanAsync(host);
        var (owner, _) = await host.TrialAccountAsync();
        await host.Subscriptions.OfferPlanAsync(PrivateCode, owner, SubscriptionTestHost.Start.AddDays(10), "platform");
        await host.SaveAsync();
        (await VisibleCodesAsync(host, owner)).Should().Contain(PrivateCode);

        host.Clock.Advance(TimeSpan.FromDays(11));

        (await VisibleCodesAsync(host, owner)).Should().NotContain(PrivateCode);
    }

    [Fact]
    public async Task RequestChange_ToAPrivatePlanNotOfferedToYou_IsRejected()
    {
        var host = new SubscriptionTestHost();
        await AddPrivatePlanAsync(host);
        var (offered, _) = await host.TrialAccountAsync("a@acme.ph");
        var (other, _) = await host.TrialAccountAsync("b@other.ph");
        await host.Subscriptions.OfferPlanAsync(PrivateCode, offered, null, "platform");
        await host.SaveAsync();

        var denied = () => host.Subscriptions.RequestPlanChangeAsync(other, PrivateCode, BillingCycle.Monthly, "owner");
        await denied.Should().ThrowAsync<SubscriptionRuleException>();

        var quote = await host.Subscriptions.RequestPlanChangeAsync(offered, PrivateCode, BillingCycle.Monthly, "owner");
        quote.PlanCode.Should().Be(PrivateCode);
        quote.AmountDue.Should().Be(7000);
    }

    [Fact]
    public async Task OfferingTwice_KeepsOneOffer_AndUpdatesItsExpiry()
    {
        var host = new SubscriptionTestHost();
        await AddPrivatePlanAsync(host);
        var (owner, _) = await host.TrialAccountAsync();

        await host.Subscriptions.OfferPlanAsync(PrivateCode, owner, null, "platform");
        await host.SaveAsync();
        var expiry = SubscriptionTestHost.Start.AddDays(30);
        await host.Subscriptions.OfferPlanAsync(PrivateCode, owner, expiry, "platform");
        await host.SaveAsync();

        var offers = await host.Subscriptions.GetOffersAsync(PrivateCode);
        offers.Should().ContainSingle().Which.ExpiresAtUtc.Should().Be(expiry);
    }

    [Fact]
    public async Task Revoking_HidesThePlan_ButLeavesAnAccountOnItWhereItIs()
    {
        var host = new SubscriptionTestHost();
        var plan = await AddPrivatePlanAsync(host);
        var (owner, _) = await host.TrialAccountAsync();
        await host.Subscriptions.OfferPlanAsync(PrivateCode, owner, null, "platform");
        await host.SaveAsync();
        await host.Subscriptions.SetPlanAsync(owner, new SetPlanCommand { PlanCode = PrivateCode }, "platform");
        await host.SaveAsync();

        await host.Subscriptions.RevokeOfferAsync(PrivateCode, owner, "platform");
        await host.SaveAsync();

        host.SubscriptionOf(owner).PlanId.Should().Be(plan.Id);
        (await VisibleCodesAsync(host, owner)).Should().NotContain(PrivateCode);
    }

    [Fact]
    public async Task AccountOnAPrivatePlan_KeepsSeeingItAfterTheOfferExpires()
    {
        var host = new SubscriptionTestHost();
        await AddPrivatePlanAsync(host);
        var (owner, _) = await host.TrialAccountAsync();
        await host.Subscriptions.OfferPlanAsync(PrivateCode, owner, SubscriptionTestHost.Start.AddDays(10), "platform");
        await host.SaveAsync();
        await host.Subscriptions.SetPlanAsync(owner, new SetPlanCommand { PlanCode = PrivateCode }, "platform");
        await host.SaveAsync();

        host.Clock.Advance(TimeSpan.FromDays(11));

        (await VisibleCodesAsync(host, owner)).Should().Contain(PrivateCode);
    }

    [Fact]
    public async Task Revoking_AnOfferThatDoesNotExist_Throws()
    {
        var host = new SubscriptionTestHost();
        await AddPrivatePlanAsync(host);
        var (owner, _) = await host.TrialAccountAsync();

        var revoke = () => host.Subscriptions.RevokeOfferAsync(PrivateCode, owner, "platform");

        await revoke.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Offer_IsRecordedInTheBillingHistory()
    {
        var host = new SubscriptionTestHost();
        await AddPrivatePlanAsync(host);
        var (owner, _) = await host.TrialAccountAsync();

        await host.Subscriptions.OfferPlanAsync(PrivateCode, owner, null, "platform");
        await host.SaveAsync();
        await host.Subscriptions.RevokeOfferAsync(PrivateCode, owner, "platform");
        await host.SaveAsync();

        host.Uow.Context.BillingEvents.Where(e => e.OwnerUserId == owner).Select(e => e.Type).ToList().Should()
            .Contain(new[] { BillingEventTypes.PlanOffered, BillingEventTypes.PlanOfferRevoked });
    }
}
