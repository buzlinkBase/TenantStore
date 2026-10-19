using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Onepunch.Common.Lib.DTO;
using TenantStoreApi.Core.Services.Platform;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Platform;

/// <summary>
/// Erasing ONE company of an account: two different operators, the company's name typed, never the
/// account's last company, and only that company's rows selected -- the account, its payments and its
/// other companies stay.
/// </summary>
public class CompanyDeletionTests
{
    private const string OwnerEmail = "owner@acme.ph";
    private static readonly PlatformOperator Alice = new(Guid.NewGuid(), "platform:alice@onepunch.test");
    private static readonly PlatformOperator Bob = new(Guid.NewGuid(), "platform:bob@onepunch.test");

    private sealed class RecordingRowEraser : IRowEraser
    {
        public List<object> Erased { get; } = new();

        public async Task<int> EraseAsync<T>(IQueryable<T> rows, CancellationToken token) where T : class
        {
            var selected = await rows.ToListAsync(token);
            Erased.AddRange(selected);
            return selected.Count;
        }
    }

    private sealed class Fixture
    {
        public SubscriptionTestHost Host { get; } = new();
        public RecordingRowEraser Rows { get; } = new();
        public Guid Owner { get; private set; }
        public Guid Keep { get; private set; }
        public Guid Test { get; private set; }

        private TenantStoreAccountEraser Eraser => new(Host.Uow, Rows);

        public PlatformCompanyDeletionService CompanyDeletion => new(Host.Uow, Host.Events, Host.Publisher.Object, Eraser, Host.Clock);

        public AccountDeletionRequestService Requests => new(
            Host.Uow, new PlatformDirectoryService(Host.Uow, Host.StateMachine),
            new PlatformAccountDeletionService(Host.Uow, Host.Events, Host.Notifier, Host.Publisher.Object, Eraser, Host.Clock),
            CompanyDeletion, Host.Events, AccountDeletionGuards.Default, AccountDeletionGuards.Company,
            Microsoft.Extensions.Options.Options.Create(Host.Options), Host.Clock);

        /// <summary>An account with a real company ("Acme") and a test one ("Acme Test").</summary>
        public async Task<Fixture> SeedAsync()
        {
            (Owner, Keep) = await Host.TrialAccountAsync(OwnerEmail);
            Test = Host.AddCompany(Owner, "Acme Test", OwnerEmail);
            return this;
        }

        public List<T> Published<T>() => Host.Publisher.Invocations
            .Where(i => i.Arguments.FirstOrDefault() is T)
            .Select(i => (T)i.Arguments[0])
            .ToList();

        public async Task<AccountDeletionRequestView> RequestAsync()
        {
            var request = await Requests.RequestCompanyAsync(Test, "Test company", "acme test", Alice);
            await Host.SaveAsync();
            return request;
        }

        public async Task ApproveAsync(Guid requestId)
        {
            await Requests.ApproveAsync(requestId, " Acme Test ", Bob);
            await Host.SaveAsync();
        }

        public async Task ReportAsync(bool succeeded = true)
        {
            var subscription = Host.SubscriptionOf(Owner);
            var handled = await CompanyDeletion.RecordResultAsync(new CompanyDataPurged
            {
                OwnerUserId = Owner,
                SubscriptionId = subscription.Id,
                TenantId = Test,
                Stage = CompanyPurgeStage.HardDelete,
                Succeeded = succeeded,
                Error = succeeded ? null : "boom",
            }, CancellationToken.None);
            handled.Should().BeTrue();
            await Host.SaveAsync();
        }
    }

    [Fact]
    public async Task Request_NeedsTheCompanyNameTyped()
    {
        var f = await new Fixture().SeedAsync();

        var act = () => f.Requests.RequestCompanyAsync(f.Test, "Test company", "Acme", Alice);

        await act.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*doesn't match*");
    }

    [Fact]
    public async Task Request_ForTheAccountsOnlyCompany_IsRefused()
    {
        var f = new Fixture();
        var (_, only) = await f.Host.TrialAccountAsync(OwnerEmail);

        var act = () => f.Requests.RequestCompanyAsync(only, "Test company", "Acme", Alice);

        await act.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*only company*");
    }

    [Fact]
    public async Task Request_IsNotBlockedByTheAccountsPayments_ButApprovalIsByALegalHold()
    {
        var f = await new Fixture().SeedAsync();
        var subscription = f.Host.SubscriptionOf(f.Owner);
        f.Host.Uow.Context.SubscriptionPayments.Add(new SubscriptionPayment
        {
            Id = Guid.NewGuid(), SubscriptionId = subscription.Id, PlanId = subscription.PlanId, Amount = 999,
            PaidAtUtc = f.Host.Clock.UtcNow, PeriodStartUtc = f.Host.Clock.UtcNow, PeriodEndUtc = f.Host.Clock.UtcNow.AddMonths(1),
        });
        await f.Host.SaveAsync();

        var request = await f.RequestAsync();
        request.TenantId.Should().Be(f.Test);
        request.CompanyName.Should().Be("Acme Test");
        request.CompanyNames.Should().Equal("Acme Test");

        subscription.LegalHold = true;
        await f.Host.SaveAsync();
        var approve = () => f.Requests.ApproveAsync(request.Id, "Acme Test", Bob);
        await approve.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*legal hold*");
    }

