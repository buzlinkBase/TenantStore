using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Onepunch.Auth.Infrastructure;
using Onepunch.Common.Lib.Platform;

namespace Onepunch.Auth.Core.Platform;

/// <summary>
/// There is no self-registration for platform operators, so the very first SuperAdmin has to come
/// from somewhere: when the platform user table is empty and <c>PlatformAuth:Bootstrap:Email</c> and
/// <c>Password</c> are set, one is created (with a forced password change and MFA enrolment at first
/// sign-in). Once any operator exists this does nothing, and the settings should be removed.
/// The one exception is <c>Recover</c>, the break-glass switch for when every SuperAdmin is locked out.
/// Never throws: a missing table (migration not applied yet) must not stop the API starting.
/// </summary>
public class PlatformBootstrapper : IHostedService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly PlatformAuthSettings _settings;
    private readonly ILogger<PlatformBootstrapper> _logger;

    public PlatformBootstrapper(IServiceScopeFactory scopes, IOptions<PlatformAuthSettings> settings, ILogger<PlatformBootstrapper> logger)
    {
        _scopes = scopes;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var bootstrap = _settings.Bootstrap;
        if (string.IsNullOrWhiteSpace(bootstrap.Email) || string.IsNullOrWhiteSpace(bootstrap.Password)) return;

        try
        {
            using var scope = _scopes.CreateScope();

            if (bootstrap.Recover)
            {
                var admins = scope.ServiceProvider.GetRequiredService<PlatformAdminService>();
                var acted = await admins.RecoverSuperAdminAsync(bootstrap.Email!, bootstrap.DisplayName, bootstrap.Password!, cancellationToken);
                _logger.LogWarning(acted
                    ? "Recovered platform SuperAdmin {Email}: sign in with the configured password, enrol an authenticator and choose a new password. Then remove PlatformAuth:Bootstrap (including Recover) from the configuration."
                    : "PlatformAuth:Bootstrap:Recover is still set, but {Email} has already signed in since the recovery -- nothing changed. Remove PlatformAuth:Bootstrap from the configuration.",
                    bootstrap.Email);
                return;
            }

            var db = scope.ServiceProvider.GetRequiredService<PlatformAuthContext>();
            if (await db.PlatformUsers.AnyAsync(cancellationToken)) return;

            var auth = scope.ServiceProvider.GetRequiredService<PlatformAuthService>();
            await auth.CreateUserAsync(
                bootstrap.Email!, bootstrap.DisplayName ?? "Platform Administrator", PlatformRoles.SuperAdmin,
                bootstrap.Password!, "bootstrap", cancellationToken);
            _logger.LogWarning(
                "Created the first platform SuperAdmin {Email}. Sign in to set a new password and enrol an authenticator, then remove PlatformAuth:Bootstrap from the configuration.",
                bootstrap.Email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Platform bootstrap skipped (is the PlatformAuth migration applied? is the password strong enough?)");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
