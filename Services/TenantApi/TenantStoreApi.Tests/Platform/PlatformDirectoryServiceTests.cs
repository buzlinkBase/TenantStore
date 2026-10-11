using FluentAssertions;
using Onepunch.Common.Lib.Subscriptions;
using TenantStoreApi.Core.Services.Platform;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Platform;

/// <summary>The platform console's cross-account read side: accounts, companies and payments.</summary>
public class PlatformDirectoryServiceTests
{
    private static PlatformDirectoryService Directory(SubscriptionTestHost host) => new(host.Uow, host.StateMachine);

    private static async Task<Guid> PaidAccountAsync(SubscriptionTestHost host, string email, string company, string plan = "payroll")
    {
        var owner = Guid.NewGuid();
        host.AddCompany(owner, company, email);
        await host.Subscriptions.StartTrialIfFirstCompanyAsync(owner, email);
        await host.SaveAsync();
        await host.Subscriptions.SetPlanAsync(owner, new SetPlanCommand { PlanCode = plan, PeriodEndUtc = host.Clock.UtcNow.AddMonths(1) }, "platform");
        await host.SaveAsync();
        return owner;
    }

    // ── Accounts ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListAccounts_ShowsOwnerIdentity_CompanyCounts_AndSubscription()
    {
        var host = new SubscriptionTestHost();
        var owner = await PaidAccountAsync(host, "boss@acme.ph", "Acme");
        host.AddCompany(owner, "Acme Branch", "boss@acme.ph", activated: false);

        var page = await Directory(host).ListAccountsAsync(new AccountQuery());

        var account = page.Items.Should().ContainSingle().Subject;
        account.OwnerUserId.Should().Be(owner);
        account.OwnerEmail.Should().Be("boss@acme.ph");
        account.OwnerName.Should().Be("Owner");
        account.Companies.Should().Be(2);
        account.ActivatedCompanies.Should().Be(1);
        account.PlanCode.Should().Be("payroll");
        account.Status.Should().Be(AccountSubscriptionStatus.Active);
        account.CurrentPeriodEndUtc.Should().Be(host.Clock.UtcNow.AddMonths(1));
        page.Total.Should().Be(1);
    }

    [Fact]
    public async Task ListAccounts_IncludesAnOwnerWhoHasNoSubscriptionYet()
    {
        var host = new SubscriptionTestHost();
        var owner = Guid.NewGuid();
        host.AddCompany(owner, "Still Setting Up", "new@acme.ph", activated: false);

        var account = (await Directory(host).ListAccountsAsync(new AccountQuery())).Items.Should().ContainSingle().Subject;

        account.OwnerUserId.Should().Be(owner);
        account.Status.Should().BeNull();
        account.PlanCode.Should().BeNull();
    }

    [Theory]
    [InlineData("boss@acme", 1)]      // owner email
    [InlineData("OWNER", 2)]          // owner name (every seeded owner is "Owner")
    [InlineData("Globex", 1)]         // company name
    [InlineData("nothing-like-this", 0)]
    public async Task ListAccounts_SearchesOwnerEmailNameAndCompanyName(string search, int expected)
    {
        var host = new SubscriptionTestHost();
        await PaidAccountAsync(host, "boss@acme.ph", "Acme");
        await PaidAccountAsync(host, "ceo@other.ph", "Globex");

        var page = await Directory(host).ListAccountsAsync(new AccountQuery(Search: search));

        page.Items.Should().HaveCount(expected);
        page.Total.Should().Be(expected);
    }

    [Fact]
    public async Task ListAccounts_FiltersByStatusAndPlan()
    {
        var host = new SubscriptionTestHost();
        var payroll = await PaidAccountAsync(host, "a@acme.ph", "A", "payroll");
        await PaidAccountAsync(host, "b@acme.ph", "B", "premium");
        var (trialOwner, _) = await host.TrialAccountAsync("c@acme.ph");

        var directory = Directory(host);

        (await directory.ListAccountsAsync(new AccountQuery(PlanCode: "payroll"))).Items.Select(a => a.OwnerUserId)
            .Should().Equal(payroll);
        (await directory.ListAccountsAsync(new AccountQuery(Status: AccountSubscriptionStatus.Trialing))).Items.Select(a => a.OwnerUserId)
            .Should().Equal(trialOwner);
        (await directory.ListAccountsAsync(new AccountQuery(Status: AccountSubscriptionStatus.Active))).Total.Should().Be(2);
    }

