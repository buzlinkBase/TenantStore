using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Moq;
using OnePunch.Auth.Domain.Entities;

namespace Onepunch.Auth.Api.Tests.TestSupport;

public static class IdentityMocks
{
    public static Mock<UserManager<User>> MockUserManager()
    {
        var store = new Mock<IUserStore<User>>();
        return new Mock<UserManager<User>>(store.Object, null, null, null, null, null, null, null, null);
    }

    /// <summary>
    /// Overload that wires a real IOptions&lt;IdentityOptions&gt; instead of null, so
    /// UserManager's own (virtual, non-mocked-through) methods -- GetUserId/GetUserAsync --
    /// exercise the actual ClaimsIdentityOptions.UserIdClaimType lookup instead of Identity's
    /// built-in default.
    /// </summary>
    public static Mock<UserManager<User>> MockUserManager(IOptions<IdentityOptions> options)
    {
        var store = new Mock<IUserStore<User>>();
        return new Mock<UserManager<User>>(store.Object, options, null, null, null, null, null, null, null);
    }

    public static Mock<RoleManager<Role>> MockRoleManager()
    {
        var store = new Mock<IRoleStore<Role>>();
        return new Mock<RoleManager<Role>>(store.Object, null, null, null, null);
    }

    public static Mock<SignInManager<User>> MockSignInManager(UserManager<User> userManager)
    {
        var contextAccessor = new Mock<IHttpContextAccessor>();
        var claimsFactory = new Mock<IUserClaimsPrincipalFactory<User>>();
        return new Mock<SignInManager<User>>(userManager, contextAccessor.Object, claimsFactory.Object, null, null, null, null);
    }
}
