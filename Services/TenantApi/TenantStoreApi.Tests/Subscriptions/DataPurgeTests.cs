using FluentAssertions;
using Onepunch.Common.Lib.DTO;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Subscriptions;

/// <summary>A locked account's data: when it gets a purge date, when hrms-api is told to archive and delete, and how the answers are recorded.</summary>
public class DataPurgeTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
    private readonly SubscriptionOptions _options = new();

    private static List<PurgeCompanyData> Requests(SubscriptionTestHost host) =>
        host.Publisher.Invocations
            .Where(i => i.Arguments.FirstOrDefault() is PurgeCompanyData)
            .Select(i => (PurgeCompanyData)i.Arguments[0])
            .ToList();

    /// <summary>An account whose trial ended unpaid, locked for good measure.</summary>
    private static async Task<(AccountSubscription Subscription, Guid TenantId)> ExpiredAccountAsync(SubscriptionTestHost host)
    {
        var (owner, tenantId) = await host.TrialAccountAsync();
        host.Clock.Advance(TimeSpan.FromDays(8));
        var subscription = host.SubscriptionOf(owner);
        await host.Lifecycle.ProcessAsync(subscription.Id, CancellationToken.None);
        return (subscription, tenantId);
    }

    [Theory]
    [InlineData(AccountSubscriptionStatus.Trialing)]
    [InlineData(AccountSubscriptionStatus.Active)]
    [InlineData(AccountSubscriptionStatus.PastDue)]
    public void FullAccessAccounts_AreNotLocked(AccountSubscriptionStatus status)
    {
        DataPurgeSchedule.IsLocked(new AccountSubscription { SubStatus = status }).Should().BeFalse();
    }

    [Fact]
    public void ExpiredTrial_GetsAPurgeDate_AYearAfterItEnded()
    {
        var trialEnds = Now.AddDays(-10);
        var subscription = new AccountSubscription { SubStatus = AccountSubscriptionStatus.Expired, TrialEndsAtUtc = trialEnds };

        DataPurgeSchedule.Reconcile(subscription, Now, _options);

        subscription.PurgeAfterUtc.Should().Be(trialEnds.AddDays(365));
        subscription.PurgeState.Should().Be(DataPurgeState.Scheduled);
    }

    [Fact]
    public void Suspended_PurgesFromTheEndOfGrace()
    {
        var graceEnds = Now.AddDays(-2);
        var subscription = new AccountSubscription { SubStatus = AccountSubscriptionStatus.Suspended, GraceEndsAtUtc = graceEnds };

        DataPurgeSchedule.Reconcile(subscription, Now, _options);

        subscription.PurgeAfterUtc.Should().Be(graceEnds.AddDays(365));
    }

    [Fact]
    public void AnAccountLockedForOverAYear_GetsTheMinimumNotice_NotAPurgeInThePast()
    {
        var subscription = new AccountSubscription { SubStatus = AccountSubscriptionStatus.Expired, TrialEndsAtUtc = Now.AddDays(-800) };

        DataPurgeSchedule.Reconcile(subscription, Now, _options);

        subscription.PurgeAfterUtc.Should().Be(Now.AddDays(_options.MinPurgeNoticeDays));
    }

    [Fact]
    public void ExistingPurgeDate_IsNotMoved()
    {
        var purge = Now.AddDays(100);
        var subscription = new AccountSubscription { SubStatus = AccountSubscriptionStatus.Canceled, CanceledAtUtc = Now, PurgeAfterUtc = purge };

        DataPurgeSchedule.Reconcile(subscription, Now, _options);

        subscription.PurgeAfterUtc.Should().Be(purge);
    }

    [Theory]
    [InlineData(DataPurgeState.Scheduled)]
    [InlineData(DataPurgeState.InProgress)]
    public void PayingUp_CancelsAPurgeThatHasNotHappened(DataPurgeState state)
    {
        var subscription = new AccountSubscription
        {
            SubStatus = AccountSubscriptionStatus.Active, PurgeState = state, PurgeAfterUtc = Now.AddDays(5),
        };

        DataPurgeSchedule.Reconcile(subscription, Now, _options);

        subscription.PurgeState.Should().Be(DataPurgeState.None);
        subscription.PurgeAfterUtc.Should().BeNull();
    }

    [Fact]
    public void PayingUp_DoesNotUndoAPurgeThatAlreadyHappened()
    {
        var subscription = new AccountSubscription { SubStatus = AccountSubscriptionStatus.Active, PurgeState = DataPurgeState.Purged };

        DataPurgeSchedule.Reconcile(subscription, Now, _options);

        subscription.PurgeState.Should().Be(DataPurgeState.Purged);
    }

    [Fact]
    public async Task Lifecycle_SchedulesTheExpiredAccount_ButAsksForNothingWhileSwitchedOff()
    {
        var host = new SubscriptionTestHost();
        var (subscription, _) = await ExpiredAccountAsync(host);
        subscription.PurgeAfterUtc.Should().NotBeNull();
        subscription.PurgeState.Should().Be(DataPurgeState.Scheduled);

        host.Clock.UtcNow = subscription.PurgeAfterUtc!.Value.AddDays(1);
        await host.Lifecycle.ProcessAsync(subscription.Id, CancellationToken.None);

        Requests(host).Should().BeEmpty();
        subscription.PurgeState.Should().Be(DataPurgeState.Scheduled);
    }

    [Fact]
    public async Task WhenDue_EveryCompanyOfTheAccountIsAsked_OncePerDay()
    {
        var host = new SubscriptionTestHost { Options = { DataPurgeEnabled = true } };
        var (subscription, firstCompany) = await ExpiredAccountAsync(host);
        var secondCompany = host.AddCompany(subscription.OwnerUserId, "Beta", activated: false);
        host.Clock.UtcNow = subscription.PurgeAfterUtc!.Value.AddHours(1);

        await host.Lifecycle.ProcessAsync(subscription.Id, CancellationToken.None);
        await host.Lifecycle.ProcessAsync(subscription.Id, CancellationToken.None);

        var requests = Requests(host);
        requests.Should().HaveCount(2);
        requests.Select(r => r.TenantId).Should().BeEquivalentTo([firstCompany, secondCompany]);
        requests.Should().OnlyContain(r => r.Stage == CompanyPurgeStage.ArchiveAndPurge && r.SubscriptionId == subscription.Id);
        subscription.PurgeState.Should().Be(DataPurgeState.InProgress);

        host.Clock.Advance(TimeSpan.FromDays(1));
        await host.Lifecycle.ProcessAsync(subscription.Id, CancellationToken.None);
        Requests(host).Should().HaveCount(4, "an unfinished purge is asked again the next day");
    }

    [Fact]
    public async Task NothingIsAsked_BeforeThePurgeDate()
    {
        var host = new SubscriptionTestHost { Options = { DataPurgeEnabled = true } };
        var (subscription, _) = await ExpiredAccountAsync(host);

        host.Clock.UtcNow = subscription.PurgeAfterUtc!.Value.AddMinutes(-1);
        await host.Lifecycle.ProcessAsync(subscription.Id, CancellationToken.None);

        Requests(host).Should().BeEmpty();
    }

    [Fact]
    public async Task LegalHold_StopsThePurge()
    {
        var host = new SubscriptionTestHost { Options = { DataPurgeEnabled = true } };
        var (subscription, _) = await ExpiredAccountAsync(host);
        subscription.LegalHold = true;
        host.Clock.UtcNow = subscription.PurgeAfterUtc!.Value.AddDays(1);

        await host.Lifecycle.ProcessAsync(subscription.Id, CancellationToken.None);

        Requests(host).Should().BeEmpty();
        subscription.PurgeState.Should().Be(DataPurgeState.Scheduled);
    }

    [Fact]
    public async Task AnAccountThatPaysAfterBeingAsked_IsNotPurged()
    {
        var host = new SubscriptionTestHost { Options = { DataPurgeEnabled = true } };
        var (subscription, _) = await ExpiredAccountAsync(host);
        host.Clock.UtcNow = subscription.PurgeAfterUtc!.Value.AddHours(1);
        await host.Lifecycle.ProcessAsync(subscription.Id, CancellationToken.None);
        Requests(host).Should().HaveCount(1);

        subscription.SubStatus = AccountSubscriptionStatus.Active;
        subscription.CurrentPeriodEndUtc = host.Clock.UtcNow.AddDays(30);
        subscription.PurgeAfterUtc = null;
        host.Clock.Advance(TimeSpan.FromDays(1));
        await host.Lifecycle.ProcessAsync(subscription.Id, CancellationToken.None);

        Requests(host).Should().HaveCount(1);
        subscription.PurgeState.Should().Be(DataPurgeState.None);
    }

    [Fact]
    public async Task AccountIsPurged_OnlyOnceEveryCompanyHasReported_AndTheArchiveClockStartsAtTheLastEntry()
    {
        var host = new SubscriptionTestHost { Options = { DataPurgeEnabled = true } };
        var (subscription, firstCompany) = await ExpiredAccountAsync(host);
        var secondCompany = host.AddCompany(subscription.OwnerUserId, "Beta", activated: false);
        subscription.PurgeState = DataPurgeState.InProgress;
        var firstEntry = Now.AddYears(-2);
        var laterEntry = Now.AddYears(-1);

        await host.DataPurge.RecordResultAsync(Done(subscription, firstCompany, firstEntry), CancellationToken.None);
        subscription.PurgeState.Should().Be(DataPurgeState.InProgress);

        await host.DataPurge.RecordResultAsync(Done(subscription, secondCompany, laterEntry), CancellationToken.None);

        subscription.PurgeState.Should().Be(DataPurgeState.Purged);
        subscription.DataPurgedAtUtc.Should().Be(host.Clock.UtcNow);
        subscription.ArchiveRetainUntilUtc.Should().Be(laterEntry.AddYears(5), "the later of the companies' last entries, plus five years");
    }

    [Fact]
    public async Task AReportedResult_IsRecordedOnce_WhenDeliveredTwice()
    {
        var host = new SubscriptionTestHost();
        var (subscription, tenantId) = await ExpiredAccountAsync(host);
        var result = Done(subscription, tenantId, Now);

        await host.DataPurge.RecordResultAsync(result, CancellationToken.None);
        await host.SaveAsync();
        await host.DataPurge.RecordResultAsync(result, CancellationToken.None);
        await host.SaveAsync();

        host.Uow.Context.BillingEvents.Count(e => e.Type == BillingEventTypes.DataPurged).Should().Be(1);
    }

    [Fact]
    public async Task AFailedPurge_LeavesTheAccountInProgress_ForTomorrowsRetry()
    {
        var host = new SubscriptionTestHost();
        var (subscription, tenantId) = await ExpiredAccountAsync(host);
        subscription.PurgeState = DataPurgeState.InProgress;

        await host.DataPurge.RecordResultAsync(
            new CompanyDataPurged
            {
                OwnerUserId = subscription.OwnerUserId, SubscriptionId = subscription.Id, TenantId = tenantId,
                Stage = CompanyPurgeStage.ArchiveAndPurge, Succeeded = false, Error = "storage down",
            }, CancellationToken.None);

        subscription.PurgeState.Should().Be(DataPurgeState.InProgress);
        host.Uow.Context.BillingEvents.Local.Should().Contain(e => e.Type == BillingEventTypes.DataPurgeFailed);
    }

    [Fact]
    public async Task AResultForACompanyOfAnotherAccount_ChangesNothing()
    {
        var host = new SubscriptionTestHost();
        var (subscription, _) = await ExpiredAccountAsync(host);
        subscription.PurgeState = DataPurgeState.InProgress;
        var stranger = host.AddCompany(Guid.NewGuid(), "Other", email: "other@x.ph");

        await host.DataPurge.RecordResultAsync(Done(subscription, stranger, Now), CancellationToken.None);

        subscription.PurgeState.Should().Be(DataPurgeState.InProgress);
        host.Uow.Context.BillingEvents.Local.Should().NotContain(e => e.Type == BillingEventTypes.DataPurged);
    }

    [Fact]
    public async Task OnceRetentionHasRunOut_TheArchiveIsDestroyed_AndThenTheAccountIsDone()
    {
        var host = new SubscriptionTestHost { Options = { DataPurgeEnabled = true } };
        var (subscription, tenantId) = await ExpiredAccountAsync(host);
        subscription.PurgeState = DataPurgeState.Purged;
        subscription.ArchiveRetainUntilUtc = host.Clock.UtcNow.AddDays(10);

        await host.Lifecycle.ProcessAsync(subscription.Id, CancellationToken.None);
        Requests(host).Should().BeEmpty("the archive is still within its retention");

        host.Clock.Advance(TimeSpan.FromDays(11));
        await host.Lifecycle.ProcessAsync(subscription.Id, CancellationToken.None);
        Requests(host).Should().ContainSingle(r => r.Stage == CompanyPurgeStage.DestroyArchive && r.TenantId == tenantId);

        await host.DataPurge.RecordResultAsync(new CompanyDataPurged
        {
            OwnerUserId = subscription.OwnerUserId, SubscriptionId = subscription.Id, TenantId = tenantId,
            Stage = CompanyPurgeStage.DestroyArchive, Succeeded = true,
        }, CancellationToken.None);

        subscription.PurgeState.Should().Be(DataPurgeState.ArchiveDestroyed);
    }

    [Fact]
    public async Task LegalHold_StopsTheArchiveBeingDestroyed()
    {
        var host = new SubscriptionTestHost { Options = { DataPurgeEnabled = true } };
        var (subscription, _) = await ExpiredAccountAsync(host);
        subscription.PurgeState = DataPurgeState.Purged;
        subscription.ArchiveRetainUntilUtc = host.Clock.UtcNow.AddDays(-1);
        subscription.LegalHold = true;

        await host.Lifecycle.ProcessAsync(subscription.Id, CancellationToken.None);

        Requests(host).Should().BeEmpty();
    }

    [Fact]
    public void PurgeWarnings_AreNotSent_WhileOnLegalHold()
    {
        var purge = Now.AddDays(7);
        var held = new AccountSubscription
        {
            Id = Guid.NewGuid(), SubStatus = AccountSubscriptionStatus.Expired, PurgeAfterUtc = purge, LegalHold = true,
        };
        var open = new AccountSubscription { Id = Guid.NewGuid(), SubStatus = AccountSubscriptionStatus.Expired, PurgeAfterUtc = purge };

        SubscriptionReminderSchedule.Rules.Select(r => r.Evaluate(held, Now, _options)).Should().OnlyContain(d => d == null || d.Template != SubscriptionEmailTemplates.PurgeWarning);
        SubscriptionReminderSchedule.Rules.Select(r => r.Evaluate(open, Now, _options)).Should().Contain(d => d != null && d.Template == SubscriptionEmailTemplates.PurgeWarning);
    }

    private static CompanyDataPurged Done(AccountSubscription subscription, Guid tenantId, DateTime lastEntry) => new()
    {
        OwnerUserId = subscription.OwnerUserId, SubscriptionId = subscription.Id, TenantId = tenantId,
        Stage = CompanyPurgeStage.ArchiveAndPurge, Succeeded = true, LastEntryUtc = lastEntry,
        ArchiveKey = $"archives/{tenantId:D}/a.zip", RowsArchived = 10, RowsDeleted = 12,
    };
}
