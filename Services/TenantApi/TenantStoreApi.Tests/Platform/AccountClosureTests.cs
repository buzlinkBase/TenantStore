using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Onepunch.Common.Lib.DTO;
using TenantStoreApi.Core.Services.Platform;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Platform;

/// <summary>
/// An owner closing their own account: asked in the app, confirmed from the email, a cooling-off the owner can
/// cancel, then an operator approves (the account is locked and its purge scheduled, never hard-deleted) or rejects.
/// </summary>
public class AccountClosureTests
{
    private const string OwnerEmail = "owner@acme.ph";
    private static readonly PlatformOperator Alice = new(Guid.NewGuid(), "platform:alice@onepunch.test");

    private sealed class Fixture
    {
        public SubscriptionTestHost Host { get; } = new() { Options = { AppBaseUrl = "https://app.test" } };
        public Guid Owner { get; private set; }

        public AccountClosureService Closures => new(
            Host.Uow, new PlatformDirectoryService(Host.Uow, Host.StateMachine), Host.Events, Host.Notifier, Host.Publisher.Object,
            Microsoft.Extensions.Options.Options.Create(Host.Options), Host.Clock);

        public AccountSubscription Subscription => Host.SubscriptionOf(Owner);

        public async Task<Fixture> SeedAsync()
        {
            (Owner, _) = await Host.TrialAccountAsync(OwnerEmail);
            return this;
        }

        public List<SendEmailRequested> Emails() => Host.Publisher.Invocations
            .Where(i => i.Arguments.FirstOrDefault() is SendEmailRequested)
            .Select(i => (SendEmailRequested)i.Arguments[0])
            .ToList();

        /// <summary>The one-time token in the link of the most recent email that has one for <paramref name="action"/>.</summary>
        public string Token(string action) =>
            Regex.Match(Emails().Last(e => e.TextBody!.Contains($"action={action}&")).TextBody!, $"action={action}&token=([0-9a-f]+)").Groups[1].Value;

        public async Task<AccountClosureView> AskAsync()
        {
            var view = await Closures.RequestAsync(Owner, "Closing the business", OwnerEmail, CancellationToken.None);
            await Host.SaveAsync();
            return view;
        }

        public async Task ConfirmAsync()
        {
            (await Closures.ConfirmAsync(Token("confirm"), CancellationToken.None)).Should().BeTrue();
            await Host.SaveAsync();
        }

        /// <summary>Asked, confirmed, and the cooling-off over, so it is waiting for an operator.</summary>
        public async Task<Guid> ReadyForReviewAsync()
        {
            var view = await AskAsync();
            await ConfirmAsync();
            Host.Clock.Advance(TimeSpan.FromDays(Host.Options.ClosureCoolingOffDays) + TimeSpan.FromHours(1));
            await Host.Lifecycle.ProcessAsync(Subscription.Id, CancellationToken.None);
            return view.Id;
        }

        public AccountClosureRequest Stored(Guid id) => Host.Uow.Context.AccountClosureRequests.AsNoTracking().Single(r => r.Id == id);
    }

    // ── Asking ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Asking_SendsAConfirmationEmail_AndChangesNothingElse()
    {
        var f = await new Fixture().SeedAsync();
        var statusBefore = f.Subscription.SubStatus;

        var view = await f.AskAsync();

        view.Status.Should().Be(AccountClosureStatus.PendingConfirmation);
        view.CoolingOffEndsAtUtc.Should().BeNull();
        f.Subscription.SubStatus.Should().Be(statusBefore);
        f.Subscription.PurgeAfterUtc.Should().BeNull();
        var email = f.Emails().Should().ContainSingle().Subject;
        email.To.Should().Be(OwnerEmail);
        email.TextBody.Should().Contain("action=confirm&").And.Contain("action=cancel&").And.Contain("https://app.test/account-closure");
        SendEmailRequested.TryNormalize(email, out _, out var problem).Should().BeTrue(problem);
    }

