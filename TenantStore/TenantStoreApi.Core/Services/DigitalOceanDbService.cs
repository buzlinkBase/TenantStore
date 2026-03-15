using Microsoft.Extensions.Configuration;
using System.Net.Http.Json;
namespace TenantStoreApi.Core.Services;
public class DigitalOceanDbService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public DigitalOceanDbService(HttpClient httpClient,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    } 
    public async Task<object> CreateTenantDatabaseAsync(string serviceName, Guid tenantId)
    {
        var clusterId = _configuration["DigitalOcean:ClusterId"]!.ToString();
        string dbName = $"{serviceName}-{tenantId}";
        var payload = new { name = dbName };
        // The BaseUrl is already set in DI, so we only provide the relative path
        var response = await _httpClient.PostAsJsonAsync<object>($"{clusterId}/dbs", payload);
        return response ;
    }

}