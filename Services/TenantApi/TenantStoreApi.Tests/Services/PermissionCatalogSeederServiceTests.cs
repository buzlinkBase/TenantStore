using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TenantStoreApi.Core;
using TenantStoreApi.Core.Services;
using TenantStoreApi.Domain.Entities;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Services;

public class PermissionCatalogSeederServiceTests
{
    private static PermissionCatalogSeederService CreateSut(out IUnitOfWorkService uow)
    {
        uow = TenantTestContextFactory.CreateUnitOfWork();
        return new PermissionCatalogSeederService(uow);
    }

    [Fact]
    public async Task EnsureSeededAsync_SeedsPermissionsAndTheSystemRoles()
    {
        var sut = CreateSut(out var uow);

        await sut.EnsureSeededAsync(CancellationToken.None);

        var permissionCount = await uow.Context.Permissions.CountAsync();
        permissionCount.Should().BeGreaterThan(0);

        var systemRoles = await uow.Context.Roles.Where(x => x.IsSystemRole).ToListAsync();
        systemRoles.Select(x => x.Name).Should().BeEquivalentTo(["Owner", "Admin", "Member", "Employee", "Client"]);
    }

    [Fact]
    public async Task EnsureSeededAsync_ClientGetsNoPermissions_UntilTheClientPortalExists()
    {
        var sut = CreateSut(out var uow);

        await sut.EnsureSeededAsync(CancellationToken.None);

        var client = await uow.Context.Roles.FirstAsync(x => x.Name == "Client");
        (await uow.Context.RolePermissions.CountAsync(x => x.RoleId == client.Id)).Should().Be(0);
    }

    [Fact]
    public async Task EnsureSeededAsync_AddsClientRole_ToAnAlreadySeededTenant()
    {
        // Simulates a tenant seeded before Client existed -- the other four System Roles are
        // there, Client isn't. The next startup must add it without duplicating the others.
        var sut = CreateSut(out var uow);
        await sut.EnsureSeededAsync(CancellationToken.None);
        uow.Context.Roles.Remove(await uow.Context.Roles.FirstAsync(x => x.Name == "Client"));
        await uow.Context.SaveChangesAsync(CancellationToken.None);

        await sut.EnsureSeededAsync(CancellationToken.None);

        var systemRoleNames = await uow.Context.Roles
            .Where(x => x.IsSystemRole)
            .Select(x => x.Name)
            .ToListAsync();
        systemRoleNames.Should().BeEquivalentTo(["Owner", "Admin", "Member", "Employee", "Client"]);
    }

    [Fact]
    public async Task EnsureSeededAsync_BackfillsLegacyClientMembershipRoles()
    {
        var sut = CreateSut(out var uow);
        await sut.EnsureSeededAsync(CancellationToken.None);
        var membership = new UserMembership { TenantId = Guid.NewGuid(), UserId = Guid.NewGuid() };
        membership.Roles.Add(new MembershipRole { Role = "Client" });
        uow.Context.Memberships.Add(membership);
        await uow.Context.SaveChangesAsync(CancellationToken.None);

        await sut.EnsureSeededAsync(CancellationToken.None);

        var client = await uow.Context.Roles.FirstAsync(x => x.Name == "Client");
        (await uow.Context.MembershipRoles.FirstAsync(x => x.Role == "Client"))
            .RoleId.Should().Be(client.Id);
    }

    [Fact]
    public async Task EnsureSeededAsync_OwnerGetsEveryPermission()
    {
        var sut = CreateSut(out var uow);

        await sut.EnsureSeededAsync(CancellationToken.None);

        var totalPermissions = await uow.Context.Permissions.CountAsync();
        var owner = await uow.Context.Roles.FirstAsync(x => x.Name == "Owner");
        var ownerGrantCount = await uow.Context.RolePermissions.CountAsync(x => x.RoleId == owner.Id);

        ownerGrantCount.Should().Be(totalPermissions);
    }

