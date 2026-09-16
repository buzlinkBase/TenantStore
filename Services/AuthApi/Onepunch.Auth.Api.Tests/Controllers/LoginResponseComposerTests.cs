using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Onepunch.Auth.Domain.DTOs;
using OnePunch.Auth.Api.Controllers;
using Xunit;

namespace Onepunch.Auth.Api.Tests.Controllers;

/// <summary>
/// LoginResponseComposer.ConvertLoginResponse -- the internal LoginResponse -> external
/// LoginResponseSimple DTO mapping shared by every login-shaped endpoint (Login, Refresh, Google
/// login/signup, tenant-switch, invitation-accept, workspace-creation). Regression guard for the
/// bug where Permissions was never copied through, so every one of those endpoints silently sent
/// an empty permissions array to the frontend regardless of what UserService.ComposeLoginResponse
/// actually computed.
/// </summary>
public class LoginResponseComposerTests
{
    private static LoginResponse BuildResponse() => new()
    {
        AccessToken = "access-token",
        RefreshToken = "refresh-token",
        ErrorMessage = string.Empty,
        Expiry = DateTime.UtcNow.AddHours(1),
        Name = "Jane Owner",
        Email = "jane@example.com",
        Roles = ["Owner"],
        Permissions = ["Leave:View", "Leave:Approve", "Approval Workflows:Edit"],
        Tenants =
        [
            new UsersTenant
            {
                TenantId = Guid.NewGuid(),
                Name = "Acme Workspace",
                Roles = ["Owner"],
                Permissions = ["Leave:View", "Leave:Approve", "Approval Workflows:Edit"],
                State = "Created",
                HrDbReady = true,
            },
        ],
    };

    private static HttpResponse BuildHttpResponse() => new DefaultHttpContext().Response;

    [Fact]
    public void ConvertLoginResponse_CopiesPermissionsThrough()
    {
        var response = BuildResponse();

        var result = LoginResponseComposer.ConvertLoginResponse(response, 7, isHttps: true, BuildHttpResponse());

        result.Permissions.Should().BeEquivalentTo(response.Permissions);
        result.Permissions.Should().NotBeEmpty();
    }

    [Fact]
    public void ConvertLoginResponse_CopiesRolesAndOtherFieldsThrough()
    {
        var response = BuildResponse();

        var result = LoginResponseComposer.ConvertLoginResponse(response, 7, isHttps: true, BuildHttpResponse());

        result.AccessToken.Should().Be(response.AccessToken);
        result.ErrorMessage.Should().Be(response.ErrorMessage);
        result.Expiry.Should().Be(response.Expiry);
        result.Name.Should().Be(response.Name);
        result.Email.Should().Be(response.Email);
        result.Roles.Should().BeEquivalentTo(response.Roles);
        result.Tenants.Should().BeEquivalentTo(response.Tenants);
    }
}
