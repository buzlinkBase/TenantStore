using BuzlinkRepository;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Core;

public interface IUnitOfWorkService : IUnitOfWork<TenantContext> { }
public class UCommand : UnitOfWork<TenantContext>, IUnitOfWorkService
{
    public UCommand(TenantContext context) : base(context)
    {
        OnCommitChanges += UCommand_OnCommitChanges;
    }
    private void UCommand_OnCommitChanges(object? sender, CommitChangesResponse e)
    {
    }
}
