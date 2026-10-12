using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using TenantStoreApi.Core.Services.Payments;
using TenantStoreApi.Domain.Entities.Subs;
using Xunit;

namespace TenantStoreApi.Tests.Payments;

/// <summary>The PayMongo adapter against canned API responses: what it sends, and how it reads replies and webhooks.</summary>
public class PayMongoGatewayTests
{
    private const string WebhookSecret = "whsk_test_secret";

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Request = request;
            RequestBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(token);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static (PayMongoGateway Gateway, StubHandler Handler) Build(HttpStatusCode status, string body, string secretKey = "sk_test_abc",
        string? statementDescriptor = null)
    {
        var handler = new StubHandler(status, body);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.paymongo.test/v1/") };
        var options = new PaymentOptions
        {
            PayMongo = { SecretKey = secretKey, WebhookSecret = WebhookSecret, StatementDescriptor = statementDescriptor },
        };
        return (new PayMongoGateway(http, Microsoft.Extensions.Options.Options.Create(options)), handler);
    }

    private static GatewayCheckoutRequest Request(Guid checkoutId) => new()
    {
        CheckoutId = checkoutId,
        ItemName = "Payroll plan (monthly)",
        Description = "One Punch HRIS subscription",
        Amount = 4999.50m,
        Currency = "PHP",
        SuccessUrl = "https://hris.test/billing/payment?checkout=1",
        CancelUrl = "https://hris.test/billing/payment?checkout=1&canceled=1",
        Metadata = new Dictionary<string, string> { ["checkout_id"] = checkoutId.ToString() },
    };

    /// <summary>A checkout_session.payment.paid delivery as PayMongo sends it (trimmed).</summary>
    private static string PaidEvent(bool live = false, string checkoutId = "00000000-0000-0000-0000-000000000001") => $$"""
        {
          "data": {
            "id": "evt_9wX1",
            "type": "event",
            "attributes": {
              "type": "checkout_session.payment.paid",
              "livemode": {{(live ? "true" : "false")}},
              "data": {
                "id": "cs_abc",
                "type": "checkout_session",
                "attributes": {
                  "checkout_url": "https://checkout.paymongo.com/cs_abc",
                  "livemode": {{(live ? "true" : "false")}},
                  "metadata": { "checkout_id": "{{checkoutId}}", "owner_user_id": "00000000-0000-0000-0000-000000000002" },
                  "payment_method_used": "gcash",
                  "payments": [
                    {
                      "id": "pay_123",
                      "type": "payment",
                      "attributes": {
                        "amount": 499950,
                        "currency": "PHP",
                        "fee": 11497,
                        "net_amount": 488453,
                        "paid_at": 1791273600,
                        "status": "paid",
                        "source": { "id": "src_1", "type": "gcash" }
                      }
                    }
                  ],
                  "status": "active"
                }
              },
              "created_at": 1791273600
            }
          }
        }
        """;

    private static string Sign(string body, bool live)
    {
        const string timestamp = "1791273600";
        var signature = PayMongoWebhookSignature.Compute(timestamp, body, WebhookSecret);
        return live ? $"t={timestamp},te=,li={signature}" : $"t={timestamp},te={signature},li=";
    }

