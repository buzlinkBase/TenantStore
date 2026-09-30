using FluentAssertions;
using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using Onepunch.AuditEngine;
using Onepunch.Auth.Api.Tests.TestSupport;
using Onepunch.Auth.Core;
using Onepunch.Auth.Core.Services;
using Onepunch.Auth.Domain.DTOs;
using Onepunch.Auth.Domain.Entities;
using Onepunch.Auth.Infrastructure;
using Onepunch.Common.Lib;
using Onepunch.Common.Lib.Cache;
using Onepunch.Common.Lib.DTO;
using Onepunch.Common.Lib.Exceptions;
using OnePunch.Auth.Core.Auditing;
using OnePunch.Auth.Domain.Entities;
using System.Security.Claims;
using Xunit;

namespace Onepunch.Auth.Api.Tests.Services;

/// <summary>
/// InvitationService's batch invite and its live recipient check: up to 100 employees (each
/// email tied to its employee) or logins for one client, re-checked on send, idempotent per
/// X-Idempotency-Key, audited, and gated by "Users:Invite".
/// </summary>
public class BatchInvitationServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private const string MemberEmail = "member@test.com";

    private sealed record Sut(
        InvitationService Service,
        AuthContext Context,
        Mock<IPublishEndpoint> Publisher,
        List<AuditEvent> Audited);

    // MEMBER_EMAIL has an account with an active membership in TenantId; nobody else has one.
    private static Sut CreateSut(User? caller = null)
    {
        var context = AuthTestContextFactory.CreateContext();
        var uow = AuthTestContextFactory.CreateUnitOfWork(context);

        var userManager = IdentityMocks.MockUserManager();
        userManager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(caller ?? new User { Id = Guid.NewGuid(), Email = "admin@test.com", DefaultTenantRoles = ["HR"] });
        userManager.Setup(m => m.FindByEmailAsync(MemberEmail))
            .ReturnsAsync(new User { Id = Guid.NewGuid(), Email = MemberEmail, FullName = "Jane Doe" });

        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetAsync<List<UsersTenant>>(It.IsAny<string>()))
            .ReturnsAsync([new UsersTenant { TenantId = TenantId, Name = "Acme", State = "Active" }]);
        var membershipCache = new MembershipCacheService(cache.Object, uow, null!);

        var audited = new List<AuditEvent>();
        var writer = new Mock<IAuditWriter>();
        writer.Setup(w => w.WriteAsync(It.IsAny<AuditEvent>(), It.IsAny<CancellationToken>()))
            .Callback<AuditEvent, CancellationToken>((e, _) => audited.Add(e))
            .Returns(Task.CompletedTask);

        var publisher = new Mock<IPublishEndpoint>();
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        var service = new InvitationService(
            uow,
            new EmailTokenService(uow),
            new TenantRequestService(uow, null!),
            Options.Create(new Domains { FrontEnd = "https://app.test" }),
            httpContextAccessor.Object,
            httpContextAccessor.Object,
            userManager.Object,
            publisher.Object,
            null!,
            membershipCache,
            null!,
            new AuthAuditRecorder(writer.Object));
        return new Sut(service, context, publisher, audited);
    }

    private static ClaimsPrincipal Inviter(params string[] permissions)
    {
        var claims = new List<Claim> { new("sub", Guid.NewGuid().ToString()) };
        claims.AddRange(permissions.Select(p => new Claim("permission", p)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static readonly ClaimsPrincipal CanInvite = Inviter(InvitationPolicy.InvitePermission);

    private static BatchInvitationRequest EmployeeBatch(params string[] emails) => new()
    {
        EntityType = InviteEntityType.Employee,
        Role = "Employee",
        Recipients = emails.Select(e => new BatchInvitationRecipient { Email = e, EntityId = Guid.NewGuid(), Name = e }).ToList(),
    };

    private static async Task SeedPendingAsync(AuthContext context, string email)
    {
        context.Invitations.Add(new Invitation
        {
            Token = Guid.NewGuid().ToString(),
            Email = email,
            TenantId = TenantId,
            TenantName = "Acme",
            Status = InvitationStatus.Pending,
            Expiry = DateTime.UtcNow.AddDays(3),
            CreatedAt = new DateTime(2026, 9, 28, 8, 0, 0, DateTimeKind.Utc),
        });
        await context.SaveChangesAsync();
    }

    // ── Live check ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CheckRecipients_ReportsEachEmailsState()
    {
        var sut = CreateSut();
        await SeedPendingAsync(sut.Context, "pending@test.com");

        var result = await sut.Service.CheckRecipientsAsync(
            ["new@test.com", " MEMBER@test.com ", "pending@test.com", "invalid-email"], TenantId, default);

        result.Should().HaveCount(4);
        result.Single(r => r.Email == "new@test.com").Status.Should().Be(InviteRecipientStatus.Ok);
        var member = result.Single(r => r.Email == MemberEmail);
        member.Status.Should().Be(InviteRecipientStatus.Member);
        member.Name.Should().Be("Jane Doe");
        var pending = result.Single(r => r.Email == "pending@test.com");
        pending.Status.Should().Be(InviteRecipientStatus.PendingInvite);
        pending.SentAt.Should().Be(new DateTime(2026, 9, 28, 8, 0, 0, DateTimeKind.Utc));
        result.Single(r => r.Email == "invalid-email").Status.Should().Be(InviteRecipientStatus.InvalidEmail);
    }

    // ── Send ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SendBatch_InvitesTheValidRecipients_AndReportsTheRest()
    {
        var sut = CreateSut();
        await SeedPendingAsync(sut.Context, "pending@test.com");

        var result = await sut.Service.SendBatchAsync(
            EmployeeBatch("alex@test.com", "sam@test.com", MemberEmail, "pending@test.com", "invalid-email"),
            TenantId, "Acme", "key-1", CanInvite, default);

        result.Sent.Should().Be(2);
        result.Failed.Select(f => f.Email).Should().BeEquivalentTo([MemberEmail, "pending@test.com", "invalid-email"]);
        result.Failed.Single(f => f.Email == MemberEmail).Reason.Should().ContainEquivalentOf("already an active member");

        var created = await sut.Context.Invitations.Where(x => x.Email == "alex@test.com" || x.Email == "sam@test.com").ToListAsync();
        created.Should().HaveCount(2).And.OnlyContain(x =>
            x.Roles.SequenceEqual(new[] { "Employee" }) && x.EmployeeId != null && x.ClientId == null &&
            x.Expiry > DateTime.UtcNow.AddDays(6.9) && x.Expiry <= DateTime.UtcNow.AddDays(7));
        sut.Publisher.Verify(p => p.Publish(It.IsAny<UserInvited>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task SendBatch_EmployeeRecipientWithoutAnEmployee_IsReported()
    {
        var sut = CreateSut();
        var request = EmployeeBatch("alex@test.com");
        request.Recipients[0].EntityId = null;

        var result = await sut.Service.SendBatchAsync(request, TenantId, "Acme", "key-1", CanInvite, default);

        result.Sent.Should().Be(0);
        result.Failed.Should().ContainSingle().Which.Reason.Should().Contain("No employee record");
    }

    [Fact]
    public async Task SendBatch_ClientLogins_CarryTheClient()
    {
        var sut = CreateSut();
        var clientId = Guid.NewGuid();
        var request = new BatchInvitationRequest
        {
            EntityType = InviteEntityType.Client,
            ClientId = clientId,
            Role = "Client",
            Recipients = [new BatchInvitationRecipient { Email = "buyer@client.com" }],
        };

        var result = await sut.Service.SendBatchAsync(request, TenantId, "Acme", "key-1", CanInvite, default);

        result.Sent.Should().Be(1);
        var invitation = await sut.Context.Invitations.SingleAsync(x => x.Email == "buyer@client.com");
        invitation.ClientId.Should().Be(clientId);
        invitation.EmployeeId.Should().BeNull();
        sut.Publisher.Verify(p => p.Publish(It.Is<UserInvited>(m => m.ClientId == clientId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendBatch_ClientBatchWithoutAClient_IsRefused()
    {
        var sut = CreateSut();
        var request = new BatchInvitationRequest
        {
            EntityType = InviteEntityType.Client,
            Role = "Client",
            Recipients = [new BatchInvitationRecipient { Email = "buyer@client.com" }],
        };

        var act = () => sut.Service.SendBatchAsync(request, TenantId, "Acme", "key-1", CanInvite, default);

        await act.Should().ThrowAsync<GuardException>().WithMessage("*client*");
    }

    [Fact]
    public async Task SendBatch_RepeatedIdempotencyKey_ReturnsTheFirstResult_WithoutSendingAgain()
    {
        var sut = CreateSut();
        var request = EmployeeBatch("alex@test.com");

        var first = await sut.Service.SendBatchAsync(request, TenantId, "Acme", "same-key", CanInvite, default);
        var retry = await sut.Service.SendBatchAsync(request, TenantId, "Acme", "same-key", CanInvite, default);

        retry.Sent.Should().Be(first.Sent);
        (await sut.Context.Invitations.CountAsync(x => x.Email == "alex@test.com")).Should().Be(1);
        sut.Publisher.Verify(p => p.Publish(It.IsAny<UserInvited>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendBatch_MoreThanOneHundredRecipients_IsRefused()
    {
        var sut = CreateSut();
        var request = EmployeeBatch(Enumerable.Range(1, 101).Select(i => $"user{i}@test.com").ToArray());

        var act = () => sut.Service.SendBatchAsync(request, TenantId, "Acme", "key-1", CanInvite, default);

        await act.Should().ThrowAsync<GuardException>().WithMessage("*at most 100*");
    }

    [Theory]
    [InlineData("Owner")]
    [InlineData("")]
    public async Task SendBatch_OwnerOrBlankRole_IsRefused(string role)
    {
        var sut = CreateSut();
        var request = EmployeeBatch("alex@test.com");
        request.Role = role;

        var act = () => sut.Service.SendBatchAsync(request, TenantId, "Acme", "key-1", CanInvite, default);

        await act.Should().ThrowAsync<GuardException>();
    }

    [Fact]
    public async Task SendBatch_WithoutTheInvitePermission_IsRefused()
    {
        var sut = CreateSut();

        var act = () => sut.Service.SendBatchAsync(EmployeeBatch("alex@test.com"), TenantId, "Acme", "key-1", Inviter("Users:View"), default);

        await act.Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task SendBatch_WritesOneAuditEvent_NamingEveryRecipient()
    {
        var sut = CreateSut();
        var request = EmployeeBatch("alex@test.com", "sam@test.com");

        await sut.Service.SendBatchAsync(request, TenantId, "Acme", "key-1", CanInvite, default);

        var audit = sut.Audited.Should().ContainSingle().Subject;
        audit.EventType.Should().Be(AuthAuditRecorder.InvitationBatchSent);
        audit.OrganizationId.Should().Be(TenantId.ToString());
        audit.Metadata["Role"].Should().Be("Employee");
        audit.Metadata["Recipients"].Should().Contain("alex@test.com").And.Contain(request.Recipients[1].EntityId.ToString());
    }

    [Fact]
    public void InvitationLinks_LastSevenDays() =>
        InvitationPolicy.LinkLifetime.Should().Be(TimeSpan.FromDays(7));
}
