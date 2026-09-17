using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Onepunch.Auth.Api.Tests.TestSupport;
using OnePunch.Auth.Domain.Entities;
using System.Security.Claims;
using Xunit;

namespace Onepunch.Auth.Api.Tests.Services;

/// <summary>
/// Regression coverage for the ServiceRegistrations.cs fix: ASP.NET Core Identity's own
/// ClaimsPrincipal reads (UserManager.GetUserAsync(User), used by WorkspaceService.Create,
/// UserService.Profile, InvitationService) default to ClaimTypes.NameIdentifier independently
/// of our own GetRequiredUserId() extension. MapInboundClaims = false (AddJwtBearer) means a
/// JWT-derived principal now carries "sub" directly instead of the remapped NameIdentifier URI --
/// without also setting IdentityOptions.ClaimsIdentity.UserIdClaimType = "sub", every
/// UserManager.GetUserAsync(User) call in AuthApi silently returned null for a real, valid token.
/// </summary>
public class IdentityClaimMappingTests
{
    private static ClaimsPrincipal SubOnlyPrincipal(Guid userId) =>
        new(new ClaimsIdentity([new Claim("sub", userId.ToString())]));

    [Fact]
    public void GetUserId_ResolvesFromSubClaim_WhenUserIdClaimTypeIsConfigured()
    {
        var userManager = IdentityMocks.MockUserManager(
            Options.Create(new IdentityOptions
            {
                ClaimsIdentity = { UserIdClaimType = "sub" }
            }));
        userManager.CallBase = true; // exercise the real (non-mocked) GetUserId implementation
        var userId = Guid.NewGuid();

        var resolved = userManager.Object.GetUserId(SubOnlyPrincipal(userId));

        resolved.Should().Be(userId.ToString());
    }

    [Fact]
    public void GetUserId_ReturnsNull_WhenUserIdClaimTypeIsLeftAtIdentitysDefault()
    {
        // Documents the exact regression: the default ClaimsIdentityOptions (ClaimTypes.NameIdentifier)
        // can't see a "sub"-only principal, which is what every AddJwtBearer token now produces.
        var userManager = IdentityMocks.MockUserManager(Options.Create(new IdentityOptions()));
        userManager.CallBase = true; // exercise the real (non-mocked) GetUserId implementation
        var userId = Guid.NewGuid();

        var resolved = userManager.Object.GetUserId(SubOnlyPrincipal(userId));

        resolved.Should().BeNull();
    }
}
