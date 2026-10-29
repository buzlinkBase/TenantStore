using System.Security.Claims;

namespace Onepunch.Common.Lib.Security;

/// <summary>
/// The claims an impersonation token carries on top of the target user's own. <c>sub</c>, roles,
/// permissions and tenant stay the target's, so every existing check treats the session as that user;
/// these identify the support operator behind it. They are signed into the token, so a client can't
/// add, drop or change them.
/// </summary>
public static class ImpersonationClaims
{
    public const string Marker = "imp";
    public const string ActorId = "act_sub";
    public const string ActorEmail = "act_email";
    public const string ActorRole = "act_role";
    public const string SessionId = "imp_sid";

    /// <summary>The shared-cache key set when a session is stopped, so every API refuses its token at once.</summary>
    public static string EndedCacheKey(Guid sessionId) => "imp-ended:" + sessionId;

    public static bool IsImpersonating(this ClaimsPrincipal user) =>
        user.FindFirst(Marker)?.Value == "1";

    public static string? GetActorId(this ClaimsPrincipal user) =>
        user.IsImpersonating() ? user.FindFirst(ActorId)?.Value : null;

    public static Guid? GetImpersonationSessionId(this ClaimsPrincipal user) =>
        user.IsImpersonating() && Guid.TryParse(user.FindFirst(SessionId)?.Value, out var id) ? id : null;
}