    [Fact]
    public async Task CreateCheckout_SendsCentavos_TheConfiguredMethods_AndTheMetadata()
    {
        var (gateway, handler) = Build(HttpStatusCode.OK,
            """{"data":{"id":"cs_new","type":"checkout_session","attributes":{"checkout_url":"https://checkout.paymongo.com/cs_new"}}}""");
        var checkoutId = Guid.NewGuid();

        var session = await gateway.CreateCheckoutAsync(Request(checkoutId), CancellationToken.None);

        session.Should().Be(new GatewayCheckoutSession("cs_new", "https://checkout.paymongo.com/cs_new"));
        handler.Request!.Method.Should().Be(HttpMethod.Post);
        handler.Request.RequestUri!.ToString().Should().Be("https://api.paymongo.test/v1/checkout_sessions");

        var attributes = JsonDocument.Parse(handler.RequestBody!).RootElement.GetProperty("data").GetProperty("attributes");
        var item = attributes.GetProperty("line_items")[0];
        item.GetProperty("amount").GetInt64().Should().Be(499950);
        item.GetProperty("quantity").GetInt32().Should().Be(1);
        item.GetProperty("name").GetString().Should().Be("Payroll plan (monthly)");
        attributes.GetProperty("payment_method_types").EnumerateArray().Select(e => e.GetString())
            .Should().Equal(PayMongoOptions.DefaultPaymentMethodTypes);
        attributes.GetProperty("reference_number").GetString().Should().Be(checkoutId.ToString("N"));
        attributes.GetProperty("metadata").GetProperty("checkout_id").GetString().Should().Be(checkoutId.ToString());
        attributes.GetProperty("success_url").GetString().Should().EndWith("checkout=1");
        attributes.TryGetProperty("statement_descriptor", out _).Should().BeFalse("unset values aren't sent");
    }

    [Fact]
    public async Task CreateCheckout_SendsEachItemizedLineAndTheirSeparateAmounts()
    {
        var (gateway, handler) = Build(HttpStatusCode.OK,
            """{"data":{"id":"cs_new","type":"checkout_session","attributes":{"checkout_url":"https://checkout.paymongo.com/cs_new"}}}""");
        var request = Request(Guid.NewGuid()) with
        {
            Amount = 5799.50m,
            LineItems =
            [
                new GatewayCheckoutLineItem("Timekeeping plan (monthly)", 4999.50m),
                new GatewayCheckoutLineItem("Storage add-ons", 800m, "Storage add-ons for the next monthly billing period"),
            ],
        };

        await gateway.CreateCheckoutAsync(request, CancellationToken.None);

        var items = JsonDocument.Parse(handler.RequestBody!).RootElement
            .GetProperty("data").GetProperty("attributes").GetProperty("line_items");
        items.GetArrayLength().Should().Be(2);
        items[0].GetProperty("name").GetString().Should().Be("Timekeeping plan (monthly)");
        items[0].GetProperty("amount").GetInt64().Should().Be(499950);
        items[1].GetProperty("name").GetString().Should().Be("Storage add-ons");
        items[1].GetProperty("amount").GetInt64().Should().Be(80000);
        items[1].GetProperty("description").GetString().Should().Contain("monthly billing period");
        items.EnumerateArray().Sum(item => item.GetProperty("amount").GetInt64())
            .Should().Be(PayMongoDocuments.ToCentavos(request.Amount));
    }

    // appsettings.json's "StatementDescriptor": null binds as "" -- sending that made PayMongo refuse
    // every checkout with "The value for statement_descriptor cannot be blank".
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateCheckout_BlankStatementDescriptor_IsLeftOut(string descriptor)
    {
        var (gateway, handler) = Build(HttpStatusCode.OK,
            """{"data":{"id":"cs_new","type":"checkout_session","attributes":{"checkout_url":"https://checkout.paymongo.com/cs_new"}}}""",
            statementDescriptor: descriptor);

        await gateway.CreateCheckoutAsync(Request(Guid.NewGuid()), CancellationToken.None);

        var attributes = JsonDocument.Parse(handler.RequestBody!).RootElement.GetProperty("data").GetProperty("attributes");
        attributes.TryGetProperty("statement_descriptor", out _).Should().BeFalse();
    }

    [Fact]
    public async Task CreateCheckout_SetStatementDescriptor_IsSentTrimmed()
    {
        var (gateway, handler) = Build(HttpStatusCode.OK,
            """{"data":{"id":"cs_new","type":"checkout_session","attributes":{"checkout_url":"https://checkout.paymongo.com/cs_new"}}}""",
            statementDescriptor: " ONEPUNCH HRIS ");

        await gateway.CreateCheckoutAsync(Request(Guid.NewGuid()), CancellationToken.None);

        var attributes = JsonDocument.Parse(handler.RequestBody!).RootElement.GetProperty("data").GetProperty("attributes");
        attributes.GetProperty("statement_descriptor").GetString().Should().Be("ONEPUNCH HRIS");
    }

