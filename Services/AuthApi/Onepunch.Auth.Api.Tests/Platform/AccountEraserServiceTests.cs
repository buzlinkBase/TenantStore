using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Onepunch.AuditEngine.EntityFrameworkCore;
using Onepunch.Auth.Api.Tests.TestSupport;
using Onepunch.Auth.Core.Platform;
using Onepunch.Auth.Core.Services;
using Onepunch.Auth.Domain.Entities;
using Onepunch.Auth.Infrastructure;
using Onepunch.Common.Lib.Cache;
using Onepunch.Common.Lib.DTO;
using OnePunch.Auth.Core.Hubs;
using OnePunch.Auth.Core.Messaging;
using OnePunch.Auth.Core.Services;
using OnePunch.Auth.Domain.Entities;
using Xunit;

namespace Onepunch.Auth.Api.Tests.Platform;

/// <summary>
/// The last step of erasing an account: the logins that existed only for it go (with their sessions,
/// tokens, invitations and sign-in trail), people who also belong elsewhere stay, and exactly one
/// operator log line is written.
/// </summary>
public class AccountEraserServiceTests
{
    /// <summary>
    /// The InMemory provider can't run set-based deletes. The test context has no soft-delete
    /// interceptor, so removing through the change tracker is a real delete here.
    /// </summary>
    private sealed class TrackedRowEraser(AuthContext context) : IRowEraser
    {
        public async Task<int> EraseAsync<T>(IQueryable<T> rows, CancellationToken token) where T : class
        {
            var selected = await rows.ToListAsync(token);
            context.Set<T>().RemoveRange(selected);
            return selected.Count;
        }
    }

    private sealed class Fixture
    {
        public AuthContext Db { get; } = AuthTestContextFactory.CreateContext();
        public PlatformAuthContext Platform { get; } = new(new DbContextOptionsBuilder<PlatformAuthContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);
        public Mock<ICacheService> Cache { get; } = new();
        public Mock<ITenantNotificationClient> Client { get; } = new();

        /// <summary>A fresh unit of work each time, as each delivery gets one (it commits only once).</summary>
        public AccountHardDeletedWorker Worker()
        {
            var hubClients = new Mock<IHubClients<ITenantNotificationClient>>();
            hubClients.Setup(c => c.User(It.IsAny<string>())).Returns(Client.Object);
            var hub = new Mock<IHubContext<TenantHub, ITenantNotificationClient>>();
            hub.Setup(h => h.Clients).Returns(hubClients.Object);
            var uow = AuthTestContextFactory.CreateUnitOfWork(Db);
            var eraser = new AccountEraserService(uow, Platform, new TrackedRowEraser(Db),
                new MembershipCacheService(Cache.Object, null!, null!), new TenantNotificationService(hub.Object), TimeProvider.System);
            return new AccountHardDeletedWorker(eraser);
        }

        public Task ConsumeAsync(CompanyHardDeleted message)
        {
            var context = new Mock<MassTransit.ConsumeContext<CompanyHardDeleted>>();
            context.SetupGet(c => c.Message).Returns(message);
            context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
            return Worker().Consume(context.Object);
        }

        public Task ConsumeAsync(AccountHardDeleted message)
        {
            var context = new Mock<MassTransit.ConsumeContext<AccountHardDeleted>>();
            context.SetupGet(c => c.Message).Returns(message);
            context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
            return Worker().Consume(context.Object);
        }
    }

    private static readonly Guid Company = Guid.NewGuid();
    private static readonly Guid OtherCompany = Guid.NewGuid();

    private static AccountHardDeleted Message(Guid owner, IEnumerable<Guid> delete, IEnumerable<Guid> detach) => new()
    {
        RequestId = Guid.NewGuid(),
        OwnerUserId = owner,
        OwnerEmail = "owner@acme.ph",
        TenantIds = [Company],
        CompanyNames = ["Acme"],
        DeleteUserIds = delete.ToList(),
        DetachUserIds = detach.ToList(),
        RequestedBy = "platform:alice@onepunch.test",
        ApprovedBy = "platform:bob@onepunch.test",
        Reason = "Test account",
    };

