namespace TenantStoreApi.Core.Utilities;
public class AuthService
{
    private readonly CheckEmailService.CheckEmailServiceClient _serviceClient;

    public AuthService(CheckEmailService.CheckEmailServiceClient serviceClient)
    {
        _serviceClient = serviceClient;
    }

    public async Task<CheckEmailResponse> CheckEmailAsync(string email, CancellationToken token)
    {
        var payload = new EmailPayload { Email = email };
        var response = await _serviceClient.CheckAsync(payload, cancellationToken: token);
        return response;
    }
}
