using MySqlConnector;

namespace Onepunch.Common.Lib.DbServices;

public class SkySqlDbService : IDbService
{
    private string _connectionString;
    public SkySqlDbService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<ConnectionModel?> CreateTenantDatabaseAsync(string clusterId, string dbName)
    {
        // Connect to the cluster (not to a specific DB yet)
        using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        // Create the database if it doesn't exist
        var cmd = new MySqlCommand($"CREATE DATABASE IF NOT EXISTS `{dbName}`;", conn);
        await cmd.ExecuteNonQueryAsync();

        // Optionally create a user/schema for isolation
        // var userCmd = new MySqlCommand($"CREATE USER IF NOT EXISTS '{dbName}_user'@'%' IDENTIFIED BY 'StrongPassword';", conn);
        // await userCmd.ExecuteNonQueryAsync();
        // var grantCmd = new MySqlCommand($"GRANT ALL PRIVILEGES ON `{dbName}`.* TO '{dbName}_user'@'%';", conn);
        // await grantCmd.ExecuteNonQueryAsync();
        _connectionString = _connectionString.Replace("=hrms", $"={dbName}");
        return new ConnectionModel
        {
            ConnectionString = _connectionString
        };
    }

    public async Task<bool> DeleteTenantDatabaseAsync(string clusterId, string dbName)
    {
        using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        var cmd = new MySqlCommand($"DROP DATABASE IF EXISTS `{dbName}`;", conn);
        await cmd.ExecuteNonQueryAsync();

        return true;
    }

    public async Task<List<string>> ListTenantDatabasesAsync(string clusterId)
    {
        var databases = new List<string>();
        using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        var cmd = new MySqlCommand("SHOW DATABASES;", conn);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            databases.Add(reader.GetString(0));
        }

        return databases;
    }
}
