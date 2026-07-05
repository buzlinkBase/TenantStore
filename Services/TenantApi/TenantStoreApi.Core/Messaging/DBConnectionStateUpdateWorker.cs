using MassTransit;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Serilog;
namespace TenantStoreApi.Core.Messaging;

public class DBConnectionStateUpdateWorker : IConsumer<DbStateUpdatePayload>
{
    private readonly IUnitOfWorkService _unitOfWork;
    public DBConnectionStateUpdateWorker(IUnitOfWorkService unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }
    public async Task Consume(ConsumeContext<DbStateUpdatePayload> context)
    {
        var message = context.Message;
        if (message == null) return;

        var connection = await _unitOfWork.Context.Connections
            .Where(x => x.ServiceOwner == message.ServiceOwner && x.TenantId == message.TenantId)
            .FirstOrDefaultAsync();

        if (connection == null)
        {
            Log.Warning("Tenant {0} not found in updating tenant connection string {1}", message.TenantId, JsonConvert.SerializeObject(message));
            return;
        }
        connection.IsActive = message.IsActive;
        connection.Status = message.Status;
        connection.Remarks = message.Remarks;
        connection.ArchieveSchedule = message.ArchieveSchedule;
        _unitOfWork.Repository.Update(connection);
    }

}
