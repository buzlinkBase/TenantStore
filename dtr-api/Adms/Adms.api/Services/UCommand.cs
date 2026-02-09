using BuzlinkRepository;

namespace Adms.api.Services;

public interface IUnitOfWorkService : IUnitOfWork<AdmsContext> { }
public class UCommand : UnitOfWork<AdmsContext>, IUnitOfWorkService
{
    public UCommand(AdmsContext context) : base(context)
    {
        OnCommitChanges += UCommand_OnCommitChanges;
    }
    private void UCommand_OnCommitChanges(object? sender, CommitChangesResponse e)
    {
    }
}
