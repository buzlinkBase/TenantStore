using MassTransit;
using TenantStoreApi.Core.Services;

namespace TenantStoreApi.Core.Messaging;

public class DbCreatedWorker : IConsumer<ConnectionStringPayload>
{
    private readonly IMapper _mapper;
    private readonly ConnectionService _service;

    public DbCreatedWorker(IMapper mapper, ConnectionService service)
    {
        _mapper = mapper;
        _service = service;
    }
    public async Task Consume(ConsumeContext<ConnectionStringPayload> context)
    {
        var model = _mapper.Map<ConnectionStringStore>(context.Message);
        await _service.AddAsync(model,context.CancellationToken);
        await _service.CommitChangesAsync(context.CancellationToken);
    }
}
