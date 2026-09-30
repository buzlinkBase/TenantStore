using FluentAssertions;
using MapsterMapper;
using Microsoft.AspNetCore.Identity;
using Moq;
using Onepunch.Auth.Api.Tests.TestSupport;
using Onepunch.Auth.Core.Services;
using Onepunch.Common.Lib;
using Onepunch.Common.Lib.Cache;
using Onepunch.Common.Lib.Exceptions;
using OnePunch.Auth.Core;
using OnePunch.Auth.Core.Services;
using OnePunch.Auth.Domain.DTOs;
using OnePunch.Auth.Domain.Entities;
using Xunit;

namespace Onepunch.Auth.Api.Tests.Services;

/// <summary>
/// A user's own password and profile changes must be committed: the unit of work holds an open
/// transaction and UserManager saves inside it, so without a commit the change is rolled back at
/// the end of the request while the caller is told it worked (a changed password then no longer
/// signs in, and the old one still does).
/// </summary>
public class UserServiceAccountUpdateTests
{
    private readonly Mock<UserManager<User>> _userManager = IdentityMocks.MockUserManager();
    private readonly Mock<IUnitOfWorkService> _uow = new();
    private readonly User _ana = new() { Id = Guid.NewGuid(), Email = "ana@acme.com", UserName = "ana@acme.com" };

    public UserServiceAccountUpdateTests()
    {
        _uow.Setup(u => u.CommitChangesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _userManager.Setup(m => m.FindByIdAsync(_ana.Id.ToString())).ReturnsAsync(_ana);
    }

    private UserService Sut() => new(
        _uow.Object,
        new MembershipCacheService(Mock.Of<ICacheService>(), null!, null!),
        null!, null!, null!,
        _userManager.Object,
        null!, null!,
        Mock.Of<IPasswordHasher<User>>(),
        new JwtService(null!, null!, Microsoft.Extensions.Options.Options.Create(new JwtSettings()), null!, null!),
        null!, null!, null!, null!, null!, null!,
        new NoOpMfaChallengeService(),
        Mock.Of<IMapper>(),
        null!);

    private static ChangePassword Change(string confirm = "N3w!Passw0rd") => new()
    {
        OldPassword = "0ld!Passw0rd",
        NewPassword = "N3w!Passw0rd",
        ConfirmPassword = confirm,
    };

    private void VerifyCommitted(Times times) =>
        _uow.Verify(u => u.CommitChangesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), times);

    [Fact]
    public async Task ChangedPassword_IsCommitted()
    {
        _userManager.Setup(m => m.ChangePasswordAsync(_ana, "0ld!Passw0rd", "N3w!Passw0rd")).ReturnsAsync(IdentityResult.Success);

        var result = await Sut().ChangePassword(_ana.Id.ToString(), Change(), CancellationToken.None);

        result!.Succeeded.Should().BeTrue();
        VerifyCommitted(Times.Once());
    }

    [Fact]
    public async Task WrongCurrentPassword_CommitsNothing()
    {
        _userManager.Setup(m => m.ChangePasswordAsync(_ana, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Incorrect password." }));

        var result = await Sut().ChangePassword(_ana.Id.ToString(), Change(), CancellationToken.None);

        result!.Succeeded.Should().BeFalse();
        VerifyCommitted(Times.Never());
    }

    [Fact]
    public async Task ChangePassword_ActsOnTheCaller_NotTheEmailInTheBody()
    {
        _userManager.Setup(m => m.ChangePasswordAsync(_ana, It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
        var payload = Change() with { Email = "someone-else@acme.com" };

        await Sut().ChangePassword(_ana.Id.ToString(), payload, CancellationToken.None);

        _userManager.Verify(m => m.FindByEmailAsync(It.IsAny<string>()), Times.Never);
        _userManager.Verify(m => m.ChangePasswordAsync(_ana, It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task MismatchedConfirmation_IsRejectedBeforeAnythingChanges()
    {
        var act = () => Sut().ChangePassword(_ana.Id.ToString(), Change(confirm: "different"), CancellationToken.None);

        await act.Should().ThrowAsync<GuardException>();
        _userManager.Verify(m => m.ChangePasswordAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UpdatedProfile_IsCommitted()
    {
        _userManager.Setup(m => m.UpdateAsync(_ana)).ReturnsAsync(IdentityResult.Success);

        var result = await Sut().UpdateProfileAsync(_ana.Id, new UpdateProfileRequest { FullName = "Ana Reyes" });

        result.Succeeded.Should().BeTrue();
        _ana.FullName.Should().Be("Ana Reyes");
        VerifyCommitted(Times.Once());
    }
}
