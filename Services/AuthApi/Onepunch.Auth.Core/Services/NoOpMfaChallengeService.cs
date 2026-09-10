using Onepunch.Auth.Core.Interfaces;
using OnePunch.Auth.Domain.Entities;

namespace Onepunch.Auth.Core.Services;

/// <summary>
/// Default no-op MFA implementation: always reports that no challenge is required, so login
/// behavior is completely unchanged until a real (e.g. TOTP) implementation is registered in
/// its place. Excluded from the assembly-scanning auto-registration convention (see
/// LibServicesRegistrations) since it's registered explicitly against IMfaChallengeService.
/// </summary>
[ServiceRegistration(exclude: true)]
public class NoOpMfaChallengeService : IMfaChallengeService
{
    public Task<bool> IsChallengeRequiredAsync(User user) => Task.FromResult(false);

    public Task<MfaChallengeResult> ValidateAsync(User user, string code) =>
        Task.FromResult(new MfaChallengeResult { Success = true });
}
