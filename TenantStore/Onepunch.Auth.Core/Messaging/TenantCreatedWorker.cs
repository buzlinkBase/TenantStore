using MassTransit;
using Onepunch.Auth.Domain.Entities;
namespace OnePunch.Auth.Core.Messaging;
public class TenantCreatedWorker : IConsumer<TenantCreatedPayload>
{
    private readonly IUnitOfWorkService _uow;
    public TenantCreatedWorker(IUnitOfWorkService uow)
    {
        _uow = uow;
    }
    public async Task Consume(ConsumeContext<TenantCreatedPayload> context)
    {
        var existing = _uow.Repository
            .Find<UserTenant>(x => x.TenantId == context.Message.TenantId && x.UserId == context.Message.UserId)
            .Select(x => x.Id)
            .Any();
        if (existing) return;
        var model = context.Message;
        var userTenants = new UserTenant
        {
            TenantId = model.TenantId,
            UserId = model.UserId,
        };
        _uow.Repository.Add(userTenants);
        _uow.CommitChanges();
    }
}
