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
    public async Task AddAsync(OutboxMessage payload) => _repository.Add(payload);
    public async Task UpdateStateAsync(OutboxMessage payload) => _repository.Update(payload);
    public OutboxMessage CreateModel(
        Guid TenantId,
        string key,
        string topic,
        string payload )
    {
        return new OutboxMessage
        {
            TenantId = TenantId,
            Key = key,
            Topic = topic,
            Payload = payload,
            RetryCount = 0,
            Status =  OutBoxState.PENDING,
        };
    }
}