    [Fact]
    public async Task EnsureSeededAsync_EmployeeGetsThePortalViewPermission()
    {
        var sut = CreateSut(out var uow);

        await sut.EnsureSeededAsync(CancellationToken.None);

        var employee = await uow.Context.Roles.FirstAsync(x => x.Name == "Employee");
        var portalPermission = await uow.Context.Permissions.FirstAsync(x => x.Code == "Employee Self-Service Portal:View");
        (await uow.Context.RolePermissions
            .AnyAsync(x => x.RoleId == employee.Id && x.PermissionId == portalPermission.Id))
            .Should().BeTrue("the frontend gates My Portal on this permission, and Employee is the role the portal exists for");
    }

    [Fact]
    public async Task EnsureSeededAsync_BackfillsThePortalPermission_ForAnAlreadySeededTenantsEmployeeRole()
    {
        // Simulates a tenant seeded before EmployeeGrantedCodes existed -- its Employee role has
        // no permissions at all yet, same as every tenant seeded under the old code.
        var sut = CreateSut(out var uow);
        await sut.EnsureSeededAsync(CancellationToken.None);
        var employee = await uow.Context.Roles.FirstAsync(x => x.Name == "Employee");
        var existingGrant = await uow.Context.RolePermissions.FirstAsync(x => x.RoleId == employee.Id);
        uow.Context.RolePermissions.Remove(existingGrant);
        await uow.Context.SaveChangesAsync(CancellationToken.None);

        await sut.EnsureSeededAsync(CancellationToken.None);

        var portalPermission = await uow.Context.Permissions.FirstAsync(x => x.Code == "Employee Self-Service Portal:View");
        (await uow.Context.RolePermissions
            .AnyAsync(x => x.RoleId == employee.Id && x.PermissionId == portalPermission.Id))
            .Should().BeTrue();
    }

    [Fact]
    public async Task EnsureSeededAsync_AdminGetsFullSetupAccess()
    {
        var sut = CreateSut(out var uow);

        await sut.EnsureSeededAsync(CancellationToken.None);

        var admin = await uow.Context.Roles.FirstAsync(x => x.Name == "Admin");
        var grantedCodes = await uow.Context.RolePermissions
            .Where(x => x.RoleId == admin.Id)
            .Select(x => x.Permission.Code)
            .ToListAsync();

        foreach (var feature in new[]
        {
            "Organization Setup", "Workforce Setup", "Time Shift Setup",
            "Deductions & Income Setup", "Leave Setup", "Statutory Tables", "Biometric Setup",
        })
        {
            foreach (var action in new[] { "View", "Create", "Edit", "Delete" })
            {
                grantedCodes.Should().Contain($"{feature}:{action}");
            }
        }
    }

    [Fact]
    public async Task EnsureSeededAsync_BackfillsSetupAccess_ForAnAlreadySeededTenantsAdminRole()
    {
        // Simulates a tenant seeded before AdminGrantedCodes covered Setup -- its Admin role has
        // only the original Tenant Administration grants, same as every tenant seeded under the
        // old code.
        var sut = CreateSut(out var uow);
        await sut.EnsureSeededAsync(CancellationToken.None);
        var admin = await uow.Context.Roles.FirstAsync(x => x.Name == "Admin");
        var setupPermissionIds = await uow.Context.Permissions
            .Where(x => x.Module == "Setup")
            .Select(x => x.Id)
            .ToListAsync();
        var setupGrants = await uow.Context.RolePermissions
            .Where(x => x.RoleId == admin.Id && setupPermissionIds.Contains(x.PermissionId))
            .ToListAsync();
        uow.Context.RolePermissions.RemoveRange(setupGrants);
        await uow.Context.SaveChangesAsync(CancellationToken.None);

        await sut.EnsureSeededAsync(CancellationToken.None);

        var organizationSetupView = await uow.Context.Permissions.FirstAsync(x => x.Code == "Organization Setup:View");
        (await uow.Context.RolePermissions
            .AnyAsync(x => x.RoleId == admin.Id && x.PermissionId == organizationSetupView.Id))
            .Should().BeTrue();
    }

