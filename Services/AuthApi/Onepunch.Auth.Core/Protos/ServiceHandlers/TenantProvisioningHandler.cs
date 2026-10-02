using System.Globalization;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Onepunch.Auth.Infrastructure;

namespace Onepunch.Auth.Core.Protos;

/// <summary>
/// Tells Tenant Service which of the given companies finished setting up (HrDbReady), and when
/// -- so it can mark existing companies activated and start trials for any whose provisioning
/// message it missed. Internal, like UserInfoHandler.
/// </summary>
public class TenantProvisioningHandler(Onepunch.Auth.Infrastructure.AuthContext context) : TenantProvisioningService.TenantProvisioningServiceBase
{
    private const int MaxTenantsPerCall = 500;

    public override async Task<ReadyTenantsResponse> GetReadyTenants(ReadyTenantsRequest request, ServerCallContext callContext)
    {
        var ids = request.TenantIds
            .Select(id => Guid.TryParse(id, out var parsed) ? parsed : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .Take(MaxTenantsPerCall)
            .ToList();

        var response = new ReadyTenantsResponse();
        if (ids.Count == 0) return response;

        var ready = await context.TenantCreationRequests.AsNoTracking()
            .Where(r => ids.Contains(r.TenantId) && r.HrDbReady)
            .Select(r => new { r.TenantId, ReadyAt = r.UpdatedAt ?? r.CreatedAt })
            .ToListAsync(callContext.CancellationToken);

        response.Tenants.AddRange(ready
            .GroupBy(r => r.TenantId)
            .Select(g => new ReadyTenant
            {
                TenantId = g.Key.ToString(),
                ReadyAtUtc = DateTime.SpecifyKind(g.Min(r => r.ReadyAt), DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture),
            }));
        return response;
    }
}