    [Fact]
    public async Task Request_DoesNotCollideWithAnAccountRequest_ButTwiceForTheCompanyIsRefused()
    {
        var f = await new Fixture().SeedAsync();
        await f.Requests.RequestAsync(f.Owner, "Test account", OwnerEmail, Bob);
        await f.Host.SaveAsync();
        await f.RequestAsync();

        var again = () => f.Requests.RequestCompanyAsync(f.Test, "Test company", "Acme Test", Bob);
        await again.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*already waiting*");

        (await f.Requests.GetLatestAsync(f.Owner))!.TenantId.Should().BeNull("the account's banner shows only account requests");
        (await f.Requests.GetLatestForCompanyAsync(f.Test))!.TenantId.Should().Be(f.Test);
        (await f.Requests.ListPendingAsync()).Should().HaveCount(2);
    }

    [Fact]
    public async Task Approve_ByTheOperatorWhoAsked_IsRefused()
    {
        var f = await new Fixture().SeedAsync();
        var request = await f.RequestAsync();

        var act = () => f.Requests.ApproveAsync(request.Id, "Acme Test", Alice);

        await act.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*second administrator*");
    }

    [Fact]
    public async Task WithTheSecondApproverSettingOff_TheOperatorWhoAsked_CanApprove()
    {
        var f = await new Fixture().SeedAsync();
        f.Host.Options.RequireSecondDeletionApprover = false;
        var request = await f.RequestAsync();

        await f.Requests.ApproveAsync(request.Id, "Acme Test", Alice);
        await f.Host.SaveAsync();

        f.Published<PurgeCompanyData>().Should().ContainSingle(m => m.TenantId == f.Test);
    }

    [Fact]
    public async Task Approve_AsksOnlyThatCompanyToBeErased_AndLeavesTheAccountAlone()
    {
        var f = await new Fixture().SeedAsync();
        var request = await f.RequestAsync();

        await f.ApproveAsync(request.Id);

        var asked = f.Published<PurgeCompanyData>().Should().ContainSingle().Subject;
        asked.TenantId.Should().Be(f.Test);
        asked.Stage.Should().Be(CompanyPurgeStage.HardDelete);
        var subscription = f.Host.SubscriptionOf(f.Owner);
        subscription.PurgeState.Should().Be(DataPurgeState.None);
        subscription.SubStatus.Should().Be(AccountSubscriptionStatus.Trialing);
        (await f.Requests.GetLatestForCompanyAsync(f.Test))!.Erasure.Should().Be(CompanyErasureStatus.Running);
        f.Rows.Erased.Should().BeEmpty();
    }

    [Fact]
    public async Task AFailure_ParksTheRequest_AndRetryAsksAgain()
    {
        var f = await new Fixture().SeedAsync();
        var request = await f.RequestAsync();
        await f.ApproveAsync(request.Id);

        await f.ReportAsync(succeeded: false);
        var parked = (await f.Requests.GetLatestForCompanyAsync(f.Test))!;
        parked.Erasure.Should().Be(CompanyErasureStatus.Failed);
        parked.ErasureError.Should().Be("boom");

        f.Host.Publisher.Invocations.Clear();
        await f.CompanyDeletion.RetryAsync(f.Test, Alice.Actor, CancellationToken.None);
        await f.Host.SaveAsync();

        f.Published<PurgeCompanyData>().Select(m => m.TenantId).Should().Equal(f.Test);
        (await f.Requests.GetLatestForCompanyAsync(f.Test))!.Erasure.Should().Be(CompanyErasureStatus.Running);
    }

    [Fact]
    public async Task WhileACompanyIsBeingErased_TheAccountCantBeRequestedForDeletion()
    {
        var f = await new Fixture().SeedAsync();
        var request = await f.RequestAsync();
        await f.ApproveAsync(request.Id);

        var act = () => f.Requests.RequestAsync(f.Owner, "Test account", OwnerEmail, Alice);

        await act.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*companies is being deleted*");
    }

    [Fact]
    public async Task Success_ErasesOnlyThatCompanysRows_AndHandsItsLoginsToAuth()
    {
        var f = await new Fixture().SeedAsync();
        var onlyHere = f.Host.AddMember(f.Test, "Admin");
        var shared = f.Host.AddMember(f.Test, "HR");
        f.Host.AddMember(f.Keep, "HR", userId: shared);
        var request = await f.RequestAsync();
        await f.ApproveAsync(request.Id);

        await f.ReportAsync();

        f.Rows.Erased.OfType<Tenant>().Select(t => t.Id).Should().Equal(f.Test);
        f.Rows.Erased.OfType<AccountSubscription>().Should().BeEmpty();
        f.Rows.Erased.OfType<SubscriptionPayment>().Should().BeEmpty();
        f.Rows.Erased.OfType<UserMembership>().Should().OnlyContain(m => m.TenantId == f.Test).And.HaveCount(3);
        f.Rows.Erased.OfType<AccountDeletionRequest>().Should().ContainSingle(r => r.Id == request.Id);
        f.Rows.Erased.OfType<BillingEvent>().Should().NotBeEmpty()
            .And.OnlyContain(e => e.PayloadJson!.Contains(f.Test.ToString()) || e.PayloadJson.Contains(request.Id.ToString()));
        f.Rows.Erased.OfType<BillingEvent>().Should().NotContain(e => e.Type == BillingEventTypes.TrialStarted);

        var done = f.Published<CompanyHardDeleted>().Should().ContainSingle().Subject;
        done.RequestId.Should().Be(request.Id);
        done.TenantId.Should().Be(f.Test);
        done.CompanyName.Should().Be("Acme Test");
        done.DeleteUserIds.Should().Equal(onlyHere);
        done.DetachUserIds.Should().BeEquivalentTo([f.Owner, shared], "the owner keeps the account's other company");
        done.RequestedBy.Should().Be(Alice.Actor);
        done.ApprovedBy.Should().Be(Bob.Actor);
        f.Published<AccountHardDeleted>().Should().BeEmpty();
    }
}
