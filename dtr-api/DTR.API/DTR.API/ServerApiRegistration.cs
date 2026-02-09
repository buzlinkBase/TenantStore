using MessagePack;
using MessagePack.Resolvers;
using Refit;
using System.Net.Http.Headers;
namespace DTR.Api;

public static class ServerApiRegistration
{
    public static void RegisterApi(this WebApplicationBuilder builder)
    {
        var hrmsBaseUrl = builder.Configuration["GrpcServer:HrmsUri"]!;

        // Register your custom logic handler
        //builder.Services.AddTransient<ApiKeyHandler>();

        // Header propagation still handles Auth and Tenant ID from the user
        builder.Services.AddHeaderPropagation(options =>
        {
            options.Headers.Add("Authorization");
            options.Headers.Add("X-Tenant-ID");
        });

        var msgPackOptions = ContractlessStandardResolver.Options
           .WithCompression(MessagePackCompression.Lz4BlockArray);

        var settings = new RefitSettings
        {
            ContentSerializer = new MessagePackContentSerializer(msgPackOptions)
        };

        builder.Services.AddRefitClient<IEmployeeMessage>(settings)
            .ConfigureHttpClient(c =>
            {
                c.BaseAddress = new Uri(hrmsBaseUrl);
                c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-msgpack"));
            })
            .AddHeaderPropagation();


        builder.Services.AddRefitClient<ITimeShiftMessage>(settings)
        .ConfigureHttpClient(c =>
        {
            c.BaseAddress = new Uri(hrmsBaseUrl);
            c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-msgpack"));
        })
        .AddHeaderPropagation();

        builder.Services.AddRefitClient<IWorkSheduleMessage>(settings)
        .ConfigureHttpClient(c =>
        {
            c.BaseAddress = new Uri(hrmsBaseUrl);
            c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-msgpack"));
        })
        .AddHeaderPropagation();

        builder.Services.AddRefitClient<ILeavesMessage>(settings)
        .ConfigureHttpClient(c =>
        {
            c.BaseAddress = new Uri(hrmsBaseUrl);
            c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-msgpack"));
        })
        .AddHeaderPropagation();

        //.AddHttpMessageHandler<ApiKeyHandler>(); // <--- THIS attaches your key logic
    }
}

//public class ApiKeyHandler : DelegatingHandler
//{
//    private readonly ISecretService _secretService; // Example service to get keys
//    public ApiKeyHandler(ISecretService secretService)
//    {
//        _secretService = secretService;
//    }
//    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
//    {
//        // 1. Logic to obtain your key (either from a service, cache, or config)
//        var apiKey = await _secretService.GetApiKeyAsync();

//        // 2. Inject it into the outgoing header
//        request.Headers.Add("Api-Key", apiKey);

//        // 3. Continue the request
//        return await base.SendAsync(request, cancellationToken);
//    }
//}
