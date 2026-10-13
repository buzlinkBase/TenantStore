using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Onepunch.Common.Lib.Security;
using TenantStoreApi.Core;
using TenantStoreApi.Core.Services;
using TenantStoreApi.Domain.Entities;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Services;

public class PermissionCatalogProductTests
{
    private const string ProcurementOfficer = "Procurement Officer";

    private sealed class FakeProcurementCatalog : IPermissionCatalogProvider
    {
        public string Product => "procurement";

        public IReadOnlyList<(string Module, string Feature, string[] Actions)> Features { get; } =
        [
            ("Purchasing", "Purchase Order", ["View", "Create", "Approve"]),
        ];

        public IReadOnlyList<SystemRoleDefinition> SystemRoles { get; } =
        [
            new(ProcurementOfficer, "Raises purchase orders.", "procurement"),
        ];

        public IReadOnlyDictionary<string, string[]> RoleGrants { get; } = new Dictionary<string, string[]>
        {
            [ProcurementOfficer] = ["procurement:Purchase Order:View", "procurement:Purchase Order:Create"],
        };
    }

    private static PermissionCatalogSeederService CreateSut(out IUnitOfWorkService uow, params IPermissionCatalogProvider[] providers)
    {
        uow = TenantTestContextFactory.CreateUnitOfWork();
        return new PermissionCatalogSeederService(uow, providers);
    }

    [Theory]
    [InlineData("hrms", "Payroll Run", "Approve", "Payroll Run:Approve")]
    [InlineData("procurement", "Purchase Order", "Approve", "procurement:Purchase Order:Approve")]
    public void BuildCode_PrefixesEveryProductExceptHrms(string product, string feature, string action, string expected)
    {
        PermissionProducts.BuildCode(product, feature, action).Should().Be(expected);
    }

    [Fact]
    public async Task EnsureSeededAsync_WithNoProvidersPassed_SeedsHrmsWithUnprefixedCodes()
    {
        var uow = TenantTestContextFactory.CreateUnitOfWork();

        await new PermissionCatalogSeederService(uow).EnsureSeededAsync(CancellationToken.None);

        var permissions = await uow.Context.Permissions.ToListAsync();
        permissions.Should().OnlyContain(x => x.Product == "hrms");
        permissions.Should().Contain(x => x.Code == "Payroll Run:Approve");
    }

    [Fact]
    public async Task EnsureSeededAsync_SeedsEachProductsPermissions_WithItsPrefixAndProduct()
    {
        var sut = CreateSut(out var uow, new HrmsPermissionCatalog(), new FakeProcurementCatalog());

        await sut.EnsureSeededAsync(CancellationToken.None);

        var procurement = await uow.Context.Permissions.Where(x => x.Product == "procurement").ToListAsync();
        procurement.Select(x => x.Code).Should().BeEquivalentTo(
        [
            "procurement:Purchase Order:View",
            "procurement:Purchase Order:Create",
            "procurement:Purchase Order:Approve",
        ]);
        procurement.Should().OnlyContain(x => x.Module == "Purchasing" && x.Feature == "Purchase Order");
        (await uow.Context.Permissions.AnyAsync(x => x.Code == "Payroll Run:Approve" && x.Product == "hrms")).Should().BeTrue();
    }

    [Fact]
    public async Task EnsureSeededAsync_OwnerGetsEveryPermissionOfEveryProduct()
    {
        var sut = CreateSut(out var uow, new HrmsPermissionCatalog(), new FakeProcurementCatalog());

        await sut.EnsureSeededAsync(CancellationToken.None);

        var owner = await uow.Context.Roles.FirstAsync(x => x.Name == "Owner");
        (await uow.Context.RolePermissions.CountAsync(x => x.RoleId == owner.Id))
            .Should().Be(await uow.Context.Permissions.CountAsync());
    }

    [Fact]
    public async Task EnsureSeededAsync_AdminGetsNothingFromAProductThatDoesNotListIt()
    {
        var sut = CreateSut(out var uow, new HrmsPermissionCatalog(), new FakeProcurementCatalog());

        await sut.EnsureSeededAsync(CancellationToken.None);

        var admin = await uow.Context.Roles.FirstAsync(x => x.Name == "Admin");
        var granted = await uow.Context.RolePermissions
            .Where(x => x.RoleId == admin.Id)
            .Select(x => x.Permission)
            .ToListAsync();
        granted.Should().Contain(x => x.Code == "Payroll Run:Approve", "Admin keeps its HRMS grants");
        granted.Should().NotContain(x => x.Product == "procurement", "an HRMS admin must not gain a new product by default");
    }

