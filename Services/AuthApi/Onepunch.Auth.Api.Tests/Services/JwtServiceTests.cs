using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Onepunch.Auth.Core.Services;
using Onepunch.Common.Lib;
using System.IdentityModel.Tokens.Jwt;
using Xunit;

namespace Onepunch.Auth.Api.Tests.Services;

public class JwtServiceTests
{
    private static JwtService CreateSut() =>
        new(null!, null!, Options.Create(new JwtSettings
        {
            Issuer = "onepunch-auth",
            Audience = ["onepunch-clients"],
        }), null!, null!);

    [Fact]
    public void Hash_ReturnsSameValue_ForSameInput()
    {
        var sut = CreateSut();

        var first = sut.Hash("some-refresh-token");
        var second = sut.Hash("some-refresh-token");

        first.Should().Be(second);
    }

    [Fact]
    public void Hash_ReturnsDifferentValues_ForDifferentInput()
    {
        var sut = CreateSut();

        var first = sut.Hash("token-a");
        var second = sut.Hash("token-b");

        first.Should().NotBe(second);
    }

    [Fact]
    public void Hash_ReturnsValidBase64String()
    {
        var sut = CreateSut();

        var hash = sut.Hash("some-refresh-token");

        var act = () => Convert.FromBase64String(hash);
        act.Should().NotThrow();
    }

    [Fact]
    public void GenerateKey_DefaultSize_Returns32ByteKey()
    {
        var sut = CreateSut();

        var key = sut.GenerateKey();

        Convert.FromBase64String(key).Should().HaveCount(32);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(64)]
    public void GenerateKey_CustomSize_ReturnsRequestedByteLength(int size)
    {
        var sut = CreateSut();

        var key = sut.GenerateKey(size);

        Convert.FromBase64String(key).Should().HaveCount(size);
    }

    [Fact]
    public void GenerateKey_ReturnsDifferentValues_OnEachCall()
    {
        var sut = CreateSut();

        var first = sut.GenerateKey();
        var second = sut.GenerateKey();

        first.Should().NotBe(second);
    }

    [Fact]
    public async Task GenerateRefreshToken_Returns32ByteBase64Token()
    {
        var sut = CreateSut();

        var token = await sut.GenerateRefreshToken();

        Convert.FromBase64String(token).Should().HaveCount(32);
    }

    [Fact]
    public async Task GenerateRefreshToken_ReturnsDifferentValues_OnEachCall()
    {
        var sut = CreateSut();

        var first = await sut.GenerateRefreshToken();
        var second = await sut.GenerateRefreshToken();

        first.Should().NotBe(second);
    }

    [Fact]
    public void TokenExpiry_And_RefreshExpiry_AreReadFromSettings()
    {
        var sut = new JwtService(null!, null!, Options.Create(new JwtSettings
        {
            Issuer = "onepunch-auth",
            Audience = ["onepunch-clients"],
            TokenExpiry = 10,
            RefreshExpiry = 60,
        }), null!, null!);

        sut.TokenExpiry.Should().Be(10);
        sut.RefreshExpiry.Should().Be(60);
    }

    // Regression guard for the JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear() bug --
    // ReadTokenToObject used to mutate that STATIC, process-wide map instead of the local
    // tokenHandler instance's own copy, silently and permanently disabling claim-type remapping
    // for every other request this process ever handled, the first time this method ran. It now
    // clears tokenHandler.InboundClaimTypeMap (a per-instance copy) instead, so the static
    // default must come out of this call completely untouched.
    [Fact]
    public async Task ReadTokenToObject_DoesNotMutateTheStaticDefaultInboundClaimTypeMap()
    {
        var originalDefaultMapCount = JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Count;
        var tempKeyPath = Path.Combine(Path.GetTempPath(), $"jwt-test-signing-{Guid.NewGuid()}.key");
        var tempDpDirectory = Path.Combine(Path.GetTempPath(), $"jwt-test-dp-{Guid.NewGuid()}");

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["JwtSettings:SigningKeyPath"] = tempKeyPath,
                })
                .Build();
            var dataProtectionProvider = DataProtectionProvider.Create(new DirectoryInfo(tempDpDirectory));
            var rsaKeyProvider = new RsaKeyProvider(configuration, dataProtectionProvider);

            var sut = new JwtService(null!, null!, Options.Create(new JwtSettings
            {
                Issuer = "onepunch-auth",
                Audience = ["onepunch-clients"],
                TokenExpiry = 10,
            }), rsaKeyProvider, null!);

            // Minted directly (not via CreateTokenAsync/JwtService) so this test only needs a
            // real signing key -- CreateTokenAsync's tenant-lookup branches need a real
            // TenantRequestService/MembershipCacheService, which is unrelated to what this test
            // is actually verifying (the InboundClaimTypeMap fix). Same claim shape either way:
            // JwtRegisteredClaimNames.Sub/.Email plus the custom tenantId/tenantName claims.
            var userId = Guid.NewGuid();
            var email = "jane@example.com";
            var unsignedToken = new JwtSecurityToken(
                issuer: "onepunch-auth",
                audience: "onepunch-clients",
                claims:
                [
                    new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                    new(JwtRegisteredClaimNames.Sub, userId.ToString()),
                    new(JwtRegisteredClaimNames.Email, email),
                    new("tenantId", Guid.Empty.ToString()),
                    new("tenantName", ""),
                ],
                expires: DateTime.UtcNow.AddMinutes(10),
                signingCredentials: new SigningCredentials(rsaKeyProvider.SigningKey, SecurityAlgorithms.RsaSha256));
            var accessToken = new JwtSecurityTokenHandler().WriteToken(unsignedToken);

            var result = sut.ReadTokenToObject(accessToken);

            result.Should().NotBeNull();
            result!.IsValid.Should().BeTrue(result.ErrorMessage);
            result.UserId.Should().Be(userId);
            result.Email.Should().Be(email);
            JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Should().HaveCount(originalDefaultMapCount);
        }
        finally
        {
            if (File.Exists(tempKeyPath)) File.Delete(tempKeyPath);
            if (Directory.Exists(tempDpDirectory)) Directory.Delete(tempDpDirectory, recursive: true);
        }
    }
}
