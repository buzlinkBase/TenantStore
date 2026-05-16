using BuzlinkRepository;
using Serilog;
using TenantStoreApi.Infrastructure;
namespace TenantStoreApi.Core;
public interface IUnitOfWorkService : IUnitOfWork<TenantContext> { }
public class UnitOfWorkService(TenantContext context) : UnitOfWork<TenantContext>(context), IUnitOfWorkService
{
    public override bool CommitChanges(string message = "")
    {
        Log.Information(message);   
        return base.CommitChanges(message);
    }
    public override Task<bool> CommitChangesAsync(string message = "", CancellationToken token = default)
    {
        Log.Information(message);
        return base.CommitChangesAsync(message, token);
    }
}
 