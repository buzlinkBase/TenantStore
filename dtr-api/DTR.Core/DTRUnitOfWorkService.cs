namespace DTR.Core;
public interface IDTRUnitOfWork : IUnitOfWork<DTRDbContext> { }
public class UCommand : UnitOfWork<DTRDbContext>, IDTRUnitOfWork
{
    public UCommand(DTRDbContext context) : base(context)
    {
        OnCommitChanges += UCommand_OnCommitChanges;
    }

    private void UCommand_OnCommitChanges(object? sender, CommitChangesResponse e)
    {
    } 
}
