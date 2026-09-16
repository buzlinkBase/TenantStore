using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace TenantStoreApi.Core.Extensions;

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
        if (user == null) throw new ArgumentNullException(nameof(user));
        // "sub" is the raw claim type AuthApi's JwtService.CreateTokenAsync mints -- AddJwtBearer's
        // MapInboundClaims = false (RegisterSelfServices.cs) keeps it from being rewritten to
        // ClaimTypes.NameIdentifier.
        var value = user.FindFirstValue("sub");
        return Guid.TryParse(value, out Guid guid) ? guid : null;
    }
    public static Guid GetRequiredUserId(this ClaimsPrincipal user)
    {
        if (user == null) throw new ArgumentNullException(nameof(user));
        return user.GetUserId()
               ?? throw new UnauthorizedAccessException("User ID claim is missing or invalid.");
    }
    public static string? GetUserClaim(this ClaimsPrincipal user, string claim)
    {
        if (user == null) throw new ArgumentNullException(nameof(user));
        if (string.IsNullOrWhiteSpace(claim)) throw new ArgumentException("Claim type must be provided.", nameof(claim));

        return user.FindFirstValue(claim);
    }

}
