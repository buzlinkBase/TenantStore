using MassTransit;
using Onepunch.Common.Lib.DTO;
using TenantStoreApi.Core.Services.Platform;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Core.Messaging;

/// <summary>
/// hrms-api finished (or failed) removing one company's data. Records it in the billing history and,
/// once every company of the account has reported, moves the account to Purged (or, for the
/// archive stage, ArchiveDestroyed). Idempotent -- every event is keyed, so a redelivery adds nothing.
/// Answers for a company or account an operator erased go to <see cref="PlatformCompanyDeletionService"/>
/// or <see cref="PlatformAccountDeletionService"/>.
/// </summary>
public class CompanyDataPurgedWorker : IConsumer<CompanyDataPurged>
{
    private readonly DataPurgeService _purge;
    private readonly PlatformAccountDeletionService _deletion;
    private readonly PlatformCompanyDeletionService _companyDeletion;
    private readonly IUnitOfWorkService _uow;

    public CompanyDataPurgedWorker(
        DataPurgeService purge, PlatformAccountDeletionService deletion, PlatformCompanyDeletionService companyDeletion, IUnitOfWorkService uow)
    {
        _purge = purge;
        _deletion = deletion;
        _companyDeletion = companyDeletion;
        _uow = uow;
    }

    public async Task Consume(ConsumeContext<CompanyDataPurged> context)
    {
        if (context.Message.Stage == CompanyPurgeStage.HardDelete)
        {
            if (!await _companyDeletion.RecordResultAsync(context.Message, context.CancellationToken))
                await _deletion.RecordResultAsync(context.Message, context.CancellationToken);
        }
        else
            await _purge.RecordResultAsync(context.Message, context.CancellationToken);
        await _uow.CommitChangesAsync("", context.CancellationToken);
    }
}
