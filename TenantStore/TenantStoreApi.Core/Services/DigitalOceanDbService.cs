using Microsoft.Extensions.Configuration;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace TenantStoreApi.Core.Services;

public class DigitalOceanDbService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public DigitalOceanDbService(HttpClient httpClient, IConfiguration configuration)
    {
        // Headers and BaseAddress are already set by the Factory. 
        // DO NOT re-assign them here.
        _httpClient = httpClient;
        _configuration = configuration;
    }
    public Uri? GetBaseAddress()=> _httpClient.BaseAddress;
    public async Task<string?> CreateTenantDatabaseAsync(string serviceName, Guid tenantId)
    {
        var clusterId = _configuration["DigitalOcean:ClusterId"];
        string dbName = $"{serviceName}_{tenantId:N}";

        // Relative path: results in https://api.digitalocean.com/v2/databases/{clusterId}/dbs
        var response = await _httpClient.PostAsJsonAsync($"{clusterId}/dbs", new { name = dbName });

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"DO Error: {error}"); // Check this in your logs!
            return null;
        }

        var clusterResponse = await _httpClient.GetFromJsonAsync<DigitalOceanDatabaseResponse>($"{clusterId}");
        var baseUri = clusterResponse?.Database?.Connection?.Uri;

        if (string.IsNullOrEmpty(baseUri)) return null;

        var uriBuilder = new UriBuilder(baseUri) { Path = dbName };
        return uriBuilder.ToString();
    }


    public async Task<List<string>> ListTenantDatabasesAsync()
    {
        var clusterId = _configuration["DigitalOcean:ClusterId"];
        var response = await _httpClient.GetFromJsonAsync<DbListWrapper>($"{clusterId}/dbs");
        return response?.Databases?.Select(d => d.Name).ToList() ?? new List<string>();
    }
}

public class DbListWrapper
{
    [JsonPropertyName("dbs")]
    public List<DbInfo> Databases { get; set; }
}

public class DbInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; }
}

public class CreateClusterRequest
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("engine")]
    public string Engine { get; set; } // "pg" for Postgres, "mysql" for MySQL

    [JsonPropertyName("version")]
    public string Version { get; set; } // e.g., "14" or "8"

    [JsonPropertyName("region")]
    public string Region { get; set; } // e.g., "nyc3"

    [JsonPropertyName("size")]
    public string Size { get; set; } // e.g., "db-s-1vcpu-1gb"

    [JsonPropertyName("num_nodes")]
    public int NumNodes { get; set; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; }
}