using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Onepunch.Common.Lib.Cache;
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

    public JwtService(UserManager<User> userManager,
        TenantRequestService tenantRequestService,
        IOptions<JwtSettings> jwtSettings)
    {
        _userManager = userManager;
        _tenantRequestService = tenantRequestService;
        _jwtSettings = jwtSettings.Value;
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
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.SigningKey));
        string tenantState = "Pending";

        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            if (Guid.TryParse(tenantId, out Guid parseTenantId))
            {
                var tenantRequest = await _tenantRequestService.FindOne(parseTenantId);
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
            new("TenantId", tenantId),
            new("TenantName", tenantName),
            new("TenantState", tenantState),
            new("TenantMemberRole", user.DefaultTenantRole ?? ""),
        };

        var roles = await _userManager.GetRolesAsync(user);
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
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
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.SigningKey));
        var tokenHandler = new JwtSecurityTokenHandler();
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = _jwtSettings.Audience.FirstOrDefault(),
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        try
        {
            var principal = tokenHandler.ValidateToken(token, validationParameters, out SecurityToken validatedToken);
            var tenantId = principal.FindFirst("TenantId")?.Value ?? Guid.Empty.ToString();
            var tenantName = principal.FindFirst("TenantName")?.Value ?? "";
            return new TokenInfo
            {
                JTI = Guid.Parse(principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value ?? Guid.Empty.ToString()),
                UserId = Guid.Parse(principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? Guid.Empty.ToString()),
                Email = principal.FindFirst(JwtRegisteredClaimNames.Email)?.Value,
                Roles = principal.FindAll(ClaimTypes.Role).Select(r => r.Value).ToList(),
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

