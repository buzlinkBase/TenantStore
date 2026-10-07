using FluentAssertions;
using Onepunch.Auth.Core.Platform;
using System.Text;
using Xunit;

namespace Onepunch.Auth.Api.Tests.Platform;

/// <summary>TOTP (RFC 6238) -- checked against the RFC's own SHA-1 test vectors, so it matches every authenticator app.</summary>
public class TotpTests
{
    // RFC 6238 appendix B: the ASCII secret "12345678901234567890", 6-digit truncation of the published 8-digit codes.
    private static readonly byte[] RfcSecret = Encoding.ASCII.GetBytes("12345678901234567890");

    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    [InlineData(20000000000L, "353130")]
    public void ComputeCode_MatchesTheRfcVectors(long unixSeconds, string expected)
    {
        var step = Totp.StepOf(DateTimeOffset.FromUnixTimeSeconds(unixSeconds));

        Totp.ComputeCode(RfcSecret, step).Should().Be(expected);
    }

    [Fact]
    public void Base32Encode_MatchesTheRfc4648Alphabet()
    {
        Totp.Base32Encode(RfcSecret).Should().Be("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ");
    }

    [Fact]
    public void Verify_AcceptsTheCurrentCode_AndReturnsItsStep()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1111111109);

        var step = Totp.Verify(RfcSecret, "081804", now, lastUsedStep: null);

        step.Should().Be(Totp.StepOf(now));
    }

    [Fact]
    public void Verify_ToleratesOneStepOfClockDrift_ButNotTwo()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1111111109);
        var current = Totp.StepOf(now);

        Totp.Verify(RfcSecret, Totp.ComputeCode(RfcSecret, current - 1), now, null).Should().Be(current - 1);
        Totp.Verify(RfcSecret, Totp.ComputeCode(RfcSecret, current + 1), now, null).Should().Be(current + 1);
        Totp.Verify(RfcSecret, Totp.ComputeCode(RfcSecret, current - 2), now, null).Should().BeNull();
    }

    [Fact]
    public void Verify_NeverAcceptsAStepThatWasAlreadyUsed()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1111111109);
        var step = Totp.StepOf(now);
        var code = Totp.ComputeCode(RfcSecret, step);

        Totp.Verify(RfcSecret, code, now, lastUsedStep: step).Should().BeNull("a code is single-use");
        Totp.Verify(RfcSecret, code, now, lastUsedStep: step - 1).Should().Be(step);
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12345a")]
    [InlineData(null)]
    public void Verify_RejectsMalformedCodes(string? code)
    {
        Totp.Verify(RfcSecret, code, DateTimeOffset.FromUnixTimeSeconds(59), null).Should().BeNull();
    }

    [Fact]
    public void Verify_IgnoresSpacesAndDashesAuthenticatorsSometimesShow()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1111111109);

        Totp.Verify(RfcSecret, "081 804", now, null).Should().NotBeNull();
        Totp.Verify(RfcSecret, "081-804", now, null).Should().NotBeNull();
    }

    [Fact]
    public void BuildOtpAuthUri_CarriesTheSecretIssuerAndParameters()
    {
        var uri = Totp.BuildOtpAuthUri("Onepunch Platform", "ops@example.com", RfcSecret);

        uri.Should().StartWith("otpauth://totp/Onepunch%20Platform:ops%40example.com?");
        uri.Should().Contain("secret=GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ")
            .And.Contain("issuer=Onepunch%20Platform")
            .And.Contain("digits=6")
            .And.Contain("period=30");
    }

    [Fact]
    public void NewSecret_Is160BitsAndDifferentEachTime()
    {
        var first = Totp.NewSecret();

        first.Should().HaveCount(20);
        Totp.NewSecret().Should().NotEqual(first);
    }
}
