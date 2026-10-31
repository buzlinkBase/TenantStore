using FluentAssertions;
using MassTransit;
using Moq;
using Onepunch.Common.Lib.DTO;
using TenantStoreApi.Core.Messaging;
using TenantStoreApi.Core.Services.Platform;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Platform;

/// <summary>
/// Answers to a purge reach the right place: an erasure counts each service's answer (named by its
/// header), the yearly purge only ever hrms-api's.
/// </summary>
public class CompanyDataPurgedWorkerTests
{
    private sealed class Fixture
    {
        public SubscriptionTestHost Host { get; } = new();

        private TenantStoreAccountEraser Eraser => new(Host.Uow, new RelationalRowEraserStub());

        public CompanyDataPurgedWorker Worker => new(
            Host.DataPurge,
            new PlatformAccountDeletionService(Host.Uow, Host.Events, Host.Notifier, Host.Publisher.Object, Eraser, Host.Confirmations, Host.Clock),
            new PlatformCompanyDeletionService(Host.Uow, Host.Events, Host.Publisher.Object, Eraser, Host.Confirmations, Host.Clock),
            Host.Uow);

        public async Task ConsumeAsync(CompanyDataPurged message, string? source)
        {
            var headers = new Mock<Headers>();
            headers.Setup(h => h.Get(PurgeSources.Header, It.IsAny<string?>())).Returns(source);
            var context = new Mock<ConsumeContext<CompanyDataPurged>>();
            context.SetupGet(c => c.Message).Returns(message);
            context.SetupGet(c => c.Headers).Returns(headers.Object);
            context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
            await Worker.Consume(context.Object);
        }
    }

    /// <summary>Nothing is erased in these tests; they are about where answers go.</summary>
    private sealed class RelationalRowEraserStub : IRowEraser
    {
        public Task<int> EraseAsync<T>(IQueryable<T> rows, CancellationToken token) where T : class => Task.FromResult(0);
    }

    private static CompanyDataPurged Answer(Domain.Entities.Subs.AccountSubscription subscription, Guid tenantId, CompanyPurgeStage stage) => new()
    {
        OwnerUserId = subscription.OwnerUserId,
        SubscriptionId = subscription.Id,
        TenantId = tenantId,
        Stage = stage,
        Succeeded = true,
    };

    [Theory]
    [InlineData(null, "hrms")]
    [InlineData("adms", "adms")]
    [InlineData(" Portal ", "portal")]
    public void TheSource_IsTheHeader_OrHrmsApiWhenThereIsNone(string? header, string expected)
    {
        PurgeSources.From(header).Should().Be(expected);
    }

    [Fact]
    public async Task AnErasureAnswer_IsRecordedForTheServiceInItsHeader()
    {
        var f = new Fixture();
        f.Host.Options.ErasureParticipants = "hrms,adms,portal";
        var (owner, company) = await f.Host.TrialAccountAsync();
        var subscription = f.Host.SubscriptionOf(owner);
        subscription.PurgeState = Domain.Entities.Subs.DataPurgeState.HardDeleting;
        await f.Host.SaveAsync();

        await f.ConsumeAsync(Answer(subscription, company, CompanyPurgeStage.HardDelete), PurgeSources.Adms);

        var pending = await new PlatformAccountDeletionService(
                f.Host.Uow, f.Host.Events, f.Host.Notifier, f.Host.Publisher.Object,
                new TenantStoreAccountEraser(f.Host.Uow, new RelationalRowEraserStub()), f.Host.Confirmations, f.Host.Clock)
            .PendingConfirmationsAsync(owner, CancellationToken.None);
        pending[company].Should().Equal(PurgeSources.Hrms, PurgeSources.Portal);
    }

    [Fact]
    public async Task AYearlyPurgeAnswerFromAnotherService_IsIgnored()
    {
        var f = new Fixture();
        var (owner, company) = await f.Host.TrialAccountAsync();
        var subscription = f.Host.SubscriptionOf(owner);
        subscription.PurgeState = Domain.Entities.Subs.DataPurgeState.InProgress;
        await f.Host.SaveAsync();
        var events = f.Host.Uow.Context.BillingEvents.Count();

        await f.ConsumeAsync(Answer(subscription, company, CompanyPurgeStage.ArchiveAndPurge), PurgeSources.Adms);

        f.Host.Uow.Context.BillingEvents.Count().Should().Be(events, "only hrms-api's answer counts for the yearly purge");
    }

    [Theory]
    [InlineData("hrms", new[] { "hrms" })]
    [InlineData("adms, portal", new[] { "hrms", "adms", "portal" })]
    [InlineData("hrms,ADMS,adms", new[] { "hrms", "adms" })]
    [InlineData("", new[] { "hrms" })]
    public void TheParticipants_AlwaysIncludeHrmsApi(string configured, string[] expected)
    {
        var host = new SubscriptionTestHost();
        host.Options.ErasureParticipants = configured;

        host.Confirmations.Participants.Should().Equal(expected);
    }

    [Fact]
    public void ByDefault_TheClientPortalIsNotAParticipant()
    {
        // The portal doesn't answer erasures yet; an erasure waiting on it would never finish.
        new SubscriptionTestHost().Confirmations.Participants.Should().Equal(PurgeSources.Hrms, PurgeSources.Adms);
    }
}
