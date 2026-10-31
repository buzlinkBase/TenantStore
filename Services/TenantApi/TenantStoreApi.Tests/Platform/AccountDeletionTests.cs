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
            Host.Uow, Host.Events, Host.Notifier, Host.Publisher.Object, new TenantStoreAccountEraser(Host.Uow, Rows), Host.Confirmations, Host.Clock);

        public PlatformCompanyDeletionService CompanyDeletion => new(
            Host.Uow, Host.Events, Host.Publisher.Object, new TenantStoreAccountEraser(Host.Uow, Rows), Host.Confirmations, Host.Clock);

        public AccountDeletionRequestService Requests => new(
            Host.Uow, new PlatformDirectoryService(Host.Uow, Host.StateMachine), Deletion, CompanyDeletion, Host.Events,
            AccountDeletionGuards.Default, AccountDeletionGuards.Company,
            Microsoft.Extensions.Options.Options.Create(Host.Options), Host.Clock);

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

        public Task ReportAsync(Guid owner, Guid tenantId, bool succeeded = true, params string[] sources) =>
            ReportAsync(Host.SubscriptionOf(owner), tenantId, succeeded, sources);

        /// <summary>Answers from <paramref name="sources"/>; by default a success from every service, a failure from hrms-api.</summary>
        public async Task ReportAsync(AccountSubscription subscription, Guid tenantId, bool succeeded = true, params string[] sources)
        {
            if (sources.Length == 0) sources = succeeded ? [PurgeSources.Hrms, PurgeSources.Adms, PurgeSources.Portal] : [PurgeSources.Hrms];
            foreach (var source in sources)
            {
                await Deletion.RecordResultAsync(new CompanyDataPurged
                {
                    OwnerUserId = subscription.OwnerUserId,
                    SubscriptionId = subscription.Id,
                    TenantId = tenantId,
                    Stage = CompanyPurgeStage.HardDelete,
                    Succeeded = succeeded,
                    Error = succeeded ? null : "boom",
                }, source, CancellationToken.None);
                await Host.SaveAsync();
            }
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
    public async Task WithTheSecondApproverSettingOff_TheOperatorWhoAsked_CanApprove()
    {
        var f = new Fixture();
        f.Host.Options.RequireSecondDeletionApprover = false;
        var (owner, _) = await f.Host.TrialAccountAsync(OwnerEmail);
        var request = await f.RequestAsync(owner);
        request.SecondApproverRequired.Should().BeFalse();

        await f.Requests.ApproveAsync(request.Id, OwnerEmail, Alice);
        await f.Host.SaveAsync();

        f.Host.SubscriptionOf(owner).PurgeState.Should().Be(DataPurgeState.HardDeleting);
        f.Host.Uow.Context.AccountDeletionRequests.Single().DecidedById.Should().Be(Alice.Id);
    }

    [Fact]
    public async Task ByDefault_TheViewSaysASecondApproverIsRequired()
    {
        var f = new Fixture();
        var (owner, _) = await f.Host.TrialAccountAsync(OwnerEmail);

        (await f.RequestAsync(owner)).SecondApproverRequired.Should().BeTrue();
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

    // ── Every service confirms ───────────────────────────────────────────────

    [Fact]
    public async Task TheAccountIsErased_OnlyOnceEveryServiceHasConfirmedEveryCompany()
    {
        var f = new Fixture();
        f.Host.Options.ErasureParticipants = "hrms,adms,portal";
        var (owner, first) = await f.Host.TrialAccountAsync(OwnerEmail);
        var second = f.Host.AddCompany(owner, "Acme Two", OwnerEmail);
        var request = await f.RequestAsync(owner);
        await f.ApproveAsync(request.Id);

        await f.ReportAsync(owner, first);
        await f.ReportAsync(owner, second, true, PurgeSources.Hrms, PurgeSources.Adms);
        f.Published<AccountHardDeleted>().Should().BeEmpty("the client portal hasn't confirmed the second company");
        (await f.Deletion.PendingConfirmationsAsync(owner, CancellationToken.None))
            .Should().ContainKey(second).WhoseValue.Should().Equal(PurgeSources.Portal);

        await f.ReportAsync(owner, second, true, PurgeSources.Portal);

        f.Published<AccountHardDeleted>().Should().ContainSingle();
    }

    [Fact]
    public async Task AFailureFromTheDeviceService_ParksTheAccount_AndRetryAsksForThatCompanyAgain()
    {
        var f = new Fixture();
        var (owner, company) = await f.Host.TrialAccountAsync(OwnerEmail);
        var request = await f.RequestAsync(owner);
        await f.ApproveAsync(request.Id);

        await f.ReportAsync(owner, company, true, PurgeSources.Hrms, PurgeSources.Portal);
        await f.ReportAsync(owner, company, false, PurgeSources.Adms);
        f.Host.SubscriptionOf(owner).PurgeState.Should().Be(DataPurgeState.HardDeleteFailed);

        f.Host.Publisher.Invocations.Clear();
        await f.Deletion.RetryAsync(owner, Alice.Actor, CancellationToken.None);
        await f.Host.SaveAsync();
        f.Published<PurgeCompanyData>().Select(m => m.TenantId).Should().Equal(company);

        await f.ReportAsync(owner, company, true, PurgeSources.Adms);
        f.Published<AccountHardDeleted>().Should().ContainSingle();
    }

    [Fact]
    public async Task AnAnswerFromAServiceThatIsntAParticipant_IsIgnored()
    {
        var f = new Fixture();
        f.Host.Options.ErasureParticipants = "hrms";
        var (owner, company) = await f.Host.TrialAccountAsync(OwnerEmail);
        var request = await f.RequestAsync(owner);
        await f.ApproveAsync(request.Id);

        await f.ReportAsync(owner, company, false, PurgeSources.Portal);
        f.Host.SubscriptionOf(owner).PurgeState.Should().Be(DataPurgeState.HardDeleting);

        await f.ReportAsync(owner, company, true, PurgeSources.Hrms);
        f.Published<AccountHardDeleted>().Should().ContainSingle("only hrms-api is configured to answer");
    }

    [Fact]
    public async Task EachAnswer_ChangesTheAccountsConcurrencyStamp_SoTwoAtOnceCantBothMissTheOther()
    {
        var f = new Fixture();
        var (owner, first) = await f.Host.TrialAccountAsync(OwnerEmail);
        f.Host.AddCompany(owner, "Acme Two", OwnerEmail);
        var request = await f.RequestAsync(owner);
        await f.ApproveAsync(request.Id);
        var before = f.Host.SubscriptionOf(owner).ConcurrencyStamp;

        await f.ReportAsync(owner, first, true, PurgeSources.Hrms);

        f.Host.SubscriptionOf(owner).ConcurrencyStamp.Should().NotBe(before);
    }

    [Fact]
    public async Task ARedeliveredConfirmation_IsCountedOnce()
    {
        var f = new Fixture();
        var (owner, company) = await f.Host.TrialAccountAsync(OwnerEmail);
        var request = await f.RequestAsync(owner);
        await f.ApproveAsync(request.Id);

        await f.ReportAsync(owner, company, true, PurgeSources.Hrms, PurgeSources.Hrms, PurgeSources.Adms);

        f.Published<AccountHardDeleted>().Should().BeEmpty();
        f.Host.Uow.Context.BillingEvents.Count(e => e.Type == BillingEventTypes.HardDeleteCompanyDone).Should().Be(2);
    }

    [Fact]
    public async Task Retry_WhenEveryServiceHasAlreadyConfirmed_FinishesTheErasure()
    {
        var f = new Fixture();
        f.Host.Options.ErasureParticipants = "hrms,adms";
        var (owner, company) = await f.Host.TrialAccountAsync(OwnerEmail);
        var request = await f.RequestAsync(owner);
        await f.ApproveAsync(request.Id);
        await f.ReportAsync(owner, company, true, PurgeSources.Hrms);
        // adms confirms while the account was parked by an earlier failure.
        f.Host.SubscriptionOf(owner).PurgeState = DataPurgeState.HardDeleteFailed;
        await f.ReportAsync(owner, company, true, PurgeSources.Adms);

        f.Published<AccountHardDeleted>().Should().ContainSingle();
    }

    [Fact]
    public async Task AnAccountWithNoCompanies_IsErasedOnApproval()
    {
        var f = new Fixture();
        var (owner, company) = await f.Host.TrialAccountAsync(OwnerEmail);
        var request = await f.RequestAsync(owner);
        // The company went to someone else meanwhile (a removed company would still be erased).
        f.Host.Uow.Context.Tenants.Single(t => t.Id == company).UserId = Guid.NewGuid();
        await f.Host.SaveAsync();

        await f.ApproveAsync(request.Id);

        f.Published<PurgeCompanyData>().Should().BeEmpty();
        f.Published<AccountHardDeleted>().Should().ContainSingle().Which.TenantIds.Should().BeEmpty();
    }

    // ── Guards ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Request_IsRefused_WhenTheAccountPaidForStorage()
    {
        var f = new Fixture();
        var (owner, _) = await f.Host.TrialAccountAsync(OwnerEmail);
        f.Host.Uow.Context.StorageAddOnPayments.Add(new StorageAddOnPayment
        {
            Id = Guid.NewGuid(), SubscriptionId = f.Host.SubscriptionOf(owner).Id, AddOnId = Guid.NewGuid(), Blocks = 1, Amount = 400,
            PaidAtUtc = f.Host.Clock.UtcNow,
        });
        await f.Host.SaveAsync();

        var act = () => f.Requests.RequestAsync(owner, "Test account", OwnerEmail, Alice);

        await act.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*paid for storage*");
    }

    [Fact]
    public async Task Request_IsNotRefused_ForAFreeStorageEntry()
    {
        var f = new Fixture();
        var (owner, _) = await f.Host.TrialAccountAsync(OwnerEmail);
        f.Host.Uow.Context.StorageAddOnPayments.Add(new StorageAddOnPayment
        {
            Id = Guid.NewGuid(), SubscriptionId = f.Host.SubscriptionOf(owner).Id, AddOnId = Guid.NewGuid(), Blocks = 1, Amount = 0,
        });
        await f.Host.SaveAsync();

        var request = await f.RequestAsync(owner);

        request.Status.Should().Be(AccountDeletionRequestStatus.Pending);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(47, true)]
    [InlineData(49, false)]
    public async Task Request_IsRefused_WhileAnOnlineCheckoutCanStillBePaid(int hoursOld, bool refused)
    {
        var f = new Fixture();
        var (owner, _) = await f.Host.TrialAccountAsync(OwnerEmail);
        var subscription = f.Host.SubscriptionOf(owner);
        f.Host.Uow.Context.PaymentCheckouts.Add(new PaymentCheckout
        {
            Id = Guid.NewGuid(), SubscriptionId = subscription.Id, OwnerUserId = owner, PlanId = subscription.PlanId,
            Provider = "paymongo", CheckoutStatus = PaymentCheckoutStatus.Open, Amount = 999,
            CreatedAtUtc = f.Host.Clock.UtcNow.AddHours(-hoursOld),
        });
        await f.Host.SaveAsync();
        var requests = new AccountDeletionRequestService(
            f.Host.Uow, new PlatformDirectoryService(f.Host.Uow, f.Host.StateMachine), f.Deletion, f.CompanyDeletion, f.Host.Events,
            [new OpenCheckoutDeletionGuard(f.Host.Clock)], AccountDeletionGuards.Company,
            Microsoft.Extensions.Options.Options.Create(f.Host.Options), f.Host.Clock);

        var act = () => requests.RequestAsync(owner, "Test account", OwnerEmail, Alice);

        if (refused) await act.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*checkout*");
        else await act.Should().NotThrowAsync();
    }

    // ── Nothing undoes an erasure halfway ────────────────────────────────────

    private static PlatformAccountActionsService Actions(SubscriptionTestHost host) => new(
        host.Uow, host.Subscriptions, host.Events, host.Notifier, host.StateMachine,
        Microsoft.Extensions.Options.Options.Create(host.Options), host.Gateway, host.Clock);

    [Fact]
    public async Task AnAccountBeingErased_CantBeReinstated_OrPutOnLegalHold()
    {
        var f = new Fixture();
        var (owner, _) = await f.Host.TrialAccountAsync(OwnerEmail);
        var request = await f.RequestAsync(owner);
        await f.ApproveAsync(request.Id);

        var reinstate = () => Actions(f.Host).ReinstateAsync(owner, "Changed my mind", Alice.Actor);
        var hold = () => Actions(f.Host).SetLegalHoldAsync(owner, "Tax audit", Alice.Actor);

        await reinstate.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*being deleted*");
        await hold.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*being deleted*");
        f.Host.SubscriptionOf(owner).LegalHold.Should().BeFalse();
    }

    // ── Who keeps a login ────────────────────────────────────────────────────

    [Fact]
    public async Task StaffWhoOwnTheirOwnSubscription_KeepTheirLogin_EvenWithoutACompany()
    {
        var f = new Fixture();
        var (owner, company) = await f.Host.TrialAccountAsync(OwnerEmail);
        var staff = f.Host.AddMember(company, "HR");
        f.Host.Uow.Context.AccountSubscriptions.Add(new AccountSubscription { Id = Guid.NewGuid(), OwnerUserId = staff });
        await f.Host.SaveAsync();
        var request = await f.RequestAsync(owner);
        await f.ApproveAsync(request.Id);

        await f.ReportAsync(owner, company);

        var done = f.Published<AccountHardDeleted>().Single();
        done.DeleteUserIds.Should().Equal(owner);
        done.DetachUserIds.Should().Equal(staff);
    }

    [Fact]
    public async Task TheView_AsksForTheOwnersCurrentEmail()
    {
        var f = new Fixture();
        var (owner, _) = await f.Host.TrialAccountAsync(OwnerEmail);

        var request = await f.RequestAsync(owner);

        request.ConfirmWith.Should().Be(OwnerEmail);
    }
}
