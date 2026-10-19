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
/// Erasing a (test) account for good: two different operators, the guards checked at both steps, the
/// companies emptied first, and only this account's rows selected for deletion.
/// </summary>
public class AccountDeletionTests
{
    private const string OwnerEmail = "owner@acme.ph";
    private static readonly PlatformOperator Alice = new(Guid.NewGuid(), "platform:alice@onepunch.test");
    private static readonly PlatformOperator Bob = new(Guid.NewGuid(), "platform:bob@onepunch.test");

    /// <summary>InMemory can't run set-based deletes: records what each delete would have selected.</summary>
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

        public PlatformAccountDeletionService Deletion => new(
            Host.Uow, Host.Events, Host.Notifier, Host.Publisher.Object, new TenantStoreAccountEraser(Host.Uow, Rows), Host.Clock);

        public PlatformCompanyDeletionService CompanyDeletion => new(
            Host.Uow, Host.Events, Host.Publisher.Object, new TenantStoreAccountEraser(Host.Uow, Rows), Host.Clock);

        public AccountDeletionRequestService Requests => new(
            Host.Uow, new PlatformDirectoryService(Host.Uow, Host.StateMachine), Deletion, CompanyDeletion, Host.Events,
            AccountDeletionGuards.Default, AccountDeletionGuards.Company, Host.Clock);

        public List<T> Published<T>() => Host.Publisher.Invocations
            .Where(i => i.Arguments.FirstOrDefault() is T)
            .Select(i => (T)i.Arguments[0])
            .ToList();

        public async Task<AccountDeletionRequestView> RequestAsync(Guid owner)
        {
            var request = await Requests.RequestAsync(owner, "Test account", OwnerEmail, Alice);
            await Host.SaveAsync();
            return request;
        }

        public async Task ApproveAsync(Guid requestId)
        {
            await Requests.ApproveAsync(requestId, OwnerEmail, Bob);
            await Host.SaveAsync();
        }

        public Task ReportAsync(Guid owner, Guid tenantId, bool succeeded = true) =>
            ReportAsync(Host.SubscriptionOf(owner), tenantId, succeeded);

