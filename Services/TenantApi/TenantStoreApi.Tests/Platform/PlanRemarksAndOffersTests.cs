using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using TenantStoreApi.Controllers;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Platform;

/// <summary>A plan's internal remarks (console only) and the private plans offered to one account.</summary>
public class PlanRemarksAndOffersTests
{
    private const string Operator = "platform:ops@onepunch.test";

    private static PlanCatalogService Catalog(SubscriptionTestHost host) => new(host.Uow, host.Events, host.Notifier);

    private static Task<Plan> CreateAsync(SubscriptionTestHost host, string code, string? remarks = null) =>
        Catalog(host).CreateAsync(new CreatePlanCommand { Code = code, Name = code, MonthlyPrice = 1000, AnnualPrice = 10000, Remarks = remarks }, Operator);

    // ── Remarks ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_StoresTrimmedRemarks_AndBlankMeansNone()
    {
        var host = new SubscriptionTestHost();

        var withRemarks = await CreateAsync(host, "acme", "  Custom for Acme, 20% off  ");
        var blank = await CreateAsync(host, "plain", "   ");
        await host.SaveAsync();

        withRemarks.Remarks.Should().Be("Custom for Acme, 20% off");
        blank.Remarks.Should().BeNull();
    }

    [Fact]
    public async Task Update_ChangesClearsAndLeavesRemarks()
    {
        var host = new SubscriptionTestHost();
        await CreateAsync(host, "acme", "First note");
        await host.SaveAsync();
        var catalog = Catalog(host);

        await catalog.UpdateAsync("acme", new UpdatePlanCommand { Name = "Acme" }, Operator);
        host.Uow.Context.Plans.Single(p => p.Code == "acme").Remarks.Should().Be("First note", "a missing field leaves it as is");

        await catalog.UpdateAsync("acme", new UpdatePlanCommand { Remarks = " Second note " }, Operator);
        host.Uow.Context.Plans.Single(p => p.Code == "acme").Remarks.Should().Be("Second note");

        await catalog.UpdateAsync("acme", new UpdatePlanCommand { Remarks = "" }, Operator);
        host.Uow.Context.Plans.Single(p => p.Code == "acme").Remarks.Should().BeNull();
    }

    [Fact]
    public async Task Remarks_LongerThanTheLimit_AreRejected()
    {
        var host = new SubscriptionTestHost();
        var tooLong = new string('x', PlanCatalogService.MaxRemarksLength + 1);

        var create = () => CreateAsync(host, "acme", tooLong);

        await create.Should().ThrowAsync<SubscriptionRuleException>();
    }

    [Fact]
    public async Task TheConsolesPlanList_CarriesRemarks()
    {
        var host = new SubscriptionTestHost();
        await CreateAsync(host, "acme", "Custom for Acme");
        await host.SaveAsync();
        var controller = new PlatformPlansController(Catalog(host));

        var list = ((OkObjectResult)await controller.List(CancellationToken.None)).Value.Should().BeAssignableTo<List<PlatformPlanView>>().Subject;

        list.Single(p => p.Plan.Code == "acme").Remarks.Should().Be("Custom for Acme");
        list.Single(p => p.Plan.Code == "payroll").Remarks.Should().BeNull();
    }

    [Fact]
    public async Task WhatCustomersReceive_NeverCarriesRemarks()
    {
        typeof(PlanView).GetProperties().Select(p => p.Name).Should().NotContain("Remarks");
        var host = new SubscriptionTestHost();
        await CreateAsync(host, "acme", "Internal only");
        var (owner, _) = await host.TrialAccountAsync();
        await host.Subscriptions.OfferPlanAsync("acme", owner, null, "platform");
        await host.SaveAsync();

        var views = await host.Views.GetVisiblePlansAsync(owner);

        views.Should().Contain(p => p.Code == "acme");
        System.Text.Json.JsonSerializer.Serialize(views).Should().NotContain("Internal only").And.NotContain("emarks");
    }

    // ── Plans offered to one account ─────────────────────────────────────────

    [Fact]
    public async Task AnAccount_CanBeOfferedSeveralPrivatePlans_AndSeesAllOfThem()
    {
        var host = new SubscriptionTestHost();
        await CreateAsync(host, "acme-basic");
        await CreateAsync(host, "acme-plus");
        var (owner, _) = await host.TrialAccountAsync();
        await host.Subscriptions.OfferPlanAsync("acme-basic", owner, null, "platform");
        await host.Subscriptions.OfferPlanAsync("acme-plus", owner, null, "platform");
        await host.SaveAsync();

        (await host.Subscriptions.GetVisiblePlansAsync(owner)).Select(p => p.Code).Should().Contain(["acme-basic", "acme-plus"]);
        (await host.Subscriptions.GetOffersForOwnerAsync(owner)).Select(o => o.Plan.Code).Should().BeEquivalentTo("acme-basic", "acme-plus");
    }

    [Fact]
    public async Task OffersForOneAccount_AreOnlyThatAccountsOwn_IncludeExpiredOnes_AndLoseWithdrawnOnes()
    {
        var host = new SubscriptionTestHost();
        await CreateAsync(host, "acme-basic", "Basic for Acme");
        await CreateAsync(host, "acme-plus");
        var (owner, _) = await host.TrialAccountAsync();
        var (other, _) = await host.TrialAccountAsync("other@acme.ph");
        await host.Subscriptions.OfferPlanAsync("acme-basic", owner, SubscriptionTestHost.Start.AddDays(5), "platform");
        await host.Subscriptions.OfferPlanAsync("acme-plus", owner, null, "platform");
        await host.Subscriptions.OfferPlanAsync("acme-plus", other, null, "platform");
        await host.SaveAsync();
        host.Clock.Advance(TimeSpan.FromDays(10));

        var offers = await host.Subscriptions.GetOffersForOwnerAsync(owner);
        offers.Should().HaveCount(2, "the other account's offer isn't included, and an expired offer still is");
        offers.Single(o => o.Plan.Code == "acme-basic").ExpiresAtUtc.Should().BeBefore(host.Clock.UtcNow);

        await host.Subscriptions.RevokeOfferAsync("acme-basic", owner, "platform");
        await host.SaveAsync();
        (await host.Subscriptions.GetOffersForOwnerAsync(owner)).Select(o => o.Plan.Code).Should().Equal("acme-plus");
        (await host.Subscriptions.GetOffersForOwnerAsync(Guid.NewGuid())).Should().BeEmpty();
    }
}