    [Fact]
    public async Task DeletesTheAccountsLogins_AndEverythingHangingOffThem()
    {
        var f = new Fixture();
        var owner = new User { Id = Guid.NewGuid(), Email = "owner@acme.ph", DefaultTenantId = Company };
        var staff = new User { Id = Guid.NewGuid(), Email = "staff@acme.ph", DefaultTenantId = Company };
        var stranger = new User { Id = Guid.NewGuid(), Email = "someone@beta.ph", DefaultTenantId = OtherCompany };
        f.Db.Users.AddRange(owner, staff, stranger);
        f.Db.RefreshTokens.AddRange(
            new RefreshToken { Id = Guid.NewGuid(), RefreshTokenHash = "h", UserId = owner.Id, TenantId = Company },
            new RefreshToken { Id = Guid.NewGuid(), RefreshTokenHash = "h", UserId = stranger.Id, TenantId = OtherCompany });
        f.Db.EmailTokens.Add(new EmailToken { Id = Guid.NewGuid(), UserId = staff.Id, Email = "staff@acme.ph", TokenType = "verify", TokenValue = "v" });
        f.Db.Invitations.AddRange(
            new Invitation { Id = Guid.NewGuid(), TenantId = Company, UserId = owner.Id, Email = "new@acme.ph", TenantName = "Acme", Token = "t1" },
            new Invitation { Id = Guid.NewGuid(), TenantId = OtherCompany, UserId = stranger.Id, Email = "new@beta.ph", TenantName = "Beta", Token = "t2" });
        f.Db.TenantCreationRequests.Add(new TenantCreationRequestStatus { Id = Guid.NewGuid(), TenantId = Company, UserId = owner.Id, TenantName = "Acme" });
        f.Db.Set<AuditEventRecord>().AddRange(
            new AuditEventRecord { Id = Guid.NewGuid(), EventType = "SignIn", OrganizationId = Company.ToString() },
            new AuditEventRecord { Id = Guid.NewGuid(), EventType = "SignIn", OrganizationId = OtherCompany.ToString() });
        await f.Db.SaveChangesAsync();

        await f.ConsumeAsync(Message(owner.Id, [owner.Id, staff.Id], []));

        f.Db.ChangeTracker.Clear();
        (await f.Db.Users.Select(u => u.Id).ToListAsync()).Should().Equal(stranger.Id);
        (await f.Db.RefreshTokens.Select(t => t.UserId).ToListAsync()).Should().Equal(stranger.Id);
        (await f.Db.EmailTokens.CountAsync()).Should().Be(0);
        (await f.Db.Invitations.Select(i => i.Token).ToListAsync()).Should().Equal("t2");
        (await f.Db.TenantCreationRequests.CountAsync()).Should().Be(0);
        (await f.Db.Set<AuditEventRecord>().Select(a => a.OrganizationId).ToListAsync()).Should().Equal(OtherCompany.ToString());
        f.Client.Verify(c => c.SessionRevoked(It.IsAny<SessionRevokedNotification>()), Times.Exactly(2));
    }

    [Fact]
    public async Task SomeoneWhoBelongsElsewhere_IsKept_WithTheErasedDefaultCompanyCleared()
    {
        var f = new Fixture();
        var owner = new User { Id = Guid.NewGuid(), Email = "owner@acme.ph", DefaultTenantId = Company };
        var shared = new User
        {
            Id = Guid.NewGuid(), Email = "hr@both.ph", DefaultTenantId = Company, DefaultTenantName = "Acme", DefaultTenantRoles = ["HR"],
        };
        f.Db.Users.AddRange(owner, shared);
        f.Db.RefreshTokens.Add(new RefreshToken { Id = Guid.NewGuid(), RefreshTokenHash = "h", UserId = shared.Id, TenantId = OtherCompany });
        await f.Db.SaveChangesAsync();

        await f.ConsumeAsync(Message(owner.Id, [owner.Id], [shared.Id]));

        f.Db.ChangeTracker.Clear();
        var kept = await f.Db.Users.SingleAsync();
        kept.Id.Should().Be(shared.Id);
        kept.DefaultTenantId.Should().BeNull();
        kept.DefaultTenantName.Should().BeNull();
        kept.DefaultTenantRoles.Should().BeEmpty();
        (await f.Db.RefreshTokens.CountAsync()).Should().Be(1, "their session in the other company stays");
        f.Cache.Verify(c => c.RemoveAsync($"membership:{shared.Id}"), Times.Once);
        (await f.Platform.PlatformAdminAudits.SingleAsync()).Detail.Should()
            .Contain("Logins kept (they belong to other companies): hr@both.ph.");
        f.Client.Verify(c => c.RolesChanged(It.IsAny<RolesChangedNotification>()), Times.Once);
    }

