using BuzlinkRepository;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Onepunch.AuditEngine;
using Onepunch.AuditEngine.MassTransit;
using TenantStoreApi.Core;
using TenantStoreApi.Core.Auditing;
using TenantStoreApi.Core.Services;
using TenantStoreApi.Core.Messaging;
using TenantStoreApi.Domain.Entities;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Infrastructure;
using Xunit;

namespace TenantStoreApi.Tests.Auditing;

/// <summary>
/// TenantApi's audit trail, wired exactly as the service registers it (AddTenantAuditTrail):
/// role/membership changes are recorded with their company and forwarded to hrms-api.
/// </summary>
public class TenantAuditTrailTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    private sealed class Host : IDisposable
    {
        private readonly ServiceProvider _root;
        public List<(AuditEventRecorded Message, string? CompanyHeader)> Forwarded { get; } = [];

        public Host(bool enabled = true)
        {
            var tenantProvider = new Mock<ITenantProvider>();
            tenantProvider.SetupGet(x => x.TenantId).Returns(CompanyId);

            var publisher = new Mock<IPublishEndpoint>();
            publisher
                .Setup(p => p.Publish(It.IsAny<AuditEventRecorded>(), It.IsAny<IPipe<PublishContext<AuditEventRecorded>>>(), It.IsAny<CancellationToken>()))
                .Returns(async (AuditEventRecorded message, IPipe<PublishContext<AuditEventRecorded>> pipe, CancellationToken _) =>
                {
                    string? header = null;
                    var headers = new Mock<SendHeaders>();
                    headers.Setup(h => h.Set(It.IsAny<string>(), It.IsAny<string>()))
                        .Callback((string key, string value) => { if (key == "X-Tenant-ID") header = value; });
                    var context = new Mock<PublishContext<AuditEventRecorded>>();
                    context.SetupGet(c => c.Headers).Returns(headers.Object);
                    await pipe.Send(context.Object);
                    Forwarded.Add((message, header));
                });

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["AuditEngine:Enabled"] = enabled.ToString() })
                .Build();

            var databaseName = Guid.NewGuid().ToString();
            var services = new ServiceCollection();
            services.AddScoped(_ => tenantProvider.Object);
            services.AddScoped(_ => publisher.Object);
            services.AddDbContext<TenantContext>(o => o
                .UseInMemoryDatabase(databaseName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
            services.AddTenantAuditTrail(configuration);
            _root = services.BuildServiceProvider();
        }

        public async Task SaveAsync(Action<TenantContext> change)
        {
            using var scope = _root.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TenantContext>();
            change(db);
            await db.SaveChangesAsync();
        }

        public async Task InScopeAsync(Func<IServiceProvider, Task> action)
        {
            using var scope = _root.CreateScope();
            await action(scope.ServiceProvider);
        }

        public async Task<List<AuditEvent>> EventsAsync()
        {
            using var scope = _root.CreateScope();
            var page = await scope.ServiceProvider.GetRequiredService<IAuditQueryService>()
                .QueryAsync(new AuditQuery { PageSize = 100 });
            return page.Items.ToList();
        }

        public void Dispose() => _root.Dispose();
    }

    [Fact]
    public async Task GrantingARole_IsRecordedForTheCompany_AndForwardedToHrms()
    {
        using var host = new Host();
        var membershipId = Guid.NewGuid();
        await host.SaveAsync(db => db.Memberships.Add(new UserMembership { Id = membershipId, TenantId = CompanyId, UserId = Guid.NewGuid() }));

        await host.SaveAsync(db => db.MembershipRoles.Add(new MembershipRole { Id = Guid.NewGuid(), UserMembershipId = membershipId, Role = "Admin" }));

        var grant = (await host.EventsAsync()).Should().ContainSingle(e => e.EntityType == nameof(MembershipRole)).Subject;
        grant.EventType.Should().Be(AuditEventTypes.Create);
        grant.OrganizationId.Should().Be(CompanyId.ToString());
        grant.Source.Should().Be(TenantAuditRegistration.SourceName);
        grant.Changes.Should().Contain(c => c.Property == nameof(MembershipRole.Role) && c.NewValue == "Admin");

        host.Forwarded.Should().Contain(f => f.Message.Id == grant.Id && f.CompanyHeader == CompanyId.ToString());
    }

    [Fact]
    public async Task EditingARolesPermissions_IsOneEventOnTheRole_NamingEachPermission()
    {
        using var host = new Host();
        var roleId = Guid.NewGuid();
        Permission Perm(string code) => new() { Id = Guid.NewGuid(), Code = code, Feature = code.Split(':')[0], Action = code.Split(':')[1] };
        var (view, approve, export) = (Perm("Payroll Run:View"), Perm("Payroll Run:Approve"), Perm("Payroll Run:Export"));
        await host.SaveAsync(db =>
        {
            db.Permissions.AddRange(view, approve, export);
            db.Roles.Add(new Role { Id = roleId, Name = "Payroll Clerk", TenantId = CompanyId });
            db.RolePermissions.AddRange(
                new RolePermission { Id = Guid.NewGuid(), RoleId = roleId, PermissionId = view.Id },
                new RolePermission { Id = Guid.NewGuid(), RoleId = roleId, PermissionId = approve.Id });
        });
        var before = (await host.EventsAsync()).Select(e => e.Id).ToHashSet();

        // Keep View, drop Approve, add Export.
        await host.InScopeAsync(sp => new RoleService(
                new UnitOfWorkService(sp.GetRequiredService<TenantContext>()), null, sp.GetRequiredService<IAuditWriter>())
            .SetPermissionsAsync(roleId, [view.Id, export.Id], CancellationToken.None));

        var events = (await host.EventsAsync()).Where(e => !before.Contains(e.Id)).ToList();
        events.Should().NotContain(e => e.EntityType == nameof(RolePermission), "a bare row id tells nobody what changed");
        var edit = events.Should().ContainSingle().Subject;
        edit.EventType.Should().Be(AuditEventTypes.Update);
        edit.EntityType.Should().Be(nameof(Role));
        edit.EntityId.Should().Be(roleId.ToString());
        edit.OrganizationId.Should().Be(CompanyId.ToString());
        edit.Description.Should().Contain("Payroll Clerk").And.Contain("1 granted, 1 removed");
        edit.Changes.Should().BeEquivalentTo([
            new AuditChange("Payroll Run:Approve", RoleService.PermissionGranted, RoleService.PermissionNotGranted),
            new AuditChange("Payroll Run:Export", RoleService.PermissionNotGranted, RoleService.PermissionGranted),
        ]);
        host.Forwarded.Should().Contain(f => f.Message.Id == edit.Id);
    }

    [Fact]
    public async Task SavingUnchangedPermissions_RecordsNothing()
    {
        using var host = new Host();
        var roleId = Guid.NewGuid();
        var view = new Permission { Id = Guid.NewGuid(), Code = "Payroll Run:View", Feature = "Payroll Run", Action = "View" };
        await host.SaveAsync(db =>
        {
            db.Permissions.Add(view);
            db.Roles.Add(new Role { Id = roleId, Name = "Payroll Clerk", TenantId = CompanyId });
            db.RolePermissions.Add(new RolePermission { Id = Guid.NewGuid(), RoleId = roleId, PermissionId = view.Id });
        });
        var before = (await host.EventsAsync()).Count;

        await host.InScopeAsync(sp => new RoleService(
                new UnitOfWorkService(sp.GetRequiredService<TenantContext>()), null, sp.GetRequiredService<IAuditWriter>())
            .SetPermissionsAsync(roleId, [view.Id], CancellationToken.None));

        (await host.EventsAsync()).Should().HaveCount(before);
    }

    [Fact]
    public async Task UnauditedTables_AreNotRecorded()
    {
        using var host = new Host();

        await host.SaveAsync(db => db.Plans.Add(new Plan { Id = Guid.NewGuid(), Name = "Pro", Description = "Pro plan" }));

        (await host.EventsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Disabled_RecordsAndForwardsNothing()
    {
        using var host = new Host(enabled: false);

        await host.SaveAsync(db => db.Roles.Add(new Role { Id = Guid.NewGuid(), Name = "Billing", TenantId = CompanyId }));

        (await host.EventsAsync()).Should().BeEmpty();
        host.Forwarded.Should().BeEmpty();
    }
}

/// <summary>A message that already names its company keeps it (e.g. a forwarded audit event).</summary>
public class TenantPublishFilterTests
{
    private static async Task<string?> RunAsync(Guid providerTenant, string? existingHeader)
    {
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.SetupGet(x => x.TenantId).Returns(providerTenant);

        string? stamped = null;
        var headers = new Mock<SendHeaders>();
        object? existing = existingHeader;
        headers.Setup(h => h.TryGetHeader("X-Tenant-ID", out existing)).Returns(existingHeader != null);
        headers.Setup(h => h.Set("X-Tenant-ID", It.IsAny<string>())).Callback((string _, string value) => stamped = value);
        var context = new Mock<PublishContext<AuditEventRecorded>>();
        context.SetupGet(c => c.Headers).Returns(headers.Object);

        await new TenantPublishFilter<AuditEventRecorded>(tenantProvider.Object)
            .Send(context.Object, Mock.Of<IPipe<PublishContext<AuditEventRecorded>>>());
        return stamped;
    }

    [Fact]
    public async Task KeepsACompanyTheMessageAlreadyCarries()
    {
        (await RunAsync(Guid.Empty, CompanyHeader)).Should().BeNull();
    }

    [Fact]
    public async Task StampsTheCurrentCompany_WhenTheMessageHasNone()
    {
        var tenantId = Guid.NewGuid();

        (await RunAsync(tenantId, existingHeader: null)).Should().Be(tenantId.ToString());
    }

    private static readonly string CompanyHeader = Guid.NewGuid().ToString();
}
