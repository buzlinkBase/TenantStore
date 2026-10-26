using FluentAssertions;
using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using Onepunch.Auth.Api.Tests.TestSupport;
using Onepunch.Auth.Core;
using Onepunch.Auth.Core.Services;
using Onepunch.Auth.Domain.Entities;
using Onepunch.Auth.Infrastructure;
using Onepunch.Common.Lib.Exceptions;
using OnePunch.Auth.Domain.Entities;
using System.Security.Claims;
using Xunit;

namespace Onepunch.Auth.Api.Tests.Services;

/// <summary>
/// An admin who revokes or removes a pending invite must stop the invitee from joining: Tenant
/// Service refuses the activation, and the accept paths turn that into INVITATION_REVOKED
/// without marking the invitation used, publishing anything or leaving an account behind.
/// </summary>
public class InvitationRevocationTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static (InvitationService Sut, AuthContext Context, Mock<UserManager<User>> UserManager, Mock<IPublishEndpoint> Publisher, Mock<MembershipGrpcClient> Grpc)
        CreateSut(MembershipResolveResult activation)
    {
        var context = AuthTestContextFactory.CreateContext();
        var uow = AuthTestContextFactory.CreateUnitOfWork(context);
        var userManager = IdentityMocks.MockUserManager();
        var publisher = new Mock<IPublishEndpoint>();
        var grpc = new Mock<MembershipGrpcClient>(MockBehavior.Loose, new object[] { null! });
        grpc.Setup(g => g.ActivateMembershipAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IEnumerable<string>>(), It.IsAny<ActivationMode>(), It.IsAny<int>()))
            .ReturnsAsync(activation);
        var httpContextAccessor = new Mock<IHttpContextAccessor>();

        var sut = new InvitationService(
            uow,
            new EmailTokenService(uow),
            new TenantRequestService(uow, null!),
            Options.Create(new Domains { FrontEnd = "https://app.test" }),
            httpContextAccessor.Object,
            httpContextAccessor.Object,
            userManager.Object,
            publisher.Object,
            null!, // JwtService -- never reached: every case here is refused before minting
            null!, // MembershipCacheService -- same
            grpc.Object);
        return (sut, context, userManager, publisher, grpc);
    }

    private static async Task<Invitation> AddInvitationAsync(AuthContext context, string token, InvitationStatus status)
    {
        var invitation = new Invitation
        {
            Token = token,
            Email = "invited@test.com",
            TenantId = TenantId,
            TenantName = "Acme",
            Status = status,
            Expiry = DateTime.UtcNow.AddHours(1),
        };
        context.Invitations.Add(invitation);
        await context.SaveChangesAsync();
        return invitation;
    }

    private static ClaimsPrincipal SomeClaimsPrincipal() =>
        new(new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())], "test"));

    private static void SignedInAs(Mock<UserManager<User>> userManager) =>
        userManager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(new User { Id = Guid.NewGuid(), Email = "invited@test.com" });

    private static void VerifyNothingPublished(Mock<IPublishEndpoint> publisher) =>
        publisher.Verify(p => p.Publish(It.IsAny<object>(), It.IsAny<Type>(), It.IsAny<CancellationToken>()), Times.Never);

    // ── Accept (existing account) ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Accept_WhenTenantRefusesTheJoin_ThrowsRevoked_AndLeavesTheInvitationPending()
    {
        var (sut, context, userManager, publisher, grpc) = CreateSut(new MembershipResolveResult { Success = true, Denied = true });
        await AddInvitationAsync(context, "tok-revoked", InvitationStatus.Pending);
        SignedInAs(userManager);

        var act = () => sut.Accept("tok-revoked", SomeClaimsPrincipal(), default);

        (await act.Should().ThrowAsync<GuardException>()).Which.Code.Should().Be("INVITATION_REVOKED");
        grpc.Verify(g => g.ActivateMembershipAsync(
            It.IsAny<Guid>(), TenantId, It.IsAny<string?>(), "invited@test.com", It.IsAny<string?>(),
            It.IsAny<IEnumerable<string>>(), ActivationMode.AcceptInvite, It.IsAny<int>()), Times.Once);
        (await context.Invitations.AsNoTracking().SingleAsync()).Status.Should().Be(InvitationStatus.Pending);
        VerifyNothingPublished(publisher);
    }

    [Fact]
    public async Task Accept_WhenTenantCannotBeReached_FailsClosed_InsteadOfMintingFromTheInvitationRoles()
    {
        var (sut, context, userManager, publisher, _) = CreateSut(new MembershipResolveResult { Success = false });
        await AddInvitationAsync(context, "tok-down", InvitationStatus.Pending);
        SignedInAs(userManager);

        var act = () => sut.Accept("tok-down", SomeClaimsPrincipal(), default);

        (await act.Should().ThrowAsync<GuardException>()).Which.Code.Should().Be("INVITATION_UNAVAILABLE");
        (await context.Invitations.AsNoTracking().SingleAsync()).Status.Should().Be(InvitationStatus.Pending);
        VerifyNothingPublished(publisher);
    }

    [Fact]
    public async Task Accept_ReplayOfAnAcceptedInvitation_IsRefused_WhenTheMemberWasRevokedSince()
    {
        var (sut, context, userManager, _, grpc) = CreateSut(new MembershipResolveResult { Success = true, Denied = true });
        await AddInvitationAsync(context, "tok-replay", InvitationStatus.Accepted);
        SignedInAs(userManager);

        var act = () => sut.Accept("tok-replay", SomeClaimsPrincipal(), default);

        (await act.Should().ThrowAsync<GuardException>()).Which.Code.Should().Be("INVITATION_REVOKED");
        grpc.Verify(g => g.ActivateMembershipAsync(
            It.IsAny<Guid>(), TenantId, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<IEnumerable<string>>(), ActivationMode.Replay, It.IsAny<int>()), Times.Once);
    }

    // ── AcceptByTokenAsync (new account) ──────────────────────────────────────────────────

    [Fact]
    public async Task AcceptByToken_WhenTenantRefusesTheJoin_ThrowsRevoked_AndDropsTheAccountItJustCreated()
    {
        var (sut, context, userManager, publisher, _) = CreateSut(new MembershipResolveResult { Success = true, Denied = true });
        await AddInvitationAsync(context, "tok-new", InvitationStatus.Pending);
        userManager.Setup(m => m.FindByEmailAsync("invited@test.com")).ReturnsAsync((User?)null);
        userManager.Setup(m => m.CreateAsync(It.IsAny<User>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
        userManager.Setup(m => m.DeleteAsync(It.IsAny<User>())).ReturnsAsync(IdentityResult.Success);

        var act = () => sut.AcceptByTokenAsync("tok-new", "New User", "P@ssw0rd1", default);

        (await act.Should().ThrowAsync<GuardException>()).Which.Code.Should().Be("INVITATION_REVOKED");
        userManager.Verify(m => m.DeleteAsync(It.IsAny<User>()), Times.Once);
        (await context.Invitations.AsNoTracking().SingleAsync()).Status.Should().Be(InvitationStatus.Pending);
        VerifyNothingPublished(publisher);
    }

    // ── ExpirePendingAsync (InviteRevoked from Tenant Service) ────────────────────────────

    [Fact]
    public async Task ExpirePending_ExpiresOnlyThePendingInvitationsOfThatTenantAndEmail()
    {
        var (sut, context, _, _, _) = CreateSut(new MembershipResolveResult());
        var target = await AddInvitationAsync(context, "tok-target", InvitationStatus.Pending);
        var accepted = await AddInvitationAsync(context, "tok-accepted", InvitationStatus.Accepted);
        var otherTenant = await AddInvitationAsync(context, "tok-other-tenant", InvitationStatus.Pending);
        otherTenant.TenantId = Guid.NewGuid();
        var otherEmail = await AddInvitationAsync(context, "tok-other-email", InvitationStatus.Pending);
        otherEmail.Email = "someone-else@test.com";
        await context.SaveChangesAsync();

        await sut.ExpirePendingAsync(TenantId, "invited@test.com");

        var rows = await context.Invitations.AsNoTracking().ToDictionaryAsync(x => x.Token);
        rows["tok-target"].Expiry.Should().BeOnOrBefore(DateTime.UtcNow);
        rows["tok-accepted"].Expiry.Should().BeAfter(DateTime.UtcNow);
        rows["tok-other-tenant"].Expiry.Should().BeAfter(DateTime.UtcNow);
        rows["tok-other-email"].Expiry.Should().BeAfter(DateTime.UtcNow);
        (await sut.IsValidAsync(target.Token)).Should().BeFalse();
        (await sut.GetPreviewAsync(target.Token))!.Valid.Should().BeFalse();
    }
}
