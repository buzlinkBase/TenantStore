using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Onepunch.Auth.Core.Platform;
using Onepunch.Auth.Infrastructure;
using Onepunch.Common.Lib.Platform;
using Xunit;

namespace Onepunch.Auth.Api.Tests.Platform;

/// <summary>There is no self-registration, so the first SuperAdmin comes from configuration -- once, and only into an empty table.</summary>
public class PlatformBootstrapperTests : IDisposable
{
    private readonly PlatformAuthHarness _h = new();

    public void Dispose() => _h.Dispose();

    private PlatformBootstrapper Bootstrapper(PlatformAuthSettings.BootstrapSettings bootstrap)
    {
        // A scope factory whose scopes hand out the harness's own context/service over the same in-memory database.
        var services = new ServiceCollection();
        services.AddScoped(_ => _h.NewContext());
        services.AddScoped(sp => _h.NewService(sp.GetRequiredService<PlatformAuthContext>()));
        services.AddScoped(sp => new PlatformAdminService(
            sp.GetRequiredService<PlatformAuthContext>(), sp.GetRequiredService<PlatformAuthService>(),
            new PlatformPasswordGenerator(), _h.Time, NullLogger<PlatformAdminService>.Instance));
        var provider = services.BuildServiceProvider();

        var settings = new PlatformAuthSettings { Bootstrap = bootstrap };
        return new PlatformBootstrapper(provider.GetRequiredService<IServiceScopeFactory>(), Options.Create(settings),
            NullLogger<PlatformBootstrapper>.Instance);
    }

    [Fact]
    public async Task WithCredentialsConfigured_AnEmptyTable_GetsOneSuperAdmin_WhoMustChangeThePassword()
    {
        await Bootstrapper(new() { Email = "root@example.com", Password = PlatformAuthHarness.StrongPassword, DisplayName = "Root" })
            .StartAsync(CancellationToken.None);

        await using var db = _h.NewContext();
        var user = await db.PlatformUsers.SingleAsync();
        user.Role.Should().Be(PlatformRoles.SuperAdmin);
        user.MustChangePassword.Should().BeTrue();
        user.MfaEnrolledAtUtc.Should().BeNull("MFA is enrolled at first sign-in");
        user.CreatedBy.Should().Be("bootstrap");
    }

    [Fact]
    public async Task WithoutCredentials_NothingIsCreated()
    {
        await Bootstrapper(new()).StartAsync(CancellationToken.None);

        await using var db = _h.NewContext();
        (await db.PlatformUsers.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task OnceAnyOperatorExists_ItDoesNothing_SoAStaleConfigCannotAddBackdoorAdmins()
    {
        await _h.SeedUserAsync("existing@example.com", PlatformRoles.Support);

        await Bootstrapper(new() { Email = "root@example.com", Password = PlatformAuthHarness.StrongPassword })
            .StartAsync(CancellationToken.None);

        await using var db = _h.NewContext();
        (await db.PlatformUsers.CountAsync()).Should().Be(1);
        (await db.PlatformUsers.AnyAsync(x => x.Role == PlatformRoles.SuperAdmin)).Should().BeFalse();
    }

    [Fact]
    public async Task AWeakBootstrapPassword_IsRefused_WithoutCrashingStartup()
    {
        var act = () => Bootstrapper(new() { Email = "root@example.com", Password = "weak" }).StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        await using var db = _h.NewContext();
        (await db.PlatformUsers.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task WithRecoverSet_ALockedOutSuperAdmin_IsTakenBack_EvenThoughTheTableIsNotEmpty()
    {
        var locked = await _h.SeedUserAsync("root@example.com", PlatformRoles.SuperAdmin);
        await using (var db = _h.NewContext())
        {
            (await db.PlatformUsers.SingleAsync(x => x.Id == locked.Id)).IsActive = false;
            await db.SaveChangesAsync();
        }

        await Bootstrapper(new() { Email = "root@example.com", Password = PlatformAuthHarness.NewStrongPassword, Recover = true })
            .StartAsync(CancellationToken.None);

        await using var check = _h.NewContext();
        var user = await check.PlatformUsers.SingleAsync();
        user.IsActive.Should().BeTrue();
        user.MustChangePassword.Should().BeTrue();
        (await check.PlatformAdminAudits.SingleAsync()).Action.Should().Be("operator.recovered");
    }

    [Fact]
    public async Task WithoutRecover_AStaleBootstrapConfig_CannotTouchAnExistingOperator()
    {
        var existing = await _h.SeedUserAsync("root@example.com", PlatformRoles.SuperAdmin);

        await Bootstrapper(new() { Email = "root@example.com", Password = PlatformAuthHarness.NewStrongPassword })
            .StartAsync(CancellationToken.None);

        await using var db = _h.NewContext();
        (await db.PlatformUsers.SingleAsync(x => x.Id == existing.Id)).MustChangePassword.Should().BeFalse();
        (await db.PlatformAdminAudits.CountAsync()).Should().Be(0);
    }
}
