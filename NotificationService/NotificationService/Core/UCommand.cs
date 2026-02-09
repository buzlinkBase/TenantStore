using OnePunch.Notification.Infrastructure.Data;

namespace OnePunch.Notification.Core;

public interface IUnitOfWorkService : IUnitOfWork<NotifContext> { }
public class UCommand : UnitOfWork<NotifContext>, IUnitOfWorkService
{
    public UCommand(NotifContext context) : base(context)
    {
        OnCommitChanges += UCommand_OnCommitChanges;
    }
    private void UCommand_OnCommitChanges(object? sender, CommitChangesResponse e)
    {
    }
}
