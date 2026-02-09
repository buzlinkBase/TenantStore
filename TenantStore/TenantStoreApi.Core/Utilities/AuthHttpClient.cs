using Microsoft.Extensions.Options;
using System.Net.Http.Json;

namespace TenantStoreApi.Core.Utilities;

public class AuthHttpClient
{
    private readonly HttpClient _httpClient;
    private readonly ApiKeySetting _options;

    public AuthHttpClient(HttpClient httpClient, IOptions<ApiKeySetting> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<bool> CheckEmailAsync(string email)
    {
        //var response = await _httpClient.GetAsync($"/api/v1/user/check-email?email={email}");
        //if (!response.IsSuccessStatusCode) return false;

        //var result = await response.Content.ReadFromJsonAsync<ResponseModel<bool>>();
        //return result?.Data ?? false;

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/user/check-email?email={email}");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("X-Api-Key", _options.ApiKey);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode) return  false;

        var result = await response.Content.ReadFromJsonAsync<ResponseModel<bool>>();
        return result?.Data ?? false;

    }
}
 