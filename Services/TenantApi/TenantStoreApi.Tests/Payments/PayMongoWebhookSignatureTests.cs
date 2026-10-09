using FluentAssertions;
using TenantStoreApi.Core.Services.Payments;
using Xunit;

namespace TenantStoreApi.Tests.Payments;

/// <summary>Paymongo-Signature: "t=..,te=..,li=..", HMAC-SHA256 of "{t}.{body}" with the webhook secret.</summary>
public class PayMongoWebhookSignatureTests
{
    private const string Secret = "whsk_test_secret";
    private const string Body = """{"data":{"id":"evt_1","attributes":{"type":"checkout_session.payment.paid","livemode":false}}}""";
    private const string Timestamp = "1791273600";

    private static string Header(string? test = null, string? live = null) =>
        $"t={Timestamp},te={test ?? ""},li={live ?? ""}";

    [Fact]
    public void Compute_IsTheLowercaseHexHmacOfTimestampDotBody()
    {
        var expected = Convert.ToHexStringLower(System.Security.Cryptography.HMACSHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(Secret), System.Text.Encoding.UTF8.GetBytes($"{Timestamp}.{Body}")));

        PayMongoWebhookSignature.Compute(Timestamp, Body, Secret).Should().Be(expected).And.HaveLength(64);
    }

    [Fact]
    public void TestModeEvent_IsCheckedAgainstTheTestSignature()
    {
        var signature = PayMongoWebhookSignature.Compute(Timestamp, Body, Secret);

        PayMongoWebhookSignature.IsValid(Body, Header(test: signature), Secret, liveMode: false).Should().BeTrue();
        PayMongoWebhookSignature.IsValid(Body, Header(live: signature), Secret, liveMode: false).Should().BeFalse("te is empty");
    }

    [Fact]
    public void LiveModeEvent_IsCheckedAgainstTheLiveSignature()
    {
        var signature = PayMongoWebhookSignature.Compute(Timestamp, Body, Secret);

        PayMongoWebhookSignature.IsValid(Body, Header(live: signature), Secret, liveMode: true).Should().BeTrue();
        PayMongoWebhookSignature.IsValid(Body, Header(test: signature), Secret, liveMode: true).Should().BeFalse("li is empty");
    }

    [Fact]
    public void UppercaseSignature_IsAccepted()
    {
        var signature = PayMongoWebhookSignature.Compute(Timestamp, Body, Secret).ToUpperInvariant();

        PayMongoWebhookSignature.IsValid(Body, Header(test: signature), Secret, liveMode: false).Should().BeTrue();
    }

    [Fact]
    public void TamperedBody_IsRejected()
    {
        var signature = PayMongoWebhookSignature.Compute(Timestamp, Body, Secret);

        PayMongoWebhookSignature.IsValid(Body.Replace("evt_1", "evt_2"), Header(test: signature), Secret, liveMode: false)
            .Should().BeFalse();
    }

    [Fact]
    public void WrongSecret_IsRejected()
    {
        var signature = PayMongoWebhookSignature.Compute(Timestamp, Body, "whsk_someone_else");

        PayMongoWebhookSignature.IsValid(Body, Header(test: signature), Secret, liveMode: false).Should().BeFalse();
    }

    [Fact]
    public void ChangedTimestamp_IsRejected()
    {
        var signature = PayMongoWebhookSignature.Compute(Timestamp, Body, Secret);

        PayMongoWebhookSignature.IsValid(Body, $"t=1791273601,te={signature},li=", Secret, liveMode: false).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("te=abc,li=def")]
    [InlineData("t=notanumber,te=abc,li=def")]
    public void MissingOrMalformedHeader_IsRejected(string? header)
    {
        PayMongoWebhookSignature.IsValid(Body, header, Secret, liveMode: false).Should().BeFalse();
    }

    [Fact]
    public void NoSecretConfigured_RejectsEverything()
    {
        var signature = PayMongoWebhookSignature.Compute(Timestamp, Body, "");

        PayMongoWebhookSignature.IsValid(Body, Header(test: signature), "", liveMode: false).Should().BeFalse();
    }
}
