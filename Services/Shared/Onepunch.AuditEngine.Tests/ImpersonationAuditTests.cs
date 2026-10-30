using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Onepunch.AuditEngine.AspNetCore;
using Xunit;

namespace Onepunch.AuditEngine.Tests;

public class ImpersonationAuditTests
{
    private static readonly ClaimsAuditActorOptions Options = new();

    private static DefaultHttpContext Context(string method, string path, bool impersonating, string? operatorRole = null)
    {
        var claims = new List<Claim> { new("sub", "user-1"), new("email", "ann@acme.test"), new("tenantId", "t-1") };
        if (impersonating)
        {
            claims.Add(new Claim("act_sub", "operator-1"));
            claims.Add(new Claim("act_email", "ops@platform.test"));
            claims.Add(new Claim("imp_sid", "session-1"));
            if (operatorRole != null) claims.Add(new Claim("act_role", operatorRole));
        }

        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")) };
        context.Request.Method = method;
        context.Request.Path = path;
        return context;
    }

    private static ImpersonationRequestAuditMiddleware Middleware(RequestDelegate next) => new(next, Options);

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task EveryImpersonatedRequest_IsRecordedAndCommitted_BeforeItRuns(string method)
    {
        var writer = new Mock<IAuditWriter>();
        var committer = new Mock<IAuditCommitter>();
        var order = new List<string>();
        writer.Setup(w => w.WriteAsync(It.IsAny<AuditEvent>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("write")).Returns(Task.CompletedTask);
        committer.Setup(c => c.CommitAsync(It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("commit")).Returns(Task.CompletedTask);
        var middleware = Middleware(_ => { order.Add("request"); return Task.CompletedTask; });

        await middleware.InvokeAsync(Context(method, "/api/v1/employees", true), writer.Object, committer.Object,
            NullLogger<ImpersonationRequestAuditMiddleware>.Instance);

        order.Should().Equal("write", "commit", "request");
        writer.Verify(w => w.WriteAsync(
            It.Is<AuditEvent>(e => e.EventType == ImpersonationRequestAuditMiddleware.EventType
                && e.Description == $"{method} /api/v1/employees"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ARequestThatCannotBeRecorded_IsRefused()
    {
        var writer = new Mock<IAuditWriter>();
        var committer = new Mock<IAuditCommitter>();
        committer.Setup(c => c.CommitAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db down"));
        var reached = false;
        var middleware = Middleware(_ => { reached = true; return Task.CompletedTask; });
        var context = Context("POST", "/api/v1/payrolls", true);

        await middleware.InvokeAsync(context, writer.Object, committer.Object, NullLogger<ImpersonationRequestAuditMiddleware>.Instance);

        reached.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
    }

    [Fact]
    public async Task ARegularRequest_IsNotRecordedByThisMiddleware()
    {
        var writer = new Mock<IAuditWriter>();
        var reached = false;
        var middleware = Middleware(_ => { reached = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(Context("GET", "/api/v1/employees", false), writer.Object, Mock.Of<IAuditCommitter>(),
            NullLogger<ImpersonationRequestAuditMiddleware>.Instance);

        reached.Should().BeTrue();
        writer.Verify(w => w.WriteAsync(It.IsAny<AuditEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void TheActorOfAnImpersonatedRequest_IsTheOperator_AndTheUserTheyActedAsIsInMetadata()
    {
        var context = Context("GET", "/api/v1/employees", true);
        var accessor = new HttpContextAccessor { HttpContext = context };
        var resolver = new ClaimsPrincipalAuditActorResolver(accessor, new AuditEngineOptions(), Options, Mock.Of<IServiceProvider>());
        var metadata = new Dictionary<string, string?>();

        var actor = resolver.ResolveActor();
        new HttpContextAuditMetadataEnricher(accessor, Options).Enrich(metadata);

        actor.Id.Should().Be("operator-1");
        actor.DisplayName.Should().Be("ops@platform.test");
        actor.Type.Should().Be(AuditActorTypes.Operator);
        metadata[HttpContextAuditMetadataEnricher.OnBehalfOfUserId].Should().Be("user-1");
        metadata[HttpContextAuditMetadataEnricher.OnBehalfOfName].Should().Be("ann@acme.test");
        metadata[HttpContextAuditMetadataEnricher.ImpersonationSessionId].Should().Be("session-1");
    }

    [Fact]
    public async Task ASuperAdminSession_IsNotRecordedByThisMiddleware_ButStillRuns()
    {
        var writer = new Mock<IAuditWriter>();
        var committer = new Mock<IAuditCommitter>();
        var reached = false;
        var middleware = Middleware(_ => { reached = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(Context("POST", "/api/v1/employees", true, "SuperAdmin"), writer.Object, committer.Object,
            NullLogger<ImpersonationRequestAuditMiddleware>.Instance);

        reached.Should().BeTrue();
        writer.Verify(w => w.WriteAsync(It.IsAny<AuditEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        committer.Verify(c => c.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ASupportSession_IsStillRecorded()
    {
        var writer = new Mock<IAuditWriter>();
        var middleware = Middleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(Context("GET", "/api/v1/employees", true, "Support"), writer.Object, Mock.Of<IAuditCommitter>(),
            NullLogger<ImpersonationRequestAuditMiddleware>.Instance);

        writer.Verify(w => w.WriteAsync(It.IsAny<AuditEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(true, "SuperAdmin", true)]
    [InlineData(true, "superadmin", true)]
    [InlineData(true, "Support", false)]
    [InlineData(true, null, false)]
    [InlineData(false, null, false)]
    public void OnlyASuperAdminImpersonationSession_IsSuppressed(bool impersonating, string? role, bool suppressed)
    {
        var accessor = new HttpContextAccessor { HttpContext = Context("POST", "/api/v1/employees", impersonating, role) };
        var resolver = new ClaimsPrincipalAuditActorResolver(accessor, new AuditEngineOptions(), Options, Mock.Of<IServiceProvider>());

        resolver.IsAuditSuppressed().Should().Be(suppressed);
    }

    [Fact]
    public void ARegularUser_IsTheirOwnActor_WithNoOnBehalfOfMetadata()
    {
        var context = Context("GET", "/api/v1/employees", false);
        var accessor = new HttpContextAccessor { HttpContext = context };
        var resolver = new ClaimsPrincipalAuditActorResolver(accessor, new AuditEngineOptions(), Options, Mock.Of<IServiceProvider>());
        var metadata = new Dictionary<string, string?>();

        var actor = resolver.ResolveActor();
        new HttpContextAuditMetadataEnricher(accessor, Options).Enrich(metadata);

        actor.Id.Should().Be("user-1");
        actor.Type.Should().Be(AuditActorTypes.User);
        metadata.Should().NotContainKey(HttpContextAuditMetadataEnricher.OnBehalfOfUserId);
    }
}
