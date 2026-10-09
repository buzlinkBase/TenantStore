using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace TenantStoreApi.Security;

/// <summary>
/// One limit for everything under <c>platform/*</c>. Only the Platform Admin Console's server calls
/// these endpoints, so the limit is per client IP (the console's) and generous for a person clicking
/// around; it exists to cap a stolen or looping token, not to shape normal use. Tunable per
/// environment: RateLimiting:platform:PermitLimit / WindowSeconds.
/// </summary>
public static class PlatformRateLimiting
{
    public const string PolicyName = "platform";

    public const int DefaultPermitLimit = 120;
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromMinutes(1);

    public static IServiceCollection AddPlatformRateLimiting(this IServiceCollection services, IConfiguration configuration) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, token) => new ValueTask(WriteRejectionAsync(context.HttpContext, context.Lease, token));
            options.AddPolicy(PolicyName, context => PartitionFor(context, PermitLimit(configuration), Window(configuration)));
        });

    public static int PermitLimit(IConfiguration configuration) =>
        configuration.GetValue($"RateLimiting:{PolicyName}:PermitLimit", DefaultPermitLimit);

    public static TimeSpan Window(IConfiguration configuration) =>
        TimeSpan.FromSeconds(configuration.GetValue($"RateLimiting:{PolicyName}:WindowSeconds", DefaultWindow.TotalSeconds));

    public static RateLimitPartition<string> PartitionFor(HttpContext context, int permitLimit, TimeSpan window) =>
        RateLimitPartition.GetFixedWindowLimiter(
            $"{PolicyName}|ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = window,
                QueueLimit = 0,
                AutoReplenishment = true,
            });

    public static async Task WriteRejectionAsync(HttpContext context, RateLimitLease lease, CancellationToken token)
    {
        var retryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out var wait) ? (int)Math.Ceiling(wait.TotalSeconds) : 60;
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers.RetryAfter = retryAfter.ToString(CultureInfo.InvariantCulture);
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Type = "https://httpstatuses.com/429",
            Title = "Too Many Requests",
            Status = StatusCodes.Status429TooManyRequests,
            Detail = $"Too many requests. Please wait {retryAfter} seconds and try again.",
            Instance = $"{context.Request.Method} {context.Request.Path}",
        }, token);
    }
}