    [Fact]
    public async Task CreateCheckout_Refused_ThrowsWithPayMongosDetail()
    {
        var (gateway, _) = Build(HttpStatusCode.BadRequest,
            """{"errors":[{"code":"parameter_invalid","detail":"gcash is not activated for this account."}]}""");

        var act = () => gateway.CreateCheckoutAsync(Request(Guid.NewGuid()), CancellationToken.None);

        (await act.Should().ThrowAsync<PaymentGatewayException>()).Which.Message.Should().Contain("gcash is not activated");
    }

    [Fact]
    public async Task Refund_SendsCentavos_ThePayment_AndPayMongosReasonName()
    {
        var (gateway, handler) = Build(HttpStatusCode.OK,
            """{"data":{"id":"rf_1","type":"refund","attributes":{"amount":100050,"payment_id":"pay_9","status":"pending"}}}""");

        var refund = await gateway.RefundPaymentAsync("pay_9", 1000.50m, GatewayRefundReason.RequestedByCustomer, " Paid twice ", CancellationToken.None);

        refund.Should().Be(new GatewayRefund("rf_1", "pay_9", 1000.50m, "pending"));
        handler.Request!.Method.Should().Be(HttpMethod.Post);
        handler.Request.RequestUri!.ToString().Should().Be("https://api.paymongo.test/v1/refunds");
        var attributes = JsonDocument.Parse(handler.RequestBody!).RootElement.GetProperty("data").GetProperty("attributes");
        attributes.GetProperty("payment_id").GetString().Should().Be("pay_9");
        attributes.GetProperty("amount").GetInt64().Should().Be(100050);
        attributes.GetProperty("reason").GetString().Should().Be("requested_by_customer");
        attributes.GetProperty("notes").GetString().Should().Be("Paid twice");
    }

    [Fact]
    public async Task Refund_Refused_ThrowsWithPayMongosDetail()
    {
        var (gateway, _) = Build(HttpStatusCode.BadRequest,
            """{"errors":[{"code":"parameter_invalid","detail":"amount cannot exceed the refundable balance."}]}""");

        var act = () => gateway.RefundPaymentAsync("pay_9", 99999m, GatewayRefundReason.Others, null, CancellationToken.None);

        (await act.Should().ThrowAsync<PaymentGatewayException>()).Which.Message.Should().Contain("refundable balance");
    }

