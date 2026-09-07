using FluentAssertions;
using Microsoft.Extensions.Options;
using Onepunch.Auth.Core.Services;
using Onepunch.Common.Lib;
using Xunit;

namespace Onepunch.Auth.Api.Tests.Services;

public class JwtServiceTests
{
    private static JwtService CreateSut() =>
        new(null!, null!, Options.Create(new JwtSettings
        {
            Issuer = "onepunch-auth",
            Audience = ["onepunch-clients"],
        }), null!);

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
        }), null!);

        sut.TokenExpiry.Should().Be(10);
        sut.RefreshExpiry.Should().Be(60);
    }
}