    [Fact]
    public async Task Asking_NeedsTheOwnerEmailTyped_AndTheAppAddressConfigured()
    {
        var f = await new Fixture().SeedAsync();

        var wrong = () => f.Closures.RequestAsync(f.Owner, null, "someone@else.ph", CancellationToken.None);
        await wrong.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*doesn't match*");

        f.Host.Options.AppBaseUrl = "";
        var noLinks = () => f.Closures.RequestAsync(f.Owner, null, OwnerEmail, CancellationToken.None);
        await noLinks.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*isn't available*");
    }

    [Fact]
    public async Task OnlyTheTokensHashesAreStored()
    {
        var f = await new Fixture().SeedAsync();
        var view = await f.AskAsync();

        var stored = f.Stored(view.Id);

        stored.ConfirmTokenHash.Should().HaveLength(64).And.NotBe(f.Token("confirm"));
        stored.CancelTokenHash.Should().HaveLength(64).And.NotBe(f.Token("cancel"));
    }

    [Fact]
    public async Task OnlyOneRequestCanBeOpenAtATime_ButANewOneCanFollowACancel()
    {
        var f = await new Fixture().SeedAsync();
        await f.AskAsync();

        var again = () => f.Closures.RequestAsync(f.Owner, null, OwnerEmail, CancellationToken.None);
        await again.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*already asked*");

        await f.Closures.CancelAsync(f.Owner, CancellationToken.None);
        await f.Host.SaveAsync();
        await f.AskAsync();
    }

