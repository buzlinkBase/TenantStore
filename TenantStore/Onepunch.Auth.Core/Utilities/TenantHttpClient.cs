
using System.Net.Http.Json;

namespace OnePunch.Auth.Core.Utilities;

public class TenantHttpClient
{
    private readonly HttpClient _httpClient;

    public TenantHttpClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<TenantInfo?> GetTenant(Guid tenantId, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/tenant/{tenantId}");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode) return null;

        var result = await response.Content.ReadFromJsonAsync<ResponseModel<TenantInfo>>();
        return result?.Data;
    }
}