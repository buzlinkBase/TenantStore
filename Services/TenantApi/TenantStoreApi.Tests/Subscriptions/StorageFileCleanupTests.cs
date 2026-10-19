using FluentAssertions;
using Moq;
using Onepunch.Common.Lib;
using TenantStoreApi.Core.Services;
using TenantStoreApi.Core.Services.Platform;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Subscriptions;

/// <summary>
/// The console's per-company file cleanup: only a company of the account itself is ever passed on
/// to hrms-api, an operator must give a reason, and the delete lands in the account's billing history.
/// </summary>
public class StorageFileCleanupTests
{
    private static readonly PlatformOperator Operator = new(Guid.NewGuid(), "platform:jo@onepunch.ph");

    private static (SubscriptionTestHost Host, Mock<IStorageManagerApi> Api, StorageFileCleanupService Service) Build()
    {
        var host = new SubscriptionTestHost();
        var api = new Mock<IStorageManagerApi>();
        return (host, api, new StorageFileCleanupService(host.Usage, host.Events, host.Uow, api.Object));
    }

    [Fact]
    public async Task ACompanyOfAnotherAccount_IsNeverPassedOn()
    {
        var (host, api, service) = Build();
        var (owner, _) = await host.TrialAccountAsync();
        var (_, theirCompany) = await host.TrialAccountAsync("other@acme.ph");

        var act = () => service.GetBreakdownAsync(owner, theirCompany, default);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        api.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Delete_NeedsAReason()
    {
        var (host, api, service) = Build();
        var (owner, company) = await host.TrialAccountAsync();

        var act = () => service.DeleteAsync(owner, company, new StorageFileDeleteCommand("unlinked", 2023, null, false, " "), Operator, default);

        await act.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*reason*");
        api.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Delete_PassesTheOperatorOn_AndRecordsItInTheBillingHistory()
    {
        var (host, api, service) = Build();
        var (owner, company) = await host.TrialAccountAsync();
        object? sent = null;
        api.Setup(a => a.DeleteAsync(company, It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, object, CancellationToken>((_, body, _) => sent = body)
            .ReturnsAsync(new ResponseModel<StorageDeleteResultView> { Data = new StorageDeleteResultView(3, 3000, 0) });

        var result = await service.DeleteAsync(
            owner, company, new StorageFileDeleteCommand("attachments-leave", 2023, null, true, "Owner asked"), Operator, default);

        result.DeletedCount.Should().Be(3);
        sent.Should().BeEquivalentTo(new { OperatorId = Operator.Id.ToString(), Reason = "Owner asked", ConfirmLocked = true });
        var recorded = host.Uow.Context.BillingEvents.Single(e => e.Type == BillingEventTypes.StorageFilesDeleted);
        recorded.OwnerUserId.Should().Be(owner);
        recorded.Actor.Should().Be(Operator.Actor);
        recorded.PayloadJson.Should().Contain("Owner asked").And.Contain("3000");
    }

    [Fact]
    public void Catalog_HasTheStoragePermissions_AndAdminHoldsThem()
    {
        var catalog = new HrmsPermissionCatalog();

        catalog.Features.Should().Contain(f => f.Feature == "Storage" && f.Actions.SequenceEqual(new[] { "View", "Manage" }));
        catalog.RoleGrants[TenantRoles.Admin].Should().Contain(["Storage:View", "Storage:Manage"]);
    }
}