    [Fact]
    public async Task EnsureSeededAsync_AdminGetsFullPayrollGenerationAccess()
    {
        var sut = CreateSut(out var uow);

        await sut.EnsureSeededAsync(CancellationToken.None);

        var admin = await uow.Context.Roles.FirstAsync(x => x.Name == "Admin");
        var grantedCodes = await uow.Context.RolePermissions
            .Where(x => x.RoleId == admin.Id)
            .Select(x => x.Permission.Code)
            .ToListAsync();

        foreach (var feature in new[]
        {
            "Payroll Run", "Payroll Summary", "13th Month Run", "Last Pay Run", "Year-End Adjustment Run",
        })
        {
            foreach (var action in new[] { "View", "Create", "Approve", "Export" })
            {
                grantedCodes.Should().Contain($"{feature}:{action}");
            }
        }
    }

    [Fact]
    public async Task EnsureSeededAsync_BackfillsPayrollGenerationAccess_ForAnAlreadySeededTenantsAdminRole()
    {
        // Simulates a tenant seeded before AdminGrantedCodes covered Payroll Generation -- its
        // Admin role predates that grant, same as every tenant seeded under the old code.
        var sut = CreateSut(out var uow);
        await sut.EnsureSeededAsync(CancellationToken.None);
        var admin = await uow.Context.Roles.FirstAsync(x => x.Name == "Admin");
        var payrollPermissionIds = await uow.Context.Permissions
            .Where(x => x.Module == "Payroll Generation")
            .Select(x => x.Id)
            .ToListAsync();
        var payrollGrants = await uow.Context.RolePermissions
            .Where(x => x.RoleId == admin.Id && payrollPermissionIds.Contains(x.PermissionId))
            .ToListAsync();
        uow.Context.RolePermissions.RemoveRange(payrollGrants);
        await uow.Context.SaveChangesAsync(CancellationToken.None);

        await sut.EnsureSeededAsync(CancellationToken.None);

        var payrollRunApprove = await uow.Context.Permissions.FirstAsync(x => x.Code == "Payroll Run:Approve");
        (await uow.Context.RolePermissions
            .AnyAsync(x => x.RoleId == admin.Id && x.PermissionId == payrollRunApprove.Id))
            .Should().BeTrue();
    }

    [Fact]
    public async Task EnsureSeededAsync_AdminGetsFullTimekeepingAccess()
    {
        var sut = CreateSut(out var uow);

        await sut.EnsureSeededAsync(CancellationToken.None);

        var admin = await uow.Context.Roles.FirstAsync(x => x.Name == "Admin");
        var grantedCodes = await uow.Context.RolePermissions
            .Where(x => x.RoleId == admin.Id)
            .Select(x => x.Permission.Code)
            .ToListAsync();

        foreach (var feature in new[]
        {
            "Upload Attendance", "Raw Logs", "Unregistered Employees", "Incomplete Punches",
        })
        {
            foreach (var action in new[] { "View", "Edit", "Export" })
            {
                grantedCodes.Should().Contain($"{feature}:{action}");
            }
        }

        foreach (var action in new[] { "View", "Edit", "Export", "Delete" })
        {
            grantedCodes.Should().Contain($"Attendance Manual Entry:{action}");
        }
    }

    [Fact]
    public async Task EnsureSeededAsync_BackfillsTimekeepingAccess_ForAnAlreadySeededTenantsAdminRole()
    {
        var sut = CreateSut(out var uow);
        await sut.EnsureSeededAsync(CancellationToken.None);
        var admin = await uow.Context.Roles.FirstAsync(x => x.Name == "Admin");
        var timekeepingPermissionIds = await uow.Context.Permissions
            .Where(x => x.Module == "Timekeeping")
            .Select(x => x.Id)
            .ToListAsync();
        var timekeepingGrants = await uow.Context.RolePermissions
            .Where(x => x.RoleId == admin.Id && timekeepingPermissionIds.Contains(x.PermissionId))
            .ToListAsync();
        uow.Context.RolePermissions.RemoveRange(timekeepingGrants);
        await uow.Context.SaveChangesAsync(CancellationToken.None);

        await sut.EnsureSeededAsync(CancellationToken.None);

        var attendanceManualEntryDelete = await uow.Context.Permissions.FirstAsync(x => x.Code == "Attendance Manual Entry:Delete");
        (await uow.Context.RolePermissions
            .AnyAsync(x => x.RoleId == admin.Id && x.PermissionId == attendanceManualEntryDelete.Id))
            .Should().BeTrue();
    }

