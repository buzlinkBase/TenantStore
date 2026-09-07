using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using TenantStoreApi.Core.Extensions;
using Xunit;

namespace TenantStoreApi.Tests;

public class HttpRequestExtensionsTests
{
    private static HttpRequest CreateRequest(string? headerName = null, string? headerValue = null)
    {
        var context = new DefaultHttpContext();
        if (headerName is not null)
        {
            context.Request.Headers[headerName] = headerValue;
        }
        return context.Request;
    }

    [Fact]
    public void GetHeader_ReturnsValue_WhenHeaderPresent()
    {
        var request = CreateRequest("X-Custom", "hello");

        request.GetHeader("X-Custom").Should().Be("hello");
    }

    [Fact]
    public void GetHeader_ReturnsNull_WhenHeaderMissing()
    {
        var request = CreateRequest();

        request.GetHeader("X-Missing").Should().BeNull();
    }

    [Theory]
    [InlineData("Bearer abc123", "abc123")]
    [InlineData("bearer abc123", "abc123")]
    [InlineData("Bearer   abc123  ", "abc123")]
    public void GetAuthorizationToken_ExtractsToken_WhenBearerScheme(string header, string expected)
    {
        var request = CreateRequest("Authorization", header);

        request.GetAuthorizationToken().Should().Be(expected);
    }

    [Theory]
    [InlineData("Basic abc123")]
    [InlineData(null)]
    public void GetAuthorizationToken_ReturnsNull_WhenNotBearerScheme(string? header)
    {
        var request = header is null ? CreateRequest() : CreateRequest("Authorization", header);

        request.GetAuthorizationToken().Should().BeNull();
    }

    [Fact]
    public void GetUserId_ReturnsGuid_WhenClaimIsValid()
    {
        var userId = Guid.NewGuid();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())]));

        principal.GetUserId().Should().Be(userId);
    }

    [Fact]
    public void GetUserId_ReturnsNull_WhenClaimMissing()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        principal.GetUserId().Should().BeNull();
    }

    [Fact]
    public void GetUserId_Throws_WhenUserIsNull()
    {
        ClaimsPrincipal? principal = null;

        var act = () => principal!.GetUserId();

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetRequiredUserId_ReturnsGuid_WhenClaimIsValid()
    {
        var userId = Guid.NewGuid();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())]));

        principal.GetRequiredUserId().Should().Be(userId);
    }

    [Fact]
    public void GetRequiredUserId_Throws_WhenClaimMissing()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        var act = () => principal.GetRequiredUserId();

        act.Should().Throw<UnauthorizedAccessException>();
    }

    [Fact]
    public void GetUserClaim_ReturnsValue_WhenPresent()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("tenantId", "tenant-123")]));

        principal.GetUserClaim("tenantId").Should().Be("tenant-123");
    }

    [Fact]
    public void GetUserClaim_Throws_WhenClaimNameIsBlank()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        var act = () => principal.GetUserClaim("  ");

        act.Should().Throw<ArgumentException>();
    }
}
