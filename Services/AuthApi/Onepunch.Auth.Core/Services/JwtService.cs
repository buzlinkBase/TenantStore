using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Onepunch.Auth.Domain.Entities;
using OnePunch.Auth.Domain.Entities;
using Serilog;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;


namespace Onepunch.Auth.Core.Services;

public class JwtService
{
    private readonly UserManager<User> _userManager;
    private readonly TenantRequestService _tenantRequestService;
    private readonly JwtSettings _jwtSettings;
    private readonly RsaKeyProvider _rsaKeyProvider;
    private readonly MembershipCacheService _membershipCacheService;

    public JwtService(UserManager<User> userManager,
        TenantRequestService tenantRequestService,
        IOptions<JwtSettings> jwtSettings,
        RsaKeyProvider rsaKeyProvider,
        MembershipCacheService membershipCacheService)
    {
        _userManager = userManager;
        _tenantRequestService = tenantRequestService;
        _jwtSettings = jwtSettings.Value;
        _rsaKeyProvider = rsaKeyProvider;
        _membershipCacheService = membershipCacheService;
    }
    public async Task<string> GenerateRefreshToken()
    {
        // Generate 32 random bytes
        var randomNumber = new byte[32];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(randomNumber);
        }
        // Convert to Base64 string
        return Convert.ToBase64String(randomNumber);
    }
    public int RefreshExpiry => _jwtSettings.RefreshExpiry;
    public int TokenExpiry => _jwtSettings.TokenExpiry;
    public string Hash(string token)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(token);
        var hashBytes = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hashBytes);
    }
    public async Task<string> CreateTokenAsync(User user) =>
        await CreateTokenAsync(user, user?.DefaultTenantId?.ToString() ?? "", user?.DefaultTenantName ?? "");

    // overrideRoles: for callers who already know the caller's role for this tenant with
    // certainty -- e.g. WorkspaceService.Create, minting a token for the tenant it just
    // requested creation of, whose UserMembership row doesn't exist yet (TenantCreationRequested
    // is handled asynchronously). Without this, the membership-cache lookup below finds nothing
    // and the token carries no role claim at all, so IsOwnerOrAdmin()-style backend checks
    // reject the caller's very first actions until their next token refresh.
    public async Task<string> CreateTokenAsync(User user, string tenantId, string tenantName, IEnumerable<string>? overrideRoles = null)
    {
        string tenantState = TenantCreationStatus.Initial.ToString();
        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            if (Guid.TryParse(tenantId, out Guid parseTenantId))
            {
                var tenantRequest = await _tenantRequestService.FindByTenant(parseTenantId);
                if (tenantRequest != null)
                {
                    tenantState = tenantRequest.Status.ToString();
                }
            }
        }

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? ""),
            new("tenantId", tenantId),
            new("tenantName", tenantName),
            new("tenantState", tenantState),
        };
        // Lets downstream services (e.g. hrms-api) check the caller's role for this tenant
        // without a separate lookup -- MembershipCacheService already has its own fallback (to
        // user.DefaultTenantRoles) if tenant-api/gRPC is unreachable, so this never blocks token
        // issuance; worst case the claim reflects stale/fallback roles, same as today's
        // LoginResponse.Roles behavior.
        if (overrideRoles != null)
        {
            claims.AddRange(overrideRoles.Select(role => new Claim(ClaimTypes.Role, role)));
        }
        else if (Guid.TryParse(tenantId, out var parsedTenantId))
        {
            var memberships = await _membershipCacheService.GetMembershipsAsync(user.Id);
            var currentTenant = memberships.FirstOrDefault(t => t.TenantId == parsedTenantId);
            if (currentTenant != null)
            {
                claims.AddRange(currentTenant.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
                // Same reasoning as Roles above -- lets hrms-api (and other downstream services)
                // check granular permission codes (e.g. "Work Rotation:ManageOwnTeam") without a
                // separate lookup. currentTenant.Permissions was already being resolved here for
                // Roles' sake and simply never embedded until now.
                claims.AddRange(currentTenant.Permissions.Select(code => new Claim("permission", code)));
            }
        }

        // The single most direct signal for "why does this user's token look incomplete" --
        // shows exactly what ended up embedded, searchable by UserId in Seq, regardless of which
        // branch above produced it (overrideRoles, a successful membership lookup, or nothing).
        Log.Logger.Information(
            "JwtService.CreateTokenAsync: user {UserId} tenant {TenantId} embedded {RoleCount} role claim(s), {PermissionCount} permission claim(s)",
            user.Id, tenantId, claims.Count(c => c.Type == ClaimTypes.Role), claims.Count(c => c.Type == "permission"));

        var creds = new SigningCredentials(_rsaKeyProvider.SigningKey, SecurityAlgorithms.RsaSha256);
        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience.FirstOrDefault(),
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_jwtSettings.TokenExpiry),
            signingCredentials: creds
        );
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public TokenInfo? ReadTokenToObject(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return new TokenInfo
            {
                IsValid = false,
                ErrorMessage = "Token is null or empty"
            };
        }

        var signingKeys = new List<SecurityKey> { _rsaKeyProvider.SigningKey };
        if (_jwtSettings.AllowLegacyHmacValidation && !string.IsNullOrEmpty(_jwtSettings.SigningKey))
        {
            // Transition window for the HMAC->RSA/JWKS migration (see JwtSettings.AllowLegacyHmacValidation):
            // accept tokens signed under the old shared secret until they've all expired/refreshed.
            signingKeys.Add(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.SigningKey)));
        }

        var tokenHandler = new JwtSecurityTokenHandler();
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = _jwtSettings.Audience.FirstOrDefault(),
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = signingKeys,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        try
        {
            // Instance-scoped, not the static JwtSecurityTokenHandler.DefaultInboundClaimTypeMap
            // -- InboundClaimTypeMap is a per-instance copy of the default map made when
            // tokenHandler was constructed above, so clearing it here only affects this one
            // ValidateToken call. Clearing the *static* default (as this used to do) mutated
            // process-wide, permanent state the first time this method ever ran, silently
            // changing how every other request's JWT Bearer authentication on this same process
            // mapped claims from then on -- see AddJwtBearer's MapInboundClaims = false
            // (ServiceRegistrations.cs), which now achieves the same "read raw claim names"
            // result deliberately and consistently instead.
            tokenHandler.InboundClaimTypeMap.Clear();

            var principal = tokenHandler.ValidateToken(token, validationParameters, out SecurityToken validatedToken);
            var jwtToken = validatedToken as JwtSecurityToken;

            // Custom Tenant Claims
            var tenantId = principal.FindFirst("tenantId")?.Value ?? Guid.Empty.ToString();
            var tenantName = principal.FindFirst("tenantName")?.Value ?? "";

            return new TokenInfo
            {
                JTI = Guid.Parse(principal.FindFirst("jti")?.Value ?? Guid.Empty.ToString()),
                UserId = Guid.Parse(principal.FindFirst("sub")?.Value ?? Guid.Empty.ToString()),
                Email = principal.FindFirst("email")?.Value,
                IsValid = true,
                IsExpired = false,
                ExpiresAt = (validatedToken as JwtSecurityToken)?.ValidTo,
                TenantId = Guid.Parse(tenantId),
                TenantName = tenantName,
            };
        }
        catch (SecurityTokenExpiredException ex)
        {
            return new TokenInfo
            {
                IsValid = false,
                IsExpired = true,
                ErrorMessage = "Token has expired",
                ExpiresAt = ex.Expires
            };
        }
        catch (Exception ex)
        {
            return new TokenInfo
            {
                IsValid = false,
                IsExpired = false,
                ErrorMessage = $"Invalid token: {ex.Message}"
            };
        }
    }
    public string GenerateKey(int size = 32)
    {
        var keyBytes = new byte[size];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(keyBytes);
        return Convert.ToBase64String(keyBytes);
    }
}

