using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using OnePunch.Auth.Api.Authorization;
using Xunit;

namespace Onepunch.Auth.Api.Tests.Authorization;

/// <summary>
/// Auth's internal gRPC services let in a signed-in user (as before) or another service holding the
/// shared key -- Tenant Service's background and Platform Console calls carry no user and got 401.
/// </summary>
public class InternalServiceAuthorizationTests
{
    private const string Key = "F03E3D60-SERVICE-KEY";

    private static async Task<bool> AllowedAsync(ClaimsPrincipal user, string? presentedKey, string? configuredKey = Key)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ApiKeySettings:ApiKey"] = configuredKey })
            .Build();
        var http = new DefaultHttpContext { User = user };
        if (presentedKey != null) http.Request.Headers[InternalServiceAuthorization.KeyHeader] = presentedKey;

        var requirement = new InternalServiceRequirement();
        var context = new AuthorizationHandlerContext([requirement], user, http);
        await new InternalServiceHandler(config, new HttpContextAccessor()).HandleAsync(context);
        return context.HasSucceeded;
    }

    private static ClaimsPrincipal Anonymous => new(new ClaimsIdentity());
    private static ClaimsPrincipal SignedIn => new(new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())], "Bearer"));

    [Fact]
    public async Task ASignedInUser_IsLetIn_AsBefore() =>
        (await AllowedAsync(SignedIn, presentedKey: null)).Should().BeTrue();

    [Fact]
    public async Task AServiceWithTheSharedKey_IsLetIn_WithoutAUser() =>
        (await AllowedAsync(Anonymous, Key)).Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong-key")]
    public async Task NoUserAndNoValidKey_IsRefused(string? presented) =>
        (await AllowedAsync(Anonymous, presented)).Should().BeFalse();

    [Fact]
    public async Task AnUnconfiguredKey_NeverMatches_EvenAnEmptyOne() =>
        (await AllowedAsync(Anonymous, "", configuredKey: null)).Should().BeFalse();
}
