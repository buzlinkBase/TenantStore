using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Onepunch.AuditEngine.AspNetCore;

/// <summary>Which claims identify the actor and organization; defaults fit a standard JWT.</summary>
public sealed class ClaimsAuditActorOptions
{
    public IList<string> UserIdClaimTypes { get; } = ["sub", ClaimTypes.NameIdentifier];
    public IList<string> DisplayNameClaimTypes { get; } = ["name", ClaimTypes.Name, "email", ClaimTypes.Email];
    public IList<string> OrganizationClaimTypes { get; } = ["tenantId"];

    /// <summary>
    /// Present only in an impersonation token: the support operator signed in as the user. When set,
    /// the operator is the event's actor and the user they acted as goes to metadata, so the trail names both.
    /// </summary>
    public string OperatorIdClaimType { get; set; } = "act_sub";
    public string OperatorNameClaimType { get; set; } = "act_email";
    public string ImpersonationSessionClaimType { get; set; } = "imp_sid";

    /// <summary>
    /// The operator's role inside an impersonation token, and the roles whose sessions are not
    /// audited at all (no entity changes, no per-request events). Other operators stay fully audited.
    /// </summary>
    public string OperatorRoleClaimType { get; set; } = "act_role";
    public ISet<string> UnauditedOperatorRoles { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SuperAdmin" };

    /// <summary>True for an impersonation session whose operator role is in <see cref="UnauditedOperatorRoles"/>.</summary>
    public bool IsUnauditedSession(ClaimsPrincipal? user) =>
        user?.Identity?.IsAuthenticated == true
        && user.FindFirst(OperatorIdClaimType) != null
        && user.FindFirst(OperatorRoleClaimType)?.Value is { } role
        && UnauditedOperatorRoles.Contains(role);

    /// <summary>
    /// Consulted before the organization claims, for hosts that know the organization some other
    /// way too (a header, or a tenant set by a message consumer where there is no request at
    /// all). Return null to fall back to the claims.
    /// </summary>
    public Func<IServiceProvider, string?>? OrganizationResolver { get; set; }
}

/// <summary>
/// The actor is the authenticated user of the current request; an unauthenticated request is
/// <see cref="AuditActor.Anonymous"/>; no request at all (a background job or message consumer)
/// is the host service itself.
/// </summary>
public class ClaimsPrincipalAuditActorResolver(
    IHttpContextAccessor httpContextAccessor,
    AuditEngineOptions engineOptions,
    ClaimsAuditActorOptions claimOptions,
    IServiceProvider services) : IAuditActorResolver
{
    protected ClaimsPrincipal? User => httpContextAccessor.HttpContext?.User;

    public virtual AuditActor ResolveActor()
    {
        if (httpContextAccessor.HttpContext == null) return AuditActor.System(engineOptions.SourceName);

        var userId = FirstClaim(claimOptions.UserIdClaimTypes);
        if (userId == null) return AuditActor.Anonymous;

        var operatorId = FirstClaim([claimOptions.OperatorIdClaimType]);
        if (operatorId != null)
            return new AuditActor(operatorId, FirstClaim([claimOptions.OperatorNameClaimType]) ?? operatorId, AuditActorTypes.Operator);

        return AuditActor.User(userId, FirstClaim(claimOptions.DisplayNameClaimTypes) ?? userId);
    }

    public virtual bool IsAuditSuppressed() => claimOptions.IsUnauditedSession(User);

    public virtual string? ResolveOrganizationId() =>
        claimOptions.OrganizationResolver?.Invoke(services) ?? FirstClaim(claimOptions.OrganizationClaimTypes);

    protected string? FirstClaim(IEnumerable<string> claimTypes)
    {
        var user = User;
        if (user?.Identity?.IsAuthenticated != true) return null;
        return claimTypes
            .Select(type => user.FindFirst(type)?.Value)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}

/// <summary>Records where the action came from. Behind a proxy, run UseForwardedHeaders first.</summary>
public sealed class HttpContextAuditMetadataEnricher(IHttpContextAccessor httpContextAccessor, ClaimsAuditActorOptions? claimOptions = null) : IAuditMetadataEnricher
{
    private readonly ClaimsAuditActorOptions _claimOptions = claimOptions ?? new();

    public const string OnBehalfOfUserId = "OnBehalfOfUserId";
    public const string OnBehalfOfName = "OnBehalfOfName";
    public const string ImpersonationSessionId = "ImpersonationSessionId";

    public const string IpAddress = "IpAddress";
    public const string UserAgent = "UserAgent";
    public const string HttpMethod = "HttpMethod";
    public const string Path = "Path";
    public const string TraceId = "TraceId";

    public void Enrich(IDictionary<string, string?> metadata)
    {
        var context = httpContextAccessor.HttpContext;
        if (context == null) return;

        metadata[IpAddress] = ClientIp(context);
        metadata[UserAgent] = context.Request.Headers.UserAgent.ToString() is { Length: > 0 } agent ? agent : null;
        metadata[HttpMethod] = context.Request.Method;
        metadata[Path] = context.Request.Path.Value;
        metadata[TraceId] = context.TraceIdentifier;

        // An impersonation session: the actor is the operator, so say whom they were acting as.
        var user = context.User;
        if (user.Identity?.IsAuthenticated == true && user.FindFirst(_claimOptions.OperatorIdClaimType) != null)
        {
            metadata[OnBehalfOfUserId] = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            metadata[OnBehalfOfName] = user.FindFirst("email")?.Value ?? user.FindFirst(ClaimTypes.Email)?.Value;
            metadata[ImpersonationSessionId] = user.FindFirst(_claimOptions.ImpersonationSessionClaimType)?.Value;
        }
    }

    private static string? ClientIp(HttpContext context)
    {
        var forwarded = context.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(forwarded))
            return forwarded.Split(',')[0].Trim();
        return context.Connection.RemoteIpAddress?.ToString();
    }
}

public static class AspNetCoreAuditBuilderExtensions
{
    /// <summary>
    /// Claims-based actor resolution plus request metadata. A host with its own identity scheme
    /// can still call <c>UseActorResolver&lt;T&gt;()</c> afterwards (the last registration wins).
    /// </summary>
    public static AuditEngineBuilder UseAspNetCore(this AuditEngineBuilder builder, Action<ClaimsAuditActorOptions>? configureClaims = null)
    {
        var claimOptions = new ClaimsAuditActorOptions();
        configureClaims?.Invoke(claimOptions);

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton(claimOptions);
        builder.UseActorResolver<ClaimsPrincipalAuditActorResolver>();
        builder.AddMetadataEnricher<HttpContextAuditMetadataEnricher>();
        return builder;
    }
}
