using FluentAssertions;
using MassTransit;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using Onepunch.Auth.Api.Tests.TestSupport;
using Onepunch.Auth.Core;
using Onepunch.Auth.Core.Portal;
using Onepunch.Auth.Core.Services;
using Onepunch.Auth.Domain.DTOs;
using Onepunch.Auth.Domain.Entities;
using Onepunch.Auth.Infrastructure;
using Onepunch.Common.Lib.DTO;
using Onepunch.Common.Lib.Exceptions;
using OnePunch.Auth.Core.Services;
using OnePunch.Auth.Domain.Entities;
using Xunit;

namespace Onepunch.Auth.Api.Tests.Portal;

/// <summary>
/// Client-user invitations: Tenant Service decides who may invite whom, AuthApi owns the link, the
/// account and the email. The link's token is stored only as a hash, and these invitations never
/// reach the HRIS accept / preview / my-invitations paths.
/// </summary>
public class PortalInvitationServiceTests
{
    private const string PortalUrl = "https://portal.example.com";
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();
    private static readonly Guid InviterId = Guid.NewGuid();
    private static readonly Guid FinanceRole = Guid.NewGuid();

    private sealed class Fixture
    {
        public AuthContext Context { get; } = AuthTestContextFactory.CreateContext();
        public Mock<UserManager<User>> Manager { get; } = IdentityMocks.MockUserManager();
        public Mock<IPublishEndpoint> Publisher { get; } = new();
        public Mock<MembershipGrpcClient> Grpc { get; } = new(MockBehavior.Loose, new object[] { null! });
        public List<UserInvitionNotificationPayload> Emails { get; } = [];

        private readonly string _portalUrl;

        // A new service (and so a new unit of work) per use, over the same database: a unit of work
        // commits once, and in production every request gets its own.
        public PortalInvitationService Sut => new(
            AuthTestContextFactory.CreateUnitOfWork(Context),
            Manager.Object,
            Grpc.Object,
            Publisher.Object,
            Options.Create(new PortalAuthSettings { PortalUrl = _portalUrl }),
            TimeProvider.System);

