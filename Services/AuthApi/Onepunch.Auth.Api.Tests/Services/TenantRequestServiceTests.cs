using FluentAssertions;
using Onepunch.Auth.Api.Tests.TestSupport;
using Onepunch.Auth.Core.Services;
using Onepunch.Auth.Domain.Entities;
using Onepunch.Auth.Infrastructure;
using Xunit;

namespace Onepunch.Auth.Api.Tests.Services;

/// <summary>
/// TenantRequestService.FindPendingByUser -- the query WorkspaceService.Create uses to resume
/// an already-in-flight tenant creation instead of firing another TenantCreationRequested. A
/// page refresh during "Setting up workspace..." used to leave a Provisioning row behind and,
/// on resubmit, start a second one from scratch; this is the lookup that stops that.
/// </summary>
public class TenantRequestServiceTests
{
    private static (TenantRequestService Sut, AuthContext Context) CreateSut()
    {
        var context = AuthTestContextFactory.CreateContext();
        var uow = AuthTestContextFactory.CreateUnitOfWork(context);
        return (new TenantRequestService(uow, null!), context);
    }

    [Fact]
    public async Task FindPendingByUser_ReturnsProvisioningRequest_ForSameUser()
    {
        var (sut, context) = CreateSut();
        var userId = Guid.NewGuid();
        var request = new TenantCreationRequestStatus
        {
            TenantId = Guid.NewGuid(),
            UserId = userId,
            TenantName = "Acme",
            Status = TenantCreationStatus.Provisioning,
            RequestExpiry = DateTime.UtcNow.AddDays(1),
        };
        context.TenantCreationRequests.Add(request);
        await context.SaveChangesAsync();

        var result = await sut.FindPendingByUser(userId);

        result.Should().NotBeNull();
        result!.TenantId.Should().Be(request.TenantId);
    }

    [Fact]
    public async Task FindPendingByUser_ReturnsNull_WhenAlreadyCreated()
    {
        var (sut, context) = CreateSut();
        var userId = Guid.NewGuid();
        context.TenantCreationRequests.Add(new TenantCreationRequestStatus
        {
            TenantId = Guid.NewGuid(),
            UserId = userId,
            TenantName = "Acme",
            Status = TenantCreationStatus.Created,
            RequestExpiry = DateTime.UtcNow.AddDays(1),
        });
        await context.SaveChangesAsync();

        var result = await sut.FindPendingByUser(userId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task FindPendingByUser_ReturnsNull_WhenExpired()
    {
        // A request stuck in Provisioning past its 1-day expiry must not keep blocking new
        // attempts forever -- InspectTenantRequestState treats the same expiry as "give up".
        var (sut, context) = CreateSut();
        var userId = Guid.NewGuid();
        context.TenantCreationRequests.Add(new TenantCreationRequestStatus
        {
            TenantId = Guid.NewGuid(),
            UserId = userId,
            TenantName = "Acme",
            Status = TenantCreationStatus.Provisioning,
            RequestExpiry = DateTime.UtcNow.AddDays(-1),
        });
        await context.SaveChangesAsync();

        var result = await sut.FindPendingByUser(userId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task FindPendingByUser_ReturnsNull_ForDifferentUser()
    {
        var (sut, context) = CreateSut();
        context.TenantCreationRequests.Add(new TenantCreationRequestStatus
        {
            TenantId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            TenantName = "Acme",
            Status = TenantCreationStatus.Provisioning,
            RequestExpiry = DateTime.UtcNow.AddDays(1),
        });
        await context.SaveChangesAsync();

        var result = await sut.FindPendingByUser(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task FindPendingByUser_ReturnsMostRecent_WhenMultiplePending()
    {
        var (sut, context) = CreateSut();
        var userId = Guid.NewGuid();
        var older = new TenantCreationRequestStatus
        {
            TenantId = Guid.NewGuid(),
            UserId = userId,
            TenantName = "Older",
            Status = TenantCreationStatus.Provisioning,
            RequestExpiry = DateTime.UtcNow.AddDays(1),
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
        };
        var newer = new TenantCreationRequestStatus
        {
            TenantId = Guid.NewGuid(),
            UserId = userId,
            TenantName = "Newer",
            Status = TenantCreationStatus.Provisioning,
            RequestExpiry = DateTime.UtcNow.AddDays(1),
            CreatedAt = DateTime.UtcNow,
        };
        context.TenantCreationRequests.AddRange(older, newer);
        await context.SaveChangesAsync();

        var result = await sut.FindPendingByUser(userId);

        result!.TenantName.Should().Be("Newer");
    }
}
