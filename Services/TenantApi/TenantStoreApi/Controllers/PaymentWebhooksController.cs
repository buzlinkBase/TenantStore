using System.Text;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TenantStoreApi.Core.Services.Payments;

namespace TenantStoreApi.Controllers;

/// <summary>
/// The payment gateway calling back. Anonymous: what makes a delivery trustworthy is its signature,
/// checked against the raw body (so the body is read as-is, never model-bound). A forged or
/// unsigned delivery gets 401; anything authentic gets 200, applied or not; a failure to save gets
/// 500, and the gateway retries.
/// </summary>
[Route("api/v{version:apiVersion}/payments")]
[ApiVersion("1.0")]
[AllowAnonymous]
[ApiController]
public class PaymentWebhooksController : ControllerBase
{
    private const int MaxBodyBytes = 256 * 1024;

    private readonly AutoRenewService _webhooks;

    public PaymentWebhooksController(AutoRenewService webhooks)
    {
        _webhooks = webhooks;
    }

    /// <summary>PayMongo webhook -- subscribe it to checkout_session.payment.paid, the refund and dispute events,
    /// and (for auto renew) subscription.invoice.created / paid / payment_failed and subscription.past_due /
    /// unpaid / updated. Auto renew events are handled first; the rest go on to the hosted checkout.</summary>
    [HttpPost("paymongo/webhook")]
    [RequestSizeLimit(MaxBodyBytes)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> PayMongo(CancellationToken token)
    {
        string body;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
        {
            body = await reader.ReadToEndAsync(token);
        }

        var outcome = await _webhooks.HandleWebhookAsync(
            body, Request.Headers[PayMongoWebhookSignature.HeaderName].FirstOrDefault(), token);
        return outcome == WebhookOutcome.Rejected ? Unauthorized() : Ok(new { outcome });
    }
}