    // ── Confirming ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Confirming_StartsTheCoolingOff_AndTheLinkWorksOnce()
    {
        var f = await new Fixture().SeedAsync();
        var view = await f.AskAsync();
        var token = f.Token("confirm");

        (await f.Closures.ConfirmAsync(token, CancellationToken.None)).Should().BeTrue();
        await f.Host.SaveAsync();

        var stored = f.Stored(view.Id);
        stored.RequestStatus.Should().Be(AccountClosureStatus.CoolingOff);
        stored.CoolingOffEndsAtUtc.Should().Be(f.Host.Clock.UtcNow.AddDays(f.Host.Options.ClosureCoolingOffDays));
        f.Emails().Should().HaveCount(2, "the confirmation, then a note on what happens next");
        (await f.Closures.ConfirmAsync(token, CancellationToken.None)).Should().BeFalse("a link can't be used twice");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-real-token")]
    public async Task AnUnknownOrEmptyToken_ConfirmsNothing(string? token)
    {
        var f = await new Fixture().SeedAsync();
        var view = await f.AskAsync();

        (await f.Closures.ConfirmAsync(token, CancellationToken.None)).Should().BeFalse();
        (await f.Closures.CancelByTokenAsync(token, CancellationToken.None)).Should().BeFalse();

        f.Stored(view.Id).RequestStatus.Should().Be(AccountClosureStatus.PendingConfirmation);
    }

    [Fact]
    public async Task AnExpiredConfirmationLink_DoesNothing_AndTheRequestExpires()
    {
        var f = await new Fixture().SeedAsync();
        var view = await f.AskAsync();
        var token = f.Token("confirm");
        f.Host.Clock.Advance(TimeSpan.FromHours(f.Host.Options.ClosureConfirmHours) + TimeSpan.FromMinutes(1));

        (await f.Closures.ConfirmAsync(token, CancellationToken.None)).Should().BeFalse();
        await f.Host.Lifecycle.ProcessAsync(f.Subscription.Id, CancellationToken.None);

        f.Stored(view.Id).RequestStatus.Should().Be(AccountClosureStatus.Expired);
    }

    // ── Changing their mind ──────────────────────────────────────────────────

    [Fact]
    public async Task TheOwnerCanCancel_FromTheApp_OrWithTheThisWasntMeLink()
    {
        var f = await new Fixture().SeedAsync();
        var first = await f.AskAsync();
        await f.ConfirmAsync();
        await f.Closures.CancelAsync(f.Owner, CancellationToken.None);
        await f.Host.SaveAsync();
        f.Stored(first.Id).RequestStatus.Should().Be(AccountClosureStatus.Canceled);

        var second = await f.AskAsync();
        (await f.Closures.CancelByTokenAsync(f.Token("cancel"), CancellationToken.None)).Should().BeTrue();
        await f.Host.SaveAsync();
        f.Stored(second.Id).RequestStatus.Should().Be(AccountClosureStatus.Canceled);
        (await f.Closures.CancelByTokenAsync(f.Token("cancel"), CancellationToken.None)).Should().BeFalse("nothing left to cancel");
    }

    [Fact]
    public async Task CancelingWithNothingOpen_IsRefused()
    {
        var f = await new Fixture().SeedAsync();

        var act = () => f.Closures.CancelAsync(f.Owner, CancellationToken.None);

        await act.Should().ThrowAsync<SubscriptionRuleException>();
    }

    // ── The cooling-off and the operator ─────────────────────────────────────

    [Fact]
    public async Task OnlyOnceTheCoolingOffIsOver_DoesItReachAnOperator()
    {
        var f = await new Fixture().SeedAsync();
        var view = await f.AskAsync();
        await f.ConfirmAsync();

        await f.Host.Lifecycle.ProcessAsync(f.Subscription.Id, CancellationToken.None);
        f.Stored(view.Id).RequestStatus.Should().Be(AccountClosureStatus.CoolingOff);
        var early = () => f.Closures.ApproveAsync(view.Id, OwnerEmail, Alice, CancellationToken.None);
        await early.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*change their mind*");

        f.Host.Clock.Advance(TimeSpan.FromDays(f.Host.Options.ClosureCoolingOffDays) + TimeSpan.FromHours(1));
        await f.Host.Lifecycle.ProcessAsync(f.Subscription.Id, CancellationToken.None);

        f.Stored(view.Id).RequestStatus.Should().Be(AccountClosureStatus.ReadyForReview);
        (await f.Closures.ListForReviewAsync(CancellationToken.None)).Should().ContainSingle(r => r.Id == view.Id && r.OwnerEmail == OwnerEmail);
    }

    [Fact]
    public async Task Approving_LocksTheAccount_SchedulesThePurge_AndTellsTheOwner_ButDeletesNothing()
    {
        var f = await new Fixture().SeedAsync();
        var id = await f.ReadyForReviewAsync();
        f.Subscription.SubStatus = AccountSubscriptionStatus.Active;
        await f.Host.SaveAsync();
        var emailsBefore = f.Emails().Count;

        await f.Closures.ApproveAsync(id, " OWNER@acme.ph ", Alice, CancellationToken.None);
        await f.Host.SaveAsync();

        var subscription = f.Subscription;
        subscription.SubStatus.Should().Be(AccountSubscriptionStatus.Suspended);
        subscription.SuspensionReason.Should().Contain("closed at the owner's request");
        subscription.PurgeAfterUtc.Should().Be(f.Host.Clock.UtcNow.AddDays(f.Host.Options.MinPurgeNoticeDays));
        subscription.PurgeState.Should().Be(DataPurgeState.Scheduled);
        f.Stored(id).RequestStatus.Should().Be(AccountClosureStatus.Approved);
        f.Stored(id).DecidedBy.Should().Be(Alice.Actor);
        f.Emails().Skip(emailsBefore).Should().ContainSingle().Which.Subject.Should().Contain("closed");
        f.Host.Publisher.Invocations.Select(i => i.Arguments.FirstOrDefault()).OfType<PurgeCompanyData>()
            .Should().BeEmpty("approval schedules the purge; the daily job runs it when due");
    }

    [Fact]
    public async Task PayingAgain_BeforeThePurgeDate_UndoesTheScheduledPurge()
    {
        var f = await new Fixture().SeedAsync();
        var id = await f.ReadyForReviewAsync();
        await f.Closures.ApproveAsync(id, OwnerEmail, Alice, CancellationToken.None);
        await f.Host.SaveAsync();

        f.Subscription.SubStatus = AccountSubscriptionStatus.Active;
        DataPurgeSchedule.Reconcile(f.Subscription, f.Host.Clock.UtcNow, f.Host.Options);

        f.Subscription.PurgeState.Should().Be(DataPurgeState.None);
        f.Subscription.PurgeAfterUtc.Should().BeNull();
    }

    [Fact]
    public async Task Approving_NeedsTheOwnerEmailAgain_AndIsRefusedForAnAccountAlreadyPurged()
    {
        var f = await new Fixture().SeedAsync();
        var id = await f.ReadyForReviewAsync();

        var wrong = () => f.Closures.ApproveAsync(id, "someone@else.ph", Alice, CancellationToken.None);
        await wrong.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*doesn't match*");

        f.Subscription.PurgeState = DataPurgeState.Purged;
        var purged = () => f.Closures.ApproveAsync(id, OwnerEmail, Alice, CancellationToken.None);
        await purged.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*already been removed*");
    }

    [Fact]
    public async Task Rejecting_NeedsAReason_LeavesTheAccountAlone_AndTellsTheOwnerWhy()
    {
        var f = await new Fixture().SeedAsync();
        var id = await f.ReadyForReviewAsync();
        var statusBefore = f.Subscription.SubStatus;
        var emailsBefore = f.Emails().Count;

        var noReason = () => f.Closures.RejectAsync(id, " ", Alice, CancellationToken.None);
        await noReason.Should().ThrowAsync<SubscriptionRuleException>();

        await f.Closures.RejectAsync(id, "Payroll run still open", Alice, CancellationToken.None);
        await f.Host.SaveAsync();

        f.Stored(id).RequestStatus.Should().Be(AccountClosureStatus.Rejected);
        f.Stored(id).DecisionReason.Should().Be("Payroll run still open");
        f.Subscription.SubStatus.Should().Be(statusBefore);
        f.Emails().Skip(emailsBefore).Should().ContainSingle().Which.TextBody.Should().Contain("Payroll run still open");
        var again = () => f.Closures.RejectAsync(id, "Twice", Alice, CancellationToken.None);
        await again.Should().ThrowAsync<SubscriptionRuleException>();
    }

    // ── The owner's view ─────────────────────────────────────────────────────

    [Fact]
    public async Task TheOwnerSeesTheirLatestRequest_AndNothingBeforeTheyAsk()
    {
        var f = await new Fixture().SeedAsync();
        (await f.Closures.GetForOwnerAsync(f.Owner, CancellationToken.None)).Should().BeNull();

        var view = await f.AskAsync();

        (await f.Closures.GetForOwnerAsync(f.Owner, CancellationToken.None))!.Id.Should().Be(view.Id);
    }

    // ── Erasing an account takes these too ───────────────────────────────────

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

    [Fact]
    public async Task ErasingTheAccount_TakesItsClosureRequests_AndNoOthers()
    {
        var f = await new Fixture().SeedAsync();
        await f.AskAsync();
        f.Host.Uow.Context.AccountClosureRequests.Add(new AccountClosureRequest
        {
            Id = Guid.NewGuid(), OwnerUserId = Guid.NewGuid(), RequestStatus = AccountClosureStatus.CoolingOff,
        });
        await f.Host.SaveAsync();
        var rows = new RecordingRowEraser();

        await new TenantStoreAccountEraser(f.Host.Uow, rows).EraseAsync(
            new AccountErasureScope(f.Owner, f.Subscription.Id, [], []), CancellationToken.None);

        rows.Erased.OfType<AccountClosureRequest>().Should().ContainSingle().Which.OwnerUserId.Should().Be(f.Owner);
    }

    // ── The emails ───────────────────────────────────────────────────────────

    [Fact]
    public void TheEmails_EncodeWhatTheyShow_AndSkipAMissingAddress()
    {
        var rejected = AccountClosureEmailComposer.Rejected("owner@acme.ph", "<b>open</b> run")!;

        rejected.HtmlBody.Should().NotContain("<b>open</b>").And.Contain("&lt;b&gt;open&lt;/b&gt;");
        AccountClosureEmailComposer.Rejected("  ", "x").Should().BeNull();
        AccountClosureEmailComposer.Approved(null, DateTime.UtcNow).Should().BeNull();
        AccountClosureEmailComposer.Approved("owner@acme.ph", new DateTime(2026, 12, 1, 4, 0, 0, DateTimeKind.Utc))!
            .TextBody.Should().Contain("December 1, 2026 12:00 PM (Philippine time)");
    }
}
