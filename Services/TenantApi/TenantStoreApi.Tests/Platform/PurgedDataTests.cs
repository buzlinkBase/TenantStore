using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Onepunch.Common.Lib;
using Onepunch.Common.Lib.DTO;
using TenantStoreApi.Core.Services.Platform;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Platform;

/// <summary>
/// What a purge keeps and what an operator may do with it: finding purged companies without their ids,
/// reading the retained audit trail and downloading an archive (each needs a reason and is recorded), and
/// destroying archives early (two different operators, never on legal hold).
/// </summary>
public class PurgedDataTests
{
    private const string OwnerEmail = "owner@acme.ph";
    private static readonly PlatformOperator Alice = new(Guid.NewGuid(), "platform:alice@onepunch.test");
    private static readonly PlatformOperator Bob = new(Guid.NewGuid(), "platform:bob@onepunch.test");

    private sealed class Fixture
    {
        public SubscriptionTestHost Host { get; } = new() { Options = { DataPurgeEnabled = true } };
        public Mock<IPurgedDataApi> Api { get; } = new();
        public Guid Owner { get; private set; }
        public Guid Company { get; private set; }

        private PlatformDirectoryService Directory(IUnitOfWorkService uow) => new(uow, Host.StateMachine);

        // A fresh unit of work over the same database for each call, as each request gets one: the service commits, and a unit of work commits only once.
        public PurgedDataService Purged()
        {
            var uow = TenantTestContextFactory.CreateUnitOfWork(Host.Uow.Context);
            return new PurgedDataService(uow, Directory(uow), new BillingEventService(uow, Host.Clock), Api.Object);
        }

        public ArchiveDestroyRequestService Destroy()
        {
            var uow = TenantTestContextFactory.CreateUnitOfWork(Host.Uow.Context);
            var events = new BillingEventService(uow, Host.Clock);
            var options = Microsoft.Extensions.Options.Options.Create(Host.Options);
            var purge = new DataPurgeService(uow, events, Host.Notifier, Host.Publisher.Object, options, Host.Clock);
            return new ArchiveDestroyRequestService(uow, Directory(uow), purge, events, options, Host.Clock);
        }

        public AccountSubscription Subscription => Host.SubscriptionOf(Owner);

        /// <summary>An account whose only company was purged, with an archive.</summary>
        public async Task<Fixture> PurgedAccountAsync(string companyName = "Acme")
        {
            (Owner, Company) = await Host.TrialAccountAsync(OwnerEmail);
            Host.Uow.Context.Tenants.Single(t => t.Id == Company).TenantName = companyName;
            var subscription = Subscription;
            subscription.SubStatus = AccountSubscriptionStatus.Expired;
            subscription.PurgeState = DataPurgeState.InProgress;
            await Host.DataPurge.RecordResultAsync(Done(Company, CompanyPurgeStage.ArchiveAndPurge), CancellationToken.None);
            await Host.SaveAsync();
            return this;
        }

        public CompanyDataPurged Done(Guid tenantId, CompanyPurgeStage stage) => new()
        {
            OwnerUserId = Owner,
            SubscriptionId = Subscription.Id,
            TenantId = tenantId,
            Stage = stage,
            Succeeded = true,
            LastEntryUtc = Host.Clock.UtcNow.AddYears(-1),
            ArchiveKey = stage == CompanyPurgeStage.ArchiveAndPurge ? $"archives/{tenantId:D}/company-archive.zip" : null,
            ArchiveSha256 = stage == CompanyPurgeStage.ArchiveAndPurge ? "abc123" : null,
            RowsArchived = 42,
        };

        public List<PurgeCompanyData> Requests() => Host.Publisher.Invocations
            .Where(i => i.Arguments.FirstOrDefault() is PurgeCompanyData)
            .Select(i => (PurgeCompanyData)i.Arguments[0])
            .ToList();

