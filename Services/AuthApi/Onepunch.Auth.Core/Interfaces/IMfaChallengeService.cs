using OnePunch.Auth.Domain.Entities;

namespace Onepunch.Auth.Core.Interfaces;

public record MfaChallengeResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Extension point for Flow C(login)'s MFA step ("Submit MFA code" / "Verify MFA code").
/// Deliberately stubbed for now (see NoOpMfaChallengeService) — no TOTP enrollment/verification
/// is implemented yet. Wired into UserService.Login between password verification and token
/// minting so a real implementation can be swapped in later without touching the login flow.
/// </summary>
public interface IMfaChallengeService
{
    Task<bool> IsChallengeRequiredAsync(User user);
    Task<MfaChallengeResult> ValidateAsync(User user, string code);
}
