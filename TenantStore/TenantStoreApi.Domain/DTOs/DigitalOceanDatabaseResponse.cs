namespace TenantStoreApi.Domain.DTOs;

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

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