    [Fact]
    public async Task EnsureSeededAsync_AdminGetsFullChangeScheduleAccess()
    {
        var sut = CreateSut(out var uow);

        await sut.EnsureSeededAsync(CancellationToken.None);

        var admin = await uow.Context.Roles.FirstAsync(x => x.Name == "Admin");
        var grantedCodes = await uow.Context.RolePermissions
            .Where(x => x.RoleId == admin.Id)
            .Select(x => x.Permission.Code)
            .ToListAsync();

        foreach (var action in new[] { "View", "Create", "Approve", "ManageOwnTeam", "Delete" })
        {
            grantedCodes.Should().Contain($"Work Rotation:{action}");
        }

        foreach (var feature in new[] { "Change Rest Day", "Change Holiday" })
        {
            foreach (var action in new[] { "View", "Create", "Approve", "Delete" })
            {
                grantedCodes.Should().Contain($"{feature}:{action}");
            }
        }
    }

    [Fact]
    public async Task EnsureSeededAsync_BackfillsChangeScheduleAccess_ForAnAlreadySeededTenantsAdminRole()
    {
        var sut = CreateSut(out var uow);
        await sut.EnsureSeededAsync(CancellationToken.None);
        var admin = await uow.Context.Roles.FirstAsync(x => x.Name == "Admin");
        var changeSchedulePermissionIds = await uow.Context.Permissions
            .Where(x => x.Module == "Change Schedule")
            .Select(x => x.Id)
            .ToListAsync();
        var changeScheduleGrants = await uow.Context.RolePermissions
            .Where(x => x.RoleId == admin.Id && changeSchedulePermissionIds.Contains(x.PermissionId))
            .ToListAsync();
        uow.Context.RolePermissions.RemoveRange(changeScheduleGrants);
        await uow.Context.SaveChangesAsync(CancellationToken.None);

        await sut.EnsureSeededAsync(CancellationToken.None);

        var workRotationDelete = await uow.Context.Permissions.FirstAsync(x => x.Code == "Work Rotation:Delete");
        (await uow.Context.RolePermissions
            .AnyAsync(x => x.RoleId == admin.Id && x.PermissionId == workRotationDelete.Id))
            .Should().BeTrue();
    }

    [Fact]
    public async Task EnsureSeededAsync_AdminGetsFullDtrGenerationAccess()
    {
        var sut = CreateSut(out var uow);

        await sut.EnsureSeededAsync(CancellationToken.None);

        var admin = await uow.Context.Roles.FirstAsync(x => x.Name == "Admin");
        var grantedCodes = await uow.Context.RolePermissions
            .Where(x => x.RoleId == admin.Id)
            .Select(x => x.Permission.Code)
            .ToListAsync();

        foreach (var feature in new[] { "DTR Master", "DTR Summary" })
        {
            foreach (var action in new[] { "View", "Manage" })
            {
                grantedCodes.Should().Contain($"{feature}:{action}");
            }
        }
    }

    [Fact]
    public async Task EnsureSeededAsync_BackfillsDtrGenerationAccess_ForAnAlreadySeededTenantsAdminRole()
    {
        var sut = CreateSut(out var uow);
        await sut.EnsureSeededAsync(CancellationToken.None);
        var admin = await uow.Context.Roles.FirstAsync(x => x.Name == "Admin");
        var dtrPermissionIds = await uow.Context.Permissions
            .Where(x => x.Module == "DTR Generation")
            .Select(x => x.Id)
            .ToListAsync();
        var dtrGrants = await uow.Context.RolePermissions
            .Where(x => x.RoleId == admin.Id && dtrPermissionIds.Contains(x.PermissionId))
            .ToListAsync();
        uow.Context.RolePermissions.RemoveRange(dtrGrants);
        await uow.Context.SaveChangesAsync(CancellationToken.None);

        await sut.EnsureSeededAsync(CancellationToken.None);

        var dtrMasterManage = await uow.Context.Permissions.FirstAsync(x => x.Code == "DTR Master:Manage");
        (await uow.Context.RolePermissions
            .AnyAsync(x => x.RoleId == admin.Id && x.PermissionId == dtrMasterManage.Id))
            .Should().BeTrue();
    }

