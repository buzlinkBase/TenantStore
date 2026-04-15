
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace OnePunch.Auth.Api.Extensions;

public static class HttpRequestExtensions
{
    public static string? GetHeader(this HttpRequest request, string key)
    {
        return request.Headers.TryGetValue(key, out var headerValue)
            ? headerValue.FirstOrDefault()
            : null;
    }

    // Specific helper for Bearer Tokens
    public static string? GetAuthorizationToken(this HttpRequest request)
    {
        var authHeader = request.GetHeader("Authorization");
        if (authHeader != null && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return authHeader.Substring("Bearer ".Length).Trim();
        }
        return null;
    }


    public static Guid? GetUserId(this ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out Guid guid) ? guid : null;
    }

    // The "Strict" version (throws an exception if not found)
    public static Guid GetRequiredUserId(this ClaimsPrincipal user)
    {
        return user.GetUserId()
               ?? throw new UnauthorizedAccessException("User ID claim is missing or invalid.");
    }
}
