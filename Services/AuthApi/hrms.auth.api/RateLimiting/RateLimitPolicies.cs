using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Onepunch.Common.Lib;

namespace OnePunch.Auth.Api.RateLimiting;

/// <summary>Whose requests share one allowance.</summary>
public enum RateLimitKey
{
    /// <summary>The signed-in user (falls back to the IP when there's none).</summary>
    User,
    /// <summary>The client IP -- for anonymous endpoints, where there's no user to go on.</summary>
    Ip,
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
        return key == RateLimitKey.User && context.User.FindFirst("sub")?.Value is { Length: > 0 } user
            ? $"user:{user}"
            : ip;
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
