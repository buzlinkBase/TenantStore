using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using Onepunch.Common.Lib.Platform;
using Onepunch.Common.Lib.Security;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using TenantStoreApi.Security;
using Xunit;

namespace TenantStoreApi.Tests.Platform;

/// <summary>
/// Validates platform access tokens the way TenantApi's "Platform" scheme does -- RS256 against the
/// JWKS AuthApi publishes for platform operators -- including everything that must be refused.
/// </summary>
public class PlatformTokenValidationTests : IDisposable
{
    private const string AuthUrl = "http://auth-api:8080/";

    private readonly RSA _platformRsa = RSA.Create(2048);
    private readonly RSA _otherRsa = RSA.Create(2048);
    private readonly RsaSecurityKey _platformKey;
    private readonly RsaSecurityKey _otherKey;
    private readonly RecordingHandler _handler;
    private readonly TokenValidationParameters _parameters;

    public PlatformTokenValidationTests()
    {
        _platformKey = new RsaSecurityKey(_platformRsa) { KeyId = "platform-kid" };
        _otherKey = new RsaSecurityKey(_otherRsa) { KeyId = "other-kid" };
        _handler = new RecordingHandler(JwksOf(_platformRsa, "platform-kid"));
        var cache = new JwksKeyCache(new FakeFactory(_handler), new NullJwksStore(), AuthUrl, jwksPath: PlatformAuthDefaults.JwksPath);
        _parameters = PlatformAuthRegistration.CreateValidationParameters(cache);
    }

    public void Dispose()
    {
        _platformRsa.Dispose();
        _otherRsa.Dispose();
    }

    private static string JwksOf(RSA rsa, string kid)
    {
        var p = rsa.ExportParameters(false);
        return $$"""{"keys":[{"kty":"RSA","use":"sig","alg":"RS256","kid":"{{kid}}","n":"{{Base64UrlEncoder.Encode(p.Modulus)}}","e":"{{Base64UrlEncoder.Encode(p.Exponent)}}"}]}""";
    }

    private static string Mint(
        SecurityKey key, string alg = SecurityAlgorithms.RsaSha256,
        string issuer = PlatformAuthDefaults.Issuer, string audience = PlatformAuthDefaults.Audience,
        DateTime? expires = null, IEnumerable<Claim>? claims = null)
    {
        var token = new JwtSecurityToken(
            issuer, audience,
            claims ?? [new Claim("sub", Guid.NewGuid().ToString()), new Claim(PlatformAuthDefaults.PermissionClaim, PlatformPermissions.AccountsRead)],
            notBefore: DateTime.UtcNow.AddMinutes(-10),
            expires: expires ?? DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(key, alg));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private bool Validates(string jwt)
    {
        try
        {
            new JwtSecurityTokenHandler { MapInboundClaims = false }.ValidateToken(jwt, _parameters, out _);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    [Fact]
    public void AValidPlatformToken_IsAccepted()
    {
        Validates(Mint(_platformKey)).Should().BeTrue();
    }

    [Fact]
    public void TheKeysAreFetchedFromThePlatformJwksPath_NotTheCompanyUsersOne()
    {
        Validates(Mint(_platformKey));

        _handler.LastUri.Should().Be("http://auth-api:8080/.well-known/platform-jwks.json");
    }

    [Fact]
    public void ATokenSignedByAnotherKey_IsRejected()
    {
        Validates(Mint(_otherKey)).Should().BeFalse();
    }

    [Theory]
    [InlineData("Onepunch")]                 // the company users' issuer
    [InlineData("someone-else")]
    public void ATokenFromAnotherIssuer_IsRejected(string issuer)
    {
        Validates(Mint(_platformKey, issuer: issuer)).Should().BeFalse();
    }

    [Theory]
    [InlineData("Onepunch.AuthService")]     // the company users' audience
    [InlineData(PlatformAuthDefaults.ChallengeAudience)]   // the password-then-MFA step token
    [InlineData("something-else")]
    public void ATokenForAnotherAudience_IsRejected(string audience)
    {
        Validates(Mint(_platformKey, audience: audience)).Should().BeFalse();
    }

    [Fact]
    public void AnExpiredToken_IsRejected()
    {
        Validates(Mint(_platformKey, expires: DateTime.UtcNow.AddMinutes(-5))).Should().BeFalse();
    }

    [Fact]
    public void AnHmacSignedToken_IsRejected_NoAlgorithmConfusion()
    {
        // An attacker signing with HS256 using the (public) RSA modulus as the secret must not pass.
        var secret = new SymmetricSecurityKey(_platformRsa.ExportParameters(false).Modulus!) { KeyId = "platform-kid" };

        Validates(Mint(secret, SecurityAlgorithms.HmacSha256)).Should().BeFalse();
    }

    [Fact]
    public void AnUnsignedToken_IsRejected()
    {
        var header = Base64UrlEncoder.Encode("""{"alg":"none","typ":"JWT"}""");
        var payload = Base64UrlEncoder.Encode(
            $$"""{"iss":"{{PlatformAuthDefaults.Issuer}}","aud":"{{PlatformAuthDefaults.Audience}}","exp":{{DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds()}},"permission":"{{PlatformPermissions.AdminsManage}}"}""");

        Validates($"{header}.{payload}.").Should().BeFalse();
    }

    private sealed class RecordingHandler(string jwks) : HttpMessageHandler
    {
        public string? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri?.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(jwks, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class FakeFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
