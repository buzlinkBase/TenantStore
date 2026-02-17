using BuzlinkRepository;
using Onepunch.Common.Lib.Entities;
namespace Onepunch.Common.Lib.Services;
public abstract class OutBoxServiceBase
{
    private readonly IRepository _repository;
    public OutBoxServiceBase(IRepository repository)
    {
        _repository = repository;
    }
    public async Task AddAsync(OutboxMessage outbox,CancellationToken token) => await _repository.AddAsync(outbox, token); 
    public  OutboxMessage CreateModel(
        Guid TenantId,
        string key,
        string topic,
        string payload)
    {
        return new OutboxMessage
        {
            TenantId = TenantId,
            Key = key,
            Topic = topic,
            Payload = payload,
            RetryCount = 0,
            Status = OutBoxState.PENDING,
        };
    }
}
