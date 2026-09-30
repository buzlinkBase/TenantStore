using Onepunch.Auth.Domain.Entities;

namespace Onepunch.Auth.Core.Services;

/// <summary>The company a refreshed session is scoped to, and whether it's one the user is still creating.</summary>
public sealed record SessionTenant(Guid? TenantId, string TenantName, bool CreationPending);

/// <summary>
/// Decides which company a token refresh re-mints for: the one the session was working in
/// (remembered on its refresh token), as long as the user still belongs to it -- otherwise the
/// account's default company, which is also where sessions from before this existed go.
/// </summary>
public static class SessionTenantResolver
{
    /// <param name="sessionTenantId">The company on the refresh token; null for older tokens.</param>
    /// <param name="memberships">The user's current active memberships.</param>
    /// <param name="ownCreationPending">
    /// True when the session's company is one this user requested and is still being set up --
    /// it has no membership row yet, but it's theirs (as Owner) all the same.
    /// </param>
    public static SessionTenant Resolve(
        Guid? sessionTenantId,
        string? sessionTenantName,
        Guid? defaultTenantId,
        string? defaultTenantName,
        IReadOnlyCollection<UsersTenant> memberships,
        bool ownCreationPending)
    {
        if (sessionTenantId is { } id && id != Guid.Empty)
        {
            var membership = memberships.FirstOrDefault(t => t.TenantId == id);
            // The membership's name is current (a rename since sign-in shows up here).
            if (membership != null)
                return new SessionTenant(id, FirstNonEmpty(membership.Name, sessionTenantName), false);
            if (ownCreationPending)
                return new SessionTenant(id, sessionTenantName ?? "", true);
        }

        // No company on the token, or the user no longer belongs to it (revoked, removed).
        var defaultMembership = memberships.FirstOrDefault(t => t.TenantId == defaultTenantId);
        return new SessionTenant(
            defaultTenantId is { } d && d != Guid.Empty ? d : null,
            FirstNonEmpty(defaultMembership?.Name, defaultTenantName),
            false);
    }

    private static string FirstNonEmpty(string? first, string? second) =>
        !string.IsNullOrWhiteSpace(first) ? first : second ?? "";
}
