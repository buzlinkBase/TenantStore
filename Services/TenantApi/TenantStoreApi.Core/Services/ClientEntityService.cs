using Microsoft.EntityFrameworkCore;

namespace TenantStoreApi.Core.Services;

/// <summary>
/// Keeps the identity layer's mirror of each client in step with the client portal, which owns the
/// real client record. The id is the portal's client id, so both sides name a client the same way.
/// Doesn't commit; the caller owns the unit of work.
/// </summary>
public class ClientEntityService : BaseService<ClientEntity>
{
    public ClientEntityService(IUnitOfWorkService uow) : base(uow)
    {
    }

    /// <summary>Creates the client, or updates its name, HRIS id and active flag. A client never moves to another company.</summary>
    public async Task<ClientEntity> UpsertAsync(
        Guid tenantId, Guid clientEntityId, string? externalId, string name, bool active, CancellationToken token = default)
    {
        if (tenantId == Guid.Empty || clientEntityId == Guid.Empty) throw new ArgumentException("A company and client id are required.");
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0) throw new ArgumentException("A client name is required.");

        var status = active ? ClientAccess.StatusActive : "Inactive";
        var existing = await Context.ClientEntities.FirstOrDefaultAsync(e => e.Id == clientEntityId, token);
        if (existing == null)
        {
            var created = new ClientEntity
            {
                Id = clientEntityId,
                TenantId = tenantId,
                ExternalId = externalId,
                Name = trimmed,
                Status = status,
            };
            await Context.ClientEntities.AddAsync(created, token);
            return created;
        }

        if (existing.TenantId != tenantId) throw new InvalidOperationException("This client belongs to a different company.");
        existing.ExternalId = externalId;
        existing.Name = trimmed;
        existing.Status = status;
        return existing;
    }
}