    [Fact]
    public async Task EnsureSeededAsync_AdminGetsFullApplicationsAccess()
    {
        var sut = CreateSut(out var uow);

        await sut.EnsureSeededAsync(CancellationToken.None);

        var admin = await uow.Context.Roles.FirstAsync(x => x.Name == "Admin");
        var grantedCodes = await uow.Context.RolePermissions
            .Where(x => x.RoleId == admin.Id)
            .Select(x => x.Permission.Code)
            .ToListAsync();

        foreach (var feature in new[]
        {
            "Leave", "Overtime", "Official Business", "Undertime", "Pass Slip",
            "Loan/Deduction", "Other Income", "Salary Adjustment",
        })
        {
            foreach (var action in new[] { "View", "Create", "Edit", "Approve", "Delete" })
            {
                grantedCodes.Should().Contain($"{feature}:{action}");
            }
        }
    }

    [Fact]
    public async Task EnsureSeededAsync_BackfillsApplicationsAccess_ForAnAlreadySeededTenantsAdminRole()
    {
        var sut = CreateSut(out var uow);
        await sut.EnsureSeededAsync(CancellationToken.None);
        var admin = await uow.Context.Roles.FirstAsync(x => x.Name == "Admin");
        var applicationsPermissionIds = await uow.Context.Permissions
            .Where(x => x.Module == "Applications")
            .Select(x => x.Id)
            .ToListAsync();
        var applicationsGrants = await uow.Context.RolePermissions
            .Where(x => x.RoleId == admin.Id && applicationsPermissionIds.Contains(x.PermissionId))
            .ToListAsync();
        uow.Context.RolePermissions.RemoveRange(applicationsGrants);
        await uow.Context.SaveChangesAsync(CancellationToken.None);

        await sut.EnsureSeededAsync(CancellationToken.None);

        var undertimeCreate = await uow.Context.Permissions.FirstAsync(x => x.Code == "Undertime:Create");
        (await uow.Context.RolePermissions
            .AnyAsync(x => x.RoleId == admin.Id && x.PermissionId == undertimeCreate.Id))
            .Should().BeTrue();
    }

    [Fact]
    public async Task EnsureSeededAsync_AdminGetsFullReportsAccess()
    {
        var sut = CreateSut(out var uow);

        await sut.EnsureSeededAsync(CancellationToken.None);

        var admin = await uow.Context.Roles.FirstAsync(x => x.Name == "Admin");
        var grantedCodes = await uow.Context.RolePermissions
            .Where(x => x.RoleId == admin.Id)
            .Select(x => x.Permission.Code)
            .ToListAsync();

        foreach (var feature in new[] { "Government Statutory Reports", "BIR Reports" })
        {
            foreach (var action in new[] { "View", "Export" })
            {
                grantedCodes.Should().Contain($"{feature}:{action}");
            }
        }

        foreach (var action in new[] { "View", "Export", "Edit" })
        {
            grantedCodes.Should().Contain($"Payroll Reports:{action}");
        }

        grantedCodes.Should().Contain("Attendance Reports:View");
    }

    [Fact]
    public async Task EnsureSeededAsync_BackfillsReportsAccess_ForAnAlreadySeededTenantsAdminRole()
    {
        var sut = CreateSut(out var uow);
        await sut.EnsureSeededAsync(CancellationToken.None);
        var admin = await uow.Context.Roles.FirstAsync(x => x.Name == "Admin");
        var reportsPermissionIds = await uow.Context.Permissions
            .Where(x => x.Module == "Reports")
            .Select(x => x.Id)
            .ToListAsync();
        var reportsGrants = await uow.Context.RolePermissions
            .Where(x => x.RoleId == admin.Id && reportsPermissionIds.Contains(x.PermissionId))
            .ToListAsync();
        uow.Context.RolePermissions.RemoveRange(reportsGrants);
        await uow.Context.SaveChangesAsync(CancellationToken.None);

        await sut.EnsureSeededAsync(CancellationToken.None);

        var attendanceReportsView = await uow.Context.Permissions.FirstAsync(x => x.Code == "Attendance Reports:View");
        (await uow.Context.RolePermissions
            .AnyAsync(x => x.RoleId == admin.Id && x.PermissionId == attendanceReportsView.Id))
            .Should().BeTrue();
    }

