using BuzlinkRepository;
using Microsoft.EntityFrameworkCore;
using Onepunch.Common.Lib.Entities;

namespace Onepunch.Common.Lib.Services;

public abstract class OutBoxServiceBase
{
    private readonly IRepository _repository;
    public OutBoxServiceBase(IRepository Repository)
    {
        _repository = Repository;
    }
    public async Task AddAsync(OutboxMessage payload)
    {
        _repository.Add(payload);
    }

    public async Task UpdateStateAsync(OutboxMessage payload, OutBoxState state)
    {
        payload.Status = state;
        payload.ProcessedOn = DateTime.UtcNow;
        _repository.Update(payload);
    }

    public async Task<bool> IsEventExists(Guid eventId)
    {
        return await FindEvent(eventId) != null;
    }

    public async Task<OutboxMessage?> FindEvent(Guid eventId)
    {
        return await _repository
            .Find<OutboxMessage>(x => x.EventId == eventId)
            .FirstOrDefaultAsync();
    }

    public OutboxMessage CreateModel(
        Guid TenantId,
        Guid aggregateId,
        Guid eventId,
        Guid causationId,
        Guid correlationId,
        string aggregateType,
        string eventType,
        OutBoxState status,
        string payload
        )
    {
        return new OutboxMessage
        {
            TenantId = TenantId,
            AggregateId = aggregateId,
            EventId = eventId,
            CausationId = causationId,
            CorrelationId = correlationId,
            AggregateType = aggregateType,
            EventType = eventType,
            ProcessedOn = DateTime.UtcNow,
            NextRetryOn = DateTime.UtcNow.AddMinutes(10),
            Payload = payload,
            RetryCount = 0,
            Status = status,
        };
    }
}
