using Refit;
using System;
using System.Threading.Tasks;

namespace Onepunch.Common.Lib.Interfaces;

public interface IConnectionClient
{
    [Get("/api/v1/connections")]
    [Headers("Accept: application/x-msgpack")]
    Task<ResponseModel<ConnectionQueryResponse?>> FindConnectionAsync(
        [Query][AliasAs("tenant-id")] Guid tenantId,
        [Query][AliasAs("service")] string service
    );
}

public interface IMigrationService
{
    void Migrate(string? connectionString);
}