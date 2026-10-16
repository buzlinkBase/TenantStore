using FluentAssertions;
using OnePunch.Auth.Domain.Entities;
using Xunit;

namespace Onepunch.Auth.Api.Tests.Services;

/// <summary>How an account's origin and its "first ..." moments are recorded on the user.</summary>
public class AccountOriginTests
{
    private static readonly DateTime T0 = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ANewAccountStartsUnknownUntilItSaysHowItBegan()
    {
        var user = new User();

        user.Origin.Should().Be(AccountOrigins.Unknown);
        user.CreatedAtUtc.Should().BeNull();

        user.BeganAs(AccountOrigins.StaffInvite, T0, "invitation-1");

        user.Origin.Should().Be(AccountOrigins.StaffInvite);
        user.OriginDetail.Should().Be("invitation-1");
        user.CreatedAtUtc.Should().Be(T0);
    }

    [Fact]
    public void TheFirstTimesAreSetOnceAndNeverMoved()
    {
        var user = new User();

        user.StampEmailConfirmed(T0);
        user.StampFirstHrisSignIn(T0.AddMinutes(1));
        user.StampFirstPortalSignIn(T0.AddMinutes(2));
        user.StampEmailConfirmed(T0.AddDays(1));
        user.StampFirstHrisSignIn(T0.AddDays(1));
        user.StampFirstPortalSignIn(T0.AddDays(1));

        user.EmailConfirmedAtUtc.Should().Be(T0);
        user.FirstHrisSignInAtUtc.Should().Be(T0.AddMinutes(1));
        user.FirstPortalSignInAtUtc.Should().Be(T0.AddMinutes(2));
    }

    [Theory]
    [InlineData("EmailSignup", true)]
    [InlineData("GoogleSignup", true)]
    [InlineData("StaffInvite", true)]
    [InlineData("ClientInvite", true)]
    [InlineData("Unknown", true)]
    [InlineData("Facebook", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyTheListedOriginsAreKnown(string? origin, bool known) => AccountOrigins.IsKnown(origin).Should().Be(known);
}
