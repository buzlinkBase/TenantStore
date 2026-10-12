using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Core.Services.Platform;

/// <summary>
/// What the payment gateway and the operators have said about each online payment, read from the
/// billing history: refunds and disputes the gateway reported (keyed
/// "{provider}:payment:{gatewayPaymentId}:{type}"), and refunds an operator issued from the console.
/// Everything is keyed by the gateway's payment id, which is the payment's <c>Reference</c>.
/// </summary>
internal sealed class GatewayIssueLedger
{
    private readonly Dictionary<string, decimal> _reportedRefunds = new();
    private readonly Dictionary<string, HashSet<string>> _refundIssueKeys = new();
    private readonly Dictionary<string, HashSet<string>> _disputeIssueKeys = new();
    private readonly HashSet<string> _reviewedIssueKeys = [];
    private readonly HashSet<string> _disputes = [];
    private readonly Dictionary<string, decimal> _issuedRefunds = new();

    private GatewayIssueLedger()
    {
    }

    /// <param name="ownerIds">Only these accounts' events; null for everyone's.</param>
    public static async Task<GatewayIssueLedger> LoadAsync(TenantContext context, IReadOnlyCollection<Guid>? ownerIds, CancellationToken token)
    {
        var types = new[]
        {
            BillingEventTypes.PaymentRefunded,
            BillingEventTypes.PaymentDisputed,
            BillingEventTypes.PaymentRefundIssued,
            BillingEventTypes.PaymentIssueReviewed,
        };
        var events = context.BillingEvents.AsNoTracking()
            .Where(e => types.Contains(e.Type) && e.OwnerUserId != null);
        if (ownerIds != null) events = events.Where(e => ownerIds.Contains(e.OwnerUserId!.Value));

        var ledger = new GatewayIssueLedger();
        foreach (var e in await events.Select(e => new { e.Type, e.DedupeKey, e.PayloadJson }).ToListAsync(token))
        {
            if (e.Type == BillingEventTypes.PaymentIssueReviewed)
            {
                if (ReadReview(e.PayloadJson) is { IssueKey.Length: > 0 } review)
                    ledger._reviewedIssueKeys.Add(review.IssueKey);
                continue;
            }

            if (e.Type == BillingEventTypes.PaymentRefundIssued)
            {
                if (Read(e.PayloadJson) is { PaymentId.Length: > 0 } issued)
                    ledger._issuedRefunds[issued.PaymentId] = ledger._issuedRefunds.GetValueOrDefault(issued.PaymentId) + issued.Amount;
                continue;
            }

            var parts = e.DedupeKey?.Split(':');
            if (parts is not { Length: >= 4 }) continue;
            var paymentId = parts[2];
            if (e.Type == BillingEventTypes.PaymentDisputed)
            {
                ledger._disputes.Add(paymentId);
                AddIssueKey(ledger._disputeIssueKeys, paymentId, e.DedupeKey!);
            }
            else
            {
                ledger._reportedRefunds[paymentId] = Read(e.PayloadJson)?.Amount ?? 0m;
                AddIssueKey(ledger._refundIssueKeys, paymentId, e.DedupeKey!);
            }
        }
        return ledger;
    }

    /// <summary>"Disputed" or "Refunded" when the gateway reported one, or an operator refunded; a dispute outranks a refund.</summary>
    public string? IssueOf(string? gatewayPaymentId)
    {
        if (gatewayPaymentId == null) return null;
        if (_disputes.Contains(gatewayPaymentId)) return "Disputed";
        return _reportedRefunds.ContainsKey(gatewayPaymentId) || _issuedRefunds.ContainsKey(gatewayPaymentId) ? "Refunded" : null;
    }

    /// <summary>How much of the payment has gone back: the larger of what the gateway reported and what operators issued.</summary>
    public decimal RefundedOf(string? gatewayPaymentId) =>
        gatewayPaymentId == null
            ? 0m
            : Math.Max(_reportedRefunds.GetValueOrDefault(gatewayPaymentId), _issuedRefunds.GetValueOrDefault(gatewayPaymentId));

    /// <summary>A dispute, or a refund the gateway reported that no operator issued here -- someone has to look.
    /// A refund an operator sent on purpose is already handled.</summary>
    public bool NeedsReview(string? gatewayPaymentId) =>
        UnreviewedIssueKeys(gatewayPaymentId).Count > 0;

    public IReadOnlyList<string> NeedingReview() =>
        _disputes.Concat(_reportedRefunds.Keys).Distinct().Where(NeedsReview).ToList();

    public IReadOnlyList<string> UnreviewedIssueKeys(string? gatewayPaymentId)
    {
        if (gatewayPaymentId == null) return [];

        var issueKeys = new List<string>();
        if (_disputeIssueKeys.TryGetValue(gatewayPaymentId, out var disputes))
            issueKeys.AddRange(disputes.Where(key => !_reviewedIssueKeys.Contains(key)));
        if (!_issuedRefunds.ContainsKey(gatewayPaymentId)
            && _refundIssueKeys.TryGetValue(gatewayPaymentId, out var refunds))
            issueKeys.AddRange(refunds.Where(key => !_reviewedIssueKeys.Contains(key)));
        return issueKeys;
    }

    private static void AddIssueKey(Dictionary<string, HashSet<string>> issueKeys, string paymentId, string key)
    {
        if (!issueKeys.TryGetValue(paymentId, out var keys))
            issueKeys[paymentId] = keys = [];
        keys.Add(key);
    }

    private static (string PaymentId, decimal Amount)? Read(string? payloadJson)
    {
        if (string.IsNullOrEmpty(payloadJson)) return null;
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            var amount = root.TryGetProperty("amount", out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDecimal() : 0m;
            var paymentId = root.TryGetProperty("gatewayPaymentId", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null;
            return paymentId == null ? (string.Empty, amount) : (paymentId, amount);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static (string PaymentId, string IssueKey)? ReadReview(string? payloadJson)
    {
        if (string.IsNullOrEmpty(payloadJson)) return null;
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            var paymentId = root.TryGetProperty("gatewayPaymentId", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null;
            var issueKey = root.TryGetProperty("issueKey", out var key) && key.ValueKind == JsonValueKind.String ? key.GetString() : null;
            return paymentId == null || issueKey == null ? null : (paymentId, issueKey);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