    [Fact]
    public async Task GetCheckout_ReadsThePaidPayment()
    {
        var eventData = JsonDocument.Parse(PaidEvent()).RootElement.GetProperty("data").GetProperty("attributes").GetProperty("data");
        var (gateway, handler) = Build(HttpStatusCode.OK, $$"""{"data":{{eventData.GetRawText()}}}""");

        var session = await gateway.GetCheckoutAsync("cs_abc", CancellationToken.None);

        handler.Request!.RequestUri!.ToString().Should().Be("https://api.paymongo.test/v1/checkout_sessions/cs_abc");
        session.IsPaid.Should().BeTrue();
        session.Payment!.Amount.Should().Be(4999.50m);
        session.Payment.Method.Should().Be(PaymentMethod.GCash);
        session.Payment.PaidAtUtc.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1791273600).UtcDateTime);
    }

    [Fact]
    public async Task GetCheckout_Unpaid_HasNoPayment()
    {
        var (gateway, _) = Build(HttpStatusCode.OK,
            """{"data":{"id":"cs_abc","type":"checkout_session","attributes":{"payments":[],"status":"active","livemode":false}}}""");

        (await gateway.GetCheckoutAsync("cs_abc", CancellationToken.None)).IsPaid.Should().BeFalse();
    }

    [Fact]
    public void ReadWebhook_SignedTestEvent_IsReadIntoACheckoutPaidEvent()
    {
        var (gateway, _) = Build(HttpStatusCode.OK, "{}");
        var body = PaidEvent();

        var received = gateway.ReadWebhook(body, Sign(body, live: false));

        received.Should().NotBeNull();
        received!.EventId.Should().Be("evt_9wX1");
        received.Kind.Should().Be(GatewayEventKind.CheckoutPaid);
        received.LiveMode.Should().BeFalse();
        received.Session!.SessionId.Should().Be("cs_abc");
        received.Session.Metadata["checkout_id"].Should().Be("00000000-0000-0000-0000-000000000001");
        received.Session.Payment!.PaymentId.Should().Be("pay_123");
    }

    [Theory]
    [InlineData("payment.refunded", "payment", "pay_123", GatewayEventKind.PaymentRefunded)]
    [InlineData("payment.refund.updated", "refund", "pay_123", GatewayEventKind.PaymentRefunded)]
    [InlineData("payment.dispute.opened", "dispute", "pay_123", GatewayEventKind.PaymentDisputed)]
    public void ReadWebhook_RefundOrDispute_NamesThePaymentItIsAbout(string eventType, string resourceType, string paymentId, GatewayEventKind kind)
    {
        var (gateway, _) = Build(HttpStatusCode.OK, "{}");
        // A payment resource carries its own id; a refund or dispute names the payment in payment_id.
        var resource = resourceType == "payment"
            ? """{ "id": "pay_123", "type": "payment", "attributes": { "amount": 250000 } }"""
            : $$"""{ "id": "x_1", "type": "{{resourceType}}", "attributes": { "payment_id": "pay_123", "amount": 250000 } }""";
        var body = $$"""
            { "data": { "id": "evt_r1", "type": "event", "attributes": { "type": "{{eventType}}", "livemode": false, "data": {{resource}} } } }
            """;

        var received = gateway.ReadWebhook(body, Sign(body, live: false));

        received!.Kind.Should().Be(kind);
        received.Issue.Should().Be(new GatewayPaymentIssue(paymentId, 2500m));
        received.Session.Should().BeNull();
    }

    [Fact]
    public void ReadWebhook_LiveEvent_NeedsTheLiveSignature()
    {
        var (gateway, _) = Build(HttpStatusCode.OK, "{}", secretKey: "sk_live_abc");
        var body = PaidEvent(live: true);

        gateway.ReadWebhook(body, Sign(body, live: false)).Should().BeNull("a live event is checked against li, which is empty");
        gateway.ReadWebhook(body, Sign(body, live: true))!.LiveMode.Should().BeTrue();
        gateway.LiveMode.Should().BeTrue();
    }

    [Fact]
    public void ReadWebhook_Unsigned_OrNotJson_IsNull()
    {
        var (gateway, _) = Build(HttpStatusCode.OK, "{}");

        gateway.ReadWebhook(PaidEvent(), null).Should().BeNull();
        gateway.ReadWebhook("not json", Sign("not json", live: false)).Should().BeNull();
    }

    [Theory]
    [InlineData("gcash", PaymentMethod.GCash)]
    [InlineData("paymaya", PaymentMethod.Maya)]
    [InlineData("card", PaymentMethod.Card)]
    [InlineData("qrph", PaymentMethod.QrPh)]
    [InlineData("grab_pay", PaymentMethod.GrabPay)]
    [InlineData("shopee_pay", PaymentMethod.ShopeePay)]
    [InlineData("billease", PaymentMethod.BillEase)]
    [InlineData("dob_ubp", PaymentMethod.OnlineBanking)]
    [InlineData("brankas_bdo", PaymentMethod.OnlineBanking)]
    [InlineData("something_new", PaymentMethod.Other)]
    [InlineData(null, PaymentMethod.Other)]
    public void PaymentSources_MapToOurMethods(string? source, PaymentMethod expected)
    {
        PayMongoDocuments.MapMethod(source).Should().Be(expected);
    }

    [Theory]
    [InlineData("4999", 499900)]
    [InlineData("4999.5", 499950)]
    [InlineData("0.005", 1)]
    public void Amounts_AreSentInCentavos(string amount, long centavos)
    {
        PayMongoDocuments.ToCentavos(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)).Should().Be(centavos);
    }
}
