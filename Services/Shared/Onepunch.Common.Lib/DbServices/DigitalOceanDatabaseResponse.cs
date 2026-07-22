

using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Onepunch.Common.Lib.Exceptions;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Onepunch.Common.Lib.DbServices;

public class DigitalOceanDbService : IDbService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    public DigitalOceanDbService(
        HttpClient httpClient,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task<ConnectionModel?> CreateTenantDatabaseAsync(string clusterId, string dbName)
    {
        // 1. Create the Database via DigitalOcean API
        var createResponse = await _httpClient.PostAsJsonAsync(
            $"v2/databases/{clusterId}/dbs",
            new { name = dbName }
        );

        if (!createResponse.IsSuccessStatusCode)
        {
            var error = await createResponse.Content.ReadAsStringAsync();
            // Handle specific case: 422 usually means the DB already exists
            throw new DOException((int)createResponse.StatusCode, $"DigitalOcean API Error: {error}");
        }

        // 2. Load Cluster Credentials from Config
        var host = _configuration["DigitalOcean:DbHost"];
        var port = uint.Parse(_configuration["DigitalOcean:DbPort"] ?? "25060");
        var user = _configuration["DigitalOcean:DbUser"];
        var pass = _configuration["DigitalOcean:DbPassword"];

        // 3. Build the highly-restricted Connection String
        // This is the "Magic Sauce" that protects your $15 plan limits
        var builder = new MySqlConnectionStringBuilder
        {
            Server = host,
            Port = port,
            UserID = user,
            Password = pass,
            Database = dbName,
            SslMode = MySqlSslMode.Required,

            // POOLING CONSTRAINTS
            Pooling = true,
            MinimumPoolSize = 0,      // Don't hold connections for idle tenants
            MaximumPoolSize = 10,      // STRICT LIMIT: Only x pipes per tenant
            ConnectionTimeout = 30,   // Wait 30s before failing if pool is full

            // Stability settings
            AllowPublicKeyRetrieval = true,
            DefaultCommandTimeout = 60
        };

        // 4. Construct the Raw URI (Used by some migration tools or external libs)
        string rawUri = $"mysql://{user}:{pass}@{host}:{port}/{dbName}?ssl-mode=REQUIRED";

        return new ConnectionModel
        {
            ConnectionString = builder.ConnectionString,
        };
    }

    public async Task<bool> DeleteTenantDatabaseAsync(string clusterId, string dbName)
    {
        // DO API: DELETE /v2/databases/{cluster_id}/dbs/{db_name}
        var response = await _httpClient.DeleteAsync($"v2/databases/{clusterId}/dbs/{dbName}");

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            // Log this as a warning - if deletion fails during rollback, it needs manual intervention
            Console.WriteLine($"[CRITICAL] Failed to delete DO database {dbName}: {error}");
            return false;
        }

        return true;
    }

    public async Task<List<string>> ListTenantDatabasesAsync(string clusterId)
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<DbListWrapper>($"v2/databases/{clusterId}/dbs");
            return response?.Databases?.Select(d => d.Name).ToList() ?? new List<string>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error listing databases: {ex.Message}");
            return new List<string>();
        }
    }
}


public class DigitalOceanErrorResponse
{
    public int Code { get; set; }
    public string message { get; set; }
    public string id { get; set; }
    public string request_Id { get; set; }
}
public class DigitalOceanDatabaseResponse
{
    [JsonPropertyName("database")]
    public DatabaseDetail Database { get; set; }
}
public class DatabaseDetail
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("engine")]
    public string Engine { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; }

    [JsonPropertyName("semantic_version")]
    public string SemanticVersion { get; set; }

    [JsonPropertyName("connection")]
    public ConnectionInfo Connection { get; set; }

    [JsonPropertyName("private_connection")]
    public ConnectionInfo PrivateConnection { get; set; }

    [JsonPropertyName("standby_connection")]
    public ConnectionInfo StandbyConnection { get; set; }

    [JsonPropertyName("standby_private_connection")]
    public ConnectionInfo StandbyPrivateConnection { get; set; }

    [JsonPropertyName("users")]
    public List<DatabaseUser> Users { get; set; }

    [JsonPropertyName("db_names")]
    public List<string> DbNames { get; set; }

    [JsonPropertyName("num_nodes")]
    public int NumNodes { get; set; }

    [JsonPropertyName("region")]
    public string Region { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("maintenance_window")]
    public MaintenanceWindow MaintenanceWindow { get; set; }

    [JsonPropertyName("size")]
    public string Size { get; set; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; }

    [JsonPropertyName("private_network_uuid")]
    public Guid? PrivateNetworkUuid { get; set; }

    [JsonPropertyName("version_end_of_life")]
    public DateTime? VersionEndOfLife { get; set; }

    [JsonPropertyName("version_end_of_availability")]
    public DateTime? VersionEndOfAvailability { get; set; }

    [JsonPropertyName("storage_size_mib")]
    public long StorageSizeMib { get; set; }

    [JsonPropertyName("do_settings")]
    public DoSettings DoSettings { get; set; }
}
public class ConnectionInfo
{
    [JsonPropertyName("uri")]
    public string Uri { get; set; }

    [JsonPropertyName("database")]
    public string Database { get; set; }

    [JsonPropertyName("host")]
    public string Host { get; set; }

    [JsonPropertyName("port")]
    public int Port { get; set; }

    [JsonPropertyName("user")]
    public string User { get; set; }

    [JsonPropertyName("password")]
    public string Password { get; set; }

    [JsonPropertyName("ssl")]
    public bool Ssl { get; set; }
}
public class DatabaseUser
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("role")]
    public string Role { get; set; }

    [JsonPropertyName("password")]
    public string Password { get; set; }
}
public class MaintenanceWindow
{
    [JsonPropertyName("day")]
    public string Day { get; set; }

    [JsonPropertyName("hour")]
    public string Hour { get; set; }

    [JsonPropertyName("pending")]
    public bool Pending { get; set; }

    [JsonPropertyName("description")]
    public List<string> Description { get; set; }
}
public class DoSettings
{
    [JsonPropertyName("service_cnames")]
    public List<string> ServiceCnames { get; set; }
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
public class ConnectionModel
{
    public required string ConnectionString { get; set; }

}