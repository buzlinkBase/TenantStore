using FluentAssertions;
using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using Onepunch.Auth.Api.Tests.TestSupport;
using Onepunch.Auth.Core;
using Onepunch.Auth.Core.Services;
using Onepunch.Auth.Domain.DTOs;
using Onepunch.Auth.Domain.Entities;
using Onepunch.Auth.Infrastructure;
using Onepunch.Common.Lib.Exceptions;
using OnePunch.Auth.Domain.Entities;
using System.Security.Claims;
using Xunit;

namespace Onepunch.Auth.Api.Tests.Services;

/// <summary>
/// InvitationService.Accept/AcceptByTokenAsync/SendUserInvitationAsync — covers the Tenant
/// Invitation Workflow fixes: the email-match security check, distinct error messages per
/// failure mode, and superseding stale pending invitations on resend. The idempotent-replay
/// success path (Accept() called twice for the same already-accepted invitation) isn't covered
/// here — it requires a real signing key/gRPC membership client to reach
/// MintTenantScopedResponseAsync, which this project's own JwtServiceTests avoids for the same
/// reason (see its Hash-only coverage). Verify that path manually/end-to-end instead.
///
/// SendUserInvitationAsync's already-a-member guard branch (the one that calls
/// MembershipCacheService.GetMembershipsAsync once FindByEmailAsync resolves an existing
/// account) isn't covered here either, since CreateSut() passes no MembershipCacheService --
/// only the "no account exists yet" branch (which never reaches it) is. The membership merge
/// itself is covered by MembershipCacheServiceTests, and the accept path's roles/permissions
/// resolution by the ResolveJoinedTenant tests at the bottom of this class.
/// </summary>
public class InvitationServiceTests
{
    private static (InvitationService Sut, AuthContext Context, Mock<Microsoft.AspNetCore.Identity.UserManager<User>> UserManager, Mock<IPublishEndpoint> Publisher)
        CreateSut()
    {
        var context = AuthTestContextFactory.CreateContext();
        var uow = AuthTestContextFactory.CreateUnitOfWork(context);
        var userManager = IdentityMocks.MockUserManager();
        var publisher = new Mock<IPublishEndpoint>();
        var tenantRequestService = new TenantRequestService(uow, null!);
        var emailTokenService = new EmailTokenService(uow);
        var domains = Options.Create(new Domains { FrontEnd = "https://app.test" });
        var httpContextAccessor = new Mock<IHttpContextAccessor>();

        var sut = new InvitationService(
            uow,
            emailTokenService,
            tenantRequestService,
            domains,
            httpContextAccessor.Object,
            httpContextAccessor.Object,
            userManager.Object,
            publisher.Object,
            null!, // JwtService — not needed by any branch under test (see class doc comment)
            null!, // MembershipCacheService — same (see class doc comment)
            null!  // MembershipGrpcClient — same; ResolveJoinedTenant below covers the activation merge
        );

        return (sut, context, userManager, publisher);
    }