    [Fact]
    public async Task EnsureSeededAsync_AdminGetsSecurityAndHolidayWageSetupAccess()
    {
        var sut = CreateSut(out var uow);

        await sut.EnsureSeededAsync(CancellationToken.None);

        var admin = await uow.Context.Roles.FirstAsync(x => x.Name == "Admin");
        var grantedCodes = await uow.Context.RolePermissions
            .Where(x => x.RoleId == admin.Id)
            .Select(x => x.Permission.Code)
            .ToListAsync();

        foreach (var feature in new[] { "Users", "Roles", "Permissions", "Audit Trail" })
        {
            foreach (var action in new[] { "View", "Create", "Edit", "Delete", "Manage" })
            {
                grantedCodes.Should().Contain($"{feature}:{action}");
            }
        }

        foreach (var feature in new[] { "Holiday Setup", "Minimum Wage Setup" })
        {
            foreach (var action in new[] { "View", "Create", "Edit", "Delete" })
            {
                grantedCodes.Should().Contain($"{feature}:{action}");
            }
        }

        grantedCodes.Should().Contain("Dashboard:View");
    }

    [Fact]
    public async Task EnsureSeededAsync_BackfillsSecurityAndHolidayWageSetup_ForAnAlreadySeededTenant()
    {
        // Simulates a tenant seeded before these codes existed: catalog rows and Admin grants
        // both missing. A re-run must add the rows and grant them to Admin (and Owner).
        var sut = CreateSut(out var uow);
        await sut.EnsureSeededAsync(CancellationToken.None);
        var newCodes = await uow.Context.Permissions
            .Where(x => x.Module == "Security" || x.Module == "Dashboard" || x.Code.StartsWith("Holiday Setup:") || x.Code.StartsWith("Minimum Wage Setup:"))
            .ToListAsync();
        var newIds = newCodes.Select(x => x.Id).ToList();
        uow.Context.RolePermissions.RemoveRange(
            await uow.Context.RolePermissions.Where(x => newIds.Contains(x.PermissionId)).ToListAsync());
        uow.Context.Permissions.RemoveRange(newCodes.Where(x => x.Module is "Setup" or "Dashboard"));
        await uow.Context.SaveChangesAsync(CancellationToken.None);

        await sut.EnsureSeededAsync(CancellationToken.None);

        var admin = await uow.Context.Roles.FirstAsync(x => x.Name == "Admin");
        var owner = await uow.Context.Roles.FirstAsync(x => x.Name == "Owner");
        foreach (var code in new[] { "Dashboard:View", "Users:View", "Audit Trail:View", "Holiday Setup:View", "Minimum Wage Setup:View" })
        {
            var permission = await uow.Context.Permissions.FirstAsync(x => x.Code == code);
            (await uow.Context.RolePermissions.AnyAsync(x => x.RoleId == admin.Id && x.PermissionId == permission.Id))
                .Should().BeTrue($"Admin should be backfilled with {code}");
            (await uow.Context.RolePermissions.AnyAsync(x => x.RoleId == owner.Id && x.PermissionId == permission.Id))
                .Should().BeTrue($"Owner should be backfilled with {code}");
        }
    }

    [Fact]
    public async Task EnsureSeededAsync_MemberGetsNoPermissions()
    {
        var sut = CreateSut(out var uow);

        await sut.EnsureSeededAsync(CancellationToken.None);

        var member = await uow.Context.Roles.FirstAsync(x => x.Name == "Member");
        var memberGrantCount = await uow.Context.RolePermissions.CountAsync(x => x.RoleId == member.Id);

        memberGrantCount.Should().Be(0);
    }

    [Fact]
    public async Task EnsureSeededAsync_IsIdempotent_DoesNotDuplicateOnSecondRun()
    {
        var sut = CreateSut(out var uow);
        await sut.EnsureSeededAsync(CancellationToken.None);
        var permissionCountAfterFirst = await uow.Context.Permissions.CountAsync();
        var roleCountAfterFirst = await uow.Context.Roles.CountAsync();

        await sut.EnsureSeededAsync(CancellationToken.None);

        (await uow.Context.Permissions.CountAsync()).Should().Be(permissionCountAfterFirst);
        (await uow.Context.Roles.CountAsync()).Should().Be(roleCountAfterFirst);
    }

