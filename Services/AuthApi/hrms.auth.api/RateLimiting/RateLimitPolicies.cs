using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Onepunch.Auth.Core.Portal;
using Onepunch.Common.Lib;

namespace OnePunch.Auth.Api.RateLimiting;

/// <summary>Whose requests share one allowance.</summary>
public enum RateLimitKey
{
    /// <summary>The signed-in user (falls back to the IP when there's none).</summary>
    User,
    /// <summary>The client IP -- for anonymous endpoints, where there's no user to go on.</summary>
    Ip,
    /// <summary>
    /// The end user's IP as told by the Client Portal's server (header <c>X-Portal-Client-Ip</c>, honoured only with the portal's service
    /// key). Every portal sign-in arrives from that one server, so the connection IP would put all of its users in a single allowance.
    /// </summary>
    PortalClient,
}

public sealed record RateLimitRule(string Name, RateLimitKey Key, int PermitLimit, TimeSpan Window);

/// <summary>
/// Per-endpoint limits for what's abusable or expensive: anonymous sign-in / sign-up / password
/// reset / invitation-token endpoints (credential stuffing, email bombing, token guessing) and
/// authenticated actions that send email. Endpoints opt in with [EnableRateLimiting(name)]; there
/// is deliberately no global limit, since a whole office can share one public IP. Each limit can
/// be tuned per environment: RateLimiting:{name}:PermitLimit / WindowSeconds.
/// </summary>
public static class RateLimitPolicies
{
    public const string Login = "auth-login";
    public const string EmailSending = "auth-email";
    public const string TokenLookup = "auth-token";
    public const string Refresh = "auth-refresh";
    public const string PasswordChange = "auth-password";
    public const string InviteSend = "invite-send";
    public const string InviteCheck = "invite-check";
    public const string WorkspaceCreate = "workspace-create";
    public const string PlatformMfa = "platform-mfa";
    public const string PlatformSession = "platform-session";
    public const string PlatformAdmin = "platform-admin";
    public const string PortalLogin = "portal-login";
    public const string PortalRefresh = "portal-refresh";
    public const string PortalEmail = "portal-email";
    public const string PortalPassword = "portal-password";

    public static readonly IReadOnlyList<RateLimitRule> Defaults =
    [
        new(Login, RateLimitKey.Ip, 30, TimeSpan.FromMinutes(1)),
        new(EmailSending, RateLimitKey.Ip, 5, TimeSpan.FromMinutes(15)),
        new(TokenLookup, RateLimitKey.Ip, 20, TimeSpan.FromMinutes(1)),
        // The frontend refreshes silently all day, for every tab of everyone behind one IP.
        new(Refresh, RateLimitKey.Ip, 120, TimeSpan.FromMinutes(1)),
        new(PasswordChange, RateLimitKey.User, 5, TimeSpan.FromMinutes(5)),
        new(InviteSend, RateLimitKey.User, 10, TimeSpan.FromMinutes(1)),
        // The batch invite modal's live lookups fire as the admin types.
        new(InviteCheck, RateLimitKey.User, 60, TimeSpan.FromMinutes(1)),
        new(WorkspaceCreate, RateLimitKey.User, 5, TimeSpan.FromHours(1)),
        // Platform operators: a 6-digit code is guessable, so the code-checking steps get their own,
        // tighter bucket (not shared with company-user sign-in), and operator management is throttled.
        new(PlatformMfa, RateLimitKey.Ip, 10, TimeSpan.FromMinutes(1)),
        new(PlatformSession, RateLimitKey.User, 60, TimeSpan.FromMinutes(1)),
        new(PlatformAdmin, RateLimitKey.User, 30, TimeSpan.FromMinutes(1)),
        // Client Portal users: the same limits as everyone else's, but counted per end user's address (see RateLimitKey.PortalClient).
        new(PortalLogin, RateLimitKey.PortalClient, 30, TimeSpan.FromMinutes(1)),
        new(PortalRefresh, RateLimitKey.PortalClient, 120, TimeSpan.FromMinutes(1)),
        new(PortalEmail, RateLimitKey.PortalClient, 5, TimeSpan.FromMinutes(15)),
        new(PortalPassword, RateLimitKey.PortalClient, 10, TimeSpan.FromMinutes(5)),
    ];

    public static IServiceCollection AddAppRateLimiting(this IServiceCollection services, IConfiguration configuration) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, token) => new ValueTask(WriteRejectionAsync(context.HttpContext, context.Lease, token));
            foreach (var rule in Defaults.Select(r => Configured(r, configuration)))
                options.AddPolicy(rule.Name, context => PartitionFor(context, rule));
        });

    public static RateLimitRule Configured(RateLimitRule rule, IConfiguration configuration) => rule with
    {
        PermitLimit = configuration.GetValue($"RateLimiting:{rule.Name}:PermitLimit", rule.PermitLimit),
        Window = TimeSpan.FromSeconds(configuration.GetValue($"RateLimiting:{rule.Name}:WindowSeconds", rule.Window.TotalSeconds)),
    };

    public static string PartitionKey(HttpContext context, RateLimitKey key)
    {
        var ip = $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
        if (key == RateLimitKey.User && context.User.FindFirst("sub")?.Value is { Length: > 0 } user)
            return $"user:{user}";
        if (key == RateLimitKey.PortalClient && PortalClientAddress(context) is { } portalClient)
            return $"portal:{portalClient}";
        return ip;
    }

    /// <summary>
    /// The end user's address as the Client Portal's server reports it -- but only when the request proves it is that server (its service key).
    /// Anyone else naming an address gets their own connection's allowance, so the header cannot be used to dodge or to burn someone else's limit.
    /// </summary>
    private static string? PortalClientAddress(HttpContext context)
    {
        var settings = context.RequestServices?.GetService<IOptions<PortalAuthSettings>>()?.Value;
        if (settings is null || !PortalTokenValidation.KeyMatches(context.Request.Headers[PortalAuthSettings.KeyHeader].ToString(), settings.ServiceKey))
            return null;
        return IPAddress.TryParse(context.Request.Headers[PortalAuthSettings.ClientIpHeader].ToString(), out var address) ? address.ToString() : null;
    }

    public static RateLimitPartition<string> PartitionFor(HttpContext context, RateLimitRule rule) =>
        RateLimitPartition.GetFixedWindowLimiter($"{rule.Name}|{PartitionKey(context, rule.Key)}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rule.PermitLimit,
                Window = rule.Window,
                QueueLimit = 0,
                AutoReplenishment = true,
            });

    // Same envelope as GlobalExceptionHandler, so the frontend shows the detail like any error.
    public static async Task WriteRejectionAsync(HttpContext context, RateLimitLease lease, CancellationToken token)
    {
        var retryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out var wait) ? (int)Math.Ceiling(wait.TotalSeconds) : 60;
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers.RetryAfter = retryAfter.ToString(CultureInfo.InvariantCulture);
        await context.Response.WriteAsJsonAsync(new ResponseModel<ProblemDetails>
        {
            Status = StatusCodes.Status429TooManyRequests,
            Message = "Error",
            Data = new ProblemDetails
            {
                Type = "https://httpstatuses.com/429",
                Title = "Too Many Requests",
                Status = StatusCodes.Status429TooManyRequests,
                Detail = $"Too many requests. Please wait {retryAfter} seconds and try again.",
                Instance = $"{context.Request.Method} {context.Request.Path}",
            },
        }, token);
    }
}
