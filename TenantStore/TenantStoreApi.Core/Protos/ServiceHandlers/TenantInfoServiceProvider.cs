using Grpc.Core;
using Onepunch.Auth.Core;
using TenantStoreApi.Core.Services;

namespace TenantStoreApi.Core.Protos.ServiceHandlers;

public class TenantInfoServiceProvider   : GetTenantService.GetTenantServiceBase
{
    private readonly TenantService _service;

    public TenantInfoServiceProvider(TenantService service)
    {
        _service = service;
    }
    public override async Task<TenantInfoResponse> Check(TenantRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.TenantId, out var id))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid GUID format for TenantId"));
        }
        var tenant =  await _service.FindTenant(id);
        if (tenant == null)
        {
            // This is the standard way to signal a missing resource in gRPC
            throw new RpcException(new Status(StatusCode.NotFound, $"Tenant with ID {id} not found"));
        }
        var response = new TenantInfoResponse
        {
            Name = tenant.CompanyName,
            TenantId = tenant.Id.ToString(),
            Token = ""
        };
        return response;
    }
}
