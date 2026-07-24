using MassTransit;
using OnePunch.Auth.Core;
using OnePunch.Auth.Core.Services;
using OnePunch.Auth.Domain.Entities;

namespace Onepunch.Auth.Core.Services;

public class SubscriptionService : BaseService<User>
{
    private readonly IPublishEndpoint _publisher;
    public SubscriptionService(IPublishEndpoint publisher, IUnitOfWorkService uow) : base(uow)
    {
        _publisher = publisher;
    }
    public async Task Create(PlanRequest payload, CancellationToken token)
    {
        await _publisher.Publish(payload);
    }
}
