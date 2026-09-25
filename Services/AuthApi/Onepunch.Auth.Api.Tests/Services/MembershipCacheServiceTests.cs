using FluentAssertions;
using Moq;
using Onepunch.Auth.Api.Tests.TestSupport;
using Onepunch.Auth.Core.Services;
using Onepunch.Auth.Domain.DTOs;
using Onepunch.Auth.Domain.Entities;
using Onepunch.Auth.Infrastructure;
using Onepunch.Common.Lib.Cache;
using Xunit;

namespace Onepunch.Auth.Api.Tests.Services;

/// <summary>
/// MembershipCacheService.ApplyRequestStateAsync (exercised through GetMembershipsAsync's cache-
/// hit path, so no gRPC channel is needed). Guards the invited-member regression: HR-DB
/// readiness is a property of the TENANT, so a member who didn't create the tenant must still
/// see HrDbReady=true -- otherwise the app shell stays on "Setting up your company" for them
/// forever -- while State keeps only ever reflecting the user's OWN creation request.
/// </summary>
public class MembershipCacheServiceTests
{
    private static (MembershipCacheService Sut, AuthContext Context) CreateSut(List<UsersTenant> cached)
    {
        var context = AuthTestContextFactory.CreateContext();
        var uow = AuthTestContextFactory.CreateUnitOfWork(context);
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetAsync<List<UsersTenant>>(It.IsAny<string>())).ReturnsAsync(cached);
        return (new MembershipCacheService(cache.Object, uow, null!), context);
    }

    [Fact]
    public async Task GetMembershipsAsync_AppliesHrDbReady_ForInvitedMember_WithoutOverwritingState()
    {
        var tenantId = Guid.NewGuid();
        var creatorId = Guid.NewGuid();
        var inviteeId = Guid.NewGuid();
        var (sut, context) = CreateSut(
        [
            new UsersTenant { TenantId = tenantId, Name = "Acme", Roles = ["Employee"], State = "Active" },
        ]);
        context.TenantCreationRequests.Add(new TenantCreationRequestStatus
        {
            TenantId = tenantId,
            UserId = creatorId,
            TenantName = "Acme",
            Status = TenantCreationStatus.Created,
            HrDbStatus = "Created",
            HrDbReady = true,
            RequestExpiry = DateTime.UtcNow.AddDays(1),
        });
        await context.SaveChangesAsync();

        var result = await sut.GetMembershipsAsync(inviteeId);

        var tenant = result.Should().ContainSingle().Subject;
        tenant.HrDbReady.Should().BeTrue();
        tenant.HrDbStatus.Should().Be("Created");
        tenant.State.Should().Be("Active");
    }

    [Fact]
    public async Task GetMembershipsAsync_AppliesOwnRequestState_ForTenantCreator()
    {
        var tenantId = Guid.NewGuid();
        var creatorId = Guid.NewGuid();
        var (sut, context) = CreateSut(
        [
            new UsersTenant { TenantId = tenantId, Name = "", Roles = ["Owner"], State = "Active" },
        ]);
        context.TenantCreationRequests.Add(new TenantCreationRequestStatus
        {
            TenantId = tenantId,
            UserId = creatorId,
            TenantName = "Acme",
            Status = TenantCreationStatus.Provisioning,
            HrDbReady = false,
            RequestExpiry = DateTime.UtcNow.AddDays(1),
        });
        await context.SaveChangesAsync();

        var result = await sut.GetMembershipsAsync(creatorId);

        var tenant = result.Should().ContainSingle().Subject;
        tenant.State.Should().Be(TenantCreationStatus.Provisioning.ToString());
        tenant.HrDbReady.Should().BeFalse();
        tenant.Name.Should().Be("Acme");
    }
}
