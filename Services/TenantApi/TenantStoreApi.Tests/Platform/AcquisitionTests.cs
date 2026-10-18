using FluentAssertions;
using Onepunch.Common.Lib.Subscriptions;
using TenantStoreApi.Core.Services.Platform;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Platform;

/// <summary>The Acquisition report: where accounts come from and how far they get, bucketed in Philippine time.</summary>
public class AcquisitionTests
{
    // 2026-10-05 is a Monday.
    private static readonly DateOnly Monday = new(2026, 10, 5);

    /// <summary>Midnight Philippine time on <paramref name="date"/> as a UTC instant, plus <paramref name="hours"/>.</summary>
    private static DateTime Ph(DateOnly date, double hours = 12) =>
        DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue).AddHours(hours) - AcquisitionCalculator.LocalOffset, DateTimeKind.Utc);

    private static AcquisitionAccount Account(string origin, DateTime? created, DateTime? confirmed = null, DateTime? hris = null,
        DateTime? company = null, DateTime? setUp = null, DateTime? trial = null, DateTime? paid = null) =>
        new(Guid.NewGuid(), origin, created, confirmed, hris, null, company, setUp, trial, paid);

    // ── Bucketing ────────────────────────────────────────────────────────────

    [Fact]
    public void ADayIsThePhilippineDay_NotTheUtcDay()
    {
        // 17:00 UTC on the 5th is 01:00 on the 6th in Manila.
        AcquisitionCalculator.LocalDate(new DateTime(2026, 10, 5, 17, 0, 0, DateTimeKind.Utc)).Should().Be(new DateOnly(2026, 10, 6));
        AcquisitionCalculator.LocalDate(new DateTime(2026, 10, 5, 15, 59, 0, DateTimeKind.Utc)).Should().Be(new DateOnly(2026, 10, 5));
    }

    [Theory]
    [InlineData(2026, 10, 5, 2026, 10, 5)]   // Monday
    [InlineData(2026, 10, 11, 2026, 10, 5)]  // Sunday belongs to the week that began the Monday before
    [InlineData(2026, 10, 12, 2026, 10, 12)] // next Monday starts a new week
    public void WeeksStartOnMonday(int y, int m, int d, int ey, int em, int ed) =>
        AcquisitionCalculator.BucketStart(new DateOnly(y, m, d), AcquisitionBucket.Week).Should().Be(new DateOnly(ey, em, ed));

    [Fact]
    public void MonthsStartOnTheFirst_AndEveryBucketInTheRangeIsListed_EvenEmptyOnes()
    {
        var buckets = AcquisitionCalculator.Buckets(new DateOnly(2026, 8, 20), new DateOnly(2026, 10, 3), AcquisitionBucket.Month);

        buckets.Should().Equal(new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1));
    }

    [Fact]
    public void ADayRange_ListsEveryDay()
    {
        AcquisitionCalculator.Buckets(Monday, Monday.AddDays(2), AcquisitionBucket.Day).Should().HaveCount(3);
    }

    // ── Series ───────────────────────────────────────────────────────────────

    [Fact]
    public void NewAccounts_AreCountedPerOriginPerBucket_AndEmptyBucketsAreZero()
    {
        var cohort = new[]
        {
            Account("EmailSignup", Ph(Monday)),
            Account("EmailSignup", Ph(Monday)),
            Account("ClientInvite", Ph(Monday.AddDays(2))),
        };

        var report = AcquisitionCalculator.Build(Monday, Monday.AddDays(3), AcquisitionBucket.Day, cohort, [], 0, true, false);

        report.Buckets.Should().HaveCount(4);
        report.NewAccounts.Select(s => s.Origin).Should().Equal("EmailSignup", "ClientInvite");
        report.NewAccounts[0].Counts.Should().Equal(2, 0, 0, 0);
        report.NewAccounts[1].Counts.Should().Equal(0, 0, 1, 0);
        report.NewAccounts[0].Total.Should().Be(2);
    }

    [Fact]
    public void AnAccountCreatedJustAfterMidnightInManila_LandsOnTheManilaDay()
    {
        var late = Account("GoogleSignup", new DateTime(2026, 10, 5, 16, 30, 0, DateTimeKind.Utc)); // 00:30 on the 6th in Manila

        var report = AcquisitionCalculator.Build(Monday, Monday.AddDays(2), AcquisitionBucket.Day, [late], [], 0, true, false);

        report.NewAccounts.Single().Counts.Should().Equal(0, 1, 0);
    }

    [Fact]
    public void AccountsOutsideTheRange_OrWithoutADate_AreNotPlacedOnTheTimeline()
    {
        var cohort = new[] { Account("EmailSignup", Ph(Monday.AddDays(-1))), Account("EmailSignup", Ph(Monday.AddDays(9))), Account("EmailSignup", null) };

        var report = AcquisitionCalculator.Build(Monday, Monday.AddDays(3), AcquisitionBucket.Day, cohort, [], 4, true, false);

        report.NewAccounts.Should().BeEmpty();
        report.UndatedAccounts.Should().Be(4);
    }

    [Fact]
    public void OwnerMilestones_AreSeriesByTheOwnersOrigin_NotWhenTheyFirstSignedUp()
    {
        var owners = new[]
        {
            new AcquisitionOwner(Guid.NewGuid(), "ClientInvite", Ph(Monday), Ph(Monday), null),
            new AcquisitionOwner(Guid.NewGuid(), "ClientInvite", Ph(Monday.AddDays(1)), null, Ph(Monday.AddDays(1))),
            new AcquisitionOwner(Guid.NewGuid(), "Unknown", Ph(Monday), null, null),
        };

        var report = AcquisitionCalculator.Build(Monday, Monday.AddDays(1), AcquisitionBucket.Day, [], owners, 0, true, false);

        report.CompaniesCreated.Single(s => s.Origin == "ClientInvite").Counts.Should().Equal(1, 1);
        report.CompaniesCreated.Single(s => s.Origin == "Unknown").Counts.Should().Equal(1, 0);
        report.TrialsStarted.Single().Counts.Should().Equal(1, 0);
        report.FirstPayments.Single().Counts.Should().Equal(0, 1);
    }

    // ── Funnel ───────────────────────────────────────────────────────────────

    [Fact]
    public void TheFunnelCountsHowFarEachOriginsAccountsGot()
    {
        var cohort = new[]
        {
            Account("ClientInvite", Ph(Monday), confirmed: Ph(Monday), hris: Ph(Monday), company: Ph(Monday), setUp: Ph(Monday), trial: Ph(Monday), paid: Ph(Monday)),
            Account("ClientInvite", Ph(Monday), confirmed: Ph(Monday), hris: Ph(Monday), company: Ph(Monday)),
            Account("ClientInvite", Ph(Monday), confirmed: Ph(Monday)),
            Account("ClientInvite", Ph(Monday)),
            Account("EmailSignup", Ph(Monday)),
        };

        var report = AcquisitionCalculator.Build(Monday, Monday, AcquisitionBucket.Day, cohort, [], 0, true, false);

        var client = report.Funnels.Single(f => f.Origin == "ClientInvite");
        client.Stages.Select(s => s.Key).Should().Equal(AcquisitionCalculator.FunnelStages);
        client.Stages.Select(s => s.Count).Should().Equal(4, 3, 2, 2, 1, 1, 1);
        report.Funnels.Single(f => f.Origin == "EmailSignup").Stages.Select(s => s.Count).Should().Equal(1, 0, 0, 0, 0, 0, 0);
    }

    [Fact]
    public void ALaterStepImpliesTheEarlierOnes_SoMissingHistoryNeverMakesTheFunnelGoUp()
    {
        // Paid, but neither the email confirmation nor the first sign-in was ever recorded.
        var oldAccount = Account("Unknown", Ph(Monday), paid: Ph(Monday));

        var report = AcquisitionCalculator.Build(Monday, Monday, AcquisitionBucket.Day, [oldAccount], [], 0, true, false);

        report.Funnels.Single().Stages.Select(s => s.Count).Should().Equal(1, 1, 1, 1, 1, 1, 1);
    }

    // ── Service ──────────────────────────────────────────────────────────────

    private sealed class FakeOrigins(params AccountOriginRecord[] accounts) : IAccountOriginSource
    {
        public bool Down { get; set; }
        public int Undated { get; set; }

        public Task<AccountOriginResult> GetCreatedBetweenAsync(DateTime fromUtc, DateTime toUtc, CancellationToken token) =>
            Task.FromResult(Down
                ? AccountOriginResult.Unavailable
                : new AccountOriginResult(accounts.Where(a => a.CreatedAtUtc >= fromUtc && a.CreatedAtUtc < toUtc).ToList(), Undated, false, true));

        public Task<AccountOriginResult> GetByIdsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken token) =>
            Task.FromResult(Down
                ? AccountOriginResult.Unavailable
                : new AccountOriginResult(accounts.Where(a => userIds.Contains(a.UserId)).ToList(), 0, false, true));
    }

    private static AccountOriginRecord Origin(Guid id, string origin, DateTime created, DateTime? hris = null) =>
        new(id, origin, created, created, hris, null, null);

    [Fact]
    public async Task Report_FollowsAClientInvitedAccountThroughCompanyTrialAndPayment()
    {
        var host = new SubscriptionTestHost();
        var owner = Guid.NewGuid();
        var signedUp = Ph(Monday);
        var tenantId = host.AddCompany(owner, "Acme", "ana@acme.ph");
        host.Uow.Context.Tenants.Single(t => t.Id == tenantId).CreatedAt = Ph(Monday.AddDays(1));
        host.Uow.Context.Tenants.Single(t => t.Id == tenantId).ActivatedAtUtc = Ph(Monday.AddDays(1), 13);
        await host.SaveAsync();
        host.Clock.UtcNow = Ph(Monday.AddDays(1), 14);
        await host.Subscriptions.StartTrialIfFirstCompanyAsync(owner, "ana@acme.ph");
        await host.SaveAsync();
        host.Clock.UtcNow = Ph(Monday.AddDays(3));
        await host.Subscriptions.SetPlanAsync(owner, new SetPlanCommand { PlanCode = "payroll", PeriodEndUtc = Ph(Monday.AddDays(33)) }, "platform");
        var payment = await host.Subscriptions.RecordPaymentAsync(owner, new RecordPaymentCommand { Amount = 100, Method = PaymentMethod.GCash, Reference = "R1" }, "platform");
        await host.SaveAsync();
        host.Uow.Context.SubscriptionPayments.Single(p => p.Id == payment.Id).PaidAtUtc = Ph(Monday.AddDays(3));
        await host.SaveAsync();
        var sut = new PlatformAcquisitionService(host.Uow, new FakeOrigins(Origin(owner, "ClientInvite", signedUp, hris: Ph(Monday))));

        var report = await sut.GetReportAsync(Monday, Monday.AddDays(6), AcquisitionBucket.Day);

        report.OriginAvailable.Should().BeTrue();
        report.NewAccounts.Single().Origin.Should().Be("ClientInvite");
        var funnel = report.Funnels.Single(f => f.Origin == "ClientInvite");
        funnel.Stages.Select(s => s.Count).Should().Equal(1, 1, 1, 1, 1, 1, 1);
        report.CompaniesCreated.Single().Counts[1].Should().Be(1);
        report.TrialsStarted.Single(s => s.Origin == "ClientInvite").Total.Should().Be(1);
        report.FirstPayments.Single(s => s.Origin == "ClientInvite").Counts[3].Should().Be(1);
    }

    [Fact]
    public async Task Report_IgnoresVoidedPayments_WhenFindingTheFirstPaidDate()
    {
        var host = new SubscriptionTestHost();
        var owner = Guid.NewGuid();
        host.AddCompany(owner, "Acme", "ana@acme.ph");
        await host.Subscriptions.StartTrialIfFirstCompanyAsync(owner, "ana@acme.ph");
        await host.SaveAsync();
        var payment = await host.Subscriptions.RecordPaymentAsync(owner, new RecordPaymentCommand { Amount = 100, Method = PaymentMethod.Cash }, "platform");
        await host.SaveAsync();
        var row = host.Uow.Context.SubscriptionPayments.Single(p => p.Id == payment.Id);
        row.PaidAtUtc = Ph(Monday);
        row.VoidedAtUtc = Ph(Monday, 15);
        await host.SaveAsync();
        var sut = new PlatformAcquisitionService(host.Uow, new FakeOrigins(Origin(owner, "EmailSignup", Ph(Monday))));

        var report = await sut.GetReportAsync(Monday, Monday, AcquisitionBucket.Day);

        report.FirstPayments.Should().BeEmpty("a voided payment was never money received");
        report.Funnels.Single().Stages.Last().Count.Should().Be(0);
    }

    [Fact]
    public async Task Report_DoesNotCountALegacyBackfillAsATrialStart()
    {
        var host = new SubscriptionTestHost();
        var owner = Guid.NewGuid();
        host.AddCompany(owner, "Acme", "ana@acme.ph");
        await host.SaveAsync();
        host.Uow.Context.BillingEvents.Add(new TenantStoreApi.Domain.Entities.Subs.BillingEvent
        {
            Id = Guid.NewGuid(), OwnerUserId = owner, Type = BillingEventTypes.LegacyBackfill, OccurredAtUtc = Ph(Monday),
        });
        await host.SaveAsync();
        var sut = new PlatformAcquisitionService(host.Uow, new FakeOrigins(Origin(owner, "EmailSignup", Ph(Monday))));

        var report = await sut.GetReportAsync(Monday, Monday, AcquisitionBucket.Day);

        report.TrialsStarted.Should().BeEmpty();
    }

    [Fact]
    public async Task Report_WhenAuthCantBeReached_StillReturnsTheDownstreamSeries_AndSaysOriginIsMissing()
    {
        var host = new SubscriptionTestHost();
        var owner = Guid.NewGuid();
        var tenantId = host.AddCompany(owner, "Acme", "ana@acme.ph");
        host.Uow.Context.Tenants.Single(t => t.Id == tenantId).CreatedAt = Ph(Monday);
        await host.SaveAsync();
        var sut = new PlatformAcquisitionService(host.Uow, new FakeOrigins { Down = true });

        var report = await sut.GetReportAsync(Monday, Monday, AcquisitionBucket.Day);

        report.OriginAvailable.Should().BeFalse();
        report.NewAccounts.Should().BeEmpty();
        report.Funnels.Should().BeEmpty();
        report.CompaniesCreated.Single().Origin.Should().Be("Unknown");
        report.CompaniesCreated.Single().Total.Should().Be(1);
    }

    [Theory]
    [InlineData(5, 4, "Day")]
    [InlineData(5, 4000, "Month")]
    [InlineData(5, 400, "Day")]
    public async Task Report_RefusesAnInvertedOrTooLongRange(int startDay, int endOffset, string bucket)
    {
        var host = new SubscriptionTestHost();
        var sut = new PlatformAcquisitionService(host.Uow, new FakeOrigins());
        var from = new DateOnly(2026, 10, startDay);

        var act = () => sut.GetReportAsync(from, from.AddDays(endOffset - startDay), Enum.Parse<AcquisitionBucket>(bucket));

        await act.Should().ThrowAsync<SubscriptionRuleException>();
    }

    [Fact]
    public async Task Journey_ShowsEachStepOfOneAccount()
    {
        var host = new SubscriptionTestHost();
        var owner = Guid.NewGuid();
        var tenantId = host.AddCompany(owner, "Acme", "ana@acme.ph");
        host.Uow.Context.Tenants.Single(t => t.Id == tenantId).CreatedAt = Ph(Monday.AddDays(1));
        await host.SaveAsync();
        var sut = new PlatformAcquisitionService(host.Uow, new FakeOrigins(Origin(owner, "StaffInvite", Ph(Monday), hris: Ph(Monday, 13))));

        var journey = await sut.GetJourneyAsync(owner);

        journey.Origin.Should().Be("StaffInvite");
        journey.CreatedAtUtc.Should().Be(Ph(Monday));
        journey.FirstHrisSignInAtUtc.Should().Be(Ph(Monday, 13));
        journey.FirstCompanyAtUtc.Should().Be(Ph(Monday.AddDays(1)));
        journey.TrialStartedAtUtc.Should().BeNull();
        journey.OriginAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task Journey_ForAnAccountAuthDoesntKnow_IsUnknownButStillShowsTheCompanySide()
    {
        var host = new SubscriptionTestHost();
        var owner = Guid.NewGuid();
        host.AddCompany(owner, "Acme", "ana@acme.ph");
        var sut = new PlatformAcquisitionService(host.Uow, new FakeOrigins { Down = true });

        var journey = await sut.GetJourneyAsync(owner);

        journey.Origin.Should().Be("Unknown");
        journey.OriginAvailable.Should().BeFalse();
        journey.FirstCompanyAtUtc.Should().NotBeNull();
    }
}