    [Fact]
    public async Task WritesOneOperatorLogLine_EvenWhenDeliveredTwice()
    {
        var f = new Fixture();
        var owner = new User { Id = Guid.NewGuid(), Email = "owner@acme.ph" };
        f.Db.Users.Add(owner);
        await f.Db.SaveChangesAsync();
        var message = Message(owner.Id, [owner.Id], []);

        await f.ConsumeAsync(message);
        await f.ConsumeAsync(message);

        var line = await f.Platform.PlatformAdminAudits.SingleAsync();
        line.Action.Should().Be(AccountEraserService.HardDeletedAction);
        line.TargetId.Should().Be(message.RequestId);
        line.TargetEmail.Should().Be("owner@acme.ph");
        line.ActorEmail.Should().Be("bob@onepunch.test");
        line.Detail.Should().Contain("alice@onepunch.test").And.Contain("Acme").And.Contain("Test account");
    }

    [Fact]
    public async Task ACompanysErasure_DeletesItsOwnLogins_KeepsTheOwner_AndLogsAsACompany()
    {
        var f = new Fixture();
        var owner = new User { Id = Guid.NewGuid(), Email = "owner@acme.ph", DefaultTenantId = Company };
        var staff = new User { Id = Guid.NewGuid(), Email = "staff@test.ph", DefaultTenantId = Company };
        f.Db.Users.AddRange(owner, staff);
        f.Db.RefreshTokens.AddRange(
            new RefreshToken { Id = Guid.NewGuid(), RefreshTokenHash = "h", UserId = owner.Id, TenantId = Company },
            new RefreshToken { Id = Guid.NewGuid(), RefreshTokenHash = "h", UserId = owner.Id, TenantId = OtherCompany });
        await f.Db.SaveChangesAsync();
        var message = new CompanyHardDeleted
        {
            RequestId = Guid.NewGuid(), OwnerUserId = owner.Id, OwnerEmail = "owner@acme.ph", TenantId = Company, CompanyName = "Acme Test",
            DeleteUserIds = [staff.Id], DetachUserIds = [owner.Id],
            RequestedBy = "platform:alice@onepunch.test", ApprovedBy = "platform:bob@onepunch.test", Reason = "Test company",
        };

        await f.ConsumeAsync(message);
        await f.ConsumeAsync(message);

        f.Db.ChangeTracker.Clear();
        var kept = await f.Db.Users.SingleAsync();
        kept.Id.Should().Be(owner.Id);
        kept.DefaultTenantId.Should().BeNull("the erased company was the default");
        (await f.Db.RefreshTokens.Select(t => t.TenantId).ToListAsync()).Should().Equal(OtherCompany);
        var line = await f.Platform.PlatformAdminAudits.SingleAsync();
        line.Action.Should().Be(AccountEraserService.CompanyHardDeletedAction);
        line.TargetId.Should().Be(message.RequestId);
        line.Detail.Should().Contain("Acme Test");
    }

    [Fact]
    public async Task ALongListOfKeptLogins_IsShortenedToFit_AndTheFactsSurvive()
    {
        var f = new Fixture();
        var owner = new User { Id = Guid.NewGuid(), Email = "owner@acme.ph" };
        var shared = Enumerable.Range(0, 40).Select(i => new User { Id = Guid.NewGuid(), Email = $"person{i:00}@another-company.ph" }).ToList();
        f.Db.Users.Add(owner);
        f.Db.Users.AddRange(shared);
        await f.Db.SaveChangesAsync();

        await f.ConsumeAsync(Message(owner.Id, [owner.Id], shared.Select(u => u.Id)));

        var detail = (await f.Platform.PlatformAdminAudits.SingleAsync()).Detail!;
        detail.Length.Should().BeLessThanOrEqualTo(500);
        detail.Should().Contain("Reason: Test account").And.Contain("kept: 40").And.Contain("person00@another-company.ph").And.MatchRegex(@"\+\d+ more\.$");
    }
}
