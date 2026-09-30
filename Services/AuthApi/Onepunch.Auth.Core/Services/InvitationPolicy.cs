using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using OnePunch.Auth.Domain.Entities;

namespace Onepunch.Auth.Core.Services;

/// <summary>
/// The rules every invitation follows, single or batch: who may send one, which roles it may
/// grant, how long its link lasts, and what counts as a usable email.
/// </summary>
public static class InvitationPolicy
{
    /// <summary>Tenant Service catalog code (RbacServices "Users" row).</summary>
    public const string InvitePermission = "Users:Invite";

    public const int MaxBatchSize = 100;

    /// <summary>How long an invitation link stays valid.</summary>
    public static readonly TimeSpan LinkLifetime = TimeSpan.FromDays(7);

    private static readonly EmailAddressAttribute EmailFormat = new();

    /// <summary>
    /// Holders of "Users:Invite" in their current company token. The Owner and Admin roles pass
    /// on their own, as they did before the permission existed (a token minted before the catalog
    /// gained the code has their roles but not the code yet).
    /// </summary>
    public static bool CanInvite(ClaimsPrincipal principal, User? user = null) =>
        principal.FindAll("permission").Any(c => c.Value == InvitePermission) ||
        principal.FindAll(ClaimTypes.Role).Any(c => c.Value is "Owner" or "Admin") ||
        (user?.DefaultTenantRoles.Any(r => r is "Owner" or "Admin") ?? false);

    /// <summary>
    /// Any role the company can assign -- system or custom -- except Owner, which only an ownership
    /// transfer grants. Tenant Service resolves the name when the invitation is accepted.
    /// </summary>
    public static string? RoleError(string? role) =>
        string.IsNullOrWhiteSpace(role) ? "A role is required."
        : string.Equals(role.Trim(), "Owner", StringComparison.OrdinalIgnoreCase) ? "The Owner role can't be granted by invitation."
        : null;

    public static string Normalize(string email) => email.Trim().ToLowerInvariant();

    public static bool IsValidEmail(string email) =>
        !string.IsNullOrWhiteSpace(email) && email.Length <= 254 && EmailFormat.IsValid(email) && !email.Contains(' ');
}
