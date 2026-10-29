namespace TenantStoreApi;

/// <summary>
/// Adds the shared service key (<c>X-Api-Key</c>) to every gRPC call this service makes to Auth, whose
/// internal services accept either a signed-in user or that key. Most calls carry no user -- background
/// jobs, and Platform Console requests (an operator token Auth doesn't issue) -- and were refused
/// with 401 without it. The key is <c>Grpc_AuthApiKey</c>, or else this service's own
/// <c>ApiKeySettings:ApiKey</c>; it must equal Auth's <c>ApiKeySettings:ApiKey</c>.
/// </summary>
public sealed class AuthServiceKeyHandler(IConfiguration config) : DelegatingHandler
{
    public const string Header = "X-Api-Key";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var key = config["Grpc_AuthApiKey"];
        if (string.IsNullOrWhiteSpace(key)) key = config["ApiKeySettings:ApiKey"];
        if (!string.IsNullOrWhiteSpace(key) && !request.Headers.Contains(Header))
            request.Headers.Add(Header, key);
        return base.SendAsync(request, cancellationToken);
    }
}