        public async Task ReportAsync(AccountSubscription subscription, Guid tenantId, bool succeeded = true)
        {
            await Deletion.RecordResultAsync(new CompanyDataPurged
            {
                OwnerUserId = subscription.OwnerUserId,
                SubscriptionId = subscription.Id,
                TenantId = tenantId,
                Stage = CompanyPurgeStage.HardDelete,
                Succeeded = succeeded,
                Error = succeeded ? null : "boom",
            }, CancellationToken.None);
            await Host.SaveAsync();
        }
    }

    // ── Requesting ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Request_NeedsTheOwnerEmailTypedExactly()
    {
        var f = new Fixture();
        var (owner, _) = await f.Host.TrialAccountAsync(OwnerEmail);

        var act = () => f.Requests.RequestAsync(owner, "Test account", "someone@else.ph", Alice);

        await act.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*doesn't match*");
    }

    [Fact]
    public async Task Request_IgnoresCaseAndSpacesInTheEmail_AndDeletesNothingYet()
    {
        var f = new Fixture();
        var (owner, _) = await f.Host.TrialAccountAsync(OwnerEmail);

        var request = await f.Requests.RequestAsync(owner, "Test account", "  OWNER@acme.ph ", Alice);
        await f.Host.SaveAsync();

        request.Status.Should().Be(AccountDeletionRequestStatus.Pending);
        request.RequestedById.Should().Be(Alice.Id);
        request.ExpiresAtUtc.Should().Be(f.Host.Clock.UtcNow.Add(AccountDeletionRequestService.Lifetime));
        request.CompanyNames.Should().Equal("Acme");
        f.Host.SubscriptionOf(owner).PurgeState.Should().Be(DataPurgeState.None);
        f.Published<PurgeCompanyData>().Should().BeEmpty();
    }

    [Fact]
    public async Task Request_IsRefused_WhileAPaymentIsNotVoided()
    {
        var f = new Fixture();
        var (owner, _) = await f.Host.TrialAccountAsync(OwnerEmail);
        var subscription = f.Host.SubscriptionOf(owner);
        var payment = new SubscriptionPayment
        {
            Id = Guid.NewGuid(), SubscriptionId = subscription.Id, PlanId = subscription.PlanId, Amount = 999,
            PaidAtUtc = f.Host.Clock.UtcNow, PeriodStartUtc = f.Host.Clock.UtcNow, PeriodEndUtc = f.Host.Clock.UtcNow.AddMonths(1),
        };
        f.Host.Uow.Context.SubscriptionPayments.Add(payment);
        await f.Host.SaveAsync();

        var act = () => f.Requests.RequestAsync(owner, "Test account", OwnerEmail, Alice);
        await act.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*isn't voided*");

        payment.VoidedAtUtc = f.Host.Clock.UtcNow;
        await f.Host.SaveAsync();
        await f.Requests.RequestAsync(owner, "Test account", OwnerEmail, Alice);
    }

    [Fact]
    public async Task Request_IsRefused_UnderLegalHold()
    {
        var f = new Fixture();
        var (owner, _) = await f.Host.TrialAccountAsync(OwnerEmail);
        f.Host.SubscriptionOf(owner).LegalHold = true;
        await f.Host.SaveAsync();

        var act = () => f.Requests.RequestAsync(owner, "Test account", OwnerEmail, Alice);

        await act.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*legal hold*");
    }

    [Fact]
    public async Task Request_Twice_IsRefused_UntilTheFirstExpires()
    {
        var f = new Fixture();
        var (owner, _) = await f.Host.TrialAccountAsync(OwnerEmail);
        await f.RequestAsync(owner);

        var again = () => f.Requests.RequestAsync(owner, "Test account", OwnerEmail, Bob);
        await again.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*already waiting*");

        f.Host.Clock.Advance(AccountDeletionRequestService.Lifetime);
        await f.Requests.RequestAsync(owner, "Test account", OwnerEmail, Bob);
    }

    // ── Deciding ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Approve_ByTheOperatorWhoAsked_IsRefused()
    {
        var f = new Fixture();
        var (owner, _) = await f.Host.TrialAccountAsync(OwnerEmail);
        var request = await f.RequestAsync(owner);

        var act = () => f.Requests.ApproveAsync(request.Id, OwnerEmail, Alice with { Actor = "platform:other-email@onepunch.test" });

        await act.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*second administrator*");
        f.Host.SubscriptionOf(owner).PurgeState.Should().Be(DataPurgeState.None);
    }

    [Fact]
    public async Task Approve_AfterTheRequestExpired_IsRefused()
    {
        var f = new Fixture();
        var (owner, _) = await f.Host.TrialAccountAsync(OwnerEmail);
        var request = await f.RequestAsync(owner);
        f.Host.Clock.Advance(AccountDeletionRequestService.Lifetime + TimeSpan.FromMinutes(1));

        var act = () => f.Requests.ApproveAsync(request.Id, OwnerEmail, Bob);

        await act.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*expired*");
        (await f.Requests.GetLatestAsync(owner))!.Status.Should().Be(AccountDeletionRequestStatus.Expired);
        (await f.Requests.ListPendingAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Approve_NeedsTheEmailTypedAgain()
    {
        var f = new Fixture();
        var (owner, _) = await f.Host.TrialAccountAsync(OwnerEmail);
        var request = await f.RequestAsync(owner);

        var act = () => f.Requests.ApproveAsync(request.Id, "wrong@acme.ph", Bob);

        await act.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*doesn't match*");
    }

    [Fact]
    public async Task Approve_LocksTheAccount_AndAsksForEveryCompanyToBeErased()
    {
        var f = new Fixture();
        var (owner, firstCompany) = await f.Host.TrialAccountAsync(OwnerEmail);
        var secondCompany = f.Host.AddCompany(owner, "Acme Two", OwnerEmail);
        var request = await f.RequestAsync(owner);

        await f.ApproveAsync(request.Id);

        var subscription = f.Host.SubscriptionOf(owner);
        subscription.PurgeState.Should().Be(DataPurgeState.HardDeleting);
        subscription.SubStatus.Should().Be(AccountSubscriptionStatus.Suspended);
        var asked = f.Published<PurgeCompanyData>();
        asked.Should().OnlyContain(m => m.Stage == CompanyPurgeStage.HardDelete && m.OwnerUserId == owner);
        asked.Select(m => m.TenantId).Should().BeEquivalentTo([firstCompany, secondCompany]);
        var stored = f.Host.Uow.Context.AccountDeletionRequests.Single();
        stored.RequestStatus.Should().Be(AccountDeletionRequestStatus.Approved);
        stored.DecidedById.Should().Be(Bob.Id);
        f.Rows.Erased.Should().BeEmpty("nothing in Tenant Service goes before the companies report back");
    }

    [Fact]
    public async Task Approve_ReChecksTheGuards()
    {
        var f = new Fixture();
        var (owner, _) = await f.Host.TrialAccountAsync(OwnerEmail);
        var request = await f.RequestAsync(owner);
        f.Host.SubscriptionOf(owner).LegalHold = true;
        await f.Host.SaveAsync();

        var act = () => f.Requests.ApproveAsync(request.Id, OwnerEmail, Bob);

        await act.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*legal hold*");
    }

    [Fact]
    public async Task Reject_LeavesTheAccountAlone_AndNeedsAReason()
    {
        var f = new Fixture();
        var (owner, _) = await f.Host.TrialAccountAsync(OwnerEmail);
        var request = await f.RequestAsync(owner);

        var noReason = () => f.Requests.RejectAsync(request.Id, " ", Bob);
        await noReason.Should().ThrowAsync<SubscriptionRuleException>();

        await f.Requests.RejectAsync(request.Id, "This is a real customer", Bob);
        await f.Host.SaveAsync();

        (await f.Requests.GetLatestAsync(owner))!.Status.Should().Be(AccountDeletionRequestStatus.Rejected);
        f.Host.SubscriptionOf(owner).PurgeState.Should().Be(DataPurgeState.None);
        var approve = () => f.Requests.ApproveAsync(request.Id, OwnerEmail, Bob);
        await approve.Should().ThrowAsync<SubscriptionRuleException>();
    }

    [Fact]
    public async Task Cancel_IsOnlyForTheOperatorWhoAsked()
    {
        var f = new Fixture();
        var (owner, _) = await f.Host.TrialAccountAsync(OwnerEmail);
        var request = await f.RequestAsync(owner);

        var byBob = () => f.Requests.CancelAsync(owner, request.Id, Bob);
        await byBob.Should().ThrowAsync<SubscriptionRuleException>();

        await f.Requests.CancelAsync(owner, request.Id, Alice);
        await f.Host.SaveAsync();
        (await f.Requests.GetLatestAsync(owner))!.Status.Should().Be(AccountDeletionRequestStatus.Canceled);
    }

    // ── The companies report back ────────────────────────────────────────────

    [Fact]
    public async Task AFailedCompany_ParksTheAccount_AndRetryAsksOnlyThePendingCompanies()
    {
        var f = new Fixture();
        var (owner, firstCompany) = await f.Host.TrialAccountAsync(OwnerEmail);
        var secondCompany = f.Host.AddCompany(owner, "Acme Two", OwnerEmail);
        var request = await f.RequestAsync(owner);
        await f.ApproveAsync(request.Id);

        await f.ReportAsync(owner, firstCompany);
        await f.ReportAsync(owner, secondCompany, succeeded: false);
        f.Host.SubscriptionOf(owner).PurgeState.Should().Be(DataPurgeState.HardDeleteFailed);

        f.Host.Publisher.Invocations.Clear();
        await f.Deletion.RetryAsync(owner, Alice.Actor, CancellationToken.None);
        await f.Host.SaveAsync();

        f.Host.SubscriptionOf(owner).PurgeState.Should().Be(DataPurgeState.HardDeleting);
        f.Published<PurgeCompanyData>().Select(m => m.TenantId).Should().Equal(secondCompany);
        f.Rows.Erased.Should().BeEmpty();
    }

    [Fact]
    public async Task AnArchivePurgeAnswer_DoesNotMoveAnAccountThatIsntBeingErased()
    {
        var f = new Fixture();
        var (owner, company) = await f.Host.TrialAccountAsync(OwnerEmail);

        await f.ReportAsync(owner, company);

        f.Host.SubscriptionOf(owner).PurgeState.Should().Be(DataPurgeState.None);
        f.Rows.Erased.Should().BeEmpty();
    }

    [Fact]
    public async Task TheLastCompany_ErasesOnlyThisAccountsRows_AndHandsTheLoginsToAuth()
    {
        var f = new Fixture();
        var (owner, company) = await f.Host.TrialAccountAsync(OwnerEmail);
        var staffOnlyHere = f.Host.AddMember(company, "Admin");
        var (otherOwner, otherCompany) = await f.Host.TrialAccountAsync("other@beta.ph");
        var sharedStaff = f.Host.AddMember(company, "HR");
        f.Host.AddMember(otherCompany, "HR", userId: sharedStaff);
        var subscription = f.Host.SubscriptionOf(owner);
        var otherSubscription = f.Host.SubscriptionOf(otherOwner);
        var request = await f.RequestAsync(owner);
        await f.ApproveAsync(request.Id);

        await f.ReportAsync(subscription, company);

        f.Rows.Erased.OfType<Tenant>().Select(t => t.Id).Should().Equal(company);
        f.Rows.Erased.OfType<AccountSubscription>().Select(s => s.Id).Should().Equal(subscription.Id);
        f.Rows.Erased.OfType<UserMembership>().Should().OnlyContain(m => m.TenantId == company).And.HaveCount(3);
        f.Rows.Erased.OfType<BillingEvent>().Should().NotBeEmpty().And.OnlyContain(e => e.OwnerUserId == owner);
        f.Rows.Erased.OfType<AccountDeletionRequest>().Should().ContainSingle();
        f.Rows.Erased.OfType<MembershipRole>().Should().HaveCount(3);
        f.Rows.Erased.Should().NotContain(r => r is AccountSubscription && ((AccountSubscription)r).Id == otherSubscription.Id);

        var done = f.Published<AccountHardDeleted>().Should().ContainSingle().Subject;
        done.RequestId.Should().Be(request.Id);
        done.OwnerEmail.Should().Be(OwnerEmail);
        done.TenantIds.Should().Equal(company);
        done.CompanyNames.Should().Equal("Acme");
        done.DeleteUserIds.Should().BeEquivalentTo([owner, staffOnlyHere]);
        done.DetachUserIds.Should().Equal(sharedStaff);
        done.RequestedBy.Should().Be(Alice.Actor);
        done.ApprovedBy.Should().Be(Bob.Actor);
        done.Reason.Should().Be("Test account");
    }

    [Fact]
    public async Task AnOwnerWhoOwnsAnotherCompanyElsewhere_IsDetachedNotDeleted()
    {
        var f = new Fixture();
        var (owner, company) = await f.Host.TrialAccountAsync(OwnerEmail);
        var (otherOwner, otherCompany) = await f.Host.TrialAccountAsync("other@beta.ph");
        f.Host.AddMember(company, "Admin", userId: otherOwner);
        var request = await f.RequestAsync(owner);
        await f.ApproveAsync(request.Id);

        await f.ReportAsync(owner, company);

        var done = f.Published<AccountHardDeleted>().Single();
        done.DeleteUserIds.Should().Equal(owner);
        done.DetachUserIds.Should().Equal(otherOwner);
        f.Rows.Erased.OfType<Tenant>().Should().NotContain(t => t.Id == otherCompany);
    }
}
