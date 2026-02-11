namespace TenantStoreApi.Core.Utilities;
public class AuthService
{
    private readonly CheckEmailService.CheckEmailServiceClient _serviceClient;

    public AuthService(CheckEmailService.CheckEmailServiceClient serviceClient)
    {
        _serviceClient = serviceClient;
    }

    public async Task<CheckEmailResponse> CheckEmailAsync(string email)
    {
        var payload = new EmailPayload { Email = email };
        var response= await _serviceClient.CheckAsync(payload);
        return response;

        //var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/user/check-email?email={email}");
        //request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("X-Api-Key", _options.ApiKey);
        //var response = await _httpClient.SendAsync(request);
        //if (!response.IsSuccessStatusCode) return  false;
        //var result = await response.Content.ReadFromJsonAsync<ResponseModel<bool>>();
        //return result?.Data ?? false;
    }
}
