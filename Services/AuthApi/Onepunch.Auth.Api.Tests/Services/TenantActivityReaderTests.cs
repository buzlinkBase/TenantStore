using FluentAssertions;
using Onepunch.Auth.Api.Tests.TestSupport;
using Onepunch.Auth.Core.Activity;
using Onepunch.Auth.Domain.Entities;
using Xunit;

namespace Onepunch.Auth.Api.Tests.Services;

/// <summary>"Last active" for the Platform Console: the newest HRIS session of each company.</summary>
public class TenantActivityReaderTests
{
    private static readonly DateTime T0 = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);

    private static RefreshToken Session(Guid? tenantId, DateTime at, string? audience = null) => new()
    {
        Id = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        RefreshTokenHash = Guid.NewGuid().ToString(),
        Expiry = at.AddDays(30),
        CreatedAt = at,
        TenantId = tenantId,
        Audience = audience,
    };

    [Fact]
    public async Task ReturnsTheNewestSessionPerCompany()
    {
        var context = AuthTestContextFactory.CreateContext();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        context.RefreshTokens.AddRange(Session(a, T0), Session(a, T0.AddDays(3)), Session(b, T0.AddDays(1)));
        await context.SaveChangesAsync();

        var result = await new TenantActivityReader(context).ReadLastActiveAsync([a, b], CancellationToken.None);

        result[a].Should().Be(T0.AddDays(3));
        result[b].Should().Be(T0.AddDays(1));
    }

    [Fact]
    public async Task IgnoresClientPortalSessions()
    {
        var context = AuthTestContextFactory.CreateContext();
        var a = Guid.NewGuid();
        context.RefreshTokens.AddRange(Session(a, T0), Session(a, T0.AddDays(5), RefreshToken.PortalAudience));
        await context.SaveChangesAsync();

        var result = await new TenantActivityReader(context).ReadLastActiveAsync([a], CancellationToken.None);

        result[a].Should().Be(T0);
    }

    [Fact]
    public async Task LeavesOutCompaniesNobodySignedInToAndCompaniesNotAsked()
    {
        var context = AuthTestContextFactory.CreateContext();
        var asked = Guid.NewGuid();
        var other = Guid.NewGuid();
        context.RefreshTokens.AddRange(Session(other, T0), Session(null, T0));
        await context.SaveChangesAsync();

        var result = await new TenantActivityReader(context).ReadLastActiveAsync([asked], CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task NoIds_ReturnsNothing()
    {
        var context = AuthTestContextFactory.CreateContext();

        var result = await new TenantActivityReader(context).ReadLastActiveAsync([], CancellationToken.None);

        result.Should().BeEmpty();
    }
}
