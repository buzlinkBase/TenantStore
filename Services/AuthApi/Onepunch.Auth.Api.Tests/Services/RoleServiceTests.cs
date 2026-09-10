using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Moq;
using Onepunch.Auth.Api.Tests.TestSupport;
using Onepunch.Auth.Core.Services;
using OnePunch.Auth.Domain.Entities;
using Xunit;

namespace Onepunch.Auth.Api.Tests.Services;

public class RoleServiceTests
{
    [Fact]
    public async Task Create_CreatesNewRole_WhenRoleDoesNotExist()
    {
        var uow = AuthTestContextFactory.CreateUnitOfWork();
        var roleManager = IdentityMocks.MockRoleManager();
        roleManager.Setup(m => m.RoleExistsAsync("Admin")).ReturnsAsync(false);
        roleManager.Setup(m => m.CreateAsync(It.IsAny<Role>())).ReturnsAsync(IdentityResult.Success);

        var sut = new RoleService(uow, roleManager.Object);

        var result = await sut.Create("Admin");

        result.Should().NotBeNull();
        result!.Name.Should().Be("Admin");
        roleManager.Verify(m => m.CreateAsync(It.Is<Role>(r => r.Name == "Admin")), Times.Once);
    }

    [Fact]
    public async Task Create_ReturnsExistingRole_WhenRoleAlreadyExists()
    {
        var uow = AuthTestContextFactory.CreateUnitOfWork();
        var roleManager = IdentityMocks.MockRoleManager();
        var existingRole = new Role { Name = "Admin" };
        roleManager.Setup(m => m.RoleExistsAsync("Admin")).ReturnsAsync(true);
        roleManager.Setup(m => m.FindByNameAsync("Admin")).ReturnsAsync(existingRole);

        var sut = new RoleService(uow, roleManager.Object);

        var result = await sut.Create("Admin");

        result.Should().BeSameAs(existingRole);
        roleManager.Verify(m => m.CreateAsync(It.IsAny<Role>()), Times.Never);
    }
}