    [Fact]
    public async Task EnsureSeededAsync_CreatesAProductsSystemRole_TaggedWithItsProduct_AndGrantsItsCodes()
    {
        var sut = CreateSut(out var uow, new HrmsPermissionCatalog(), new FakeProcurementCatalog());

        await sut.EnsureSeededAsync(CancellationToken.None);

        var role = await uow.Context.Roles.SingleAsync(x => x.Name == ProcurementOfficer);
        role.IsSystemRole.Should().BeTrue();
        role.Product.Should().Be("procurement");
        (await uow.Context.RolePermissions.Where(x => x.RoleId == role.Id).Select(x => x.Permission.Code).ToListAsync())
            .Should().BeEquivalentTo(["procurement:Purchase Order:View", "procurement:Purchase Order:Create"]);
    }

    [Fact]
    public async Task EnsureSeededAsync_LeavesTheCrossProductSystemRolesUntagged()
    {
        var sut = CreateSut(out var uow, new HrmsPermissionCatalog(), new FakeProcurementCatalog());

        await sut.EnsureSeededAsync(CancellationToken.None);

        var core = await uow.Context.Roles.Where(x => new[] { "Owner", "Admin", "Member", "Employee", "Client" }.Contains(x.Name)).ToListAsync();
        core.Should().HaveCount(5);
        core.Should().OnlyContain(x => x.Product == null);
    }

    [Fact]
    public async Task EnsureSeededAsync_AddingAProductLater_ReachesAnAlreadySeededTenantWithoutDuplicatingHrms()
    {
        var uow = TenantTestContextFactory.CreateUnitOfWork();
        await new PermissionCatalogSeederService(uow).EnsureSeededAsync(CancellationToken.None);
        var hrmsCount = await uow.Context.Permissions.CountAsync();

        await new PermissionCatalogSeederService(uow, [new HrmsPermissionCatalog(), new FakeProcurementCatalog()])
            .EnsureSeededAsync(CancellationToken.None);

        (await uow.Context.Permissions.CountAsync(x => x.Product == "hrms")).Should().Be(hrmsCount);
        (await uow.Context.Permissions.CountAsync(x => x.Product == "procurement")).Should().Be(3);
        var owner = await uow.Context.Roles.FirstAsync(x => x.Name == "Owner");
        (await uow.Context.RolePermissions.CountAsync(x => x.RoleId == owner.Id))
            .Should().Be(await uow.Context.Permissions.CountAsync(), "Owner is caught up with the new product's codes");
    }

    [Fact]
    public async Task EnsureSeededAsync_GivesPermissionsWithABlankProduct_TheProductTheirCodeBelongsTo()
    {
        // What the migration leaves behind: the column's database default is blank.
        var sut = CreateSut(out var uow, new HrmsPermissionCatalog(), new FakeProcurementCatalog());
        await sut.EnsureSeededAsync(CancellationToken.None);
        var all = await uow.Context.Permissions.ToListAsync();
        all.ForEach(x => x.Product = "");
        await uow.Context.SaveChangesAsync(CancellationToken.None);

        await sut.EnsureSeededAsync(CancellationToken.None);

        var permissions = await uow.Context.Permissions.ToListAsync();
        permissions.Where(x => x.Code == "Payroll Run:Approve").Should().OnlyContain(x => x.Product == "hrms");
        permissions.Where(x => x.Code.StartsWith("procurement:")).Should().OnlyContain(x => x.Product == "procurement");
        permissions.Should().NotContain(x => x.Product == "");
    }

    [Fact]
    public void Constructor_RejectsTheSameProductRegisteredTwice()
    {
        var uow = TenantTestContextFactory.CreateUnitOfWork();

        var act = () => new PermissionCatalogSeederService(uow, [new HrmsPermissionCatalog(), new HrmsPermissionCatalog()]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*hrms*");
    }
}