        public List<BillingEvent> Events(string type) =>
            Host.Uow.Context.BillingEvents.AsNoTracking().Where(e => e.Type == type).ToList();
    }

    // ── What the purge keeps ─────────────────────────────────────────────────

    [Fact]
    public async Task APurge_KeepsAFindableRecordOfEachCompany_OnceEvenIfReportedTwice()
    {
        var f = await new Fixture().PurgedAccountAsync("Acme Corp");
        await f.Host.DataPurge.RecordResultAsync(f.Done(f.Company, CompanyPurgeStage.ArchiveAndPurge), CancellationToken.None);
        await f.Host.SaveAsync();

        var record = f.Host.Uow.Context.PurgedCompanyArchives.AsNoTracking().Should().ContainSingle().Subject;
        record.OwnerUserId.Should().Be(f.Owner);
        record.TenantId.Should().Be(f.Company);
        record.CompanyName.Should().Be("Acme Corp");
        record.ArchiveKey.Should().Be($"archives/{f.Company:D}/company-archive.zip");
        record.ArchiveSha256.Should().Be("abc123");
        record.RowsArchived.Should().Be(42);
        record.DestroyedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task WhenTheArchiveIsDestroyed_TheRecordIsStamped()
    {
        var f = await new Fixture().PurgedAccountAsync();
        f.Host.Clock.Advance(TimeSpan.FromDays(3));

        await f.Host.DataPurge.RecordResultAsync(f.Done(f.Company, CompanyPurgeStage.DestroyArchive), CancellationToken.None);
        await f.Host.SaveAsync();

        f.Host.Uow.Context.PurgedCompanyArchives.AsNoTracking().Single().DestroyedAtUtc.Should().Be(f.Host.Clock.UtcNow);
    }

    // ── Finding purged accounts ──────────────────────────────────────────────

    [Fact]
    public async Task APurgedAccount_IsFoundByOwnerEmail_OrCompanyName_WithoutAnyId()
    {
        var f = await new Fixture().PurgedAccountAsync("Zeta Trading");

        var byEmail = await f.Purged().ListAsync(new PurgedDataQuery(Search: "OWNER@acme"), CancellationToken.None);
        var byName = await f.Purged().ListAsync(new PurgedDataQuery(Search: "zeta"), CancellationToken.None);
        var nobody = await f.Purged().ListAsync(new PurgedDataQuery(Search: "nobody"), CancellationToken.None);

        byEmail.Items.Should().ContainSingle().Which.Companies.Should().ContainSingle(c => c.CompanyName == "Zeta Trading" && c.HasArchive);
        byEmail.Items[0].OwnerEmail.Should().Be(OwnerEmail);
        byName.Items.Should().ContainSingle().Which.OwnerUserId.Should().Be(f.Owner);
        nobody.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task TheList_OnlyHoldsPurgedAccounts_AndFiltersByStateAndHold()
    {
        var f = await new Fixture().PurgedAccountAsync();
        await f.Host.TrialAccountAsync("live@other.ph");

        (await f.Purged().ListAsync(new PurgedDataQuery(), CancellationToken.None)).Items
            .Should().ContainSingle("an account that was never purged isn't listed");
        (await f.Purged().ListAsync(new PurgedDataQuery(Filter: "destroyed"), CancellationToken.None)).Items.Should().BeEmpty();
        (await f.Purged().ListAsync(new PurgedDataQuery(Filter: "hold"), CancellationToken.None)).Items.Should().BeEmpty();

        f.Subscription.LegalHold = true;
        await f.Host.SaveAsync();
        (await f.Purged().ListAsync(new PurgedDataQuery(Filter: "hold"), CancellationToken.None)).Items.Should().ContainSingle();
    }

    // ── Reading what was kept: a reason, and a record ────────────────────────

    [Fact]
    public async Task TheAuditTrail_NeedsAReason_AndAsksHrmsApiForNothingWithout()
    {
        var f = await new Fixture().PurgedAccountAsync();

        var act = () => f.Purged().GetAuditAsync(f.Owner, f.Company, new PurgedAuditQuery(), " ", Alice, CancellationToken.None);

        await act.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*reason*");
        f.Api.Verify(a => a.GetAuditAsync(It.IsAny<Guid>(), It.IsAny<PurgedAuditQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TheAuditTrail_IsReadForTheCompany_AndRecordedWithWhoAndWhy()
    {
        var f = await new Fixture().PurgedAccountAsync("Acme");
        f.Api.Setup(a => a.GetAuditAsync(f.Company, It.IsAny<PurgedAuditQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResponseModel<PurgedAuditPage> { Data = new PurgedAuditPage([], 0, 1, 25) });

        var page = await f.Purged().GetAuditAsync(f.Owner, f.Company, new PurgedAuditQuery(), "Customer asked in writing", Alice, CancellationToken.None);

        page.Total.Should().Be(0);
        var recorded = f.Events(BillingEventTypes.PurgedAuditViewed).Should().ContainSingle().Subject;
        recorded.Actor.Should().Be(Alice.Actor);
        recorded.OwnerUserId.Should().Be(f.Owner);
        recorded.PayloadJson.Should().Contain("Customer asked in writing").And.Contain("Acme");
    }

    [Fact]
    public async Task PagingThroughTheTrail_IsNotRecordedAgain()
    {
        var f = await new Fixture().PurgedAccountAsync();
        f.Api.Setup(a => a.GetAuditAsync(f.Company, It.IsAny<PurgedAuditQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResponseModel<PurgedAuditPage> { Data = new PurgedAuditPage([], 0, 2, 25) });

        await f.Purged().GetAuditAsync(f.Owner, f.Company, new PurgedAuditQuery(Page: 2), "Audit", Alice, CancellationToken.None);

        f.Events(BillingEventTypes.PurgedAuditViewed).Should().BeEmpty();
    }

    [Fact]
    public async Task ACompanyThatWasNotPurged_HasNothingToRead()
    {
        var f = await new Fixture().PurgedAccountAsync();

        var act = () => f.Purged().GetAuditAsync(f.Owner, Guid.NewGuid(), new PurgedAuditQuery(), "Audit", Alice, CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task TheArchiveLink_NeedsAReason_IsRecordedBeforeItIsHandedOut_AndUsesTheStoredKey()
    {
        var f = await new Fixture().PurgedAccountAsync();
        f.Api.Setup(a => a.GetArchiveLinkAsync(f.Company, $"archives/{f.Company:D}/company-archive.zip", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResponseModel<PurgedArchiveLink> { Data = new PurgedArchiveLink("https://files.test/x", "company-archive.zip", 10) });

        var refused = () => f.Purged().GetArchiveLinkAsync(f.Owner, f.Company, null, Alice, CancellationToken.None);
        await refused.Should().ThrowAsync<SubscriptionRuleException>();
        f.Events(BillingEventTypes.PurgedArchiveDownloaded).Should().BeEmpty();

        var link = await f.Purged().GetArchiveLinkAsync(f.Owner, f.Company, "Court order 2026-114", Alice, CancellationToken.None);

        link.Url.Should().Be("https://files.test/x");
        f.Events(BillingEventTypes.PurgedArchiveDownloaded).Should().ContainSingle()
            .Which.PayloadJson.Should().Contain("Court order 2026-114").And.Contain("abc123");
    }

    [Fact]
    public async Task ADestroyedArchive_CannotBeDownloaded()
    {
        var f = await new Fixture().PurgedAccountAsync();
        await f.Host.DataPurge.RecordResultAsync(f.Done(f.Company, CompanyPurgeStage.DestroyArchive), CancellationToken.None);
        await f.Host.SaveAsync();

        var act = () => f.Purged().GetArchiveLinkAsync(f.Owner, f.Company, "Audit", Alice, CancellationToken.None);

        await act.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*destroyed*");
    }

    // ── Destroying archives early ────────────────────────────────────────────

    [Fact]
    public async Task DestroyingEarly_NeedsTheOwnerEmailTyped_AndAReason()
    {
        var f = await new Fixture().PurgedAccountAsync();

        var wrongEmail = () => f.Destroy().RequestAsync(f.Owner, "Customer asked", "someone@else.ph", Alice);
        var noReason = () => f.Destroy().RequestAsync(f.Owner, "", OwnerEmail, Alice);

        await wrongEmail.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*doesn't match*");
        await noReason.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*reason*");
    }

    [Fact]
    public async Task DestroyingEarly_IsRefusedOnLegalHold_AndOnAnAccountThatWasNeverPurged()
    {
        var f = await new Fixture().PurgedAccountAsync();
        f.Subscription.LegalHold = true;
        await f.Host.SaveAsync();
        var held = () => f.Destroy().RequestAsync(f.Owner, "Customer asked", OwnerEmail, Alice);
        await held.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*legal hold*");

        var (live, _) = await f.Host.TrialAccountAsync("live@other.ph");
        var neverPurged = () => f.Destroy().RequestAsync(live, "Customer asked", "live@other.ph", Alice);
        await neverPurged.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*purged*");
    }

    [Fact]
    public async Task Request_DestroysNothing_UntilADifferentOperatorApproves()
    {
        var f = await new Fixture().PurgedAccountAsync();
        var retainUntil = f.Subscription.ArchiveRetainUntilUtc;

        var request = await RequestAsync(f);

        request.Status.Should().Be(AccountDeletionRequestStatus.Pending);
        f.Requests().Where(r => r.Stage == CompanyPurgeStage.DestroyArchive).Should().BeEmpty();
        f.Subscription.ArchiveRetainUntilUtc.Should().Be(retainUntil);

        var ownApproval = () => f.Destroy().ApproveAsync(request.Id, OwnerEmail, Alice);
        await ownApproval.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*second administrator*");
    }

    [Fact]
    public async Task Approval_MakesTheArchivesDueNow_AndAsksEveryCompanyToDestroyItsArchive()
    {
        var f = await new Fixture().PurgedAccountAsync();
        var second = f.Host.AddCompany(f.Owner, "Beta");
        var request = await RequestAsync(f);

        await f.Destroy().ApproveAsync(request.Id, " OWNER@acme.ph ", Bob);
        await f.Host.SaveAsync();

        f.Subscription.ArchiveRetainUntilUtc.Should().Be(f.Host.Clock.UtcNow);
        f.Requests().Where(r => r.Stage == CompanyPurgeStage.DestroyArchive).Select(r => r.TenantId)
            .Should().BeEquivalentTo([f.Company, second]);
        f.Subscription.PurgeState.Should().Be(DataPurgeState.Purged, "the account only becomes 'archive destroyed' once hrms-api has reported");
        f.Events(BillingEventTypes.ArchiveDestroyApproved).Should().ContainSingle().Which.Actor.Should().Be(Bob.Actor);
        (await f.Destroy().GetLatestAsync(f.Owner))!.Status.Should().Be(AccountDeletionRequestStatus.Approved);
    }

    [Fact]
    public async Task Approval_IsRefused_IfALegalHoldWasSetMeanwhile_OrTheEmailIsWrong()
    {
        var f = await new Fixture().PurgedAccountAsync();
        var request = await RequestAsync(f);

        var wrong = () => f.Destroy().ApproveAsync(request.Id, "someone@else.ph", Bob);
        await wrong.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*doesn't match*");

        f.Subscription.LegalHold = true;
        await f.Host.SaveAsync();
        var held = () => f.Destroy().ApproveAsync(request.Id, OwnerEmail, Bob);
        await held.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*legal hold*");
        f.Requests().Where(r => r.Stage == CompanyPurgeStage.DestroyArchive).Should().BeEmpty();
    }

    [Fact]
    public async Task Approval_IsRefused_WhileDataRemovalIsSwitchedOff()
    {
        var f = await new Fixture().PurgedAccountAsync();
        var request = await RequestAsync(f);
        f.Host.Options.DataPurgeEnabled = false;

        var act = () => f.Destroy().ApproveAsync(request.Id, OwnerEmail, Bob);

        await act.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*switched off*");
    }

    [Fact]
    public async Task OnlyTheRequesterCanWithdraw_AndTheOtherOperatorCanReject()
    {
        var f = await new Fixture().PurgedAccountAsync();
        var request = await RequestAsync(f);

        var notYours = () => f.Destroy().WithdrawAsync(f.Owner, request.Id, Bob);
        await notYours.Should().ThrowAsync<SubscriptionRuleException>();

        var service = f.Destroy();
        await service.RejectAsync(request.Id, "Not now", Bob);
        await service.CommitChangesAsync();

        (await f.Destroy().GetLatestAsync(f.Owner))!.Status.Should().Be(AccountDeletionRequestStatus.Rejected);
        f.Events(BillingEventTypes.ArchiveDestroyRejected).Should().ContainSingle();
    }

    [Fact]
    public async Task APendingRequest_ExpiresAfterADay()
    {
        var f = await new Fixture().PurgedAccountAsync();
        var request = await RequestAsync(f);
        f.Host.Clock.Advance(ArchiveDestroyRequestService.Lifetime + TimeSpan.FromMinutes(1));

        var act = () => f.Destroy().ApproveAsync(request.Id, OwnerEmail, Bob);

        await act.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*expired*");
        (await f.Destroy().ListPendingAsync()).Should().BeEmpty();
    }

    private static async Task<ArchiveDestroyRequestView> RequestAsync(Fixture f)
    {
        var service = f.Destroy();
        var request = await service.RequestAsync(f.Owner, "Customer asked to destroy", OwnerEmail, Alice);
        await service.CommitChangesAsync();
        return request;
    }

    // ── Erasing the account takes these records too ──────────────────────────

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
    public async Task ErasingTheAccount_TakesItsPurgedRecordsAndDestroyRequests_AndNoOthers()
    {
        var f = await new Fixture().PurgedAccountAsync();
        await RequestAsync(f);
        f.Host.Uow.Context.PurgedCompanyArchives.Add(new PurgedCompanyArchive
        {
            Id = Guid.NewGuid(), SubscriptionId = Guid.NewGuid(), OwnerUserId = Guid.NewGuid(), TenantId = Guid.NewGuid(), CompanyName = "Other",
        });
        await f.Host.SaveAsync();
        var rows = new RecordingRowEraser();

        await new TenantStoreAccountEraser(f.Host.Uow, rows).EraseAsync(
            new AccountErasureScope(f.Owner, f.Subscription.Id, [f.Company], []), CancellationToken.None);

        rows.Erased.OfType<PurgedCompanyArchive>().Should().ContainSingle().Which.OwnerUserId.Should().Be(f.Owner);
        rows.Erased.OfType<ArchiveDestroyRequest>().Should().ContainSingle().Which.OwnerUserId.Should().Be(f.Owner);
    }

    [Fact]
    public async Task ErasingOneCompany_TakesItsPurgedRecord()
    {
        var f = await new Fixture().PurgedAccountAsync();
        var rows = new RecordingRowEraser();

        await new TenantStoreAccountEraser(f.Host.Uow, rows).EraseCompanyAsync(
            new CompanyErasureScope(f.Owner, f.Subscription.Id, f.Company, Guid.NewGuid(), []), CancellationToken.None);

        rows.Erased.OfType<PurgedCompanyArchive>().Should().ContainSingle().Which.TenantId.Should().Be(f.Company);
    }
}
