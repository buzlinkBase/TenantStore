using FluentAssertions;
using OnePunch.Auth.Domain.Entities;
using Xunit;

namespace Onepunch.Auth.Api.Tests.Services;

/// <summary>The Terms of Service version a person agreed to on the sign-up page, recorded on the user.</summary>
public class TermsAgreementTests
{
    private static readonly DateTime T0 = new(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AgreeingRecordsTheVersionTimeAndAddress()
    {
        var user = new User().AgreedToTerms(3, T0, "203.0.113.7");

        user.TermsVersionAccepted.Should().Be(3);
        user.TermsAcceptedAtUtc.Should().Be(T0);
        user.TermsAcceptedIp.Should().Be("203.0.113.7");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void NoVersion_RecordsNothing(int? version)
    {
        var user = new User().AgreedToTerms(version, T0, "203.0.113.7");

        user.TermsVersionAccepted.Should().BeNull();
        user.TermsAcceptedAtUtc.Should().BeNull();
        user.TermsAcceptedIp.Should().BeNull();
    }

    [Fact]
    public void AnOverlongAddressIsCut()
    {
        var user = new User().AgreedToTerms(1, T0, new string('a', 100));

        user.TermsAcceptedIp.Should().HaveLength(64);
    }
}
