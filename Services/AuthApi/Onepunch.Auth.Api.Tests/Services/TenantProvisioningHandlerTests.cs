using FluentAssertions;
using Grpc.Core;
using Onepunch.Auth.Api.Tests.TestSupport;
using Onepunch.Auth.Core;
using Onepunch.Auth.Core.Protos;
using Onepunch.Auth.Domain.Entities;
using Xunit;

namespace Onepunch.Auth.Api.Tests.Services;

/// <summary>
/// Tenant Service asks which companies finished setting up (HrDbReady) to mark existing ones
/// activated; only those come back, with the time they became ready.
/// </summary>
public class TenantProvisioningHandlerTests
{
    private sealed class TestCallContext : ServerCallContext
    {
        protected override string MethodCore => "GetReadyTenants";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "test";
        protected override DateTime DeadlineCore => DateTime.MaxValue;
        protected override Metadata RequestHeadersCore => new();
        protected override CancellationToken CancellationTokenCore => CancellationToken.None;
        protected override Metadata ResponseTrailersCore => new();
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override Grpc.Core.AuthContext AuthContextCore => new(null, new Dictionary<string, List<AuthProperty>>());
        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) => throw new NotSupportedException();
        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
    }

    [Fact]
    public async Task ReturnsOnlyCompaniesWhoseSetupFinished()
    {
        var context = AuthTestContextFactory.CreateContext();
        var ready = Guid.NewGuid();
        var failed = Guid.NewGuid();
        var readyAt = new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc);
        context.TenantCreationRequests.AddRange(
            new TenantCreationRequestStatus { Id = Guid.NewGuid(), TenantId = ready, HrDbReady = true, CreatedAt = readyAt.AddMinutes(-5), UpdatedAt = readyAt },
            new TenantCreationRequestStatus { Id = Guid.NewGuid(), TenantId = failed, HrDbReady = false, CreatedAt = readyAt });
        await context.SaveChangesAsync();

        var request = new ReadyTenantsRequest();
        request.TenantIds.AddRange([ready.ToString(), failed.ToString(), Guid.NewGuid().ToString(), "not-a-guid"]);
        var response = await new TenantProvisioningHandler(context).GetReadyTenants(request, new TestCallContext());

        response.Tenants.Should().ContainSingle();
        response.Tenants[0].TenantId.Should().Be(ready.ToString());
        DateTime.Parse(response.Tenants[0].ReadyAtUtc).ToUniversalTime().Should().Be(readyAt);
    }

    [Fact]
    public async Task NoIds_NoQuery()
    {
        var response = await new TenantProvisioningHandler(AuthTestContextFactory.CreateContext())
            .GetReadyTenants(new ReadyTenantsRequest(), new TestCallContext());

        response.Tenants.Should().BeEmpty();
    }
}
