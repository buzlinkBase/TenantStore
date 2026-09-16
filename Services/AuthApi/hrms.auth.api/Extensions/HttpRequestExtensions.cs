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
        if (user == null) return null;
        // "sub" is the raw claim type JwtService.CreateTokenAsync mints -- AddJwtBearer's
        // MapInboundClaims = false (ServiceRegistrations.cs) keeps it from being rewritten to
        // ClaimTypes.NameIdentifier, so this reads the same short name end to end.
        var value = user.FindFirstValue("sub");
        return Guid.TryParse(value, out Guid guid) ? guid : null;
    }

    // The "Strict" version (throws an exception if not found)
    public static Guid GetRequiredUserId(this ClaimsPrincipal user)
    {
        return user.GetUserId()
               ?? throw new UnauthorizedAccessException("User Id claim is missing or invalid.");
    }
    public static string? GetUserClaim(this ClaimsPrincipal user, string claim)
    {
        if (user == null) throw new ArgumentNullException(nameof(user));
        if (string.IsNullOrWhiteSpace(claim)) throw new ArgumentException("Claim type must be provided.", nameof(claim));

        return user.FindFirstValue(claim);
    }

    /// <summary>A user can hold multiple values for the same claim type (e.g. multiple tenant
    /// roles) — this returns all of them, unlike GetUserClaim's single-value FindFirstValue.</summary>
    public static List<string> GetUserClaims(this ClaimsPrincipal user, string claim)
    {
        if (user == null) throw new ArgumentNullException(nameof(user));
        if (string.IsNullOrWhiteSpace(claim)) throw new ArgumentException("Claim type must be provided.", nameof(claim));

        return user.FindAll(claim).Select(c => c.Value).ToList();
    }
}