    [Fact]
    public async Task EnsureSeededAsync_BackfillsExistingMembershipRoleRowsByName()
    {
        var sut = CreateSut(out var uow);
        var membership = new UserMembership { TenantId = Guid.NewGuid(), UserId = Guid.NewGuid() };
        await uow.Repository.AddAsync(membership, CancellationToken.None);
        var membershipRole = new MembershipRole { UserMembershipId = membership.Id, Role = "Owner" };
        await uow.Repository.AddAsync(membershipRole, CancellationToken.None);
        await uow.CommitChangesAsync("", CancellationToken.None);

        await sut.EnsureSeededAsync(CancellationToken.None);

        var owner = await uow.Context.Roles.FirstAsync(x => x.Name == "Owner");
        var reloaded = await uow.Context.MembershipRoles.FirstAsync(x => x.Id == membershipRole.Id);
        reloaded.RoleId.Should().Be(owner.Id);
    }

    [Fact]
    public async Task EnsureSeededAsync_BackfillsAPermissionMissingFromAnAlreadySeededTenant()
    {
        // Simulates a permission added to the catalog after this tenant's Permissions table was
        // already seeded (e.g. Work Rotation's ManageOwnTeam) -- removing one existing permission
        // (and its Owner grant) after the initial seed stands in for "the catalog gained an entry
        // this tenant doesn't have yet."
        var sut = CreateSut(out var uow);
        await sut.EnsureSeededAsync(CancellationToken.None);

        var totalPermissions = await uow.Context.Permissions.CountAsync();
        var removed = await uow.Context.Permissions.FirstAsync();
        var owner = await uow.Context.Roles.FirstAsync(x => x.Name == "Owner");
        var ownerGrantForRemoved = await uow.Context.RolePermissions
            .FirstAsync(x => x.RoleId == owner.Id && x.PermissionId == removed.Id);
        uow.Context.RolePermissions.Remove(ownerGrantForRemoved);
        uow.Context.Permissions.Remove(removed);
        await uow.Context.SaveChangesAsync(CancellationToken.None);

        await sut.EnsureSeededAsync(CancellationToken.None);

        (await uow.Context.Permissions.CountAsync()).Should().Be(totalPermissions);
        var restored = await uow.Context.Permissions.FirstAsync(x => x.Code == removed.Code);
        var ownerGrantCount = await uow.Context.RolePermissions.CountAsync(x => x.RoleId == owner.Id);
        ownerGrantCount.Should().Be(totalPermissions, "the restored permission must be re-granted to Owner too");
        (await uow.Context.RolePermissions
            .AnyAsync(x => x.RoleId == owner.Id && x.PermissionId == restored.Id))
            .Should().BeTrue();
    }

    [Fact]
    public async Task EnsureSeededAsync_DoesNotTouchAlreadyBackfilledRows()
    {
        var sut = CreateSut(out var uow);
        await sut.EnsureSeededAsync(CancellationToken.None);
        var admin = await uow.Context.Roles.FirstAsync(x => x.Name == "Admin");

        var membership = new UserMembership { TenantId = Guid.NewGuid(), UserId = Guid.NewGuid() };
        await uow.Repository.AddAsync(membership, CancellationToken.None);
        // Deliberately mismatched Role string vs RoleId -- proves an already-backfilled row
        // (RoleId already set) is left alone rather than being re-derived from the string.
        var membershipRole = new MembershipRole { UserMembershipId = membership.Id, Role = "Owner", RoleId = admin.Id };
        await uow.Repository.AddAsync(membershipRole, CancellationToken.None);
        // Raw SaveChangesAsync, not uow.CommitChangesAsync -- the seeder call above already
        // consumed this uow's one-shot commit (see EnsureSeededAsync's own final commit); a second
        // CommitChangesAsync on the same instance would silently no-op.
        await uow.Context.SaveChangesAsync(CancellationToken.None);

        await sut.EnsureSeededAsync(CancellationToken.None);

        var reloaded = await uow.Context.MembershipRoles.FirstAsync(x => x.Id == membershipRole.Id);
        reloaded.RoleId.Should().Be(admin.Id);
    }
}