        public Fixture(string portalUrl = PortalUrl)
        {
            _portalUrl = portalUrl;
            Grpc.Setup(g => g.CheckClientInviteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<IEnumerable<Guid>>(), It.IsAny<int>()))
                .ReturnsAsync(new ClientInviteCheck(true, null, TenantId, "Agency Co", "Acme Corp"));
            Grpc.Setup(g => g.GrantClientAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<IEnumerable<Guid>>(), It.IsAny<int>()))
                .ReturnsAsync((true, (string?)null));
            Publisher.Setup(p => p.Publish(It.IsAny<UserInvitionNotificationPayload>(), It.IsAny<CancellationToken>()))
                .Callback<UserInvitionNotificationPayload, CancellationToken>((m, _) => Emails.Add(m))
                .Returns(Task.CompletedTask);
        }

        public void Refuse(string error) =>
            Grpc.Setup(g => g.CheckClientInviteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<IEnumerable<Guid>>(), It.IsAny<int>()))
                .ReturnsAsync(new ClientInviteCheck(false, error, Guid.Empty, string.Empty, string.Empty));

        public void GrantFails(string error) =>
            Grpc.Setup(g => g.GrantClientAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<IEnumerable<Guid>>(), It.IsAny<int>()))
                .ReturnsAsync((false, error));

        public Task<PortalInvitationView> InviteAsync(string email = "ana@client.com", string? name = "Ana Cruz") =>
            Sut.InviteAsync(new PortalInviteRequest(InviterId, ClientId, email, name, [FinanceRole]), CancellationToken.None);

        // The token the invitee holds: it is in the emailed link, never in the database.
        public string LatestToken() => Emails[^1].InviteLink.Split("token=")[1];
    }

    // ── Sending ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Invite_StoresAClientInvitation_AndEmailsAPortalLink_WhoseTokenIsOnlyStoredHashed()
    {
        var f = new Fixture();

        var view = await f.InviteAsync(" Ana@Client.com ");

        var stored = await f.Context.Invitations.SingleAsync();
        stored.Category.Should().Be(InvitationCategory.ClientUser);
        stored.Email.Should().Be("ana@client.com");
        stored.ClientEntityId.Should().Be(ClientId);
        stored.ClientRoleIds.Should().Equal(FinanceRole);
        stored.TenantId.Should().Be(TenantId);
        stored.TenantName.Should().Be("Agency Co");
        stored.ClientName.Should().Be("Acme Corp");
        stored.UserId.Should().Be(InviterId);
        stored.Roles.Should().BeEmpty("a client invitation carries role ids, not HRIS role names");
        stored.Status.Should().Be(InvitationStatus.Pending);
        stored.Expiry.Should().BeCloseTo(DateTime.UtcNow.Add(InvitationPolicy.LinkLifetime), TimeSpan.FromMinutes(1));

        var email = f.Emails.Should().ContainSingle().Subject;
        email.Email.Should().Be("ana@client.com");
        email.Organization.Should().Be("Acme Corp");
        email.InviteLink.Should().StartWith($"{PortalUrl}/accept-invite?token=");
        var token = f.LatestToken();
        stored.Token.Should().Be(PortalAuthService.HashToken(token)).And.NotBe(token);
        view.ClientEntityId.Should().Be(ClientId);
        view.RoleIds.Should().Equal(FinanceRole);
    }

    [Fact]
    public async Task Invite_AsksTenantServiceWithWhoIsInvitingWhom()
    {
        var f = new Fixture();

        await f.InviteAsync("ana@client.com");

        f.Grpc.Verify(g => g.CheckClientInviteAsync(InviterId, ClientId, "ana@client.com",
            It.Is<IEnumerable<Guid>>(r => r.SequenceEqual(new[] { FinanceRole })), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task Invite_WhenTenantServiceRefuses_NothingIsStoredOrSent()
    {
        var f = new Fixture();
        f.Refuse("You can't grant the 'Client Admin' role: it includes permissions you don't have.");

        var act = () => f.InviteAsync();

        (await act.Should().ThrowAsync<GuardException>()).WithMessage("*can't grant*");
        (await f.Context.Invitations.AnyAsync()).Should().BeFalse();
        f.Emails.Should().BeEmpty();
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("")]
    public async Task Invite_RejectsABadEmail_WithoutAskingTenantService(string email)
    {
        var f = new Fixture();

        var act = () => f.InviteAsync(email);

        await act.Should().ThrowAsync<GuardException>();
        f.Grpc.Verify(g => g.CheckClientInviteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<IEnumerable<Guid>>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Invite_NeedsAtLeastOneRole()
    {
        var f = new Fixture();

        var act = () => f.Sut.InviteAsync(new PortalInviteRequest(InviterId, ClientId, "ana@client.com", null, []), CancellationToken.None);

        await act.Should().ThrowAsync<GuardException>();
    }

    [Fact]
    public async Task Invite_WithoutAPortalAddress_FailsLoudlyRatherThanSendingNothing()
    {
        var f = new Fixture(portalUrl: "");

        var act = () => f.InviteAsync();

        (await act.Should().ThrowAsync<GuardException>()).WithMessage("*address*");
        (await f.Context.Invitations.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Invite_SupersedesAnEarlierInvitationForTheSameEmailAndClient_ButNotOtherClients()
    {
        var f = new Fixture();
        await f.InviteAsync("ana@client.com");
        var firstToken = f.LatestToken();
        var otherClient = Guid.NewGuid();
        await f.Sut.InviteAsync(new PortalInviteRequest(InviterId, otherClient, "ana@client.com", null, [FinanceRole]), CancellationToken.None);

        await f.InviteAsync("ana@client.com");

        (await f.Sut.PreviewAsync(firstToken, CancellationToken.None))!.Valid.Should().BeFalse("the old link stops working");
        (await f.Sut.PreviewAsync(f.LatestToken(), CancellationToken.None))!.Valid.Should().BeTrue();
        (await f.Sut.ListPendingAsync(ClientId, CancellationToken.None)).Should().ContainSingle();
        (await f.Sut.ListPendingAsync(otherClient, CancellationToken.None)).Should().ContainSingle();
    }

    // ── Listing, resending, revoking ─────────────────────────────────────────

    [Fact]
    public async Task ListPending_ShowsOnlyThatClientsOpenInvitations()
    {
        var f = new Fixture();
        await f.InviteAsync("a@client.com");
        await f.InviteAsync("b@client.com");
        await f.Sut.InviteAsync(new PortalInviteRequest(InviterId, Guid.NewGuid(), "c@other.com", null, [FinanceRole]), CancellationToken.None);

        var pending = await f.Sut.ListPendingAsync(ClientId, CancellationToken.None);

        pending.Select(p => p.Email).Should().BeEquivalentTo("a@client.com", "b@client.com");
    }

    [Fact]
    public async Task Resend_IssuesANewLink_AndTheOldOneStopsWorking()
    {
        var f = new Fixture();
        var view = await f.InviteAsync();
        var oldToken = f.LatestToken();

        await f.Sut.ResendAsync(view.Id, InviterId, CancellationToken.None);

        f.Emails.Should().HaveCount(2);
        f.LatestToken().Should().NotBe(oldToken);
        (await f.Sut.PreviewAsync(oldToken, CancellationToken.None)).Should().BeNull();
        (await f.Sut.PreviewAsync(f.LatestToken(), CancellationToken.None))!.Valid.Should().BeTrue();
        (await f.Context.Invitations.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Resend_AndRevoke_NeedTheSameStandingAsSending()
    {
        var f = new Fixture();
        var view = await f.InviteAsync();
        var token = f.LatestToken();
        f.Refuse("You don't have permission to do that for this client.");

        await ((Func<Task>)(() => f.Sut.ResendAsync(view.Id, Guid.NewGuid(), CancellationToken.None))).Should().ThrowAsync<GuardException>();
        await ((Func<Task>)(() => f.Sut.RevokeAsync(view.Id, Guid.NewGuid(), CancellationToken.None))).Should().ThrowAsync<GuardException>();

        (await f.Sut.PreviewAsync(token, CancellationToken.None))!.Valid.Should().BeTrue("the refused revoke changed nothing");
        f.Emails.Should().HaveCount(1);
    }

    [Fact]
    public async Task Revoke_StopsTheLinkAtOnce_AndTakesItOffTheList()
    {
        var f = new Fixture();
        var view = await f.InviteAsync();
        var token = f.LatestToken();

        await f.Sut.RevokeAsync(view.Id, InviterId, CancellationToken.None);

        (await f.Sut.PreviewAsync(token, CancellationToken.None))!.Valid.Should().BeFalse();
        (await f.Sut.ListPendingAsync(ClientId, CancellationToken.None)).Should().BeEmpty();
        await ((Func<Task>)(() => f.Sut.AcceptAsync(new PortalAcceptInvitationRequest(token, "Ana", "G00d!Passw0rd"), CancellationToken.None)))
            .Should().ThrowAsync<GuardException>().WithMessage("*expired*");
    }

    [Fact]
    public async Task Resend_AnInvitationThatDoesNotExistOrWasAlreadyAccepted_IsRefused()
    {
        var f = new Fixture();
        var view = await f.InviteAsync();
        (await f.Context.Invitations.SingleAsync()).Status = InvitationStatus.Accepted;
        await f.Context.SaveChangesAsync();

        await ((Func<Task>)(() => f.Sut.ResendAsync(view.Id, InviterId, CancellationToken.None))).Should().ThrowAsync<GuardException>();
        await ((Func<Task>)(() => f.Sut.ResendAsync(Guid.NewGuid(), InviterId, CancellationToken.None))).Should().ThrowAsync<GuardException>();
    }

    // ── Preview ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Preview_ShowsWhoAndWhere_AndWhetherTheyAlreadyHaveAnAccount()
    {
        var f = new Fixture();
        await f.InviteAsync("ana@client.com", "Ana Cruz");
        f.Manager.Setup(m => m.FindByEmailAsync("ana@client.com")).ReturnsAsync(new User { Email = "ana@client.com" });

        var preview = await f.Sut.PreviewAsync(f.LatestToken(), CancellationToken.None);

        preview.Should().NotBeNull();
        preview!.Email.Should().Be("ana@client.com");
        preview.Name.Should().Be("Ana Cruz");
        preview.ClientName.Should().Be("Acme Corp");
        preview.CompanyName.Should().Be("Agency Co");
        preview.Valid.Should().BeTrue();
        preview.AccountExists.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("made-up-token")]
    public async Task Preview_UnknownLink_IsNull(string? token)
    {
        var f = new Fixture();
        await f.InviteAsync();

        (await f.Sut.PreviewAsync(token, CancellationToken.None)).Should().BeNull();
    }

    // ── Accepting ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Accept_NewPerson_CreatesAPortalOnlyLogin_AndGrantsTheInvitedRoles()
    {
        var f = new Fixture();
        await f.InviteAsync("ana@client.com", "Ana Cruz");
        var newId = Guid.NewGuid();
        User? created = null;
        f.Manager.Setup(m => m.CreateAsync(It.IsAny<User>(), "G00d!Passw0rd"))
            .Callback<User, string>((u, _) => { u.Id = newId; created = u; })
            .ReturnsAsync(IdentityResult.Success);

        var result = await f.Sut.AcceptAsync(new PortalAcceptInvitationRequest(f.LatestToken(), null, "G00d!Passw0rd"), CancellationToken.None);

        result.Should().Be(new PortalAcceptInvitationResult(newId, ClientId, true));
        created!.PortalOnly.Should().BeTrue();
        created.EmailConfirmed.Should().BeTrue();
        created.Email.Should().Be("ana@client.com");
        created.FullName.Should().Be("Ana Cruz", "the name from the invitation is used when none is typed");
        f.Grpc.Verify(g => g.GrantClientAccessAsync(newId, ClientId, "ana@client.com", "Ana Cruz",
            It.Is<IEnumerable<Guid>>(r => r.SequenceEqual(new[] { FinanceRole })), It.IsAny<int>()), Times.Once);
        (await f.Context.Invitations.SingleAsync()).Status.Should().Be(InvitationStatus.Accepted);
    }

    [Fact]
    public async Task Accept_ExistingAccount_KeepsItsPassword_AndJustGetsTheAccess()
    {
        var f = new Fixture();
        await f.InviteAsync("ana@client.com");
        var existing = new User { Id = Guid.NewGuid(), Email = "ana@client.com", FullName = "Ana C." };
        f.Manager.Setup(m => m.FindByEmailAsync("ana@client.com")).ReturnsAsync(existing);

        var result = await f.Sut.AcceptAsync(new PortalAcceptInvitationRequest(f.LatestToken(), null, null), CancellationToken.None);

        result.Should().Be(new PortalAcceptInvitationResult(existing.Id, ClientId, false));
        f.Manager.Verify(m => m.CreateAsync(It.IsAny<User>(), It.IsAny<string>()), Times.Never);
        f.Grpc.Verify(g => g.GrantClientAccessAsync(existing.Id, ClientId, "ana@client.com", "Ana C.", It.IsAny<IEnumerable<Guid>>(), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task Accept_NewPersonNeedsAPasswordAndName_AndNothingIsGrantedWithout()
    {
        var f = new Fixture();
        await f.InviteAsync("ana@client.com", name: null);

        await ((Func<Task>)(() => f.Sut.AcceptAsync(new PortalAcceptInvitationRequest(f.LatestToken(), "Ana", null), CancellationToken.None)))
            .Should().ThrowAsync<GuardException>().WithMessage("*password*");
        await ((Func<Task>)(() => f.Sut.AcceptAsync(new PortalAcceptInvitationRequest(f.LatestToken(), null, "G00d!Passw0rd"), CancellationToken.None)))
            .Should().ThrowAsync<GuardException>().WithMessage("*name*");

        f.Grpc.Verify(g => g.GrantClientAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<IEnumerable<Guid>>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Accept_AWeakPassword_IsRefusedWithTheReason_AndNothingIsGranted()
    {
        var f = new Fixture();
        await f.InviteAsync();
        f.Manager.Setup(m => m.CreateAsync(It.IsAny<User>(), "weak"))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Code = "PasswordTooShort", Description = "Passwords must be at least 8 characters." }));

        var act = () => f.Sut.AcceptAsync(new PortalAcceptInvitationRequest(f.LatestToken(), null, "weak"), CancellationToken.None);

        (await act.Should().ThrowAsync<GuardException>()).WithMessage("*at least 8*");
        f.Grpc.Verify(g => g.GrantClientAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<IEnumerable<Guid>>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Accept_WhenTheGrantFails_TheInvitationStaysPending_SoItCanBeRetried()
    {
        var f = new Fixture();
        await f.InviteAsync();
        var token = f.LatestToken();
        f.Manager.Setup(m => m.CreateAsync(It.IsAny<User>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
        f.GrantFails("This client is not active.");

        var act = () => f.Sut.AcceptAsync(new PortalAcceptInvitationRequest(token, null, "G00d!Passw0rd"), CancellationToken.None);

        (await act.Should().ThrowAsync<GuardException>()).WithMessage("*not active*");
        (await f.Context.Invitations.SingleAsync()).Status.Should().Be(InvitationStatus.Pending);
        (await f.Sut.PreviewAsync(token, CancellationToken.None))!.Valid.Should().BeTrue();
    }

    [Fact]
    public async Task Accept_AnInvitationCanBeUsedOnlyOnce()
    {
        var f = new Fixture();
        await f.InviteAsync();
        var token = f.LatestToken();
        f.Manager.Setup(m => m.CreateAsync(It.IsAny<User>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
        await f.Sut.AcceptAsync(new PortalAcceptInvitationRequest(token, null, "G00d!Passw0rd"), CancellationToken.None);

        var again = () => f.Sut.AcceptAsync(new PortalAcceptInvitationRequest(token, null, "G00d!Passw0rd"), CancellationToken.None);

        (await again.Should().ThrowAsync<GuardException>()).WithMessage("*already been used*");
    }

    [Fact]
    public async Task Accept_UnknownOrLapsedLink_IsRefused()
    {
        var f = new Fixture();
        await f.InviteAsync();
        var token = f.LatestToken();
        (await f.Context.Invitations.SingleAsync()).Expiry = DateTime.UtcNow.AddMinutes(-1);
        await f.Context.SaveChangesAsync();

        await ((Func<Task>)(() => f.Sut.AcceptAsync(new PortalAcceptInvitationRequest("nope", null, "G00d!Passw0rd"), CancellationToken.None)))
            .Should().ThrowAsync<GuardException>().WithMessage("*invalid*");
        await ((Func<Task>)(() => f.Sut.AcceptAsync(new PortalAcceptInvitationRequest(token, null, "G00d!Passw0rd"), CancellationToken.None)))
            .Should().ThrowAsync<GuardException>().WithMessage("*expired*");
    }

    [Fact]
    public async Task Accept_TheStoredHashIsNotAUsableToken()
    {
        var f = new Fixture();
        await f.InviteAsync();
        var hash = (await f.Context.Invitations.SingleAsync()).Token;

        var act = () => f.Sut.AcceptAsync(new PortalAcceptInvitationRequest(hash, null, "G00d!Passw0rd"), CancellationToken.None);

        await act.Should().ThrowAsync<GuardException>().WithMessage("*invalid*");
    }

    // ── Keeping the HRIS side apart ──────────────────────────────────────────

    [Fact]
    public async Task HrisInvitationPaths_NeverSeeAClientInvitation()
    {
        var f = new Fixture();
        await f.InviteAsync("ana@client.com");
        var hash = (await f.Context.Invitations.SingleAsync()).Token;
        var token = f.LatestToken();
        var uow = AuthTestContextFactory.CreateUnitOfWork(f.Context);
        var hris = new InvitationService(
            uow, new EmailTokenService(uow), new TenantRequestService(uow, null!),
            Options.Create(new Domains { FrontEnd = "https://app.test" }),
            new Mock<Microsoft.AspNetCore.Http.IHttpContextAccessor>().Object,
            new Mock<Microsoft.AspNetCore.Http.IHttpContextAccessor>().Object,
            f.Manager.Object, f.Publisher.Object, null!, null!, null!);

        // By the emailed token or by the stored hash, an HRIS preview / validity check finds nothing.
        (await hris.GetPreviewAsync(token)).Should().BeNull();
        (await hris.GetPreviewAsync(hash)).Should().BeNull();
        (await hris.IsValidAsync(token)).Should().BeFalse();
        (await hris.IsValidAsync(hash)).Should().BeFalse();
        (await hris.GetPendingByEmailAsync("ana@client.com")).Should().BeEmpty();

        // And an HRIS user holding the link can't use it to join the company.
        f.Manager.Setup(m => m.GetUserAsync(It.IsAny<System.Security.Claims.ClaimsPrincipal>()))
            .ReturnsAsync(new User { Email = "ana@client.com" });
        var accept = () => hris.Accept(token, new System.Security.Claims.ClaimsPrincipal(), default);
        (await accept.Should().ThrowAsync<GuardException>()).WithMessage("*invalid*");
        var acceptByToken = () => hris.AcceptByTokenAsync(token, "Ana", "G00d!Passw0rd", default);
        (await acceptByToken.Should().ThrowAsync<GuardException>()).WithMessage("*invalid*");
    }
}
