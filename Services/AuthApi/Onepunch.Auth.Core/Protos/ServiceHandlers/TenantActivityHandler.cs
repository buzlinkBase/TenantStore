using System.Globalization;
using Grpc.Core;
using Onepunch.Auth.Core.Activity;

namespace Onepunch.Auth.Core.Protos;

/// <summary>When each company last had an HRIS sign-in, for Tenant Service's platform lists. Internal, like AccountOriginsHandler.</summary>
public class TenantActivityHandler(TenantActivityReader reader) : TenantActivityService.TenantActivityServiceBase
{
    public override async Task<TenantActivityResponse> GetLastActive(TenantActivityRequest request, ServerCallContext context)
    {
        var ids = request.TenantIds
            .Select(id => Guid.TryParse(id, out var parsed) ? parsed : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToList();

        var found = await reader.ReadLastActiveAsync(ids, context.CancellationToken);

        var response = new TenantActivityResponse();
        response.Tenants.AddRange(found.Select(f => new TenantActivityRow
        {
            TenantId = f.Key.ToString(),
            LastActiveAtUtc = f.Value.ToString("O", CultureInfo.InvariantCulture),
        }));
        return response;
    }
}
