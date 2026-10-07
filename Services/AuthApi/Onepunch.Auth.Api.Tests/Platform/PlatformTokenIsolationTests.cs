using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Onepunch.Auth.Core.Platform;
using Onepunch.Auth.Core.Services;
using Onepunch.Common.Lib.Platform;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Xunit;

namespace Onepunch.Auth.Api.Tests.Platform;

/// <summary>
/// Platform operators and tenant users must never be able to pass for each other: different signing
/// key, issuer and audience. These tests mint tokens both ways and check each side rejects the other.
/// </summary>
public class PlatformTokenIsolationTests : IDisposable
{
    private readonly PlatformAuthHarness _h = new();
    private readonly string _tenantKeyDirectory = Path.Combine(Path.GetTempPath(), "tenant-key-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        _h.Dispose();
        if (Directory.Exists(_tenantKeyDirectory)) Directory.Delete(_tenantKeyDirectory, recursive: true);
    }

    private static bool Validates(string jwt, TokenValidationParameters parameters)
    {
        try
        {
            new JwtSecurityTokenHandler { MapInboundClaims = false }.ValidateToken(jwt, parameters, out _);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private string TenantStyleToken()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["JwtSettings:SigningKeyPath"] = Path.Combine(_tenantKeyDirectory, "auth.key") })
            .Build();
        var tenantKey = new RsaKeyProvider(config, new EphemeralDataProtectionProvider());
        var token = new JwtSecurityToken(
            issuer: "Onepunch", audience: "Onepunch.AuthService",
            claims: [new Claim("sub", Guid.NewGuid().ToString()), new Claim("email", "owner@tenant.com")],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(tenantKey.SigningKey, SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [Fact]
    public async Task ARealPlatformAccessToken_ValidatesAgainstThePlatformKey()
    {
        var user = await _h.SeedUserAsync();
        var (access, _) = _h.Tokens.CreateAccessToken(user);

        Validates(access, PlatformTokenValidation.AccessToken(_h.Keys.SigningKey)).Should().BeTrue();
    }

    [Fact]
    public void ATenantToken_IsRejectedByThePlatformValidation()
    {
        Validates(TenantStyleToken(), PlatformTokenValidation.AccessToken(_h.Keys.SigningKey)).Should().BeFalse();
    }

    [Fact]
    public async Task APlatformToken_IsRejectedByTheTenantValidation()
    {
        var user = await _h.SeedUserAsync();
        var (access, _) = _h.Tokens.CreateAccessToken(user);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["JwtSettings:SigningKeyPath"] = Path.Combine(_tenantKeyDirectory, "auth.key") })
            .Build();
        var tenantKey = new RsaKeyProvider(config, new EphemeralDataProtectionProvider());

        Validates(access, new TokenValidationParameters
        {
            ValidateIssuer = true, ValidIssuer = "Onepunch",
            ValidateAudience = true, ValidAudience = "Onepunch.AuthService",
            ValidateIssuerSigningKey = true, IssuerSigningKey = tenantKey.SigningKey,
            ValidateLifetime = true,
        }).Should().BeFalse();
    }

    [Fact]
    public async Task AChallengeToken_CannotBeUsedAsAnAccessToken()
    {
        var user = await _h.SeedUserAsync();
        var (challenge, _) = _h.Tokens.CreateChallengeToken(user);

        Validates(challenge, PlatformTokenValidation.AccessToken(_h.Keys.SigningKey)).Should().BeFalse();
    }

    [Fact]
    public async Task AnAccessToken_CannotBeUsedAsAChallengeToken()
    {
        var user = await _h.SeedUserAsync();
        var (access, _) = _h.Tokens.CreateAccessToken(user);

        _h.Tokens.ReadChallengeToken(access).Should().BeNull();
    }

    [Fact]
    public async Task ATamperedToken_IsRejected()
    {
        var user = await _h.SeedUserAsync();
        var (access, _) = _h.Tokens.CreateAccessToken(user);
        var parts = access.Split('.');
        var tampered = $"{parts[0]}.{parts[1]}A.{parts[2]}";

        Validates(tampered, PlatformTokenValidation.AccessToken(_h.Keys.SigningKey)).Should().BeFalse();
    }

    [Fact]
    public async Task TheTwoSigningKeys_AreDifferent()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["JwtSettings:SigningKeyPath"] = Path.Combine(_tenantKeyDirectory, "auth.key") })
            .Build();
        var tenantKey = new RsaKeyProvider(config, new EphemeralDataProtectionProvider());
        await Task.CompletedTask;

        _h.Keys.Kid.Should().NotBe(tenantKey.Kid);
    }
}
