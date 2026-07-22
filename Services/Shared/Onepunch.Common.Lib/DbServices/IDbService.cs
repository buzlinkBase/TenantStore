namespace Onepunch.Common.Lib.DbServices;

public interface IDbService
{
    Task<ConnectionModel?> CreateTenantDatabaseAsync(string clusterId, string dbName);
    Task<List<string>> ListTenantDatabasesAsync(string clusterId);
    Task<bool> DeleteTenantDatabaseAsync(string clusterId, string dbName);
}
