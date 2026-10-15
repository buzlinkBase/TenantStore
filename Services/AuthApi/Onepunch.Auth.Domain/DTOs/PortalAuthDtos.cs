namespace Onepunch.Auth.Domain.DTOs;

// Wire contracts of the Client Portal's sign-in (api/v1/portal/auth). Only the portal's server side
// calls these, so refresh tokens travel in the body, never a browser cookie.

public record PortalLoginRequest(string Email, string Password);

public record PortalRefreshRequest(string RefreshToken);

public record PortalForgotPasswordRequest(string Email);

public record PortalResetPasswordRequest(string Email, string Token, string NewPassword);

/// <summary>Asks for a login to exist for a person the portal has just verified an invitation for.</summary>
public record PortalEnsureAccountRequest(string Email, string FullName, string? Password);

/// <summary>
/// <see cref="Created"/> is false when the email already had an account (their password was left alone and
/// they sign in with it). <see cref="AccountId"/> is the person's id in AuthApi: the token's <c>sub</c>.
/// </summary>
public record PortalEnsureAccountResult(Guid AccountId, bool Created);

public record PortalSession(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc,
    Guid AccountId,
    string Email,
    string? FullName);
