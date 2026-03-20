

using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Onepunch.Common.Lib.Exceptions;
using Polly;
using Serilog;
using Serilog.Events;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Onepunch.Common.Lib;

public interface IDigitalOceanDbService
{
    Task<ConnectionModel?> CreateTenantDatabaseAsync(string dbName);
    Task<List<string>> ListTenantDatabasesAsync();
    Task<bool> DeleteTenantDatabaseAsync(string dbName);
}

public class DigitalOceanDbService : IDigitalOceanDbService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public DigitalOceanDbService(HttpClient httpClient,
        IPollyPolicyFactory policyFactory,
        IConfiguration configuration)
    {
        _configuration = configuration;
        _httpClient = httpClient;
    }

    public async Task<ConnectionModel?> CreateTenantDatabaseAsync(string dbName)
    {
        var clusterId = _configuration["DigitalOcean:ClusterId"];
        // 1. Create the Database in DigitalOcean (The only API call needed)
        var createResponse = await _httpClient.PostAsJsonAsync(
            $"v2/databases/{clusterId}/dbs",
            new { name = dbName }
        );
        if (!createResponse.IsSuccessStatusCode)
        {
            var error = await createResponse.Content.ReadAsStringAsync();
            // Check for 422 (Already exists) or other errors
            throw new DOException((int)createResponse.StatusCode, $"DO API Error: {error}");
        }

        // 2. Build the Connection String using Configuration (No second API call!)
        var host = _configuration["DigitalOcean:DbHost"];
        var port = uint.Parse(_configuration["DigitalOcean:DbPort"] ?? "25060");
        var user = _configuration["DigitalOcean:DbUser"];
        var pass = _configuration["DigitalOcean:DbPassword"];

        var builder = new MySqlConnectionStringBuilder
        {
            Server = host,
            Port = port,
            UserID = user,
            Password = pass,
            Database = dbName,
            SslMode = MySqlSslMode.Required,
            AllowPublicKeyRetrieval = true,
            Pooling = true,
            MaximumPoolSize = 100
        };

        // 3. Manually construct the Raw URI for consistency
        // Format: mysql://user:pass@host:port/database
        string rawUri = $"mysql://{user}:{pass}@{host}:{port}/{dbName}?ssl-mode=REQUIRED";
        return new ConnectionModel
        {
            ConnectionString = builder.ConnectionString,
            RawConnectionString = rawUri
        };
    }

    //public async Task<ConnectionModel?> CreateTenantDatabaseAsync(string dbName)
    //{
    //    var clusterId = _configuration["DigitalOcean:ClusterId"];
    //    // 1. Create the Database in DigitalOcean
    //    var createResponse = await _httpClient.PostAsJsonAsync($"v2/databases/{clusterId}/dbs", new { name = dbName });
    //    if (!createResponse.IsSuccessStatusCode)
    //    {
    //        var error = await createResponse.Content.ReadAsStringAsync();
    //        var parseErr = ObjectSerializer.Deserialize<DigitalOceanErrorResponse>(error);
    //        if (parseErr == null) throw new DOException(-1, error);
    //        int statusCodeInt = (int)createResponse.StatusCode;
    //        parseErr.Code= statusCodeInt;
    //        Log.Logger.Error(error, parseErr);
    //        throw new DOException(parseErr.Code, parseErr.message);
    //    }
    //    // 2. Get Cluster Details to extract the base connection URI
    //    // DigitalOcean returns the "primary" connection string for the whole cluster
    //    var clusterResponse = await _httpClient.GetAsync($"v2/databases/{clusterId}");

    //    if (!clusterResponse.IsSuccessStatusCode) return null;

    //    var data = await clusterResponse.Content.ReadFromJsonAsync<DigitalOceanDatabaseResponse>();
    //    var baseUriStr = data?.Database?.Connection?.Uri;

    //    if (string.IsNullOrEmpty(baseUriStr)) return null;

    //    // 3. Parse URI and build the EF Core Connection String
    //    var uri = new Uri(baseUriStr);
    //    var userInfo = uri.UserInfo.Split(':');

    //    if (userInfo.Length < 2) return null;

    //    var builder = new MySqlConnectionStringBuilder
    //    {
    //        Server = uri.Host,
    //        Port = (uint)uri.Port,
    //        UserID = userInfo[0],
    //        Password = userInfo[1],
    //        Database = dbName,
    //        SslMode = MySqlSslMode.Required,
    //        AllowPublicKeyRetrieval = true,
    //        Pooling = true,
    //        MaximumPoolSize = 100
    //    };
    //    var finalUri = new UriBuilder(uri)
    //    {
    //        Path = dbName
    //    }.Uri;
    //    return new ConnectionModel
    //    {
    //        ConnectionString = builder.ConnectionString,
    //        RawConnectionString = finalUri.ToString(), 
    //    };
    //}

    public async Task<bool> DeleteTenantDatabaseAsync(string dbName)
    {
        var clusterId = _configuration["DigitalOcean:ClusterId"];
        // DO API: DELETE /v2/databases/{cluster_id}/dbs/{db_name}
        var response = await _httpClient.DeleteAsync($"v2/databases/{clusterId}/dbs/{dbName}");
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"Failed to rollback/delete DO database {dbName}: {error}");
            return false;
        }
        return true;
    }

    public async Task<List<string>> ListTenantDatabasesAsync()
    {
        var clusterId = _configuration["DigitalOcean:ClusterId"];
        var response = await _httpClient.GetFromJsonAsync<DbListWrapper>($"v2/databases/{clusterId}/dbs");
        return response?.Databases?.Select(d => d.Name).ToList() ?? new List<string>();
    }
}

public class DigitalOceanErrorResponse
{
    public int Code { get; set; }
    public string message  { get; set; }
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
    public string ConnectionString { get; set; }
    public string RawConnectionString { get; set; }
}