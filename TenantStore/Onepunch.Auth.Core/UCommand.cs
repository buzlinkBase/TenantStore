namespace OnePunch.Auth.Core;

public interface IUnitOfWorkService : IUnitOfWork<AuthContext> { }
public class UCommand : UnitOfWork<AuthContext>, IUnitOfWorkService
{
    public UCommand(AuthContext context) : base(context)
    {
        OnCommitChanges += UCommand_OnCommitChanges;
    }
    private void UCommand_OnCommitChanges(object? sender, CommitChangesResponse e)
    {
    }
}
