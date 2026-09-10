using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Onepunch.Auth.Domain.Entities;
using OnePunch.Auth.Domain.Entities;
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

    public JwtService(UserManager<User> userManager,
        TenantRequestService tenantRequestService,
        IOptions<JwtSettings> jwtSettings,
        RsaKeyProvider rsaKeyProvider)
    {
        _userManager = userManager;
        _tenantRequestService = tenantRequestService;
        _jwtSettings = jwtSettings.Value;
        _rsaKeyProvider = rsaKeyProvider;
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
    public async Task<string> CreateTokenAsync(User user, string tenantId, string tenantName)
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
            // Clear the inbound map so .NET stops changing standard JWT claim names into XML URIs
            JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

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

