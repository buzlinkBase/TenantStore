using System.Security.Cryptography;
using System.Text;

namespace TenantStoreApi.Core.Services.Payments;

/// <summary>
/// PayMongo's webhook signature. The Paymongo-Signature header is "t={unix time},te={test
/// signature},li={live signature}"; a signature is the lowercase hex HMAC-SHA256 of
/// "{t}.{raw request body}" keyed with the webhook's secret. The test one is checked for test-mode
/// events, the live one for live events.
/// No timestamp window: a replayed delivery is harmless, since every event and checkout is
/// processed once (SubscriptionCheckoutService dedupes them).
/// </summary>
public static class PayMongoWebhookSignature
{
    public const string HeaderName = "Paymongo-Signature";

    public static bool IsValid(string rawBody, string? header, string secret, bool liveMode)
    {
        if (string.IsNullOrWhiteSpace(header) || string.IsNullOrEmpty(secret)) return false;

        string? timestamp = null, test = null, live = null;
        foreach (var part in header.Split(','))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2) continue;
            var value = pair[1].Trim();
            switch (pair[0].Trim())
            {
                case "t": timestamp = value; break;
                case "te": test = value; break;
                case "li": live = value; break;
            }
        }

        var expected = liveMode ? live : test;
        if (!long.TryParse(timestamp, out _) || string.IsNullOrEmpty(expected)) return false;

        var computed = Compute(timestamp!, rawBody, secret);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(computed),
            Encoding.ASCII.GetBytes(expected.ToLowerInvariant()));
    }

    public static string Compute(string timestamp, string rawBody, string secret)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{rawBody}"));
        return Convert.ToHexStringLower(hash);
    }
}
