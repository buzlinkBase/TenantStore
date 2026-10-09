using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using TenantStoreApi.Core.Services.Payments;

namespace TenantStoreApi.Core.Extensions;

/// <summary>
/// Online payment. PayMongo when Payments:ReturnBaseUrl and Payments:PayMongo:SecretKey are set;
/// otherwise <see cref="DisabledPaymentGateway"/>, and owners pay by the manual instructions.
/// (SubscriptionCheckoutService itself is picked up by RegisterCoreServices' convention scan.)
/// </summary>
public static class PaymentRegistrations
{
    public static void AddPayments(this WebApplicationBuilder builder)
    {
        var section = builder.Configuration.GetSection(PaymentOptions.Section);
        builder.Services.Configure<PaymentOptions>(section);

        var options = section.Get<PaymentOptions>() ?? new PaymentOptions();
        if (!options.PayMongo.IsConfigured || string.IsNullOrWhiteSpace(options.ReturnBaseUrl))
        {
            Log.Logger.Information("Online payment is off: set Payments:ReturnBaseUrl and Payments:PayMongo:SecretKey to turn it on");
            builder.Services.AddSingleton<IPaymentGateway, DisabledPaymentGateway>();
            return;
        }

        Log.Logger.Information("Online payment: PayMongo ({Mode} mode)", options.PayMongo.LiveMode ? "live" : "test");
        if (string.IsNullOrWhiteSpace(options.PayMongo.WebhookSecret))
        {
            Log.Logger.Warning("Payments:PayMongo:WebhookSecret isn't set: PayMongo webhooks will be refused, and a payment is " +
                               "confirmed only when the payer's return screen checks it -- set it in production");
        }
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(options.PayMongo.SecretKey + ":"));
        builder.Services.AddHttpClient<IPaymentGateway, PayMongoGateway>(client =>
        {
            client.BaseAddress = new Uri(options.PayMongo.ApiBaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });
    }
}
