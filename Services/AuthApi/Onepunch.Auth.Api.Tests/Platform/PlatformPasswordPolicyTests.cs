using FluentAssertions;
using Onepunch.Auth.Core.Platform;
using Xunit;

namespace Onepunch.Auth.Api.Tests.Platform;

public class PlatformPasswordPolicyTests
{
    [Fact]
    public void AStrongPassword_HasNoErrors()
    {
        PlatformPasswordPolicy.Validate("Correct-Horse-Battery-9", "ops@example.com").Should().BeEmpty();
    }

    [Theory]
    [InlineData("Short1!", "at least 12")]
    [InlineData("alllowercase-1234!", "uppercase")]
    [InlineData("ALLUPPERCASE-1234!", "lowercase")]
    [InlineData("NoDigitsHere-Ok!!", "digit")]
    [InlineData("NoSymbolsHere12345", "symbol")]
    public void ARuleThatIsBroken_IsReported(string password, string expectedFragment)
    {
        PlatformPasswordPolicy.Validate(password, "ops@example.com")
            .Should().Contain(e => e.Contains(expectedFragment));
    }

    [Fact]
    public void ContainingTheEmailName_IsRejected()
    {
        PlatformPasswordPolicy.Validate("Maria.Santos-2026!", "maria.santos@example.com")
            .Should().Contain(e => e.Contains("email"));
    }

    [Fact]
    public void ABlankPassword_IsRejected()
    {
        PlatformPasswordPolicy.Validate("", "a@b.com").Should().ContainSingle();
        PlatformPasswordPolicy.Validate(null, "a@b.com").Should().ContainSingle();
    }
}
