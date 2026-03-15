using Refit;

namespace Onepunch.Common.Lib.Interfaces;
public interface IConnectionClient
{
    [Get("/api/v1/tenantconnection")]
    Task<ResponseModel<string?>> FindConnectionAsync([AliasAs("tenant-id")] Guid tenantId);
}
