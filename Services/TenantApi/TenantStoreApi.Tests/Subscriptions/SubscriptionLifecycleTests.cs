using FluentAssertions;
using MassTransit;
using Moq;
using Onepunch.Common.Lib.DTO;
using Onepunch.Common.Lib.Subscriptions;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Subscriptions;

/// <summary>The hourly lifecycle pass: transitions by date, each email sent exactly once.</summary>
public class SubscriptionLifecycleTests
{
    private static List<string> SentTemplates(SubscriptionTestHost host) =>
        host.Publisher.Invocations
            .Where(i => i.Arguments.FirstOrDefault() is SubscriptionReminderEmail)
            .Select(i => ((SubscriptionReminderEmail)i.Arguments[0]).Template)
            .ToList();

    [Fact]
    public async Task Welcome_IsSentOnce_AcrossRepeatedRuns()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();
        var id = host.SubscriptionOf(owner).Id;

        await host.Lifecycle.ProcessAsync(id, CancellationToken.None);
        await host.Lifecycle.ProcessAsync(id, CancellationToken.None);

        SentTemplates(host).Should().Equal(SubscriptionEmailTemplates.TrialWelcome);
    }

    [Fact]
    public async Task TrialSchedule_DayFour_DaySix_DaySeven()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();
        var subscription = host.SubscriptionOf(owner);
        subscription.CreatedAt = SubscriptionTestHost.Start;
        await host.SaveAsync();

        foreach (var hour in Enumerable.Range(0, 24 * 8))
        {
            host.Clock.UtcNow = SubscriptionTestHost.Start.AddHours(hour);
            await host.Lifecycle.ProcessAsync(subscription.Id, CancellationToken.None);
        }

        SentTemplates(host).Should().Equal(
            SubscriptionEmailTemplates.TrialWelcome,
            SubscriptionEmailTemplates.TrialUsageSummary,
            SubscriptionEmailTemplates.TrialEndsTomorrow,
            SubscriptionEmailTemplates.TrialEnded);
        host.SubscriptionOf(owner).SubStatus.Should().Be(AccountSubscriptionStatus.Expired);
    }

    [Fact]
    public async Task ExpiredReminders_GoOutOnDaysOneThreeAndSeven()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();
        var subscription = host.SubscriptionOf(owner);
        var trialEnds = subscription.TrialEndsAtUtc!.Value;

        foreach (var hour in Enumerable.Range(0, 24 * 9))
        {
            host.Clock.UtcNow = trialEnds.AddHours(hour);
            await host.Lifecycle.ProcessAsync(subscription.Id, CancellationToken.None);
        }

        SentTemplates(host).Count(t => t == SubscriptionEmailTemplates.TrialExpiredReminder).Should().Be(3);
    }

    [Fact]
    public async Task Expiry_TellsTheOtherServices()
    {
        var host = new SubscriptionTestHost();
        var (owner, tenantId) = await host.TrialAccountAsync();
        host.Clock.Advance(TimeSpan.FromDays(8));

        var transitions = await host.Lifecycle.ProcessAsync(host.SubscriptionOf(owner).Id, CancellationToken.None);

        transitions.Should().ContainSingle(t => t.To == AccountSubscriptionStatus.Expired);
        host.Publisher.Verify(p => p.Publish(
            It.Is<SubscriptionChanged>(m => m.OwnerUserId == owner && m.TenantIds.Contains(tenantId)),
            It.IsAny<CancellationToken>()), Times.Once);
        host.Uow.Context.BillingEvents.Should().Contain(e => e.Type == BillingEventTypes.TrialExpired);
    }

    [Fact]
    public async Task Emails_GoToTheOwnersAddress()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync("boss@acme.ph");

        await host.Lifecycle.ProcessAsync(host.SubscriptionOf(owner).Id, CancellationToken.None);

        host.Publisher.Verify(p => p.Publish(
            It.Is<SubscriptionReminderEmail>(m => m.Email == "boss@acme.ph" && m.PlanName == "Premium"),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}

public class SubscriptionReminderRuleTests
{
    private static readonly SubscriptionOptions Options = new();

    private static List<string> Due(AccountSubscription subscription, DateTime now) =>
        SubscriptionReminderSchedule.Rules
            .Select(r => r.Evaluate(subscription, now, Options))
            .Where(d => d != null)
            .Select(d => d!.Template)
            .ToList();

    [Fact]
    public void PaymentDue_InTheLastWeek_Only_AndNotWhenCanceled()
    {
        var now = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var subscription = new AccountSubscription { Id = Guid.NewGuid(), SubStatus = AccountSubscriptionStatus.Active, CurrentPeriodEndUtc = now.AddDays(10) };

        Due(subscription, now).Should().BeEmpty();
        Due(subscription, now.AddDays(4)).Should().Contain(SubscriptionEmailTemplates.PaymentDue);

        subscription.CancelAtPeriodEnd = true;
        Due(subscription, now.AddDays(4)).Should().NotContain(SubscriptionEmailTemplates.PaymentDue);
    }

    [Fact]
    public void PurgeWarnings_ThirtySevenAndOneDaysBefore()
    {
        var purge = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var subscription = new AccountSubscription { Id = Guid.NewGuid(), SubStatus = AccountSubscriptionStatus.Canceled, PurgeAfterUtc = purge, CanceledAtUtc = purge.AddDays(-90) };

        Due(subscription, purge.AddDays(-30).AddHours(1)).Should().Contain(SubscriptionEmailTemplates.PurgeWarning);
        Due(subscription, purge.AddDays(-20)).Should().NotContain(SubscriptionEmailTemplates.PurgeWarning);
        Due(subscription, purge.AddDays(-7).AddHours(1)).Should().Contain(SubscriptionEmailTemplates.PurgeWarning);
        Due(subscription, purge.AddHours(-1)).Should().Contain(SubscriptionEmailTemplates.PurgeWarning);
    }

    [Fact]
    public void DedupeKeys_DifferPerOccurrence()
    {
        var subscription = new AccountSubscription { Id = Guid.NewGuid(), SubStatus = AccountSubscriptionStatus.Active };
        var now = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        string KeyFor(DateTime periodEnd)
        {
            subscription.CurrentPeriodEndUtc = periodEnd;
            return SubscriptionReminderSchedule.Rules
                .Select(r => r.Evaluate(subscription, now, Options))
                .Single(d => d?.Template == SubscriptionEmailTemplates.PaymentDue)!.DedupeKey;
        }

        KeyFor(now.AddDays(3)).Should().NotBe(KeyFor(now.AddDays(5)), "each renewal gets its own reminder");
    }
}

public class PlanAllowanceTests
{
    [Fact]
    public async Task FirstCompany_IsAlwaysAllowed()
    {
        var host = new SubscriptionTestHost();

        (await host.Allowance.CheckCompanyAsync(Guid.NewGuid())).Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task SecondCompany_NeedsAPlanThatAllowsIt()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();
        (await host.Allowance.CheckCompanyAsync(owner)).Allowed.Should().BeTrue("the Premium trial allows 5");

        await host.Subscriptions.SetPlanAsync(owner, new SetPlanCommand { PlanCode = "payroll", PeriodEndUtc = host.Clock.UtcNow.AddMonths(1) }, "platform");
        await host.SaveAsync();

        var allowance = await host.Allowance.CheckCompanyAsync(owner);
        allowance.Allowed.Should().BeFalse();
        allowance.ProblemType.Should().Be(SubscriptionProblemTypes.PlanLimitReached);
        allowance.Max.Should().Be(1);
    }

    [Fact]
    public async Task ReadOnlyAccount_CantAddCompanies()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();
        host.SubscriptionOf(owner).SubStatus = AccountSubscriptionStatus.Expired;
        await host.SaveAsync();

        (await host.Allowance.CheckCompanyAsync(owner)).ProblemType.Should().Be(SubscriptionProblemTypes.ReadOnly);
    }

    [Fact]
    public async Task AdminSeats_CountActiveAndInvited_NotPortalOnlyMembers()
    {
        var host = new SubscriptionTestHost();
        var (owner, tenantId) = await host.TrialAccountAsync();
        await host.Subscriptions.SetPlanAsync(owner, new SetPlanCommand { PlanCode = "timekeeping", PeriodEndUtc = host.Clock.UtcNow.AddMonths(1) }, "platform");
        await host.SaveAsync();
        host.AddMember(tenantId, "Admin");
        host.AddMember(tenantId, "Member", status: "Invited");
        host.AddMember(tenantId, "Employee");
        host.AddMember(tenantId, "Admin", status: "Revoked");

        var allowance = await host.Allowance.CheckAdminSeatsAsync(tenantId);

        allowance.Used.Should().Be(2);
        allowance.Allowed.Should().BeFalse("Timekeeping allows 2 admins and both are taken");
    }

    [Fact]
    public async Task PromotingAnEmployee_NeedsAFreeSeat_ButKeepingAnAdminDoesnt()
    {
        var host = new SubscriptionTestHost();
        var (owner, tenantId) = await host.TrialAccountAsync();
        await host.Subscriptions.SetPlanAsync(owner, new SetPlanCommand { PlanCode = "timekeeping", PeriodEndUtc = host.Clock.UtcNow.AddMonths(1) }, "platform");
        await host.SaveAsync();
        host.AddMember(tenantId, "Admin");
        host.AddMember(tenantId, "Admin");
        host.AddMember(tenantId, "Employee");
        var employee = host.Uow.Context.Memberships.Single(m => m.TenantId == tenantId && m.Roles.Any(r => r.Role == "Employee"));
        var admin = host.Uow.Context.Memberships.First(m => m.TenantId == tenantId && !m.IsHidden && m.Roles.Any(r => r.Role == "Admin"));

        var promote = () => host.Allowance.EnsureAdminSeatForChangeAsync(tenantId, employee.Id, ["Admin"], null);
        await promote.Should().ThrowAsync<PlanAllowanceException>();

        var keep = () => host.Allowance.EnsureAdminSeatForChangeAsync(tenantId, admin.Id, ["Member"], null);
        await keep.Should().NotThrowAsync();
    }
}