    private static ClaimsPrincipal SomeClaimsPrincipal() =>
        new(new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())], "test"));

    // ── Accept ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Accept_Throws_WhenTokenNotFound()
    {
        var (sut, _, userManager, _) = CreateSut();
        userManager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(new User { Email = "someone@test.com" });

        var act = () => sut.Accept("no-such-token", SomeClaimsPrincipal(), default);

        (await act.Should().ThrowAsync<GuardException>()).WithMessage("*invalid*");
    }

    [Fact]
    public async Task Accept_Throws_WhenEmailDoesNotMatch()
    {
        // The security fix: a valid, still-pending token for someone ELSE's email must not be
        // acceptable by a different authenticated user.
        var (sut, context, userManager, publisher) = CreateSut();
        context.Invitations.Add(new Invitation
        {
            Token = "tok-1",
            Email = "invited@test.com",
            TenantId = Guid.NewGuid(),
            TenantName = "Acme",
            Status = InvitationStatus.Pending,
            Expiry = DateTime.UtcNow.AddHours(1),
        });
        await context.SaveChangesAsync();

        userManager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(new User { Email = "attacker@test.com" });

        var act = () => sut.Accept("tok-1", SomeClaimsPrincipal(), default);

        (await act.Should().ThrowAsync<GuardException>()).WithMessage("*different email*");
        publisher.Verify(p => p.Publish(It.IsAny<object>(), It.IsAny<Type>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Accept_Throws_WhenExpired()
    {
        var (sut, context, userManager, _) = CreateSut();
        context.Invitations.Add(new Invitation
        {
            Token = "tok-2",
            Email = "invited@test.com",
            TenantId = Guid.NewGuid(),
            TenantName = "Acme",
            Status = InvitationStatus.Pending,
            Expiry = DateTime.UtcNow.AddHours(-1),
        });
        await context.SaveChangesAsync();

        userManager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(new User { Email = "invited@test.com" });

        var act = () => sut.Accept("tok-2", SomeClaimsPrincipal(), default);

        (await act.Should().ThrowAsync<GuardException>()).WithMessage("*expired*");
    }

    // ── AcceptByTokenAsync ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task AcceptByTokenAsync_Throws_WhenTokenNotFound()
    {
        var (sut, _, _, _) = CreateSut();

        var act = () => sut.AcceptByTokenAsync("no-such-token", "New User", "P@ssw0rd1", default);

        (await act.Should().ThrowAsync<GuardException>()).WithMessage("*invalid*");
    }

    [Fact]
    public async Task AcceptByTokenAsync_Throws_WhenAccountAlreadyExists_EvenIfInvitationAlreadyAccepted()
    {
        // existingUser check must win over the invitation's own status — it's the most
        // actionable message regardless of what state the invitation is in.
        var (sut, context, userManager, _) = CreateSut();
        context.Invitations.Add(new Invitation
        {
            Token = "tok-3",
            Email = "invited@test.com",
            TenantId = Guid.NewGuid(),
            TenantName = "Acme",
            Status = InvitationStatus.Accepted,
            Expiry = DateTime.UtcNow.AddHours(1),
        });
        await context.SaveChangesAsync();

        userManager.Setup(m => m.FindByEmailAsync("invited@test.com"))
            .ReturnsAsync(new User { Email = "invited@test.com" });

        var act = () => sut.AcceptByTokenAsync("tok-3", "New User", "P@ssw0rd1", default);

        (await act.Should().ThrowAsync<GuardException>()).WithMessage("*sign in*");
    }

    [Fact]
    public async Task AcceptByTokenAsync_Throws_WhenAlreadyUsed_AndNoAccountExists()
    {
        var (sut, context, userManager, _) = CreateSut();
        context.Invitations.Add(new Invitation
        {
            Token = "tok-4",
            Email = "invited@test.com",
            TenantId = Guid.NewGuid(),
            TenantName = "Acme",
            Status = InvitationStatus.Accepted,
            Expiry = DateTime.UtcNow.AddHours(1),
        });
        await context.SaveChangesAsync();

        userManager.Setup(m => m.FindByEmailAsync("invited@test.com")).ReturnsAsync((User?)null);

        var act = () => sut.AcceptByTokenAsync("tok-4", "New User", "P@ssw0rd1", default);

        (await act.Should().ThrowAsync<GuardException>()).WithMessage("*already been used*");
    }

    [Fact]
    public async Task AcceptByTokenAsync_Throws_WhenExpired()
    {
        var (sut, context, userManager, _) = CreateSut();
        context.Invitations.Add(new Invitation
        {
            Token = "tok-5",
            Email = "invited@test.com",
            TenantId = Guid.NewGuid(),
            TenantName = "Acme",
            Status = InvitationStatus.Pending,
            Expiry = DateTime.UtcNow.AddHours(-1),
        });
        await context.SaveChangesAsync();

        userManager.Setup(m => m.FindByEmailAsync("invited@test.com")).ReturnsAsync((User?)null);

        var act = () => sut.AcceptByTokenAsync("tok-5", "New User", "P@ssw0rd1", default);

        (await act.Should().ThrowAsync<GuardException>()).WithMessage("*expired*");
    }

    // ── SendUserInvitationAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SendUserInvitationAsync_SupersedesPriorPendingInvitation_ForSameEmailAndTenant()
    {
        var (sut, context, userManager, _) = CreateSut();
        var tenantId = Guid.NewGuid();
        var prior = new Invitation
        {
            Token = "prior-tok",
            Email = "invited@test.com",
            TenantId = tenantId,
            TenantName = "Acme",
            Status = InvitationStatus.Pending,
            Expiry = DateTime.UtcNow.AddHours(1),
        };
        context.Invitations.Add(prior);
        await context.SaveChangesAsync();

        var admin = new User { Email = "admin@test.com", DefaultTenantRoles = ["Owner"] };
        userManager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(admin);

        await sut.SendUserInvitationAsync(
            new InvitationRequest { Email = "invited@test.com" },
            tenantId,
            "Acme",
            "app.test",
            SomeClaimsPrincipal(),
            default);

        var reloaded = await context.Invitations.FirstAsync(x => x.Token == "prior-tok");
        reloaded.Expiry.Should().BeOnOrBefore(DateTime.UtcNow);
    }

    [Fact]
    public async Task SendUserInvitationAsync_DoesNotSupersede_InvitationForDifferentTenant()
    {
        var (sut, context, userManager, _) = CreateSut();
        var otherTenantId = Guid.NewGuid();
        var targetTenantId = Guid.NewGuid();
        var unrelated = new Invitation
        {
            Token = "unrelated-tok",
            Email = "invited@test.com",
            TenantId = otherTenantId,
            TenantName = "Other Co",
            Status = InvitationStatus.Pending,
            Expiry = DateTime.UtcNow.AddHours(1),
        };
        context.Invitations.Add(unrelated);
        await context.SaveChangesAsync();
        var originalExpiry = unrelated.Expiry;

        var admin = new User { Email = "admin@test.com", DefaultTenantRoles = ["Owner"] };
        userManager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(admin);

        await sut.SendUserInvitationAsync(
            new InvitationRequest { Email = "invited@test.com" },
            targetTenantId,
            "Acme",
            "app.test",
            SomeClaimsPrincipal(),
            default);

        var reloaded = await context.Invitations.FirstAsync(x => x.Token == "unrelated-tok");
        reloaded.Expiry.Should().Be(originalExpiry);
    }

    [Fact]
    public async Task SendUserInvitationAsync_Allows_WhenNoAccountExistsForEmail()
    {
        // No User row at all for this email -- FindByEmailAsync returns null, so the guard is
        // skipped entirely (MembershipCacheService is never consulted, matching CreateSut()'s
        // default null! for every test that doesn't need it).
        var (sut, context, userManager, _) = CreateSut();
        var tenantId = Guid.NewGuid();

        var admin = new User { Email = "admin@test.com", DefaultTenantRoles = ["Owner"] };
        userManager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(admin);
        userManager.Setup(m => m.FindByEmailAsync("new-person@test.com")).ReturnsAsync((User?)null);

        await sut.SendUserInvitationAsync(
            new InvitationRequest { Email = "new-person@test.com" }, tenantId, "Acme", "app.test", SomeClaimsPrincipal(), default);

        context.Invitations.Should().ContainSingle(x => x.Email == "new-person@test.com");
    }

    [Fact]
    public async Task SendUserInvitationAsync_AllowsInvitingAsClient()
    {
        var (sut, context, userManager, _) = CreateSut();
        var admin = new User { Email = "admin@test.com", DefaultTenantRoles = ["Owner"] };
        userManager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(admin);
        userManager.Setup(m => m.FindByEmailAsync("client@test.com")).ReturnsAsync((User?)null);

        await sut.SendUserInvitationAsync(
            new InvitationRequest { Email = "client@test.com", Roles = ["Client"] },
            Guid.NewGuid(), "Acme", "app.test", SomeClaimsPrincipal(), default);

        context.Invitations.Should().ContainSingle(x => x.Email == "client@test.com")
            .Which.Roles.Should().Equal("Client");
    }

    [Fact]
    public async Task SendUserInvitationAsync_StillRejectsOwner()
    {
        var (sut, _, userManager, _) = CreateSut();
        var admin = new User { Email = "admin@test.com", DefaultTenantRoles = ["Owner"] };
        userManager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(admin);

        var act = () => sut.SendUserInvitationAsync(
            new InvitationRequest { Email = "x@test.com", Roles = ["Owner"] },
            Guid.NewGuid(), "Acme", "app.test", SomeClaimsPrincipal(), default);

        (await act.Should().ThrowAsync<GuardException>()).WithMessage("*Invalid role*");
    }

    // ── ResolveJoinedTenant ──────────────────────────────────────────────────────────────
    // The pure half of MintTenantScopedResponseAsync: decides which roles/permissions the
    // accept response AND the minted JWT carry. The regression it guards: a fresh acceptance
    // used to come back with Permissions=[] (the async UserJoin event hadn't been delivered yet),
    // which sent the frontend's permission guards into an endless redirect loop.

    private static Invitation SomeInvitation(Guid tenantId) => new()
    {
        Token = "tok",
        Email = "invited@test.com",
        TenantId = tenantId,
        TenantName = "Acme",
        Roles = ["Employee"],
        Status = InvitationStatus.Accepted,
        Expiry = DateTime.UtcNow.AddHours(1),
    };

    [Fact]
    public void ResolveJoinedTenant_UsesActivationPermissions_WhenLookupDidNotIncludeTenantYet()
    {
        var tenantId = Guid.NewGuid();
        var tenants = new List<UsersTenant>();
        var activation = new MembershipResolveResult
        {
            Success = true,
            Found = true,
            Roles = ["Employee"],
            Permissions = ["Employee Self-Service Portal:View"],
            Status = "Active",
            TenantName = "Acme",
        };

        var joined = InvitationService.ResolveJoinedTenant(tenants, SomeInvitation(tenantId), activation);

        joined.TenantId.Should().Be(tenantId);
        joined.Permissions.Should().Equal("Employee Self-Service Portal:View");
        joined.Roles.Should().Equal("Employee");
        joined.HrDbReady.Should().BeTrue();
        tenants.Should().ContainSingle().Which.Should().BeSameAs(joined);
    }

    [Fact]
    public void ResolveJoinedTenant_OverlaysActivation_OntoExistingLookupEntry()
    {
        var tenantId = Guid.NewGuid();
        var existing = new UsersTenant
        {
            TenantId = tenantId,
            Name = "Acme",
            Roles = ["Member"],
            Permissions = [],
            State = "Active",
            HrDbReady = true,
        };
        var tenants = new List<UsersTenant> { existing };
        var activation = new MembershipResolveResult
        {
            Success = true,
            Found = true,
            Roles = ["Admin"],
            Permissions = ["Users:Edit"],
            Status = "Active",
        };

        var joined = InvitationService.ResolveJoinedTenant(tenants, SomeInvitation(tenantId), activation);

        joined.Should().BeSameAs(existing);
        joined.Roles.Should().Equal("Admin");
        joined.Permissions.Should().Equal("Users:Edit");
        tenants.Should().ContainSingle();
    }

    [Fact]
    public void ResolveJoinedTenant_FallsBackToInvitationRoles_WhenActivationFailed()
    {
        var tenantId = Guid.NewGuid();
        var tenants = new List<UsersTenant>();

        var joined = InvitationService.ResolveJoinedTenant(
            tenants, SomeInvitation(tenantId), new MembershipResolveResult { Success = false });

        joined.Roles.Should().Equal("Employee");
        joined.Permissions.Should().BeEmpty();
        joined.State.Should().Be("Active");
    }

    [Fact]
    public void ResolveJoinedTenant_KeepsLookupEntry_WhenActivationFailed()
    {
        var tenantId = Guid.NewGuid();
        var existing = new UsersTenant
        {
            TenantId = tenantId,
            Name = "Acme",
            Roles = ["Employee"],
            Permissions = ["Employee Self-Service Portal:View"],
            State = "Active",
        };
        var tenants = new List<UsersTenant> { existing };

        var joined = InvitationService.ResolveJoinedTenant(
            tenants, SomeInvitation(tenantId), new MembershipResolveResult { Success = false });

        joined.Permissions.Should().Equal("Employee Self-Service Portal:View");
    }
}