    [Fact]
    public async Task ListAccounts_ListsTheNewestAccountFirst_AndPages()
    {
        var host = new SubscriptionTestHost();
        var ids = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            var owner = await PaidAccountAsync(host, $"o{i}@acme.ph", $"Company {i}");
            var tenant = host.Uow.Context.Tenants.Single(t => t.UserId == owner);
            tenant.CreatedAt = SubscriptionTestHost.Start.AddDays(i);
            ids.Add(owner);
        }
        await host.SaveAsync();

        var first = await Directory(host).ListAccountsAsync(new AccountQuery(Page: 1, PageSize: 2));
        var third = await Directory(host).ListAccountsAsync(new AccountQuery(Page: 3, PageSize: 2));

        first.Total.Should().Be(5);
        first.Items.Select(a => a.OwnerUserId).Should().Equal(ids[4], ids[3]);
        third.Items.Select(a => a.OwnerUserId).Should().Equal(ids[0]);
    }

    [Fact]
    public async Task Paging_IsClamped_SoOneRequestCantReadTheWholeTable()
    {
        new PageRequest(Page: 0, PageSize: 100000).SafePageSize.Should().Be(PageRequest.MaxPageSize);
        new PageRequest(Page: -3, PageSize: 0).SafePage.Should().Be(1);
        new PageRequest(Page: -3, PageSize: 0).SafePageSize.Should().Be(1);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task GetAccount_ReturnsTheHeaderTheBillingTabsNeed()
    {
        var host = new SubscriptionTestHost();
        var owner = await PaidAccountAsync(host, "boss@acme.ph", "Acme");
        await host.Subscriptions.AddDiscountAsync(owner, new AddDiscountCommand { Percent = 10, Reason = "Loyal" }, "platform");
        await host.Subscriptions.AddOverrideAsync(owner, new AddOverrideCommand { Key = EntitlementKeys.Analytics, Value = "true" }, "platform");
        await host.SaveAsync();

        var detail = await Directory(host).GetAccountAsync(owner);

        detail.Account.OwnerEmail.Should().Be("boss@acme.ph");
        detail.AccessMode.Should().Be(SubscriptionAccessMode.Full);
        detail.Companies.Should().ContainSingle(c => c.Name == "Acme");
        detail.Discounts.Should().ContainSingle(d => d.Percent == 10 && d.Reason == "Loyal");
        detail.OverrideCount.Should().Be(1);
    }

    [Fact]
    public async Task GetAccount_OfALapsedAccount_ReportsReadOnly()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();
        host.SubscriptionOf(owner).SubStatus = AccountSubscriptionStatus.Expired;
        await host.SaveAsync();

        (await Directory(host).GetAccountAsync(owner)).AccessMode.Should().Be(SubscriptionAccessMode.ReadOnly);
    }

    [Fact]
    public async Task GetAccount_ForAnUnknownOwner_IsNotFound()
    {
        var act = () => Directory(new SubscriptionTestHost()).GetAccountAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ── Companies ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListCompanies_ShowsOwner_ActivationAndAccess()
    {
        var host = new SubscriptionTestHost();
        var owner = await PaidAccountAsync(host, "boss@acme.ph", "Acme");
        host.AddCompany(owner, "Pending Co", "boss@acme.ph", activated: false);

        var all = await Directory(host).ListCompaniesAsync(new CompanyQuery());
        var pending = await Directory(host).ListCompaniesAsync(new CompanyQuery(Activated: false));

        all.Total.Should().Be(2);
        all.Items.Should().OnlyContain(c => c.OwnerEmail == "boss@acme.ph" && c.PlanName != null);
        pending.Items.Should().ContainSingle(c => c.Name == "Pending Co" && !c.Activated && c.ActivatedAtUtc == null);
    }

    [Fact]
    public async Task ListCompanies_SearchesByCompanyNameOrOwnerEmail_AndFiltersByOwner()
    {
        var host = new SubscriptionTestHost();
        var acme = await PaidAccountAsync(host, "boss@acme.ph", "Acme");
        await PaidAccountAsync(host, "ceo@other.ph", "Globex");
        var directory = Directory(host);

        (await directory.ListCompaniesAsync(new CompanyQuery(Search: "Glob"))).Items.Select(c => c.Name).Should().Equal("Globex");
        (await directory.ListCompaniesAsync(new CompanyQuery(Search: "boss@"))).Items.Select(c => c.Name).Should().Equal("Acme");
        (await directory.ListCompaniesAsync(new CompanyQuery(OwnerUserId: acme))).Items.Should().ContainSingle();
    }

    // ── Payments ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListPayments_ShowsOwnerPlanAndVoidState_AndFilters()
    {
        var host = new SubscriptionTestHost();
        var a = await PaidAccountAsync(host, "a@acme.ph", "A");
        var b = await PaidAccountAsync(host, "b@acme.ph", "B");
        var first = await host.Subscriptions.RecordPaymentAsync(a, new RecordPaymentCommand { Amount = 100, Method = PaymentMethod.GCash, Reference = "R1" }, "platform");
        await host.Subscriptions.RecordPaymentAsync(b, new RecordPaymentCommand { Amount = 200, Method = PaymentMethod.Cash }, "platform");
        await host.SaveAsync();
        host.Uow.Context.SubscriptionPayments.Single(p => p.Id == first.Id).VoidedAtUtc = host.Clock.UtcNow;
        await host.SaveAsync();
        var directory = Directory(host);

        var all = await directory.ListPaymentsAsync(new PaymentQuery());
        all.Total.Should().Be(2);
        all.Items.Should().ContainSingle(p => p.OwnerEmail == "a@acme.ph" && p.Reference == "R1" && p.VoidedAtUtc != null && p.PlanName == "Payroll");

        (await directory.ListPaymentsAsync(new PaymentQuery(OwnerUserId: b))).Items.Should().ContainSingle(p => p.Amount == 200);
        (await directory.ListPaymentsAsync(new PaymentQuery(Method: PaymentMethod.GCash))).Items.Should().ContainSingle(p => p.Amount == 100);
        (await directory.ListPaymentsAsync(new PaymentQuery(Voided: true))).Items.Should().ContainSingle(p => p.Amount == 100);
        (await directory.ListPaymentsAsync(new PaymentQuery(Voided: false))).Items.Should().ContainSingle(p => p.Amount == 200);
    }

    [Fact]
    public async Task ListPayments_MarksAPaymentTheGatewayRefundedOrDisputed()
    {
        var host = new SubscriptionTestHost();
        var owner = await PaidAccountAsync(host, "a@acme.ph", "A");
        await host.Subscriptions.RecordPaymentAsync(owner, new RecordPaymentCommand { Amount = 100, Method = PaymentMethod.GCash, Reference = "pay_1" }, "platform");
        await host.Subscriptions.RecordPaymentAsync(owner, new RecordPaymentCommand { Amount = 200, Method = PaymentMethod.GCash, Reference = "pay_2" }, "platform");
        await host.Subscriptions.RecordPaymentAsync(owner, new RecordPaymentCommand { Amount = 300, Method = PaymentMethod.GCash, Reference = "pay_3" }, "platform");
        await host.SaveAsync();
        // The keys the checkout service writes: "{provider}:payment:{gatewayPaymentId}:{event type}".
        await host.Events.AddAsync(null, BillingEventTypes.PaymentRefunded, new { }, "paymongo", "paymongo:payment:pay_1:payment.refunded", owner);
        await host.Events.AddAsync(null, BillingEventTypes.PaymentDisputed, new { }, "paymongo", "paymongo:payment:pay_2:payment.disputed", owner);
        await host.SaveAsync();

        var items = (await Directory(host).ListPaymentsAsync(new PaymentQuery())).Items;

        items.Single(p => p.Reference == "pay_1").GatewayIssue.Should().Be("Refunded");
        items.Single(p => p.Reference == "pay_2").GatewayIssue.Should().Be("Disputed");
        items.Single(p => p.Reference == "pay_3").GatewayIssue.Should().BeNull();
    }

    [Fact]
    public async Task ListPayments_IncludesStorageBlockPayments_NewestFirst_AndPagesAcrossBoth()
    {
        var host = new SubscriptionTestHost();
        var owner = await PaidAccountAsync(host, "a@acme.ph", "A");
        await host.Subscriptions.RecordPaymentAsync(owner, new RecordPaymentCommand { Amount = 100, Method = PaymentMethod.GCash }, "platform");
        await host.SaveAsync();
        host.Clock.Advance(TimeSpan.FromDays(1));
        var offering = host.Uow.Context.StorageAddOnOfferings.First();
        await host.Storage.ActivateAsync(owner, offering.Code, 2, 300m, PaymentMethod.GCash, "R2", "platform");
        await host.SaveAsync();
        var directory = Directory(host);

        var all = await directory.ListPaymentsAsync(new PaymentQuery());

        all.Total.Should().Be(2);
        all.Items.Select(p => p.Kind).Should().Equal("Storage", "Plan");
        all.Items[0].Description.Should().Contain("2 x");
        all.Items[0].Amount.Should().Be(300m);

        var second = await directory.ListPaymentsAsync(new PaymentQuery(Page: 2, PageSize: 1));
        second.Items.Should().ContainSingle(p => p.Kind == "Plan");
        (await directory.ListPaymentsAsync(new PaymentQuery(Voided: true))).Items.Should().BeEmpty("storage payments are never void");
    }
}
