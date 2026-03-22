using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace TenantStoreApi.Core.Messaging;

public class SchemaMigrationUpdatedWorker : IConsumer<SchemaVersionUpdatePayload>
{
    private readonly IUnitOfWorkService _unitOfWork;
    public SchemaMigrationUpdatedWorker(IUnitOfWorkService unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }
    public async Task Consume(ConsumeContext<SchemaVersionUpdatePayload> context)
    {
        var message = context.Message;
        if (message == null) return;

        var schema = await _unitOfWork.Context.SchemaVersions
            .FirstOrDefaultAsync(sv => sv.System == message.System && sv.TenantId == message.TenantId);

        if (schema == null)
        {
            schema = new SchemaVersion
            {
                TenantId = message.TenantId,
                System = message.System,
                CurrentVersion = message.CurrentVersion,
                // If it's brand new, Target and Current are the same
                TargetVersion = message.CurrentVersion,
                Status = "Active" // Ensure it starts as Active
            };
            await _unitOfWork.Repository.AddAsync(schema);
        }
        else
        {
            schema.CurrentVersion = message.CurrentVersion;
            // CRITICAL: If Current matches Target, set status back to Active
            if (schema.CurrentVersion == schema.TargetVersion)
            {
                schema.Status = "Active";
            }
            _unitOfWork.Repository.Update(schema);
        }

        // Pass the user/system name for audit logs if your UnitOfWork supports it
        await _unitOfWork.CommitChangesAsync($"MigrationUpdate-{message.System}", context.CancellationToken);
    }
}
