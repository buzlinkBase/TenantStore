using FluentAssertions;
using MassTransit;
using Moq;
using Onepunch.Auth.Api.Tests.TestSupport;
using Onepunch.Auth.Core.Platform;
using Onepunch.Auth.Core.Services;
using Onepunch.Auth.Domain.DTOs;
using Onepunch.Auth.Domain.Entities;
using Onepunch.Auth.Infrastructure;
using Onepunch.Common.Lib.DTO;
using Onepunch.Common.Lib.Exceptions;
using Onepunch.Common.Lib.Platform;
using Onepunch.Common.Lib.Subscriptions;
using Microsoft.Extensions.Logging.Abstractions;
using OnePunch.Auth.Domain.Entities;
using Xunit;

namespace Onepunch.Auth.Api.Tests.Platform;

public class PlatformCompanyServiceTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private sealed record Rig(
        PlatformCompanyService Sut,
        AuthContext Context,
        Mock<IPublishEndpoint> Publisher,
        Mock<MembershipGrpcClient> Plan,
        User Owner);

    private static Rig CreateRig(bool ownerExists = true)
    {
        var context = AuthTestContextFactory.CreateContext();
        var uow = AuthTestContextFactory.CreateUnitOfWork(context);
        var owner = new User { Id = Guid.NewGuid(), Email = "owner@acme.test", FullName = "Olivia Owner" };

        var users = IdentityMocks.MockUserManager();
        users.Setup(u => u.FindByIdAsync(owner.Id.ToString())).ReturnsAsync(ownerExists ? owner : null);

        var plan = new Mock<MembershipGrpcClient>(MockBehavior.Loose, new object[] { null! });
        var publisher = new Mock<IPublishEndpoint>();

        var sut = new PlatformCompanyService(
            users.Object,
            new TenantRequestService(uow, null!),
            plan.Object,
            publisher.Object,
            new FakeTenantProvider(),
            NullLogger<PlatformCompanyService>.Instance);
        return new Rig(sut, context, publisher, plan, owner);
    }

    [Fact]
    public async Task Create_RequestsTheCompanyForTheOwner_AndTracksIt()
    {
        var rig = CreateRig();

        var result = await rig.Sut.CreateForOwnerAsync(
            rig.Owner.Id, new CreateWorkspaceRequest { TenantName = "  Acme Branch  ", Tin = "123" }, "ops@platform.test", Ct);

        result.TenantName.Should().Be("Acme Branch");
        result.OwnerUserId.Should().Be(rig.Owner.Id);
        result.Status.Should().Be("Provisioning");

        rig.Publisher.Verify(p => p.Publish(
            It.Is<TenantCreationRequested>(m =>
                m.TenantId == result.TenantId
                && m.UserId == rig.Owner.Id
                && m.Email == "owner@acme.test"
                && m.TenantName == "Acme Branch"
                && m.Profile != null && m.Profile.Tin == "123"),
            It.IsAny<CancellationToken>()), Times.Once);
        rig.Context.TenantCreationRequests.Should().ContainSingle(r =>
            r.TenantId == result.TenantId && r.UserId == rig.Owner.Id && r.Status == TenantCreationStatus.Provisioning);
    }

    [Fact]
    public async Task Create_ChecksTheOwnersCompanyLimit()
    {
        var rig = CreateRig();

        await rig.Sut.CreateForOwnerAsync(rig.Owner.Id, new CreateWorkspaceRequest { TenantName = "Acme" }, "ops", Ct);

        rig.Plan.Verify(p => p.EnsurePlanAllowsAsync(EntitlementKeys.CompaniesMax, rig.Owner.Id, null, 1, It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task Create_RefusesAndStartsNothing_WhenThePlanLimitIsReached()
    {
        var rig = CreateRig();
        rig.Plan
            .Setup(p => p.EnsurePlanAllowsAsync(EntitlementKeys.CompaniesMax, rig.Owner.Id, null, 1, It.IsAny<int>()))
            .ThrowsAsync(new GuardException("Your plan allows 1 company. Upgrade to add another."));

        var act = () => rig.Sut.CreateForOwnerAsync(rig.Owner.Id, new CreateWorkspaceRequest { TenantName = "Acme" }, "ops", Ct);

        await act.Should().ThrowAsync<GuardException>();
        rig.Publisher.Verify(p => p.Publish(It.IsAny<TenantCreationRequested>(), It.IsAny<CancellationToken>()), Times.Never);
        rig.Context.TenantCreationRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_RefusesABlankName()
    {
        var rig = CreateRig();

        var act = () => rig.Sut.CreateForOwnerAsync(rig.Owner.Id, new CreateWorkspaceRequest { TenantName = "   " }, "ops", Ct);

        await act.Should().ThrowAsync<GuardException>().WithMessage("*name*");
    }

    [Fact]
    public async Task Create_RefusesAnAccountThatDoesNotExist()
    {
        var rig = CreateRig(ownerExists: false);

        var act = () => rig.Sut.CreateForOwnerAsync(rig.Owner.Id, new CreateWorkspaceRequest { TenantName = "Acme" }, "ops", Ct);

        await act.Should().ThrowAsync<GuardException>().WithMessage("*doesn't exist*");
    }

    [Fact]
    public async Task Create_RefusesWhileAnotherCompanyIsStillBeingSetUp()
    {
        var rig = CreateRig();
        rig.Context.TenantCreationRequests.Add(new TenantCreationRequestStatus
        {
            TenantId = Guid.NewGuid(),
            UserId = rig.Owner.Id,
            TenantName = "Half done",
            Status = TenantCreationStatus.Provisioning,
            RequestExpiry = DateTime.UtcNow.AddDays(1),
        });
        await rig.Context.SaveChangesAsync();

        var act = () => rig.Sut.CreateForOwnerAsync(rig.Owner.Id, new CreateWorkspaceRequest { TenantName = "Acme" }, "ops", Ct);

        await act.Should().ThrowAsync<GuardException>().WithMessage("*still being set up*");
        rig.Publisher.Verify(p => p.Publish(It.IsAny<TenantCreationRequested>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void OnlySuperAdminAndSupport_MayCreateCompanies()
    {
        PlatformRolePermissions.For(PlatformRoles.SuperAdmin).Should().Contain(PlatformPermissions.CompaniesCreate);
        PlatformRolePermissions.For(PlatformRoles.Support).Should().Contain(PlatformPermissions.CompaniesCreate);
        PlatformRolePermissions.For(PlatformRoles.Billing).Should().NotContain(PlatformPermissions.CompaniesCreate);
    }
}
